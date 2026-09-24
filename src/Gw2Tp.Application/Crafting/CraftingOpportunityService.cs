using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Analytics.OrderBooks;

namespace Gw2Tp.Application.Crafting;

public interface ICraftingOpportunityService
{
    Task<CraftingPlannerResult> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Builds a bounded planner input exclusively from typed local/account gateways.</summary>
public sealed class CraftingOpportunityService(
    IPersonalTradingPostGateway personalTradingPost,
    IAccountCraftingSnapshotService snapshots,
    ICraftingReferenceGateway recipes,
    IGw2ApiClient market,
    IHistoricalMarketAnalyticsService history,
    ICraftingOpportunityPlanner planner) : ICraftingOpportunityService
{
    public async Task<CraftingPlannerResult> GetAsync(CancellationToken cancellationToken = default)
    {
        var scope = await personalTradingPost.GetAccountScopeAsync(cancellationToken).ConfigureAwait(false);
        if (!scope.IsSuccess || scope.Value is null)
            return new(CraftingOpportunityState.Degraded, [], [], [CraftingOpportunityExclusion.StaleEvidence]);
        var snapshot = await snapshots.GetLatestAsync(scope.Value, cancellationToken).ConfigureAwait(false);
        if (snapshot is null || DateTimeOffset.UtcNow - snapshot.CapturedAtUtc > TimeSpan.FromMinutes(15) ||
            snapshot.RecipeUnlocks.Availability != CraftingFeatureAvailability.Available ||
            snapshot.CharacterCrafting.Availability != CraftingFeatureAvailability.Available ||
            snapshot.BankInventory.Availability != CraftingFeatureAvailability.Available ||
            snapshot.MaterialStorage.Availability != CraftingFeatureAvailability.Available)
            return new(CraftingOpportunityState.Degraded, [], [], [CraftingOpportunityExclusion.CapabilityUnavailable]);

        var limits = CraftingPlannerLimits.Default;
        var unlocked = (snapshot.RecipeUnlocks.Value ?? []).OrderBy(id => id).ToArray();
        var recipeLimited = unlocked.Length > limits.MaximumRecipes;
        var requested = unlocked.Take(limits.MaximumRecipes).ToArray();
        var definitions = await recipes.GetRecipesAsync(requested, cancellationToken).ConfigureAwait(false);
        if (!definitions.IsSuccess || definitions.Value is null || definitions.IsPartialData)
            return new(CraftingOpportunityState.Degraded, [], [], [CraftingOpportunityExclusion.MissingInputEvidence]);
        var allItemIds = definitions.Value.Select(recipe => recipe.OutputItemId)
            .Concat(definitions.Value.SelectMany(recipe => recipe.Ingredients.Where(ingredient => ingredient.Type == "Item").Select(ingredient => ingredient.Id)))
            .Where(id => id > 0).Distinct().OrderBy(id => id).ToArray();
        var marketLimited = allItemIds.Length > 200;
        var itemIds = allItemIds.Take(200).ToArray();
        var listingTask = market.GetListingsAsync(itemIds, cancellationToken);
        var metadataTask = market.GetItemMetadataAsync(itemIds, cancellationToken);
        await Task.WhenAll(listingTask, metadataTask).ConfigureAwait(false);
        var listings = await listingTask.ConfigureAwait(false);
        var metadata = await metadataTask.ConfigureAwait(false);
        if (!listings.IsSuccess || listings.Value is null || listings.IsPartialData || !metadata.IsSuccess || metadata.Value is null || metadata.IsPartialData)
            return new(CraftingOpportunityState.Degraded, [], [], [CraftingOpportunityExclusion.MissingInputEvidence]);
        var listingByItem = listings.Value.ToDictionary(value => value.ItemId);
        var metadataByItem = metadata.Value.ToDictionary(value => value.ItemId);
        var outputIds = definitions.Value.Select(recipe => recipe.OutputItemId).Distinct().OrderBy(id => id).ToArray();
        var histories = await Task.WhenAll(outputIds.Select(async itemId => new { ItemId = itemId, Value = await history.GetAsync(itemId, cancellationToken).ConfigureAwait(false) })).ConfigureAwait(false);
        var historyByItem = histories.ToDictionary(value => value.ItemId, value => value.Value);
        var markets = itemIds.Where(id => listingByItem.ContainsKey(id) && metadataByItem.ContainsKey(id)).ToDictionary(id => id,
            id => new CraftingMarketEvidence(listingByItem[id], metadataByItem[id], true,
                historyByItem.TryGetValue(id, out var analytics) && analytics.Windows.Count(window => window.State == HistoricalMarketWindowState.Available) >= 2,
                historyByItem.TryGetValue(id, out analytics) ? HistoryConfidence(analytics) : 0));
        var result = planner.Plan(new(definitions.Value, unlocked.ToHashSet(), snapshot.CharacterCrafting.Value ?? [], markets, Owned(snapshot, markets), limits));
        var extra = (recipeLimited ? new[] { CraftingSearchTruncationReason.RecipeLimit } : [])
            .Concat(marketLimited ? new[] { CraftingSearchTruncationReason.MarketDataLimit } : []).Distinct().OrderBy(value => value).ToArray();
        return extra.Length == 0 ? result : result with { TruncationReasons = result.TruncationReasons.Concat(extra).Distinct().OrderBy(value => value).ToArray() };
    }

    private static IReadOnlyDictionary<int, CraftingOwnedEvidence> Owned(AccountCraftingSnapshot snapshot,
        IReadOnlyDictionary<int, CraftingMarketEvidence> markets)
    {
        var rows = (snapshot.BankInventory.Value ?? []).Select(value => (value.ItemId, value.Quantity, value.Binding))
            .Concat((snapshot.MaterialStorage.Value ?? []).Select(value => (value.ItemId, value.Quantity, value.Binding)));
        return rows.Where(value => value.ItemId > 0 && value.Quantity > 0).GroupBy(value => value.ItemId).ToDictionary(group => group.Key,
            group =>
            {
                var tradableQuantity = group.Where(value => value.Binding == AccountItemBinding.Unspecified).Sum(value => value.Quantity);
                var liquidation = tradableQuantity > 0 && markets.TryGetValue(group.Key, out var market)
                    ? CraftingExecutionEvidence.FromOrderBookExecution(new OrderBookExecutionSimulator().SimulateLiquidation(
                        market.Listing.Buys.Where(level => level.Quantity > 0 && level.UnitPriceInCopper > 0)
                            .Select(level => new OrderBookLevel(level.Quantity, new Gw2Tp.Domain.Finance.Money(level.UnitPriceInCopper))).ToArray(), tradableQuantity))
                    : null;
                var tradable = liquidation?.IsFullyFilled == true && liquidation.SourceScenario?.Kind == OrderBookExecutionKind.Liquidation;
                return new CraftingOwnedEvidence(group.Select(value => new CraftingOwnedMaterial(value.Quantity,
                    value.Binding == AccountItemBinding.Unspecified && tradable ? CraftingOwnedMaterialState.Tradable :
                    value.Binding == AccountItemBinding.Unspecified ? CraftingOwnedMaterialState.Unknown : CraftingOwnedMaterialState.Bound)).ToArray(), liquidation);
            });
    }

    private static int HistoryConfidence(HistoricalMarketAnalytics analytics)
    {
        var available = analytics.Windows.Count(window => window.State == HistoricalMarketWindowState.Available);
        return available >= 3 ? 9_000 : available == 2 ? 8_000 : available == 1 ? 5_000 : 0;
    }
}
