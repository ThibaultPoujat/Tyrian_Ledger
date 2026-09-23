using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.PersonalTradingPost;

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
            snapshot.CharacterCrafting.Availability != CraftingFeatureAvailability.Available)
            return new(CraftingOpportunityState.Degraded, [], [], [CraftingOpportunityExclusion.CapabilityUnavailable]);

        var limits = CraftingPlannerLimits.Default;
        var unlocked = (snapshot.RecipeUnlocks.Value ?? []).OrderBy(id => id).ToArray();
        var requested = unlocked.Take(limits.MaximumRecipes).ToArray();
        var definitions = await recipes.GetRecipesAsync(requested, cancellationToken).ConfigureAwait(false);
        if (!definitions.IsSuccess || definitions.Value is null || definitions.IsPartialData)
            return new(CraftingOpportunityState.Degraded, [], [], [CraftingOpportunityExclusion.MissingInputEvidence]);
        var itemIds = definitions.Value.Select(recipe => recipe.OutputItemId)
            .Concat(definitions.Value.SelectMany(recipe => recipe.Ingredients.Where(ingredient => ingredient.Type == "Item").Select(ingredient => ingredient.Id)))
            .Where(id => id > 0).Distinct().OrderBy(id => id).Take(200).ToArray();
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
                historyByItem.TryGetValue(id, out var analytics) && analytics.Windows.Any(window => window.State == HistoricalMarketWindowState.Available)));
        return planner.Plan(new(definitions.Value, unlocked.ToHashSet(), snapshot.CharacterCrafting.Value ?? [], markets, Owned(snapshot), limits));
    }

    private static IReadOnlyDictionary<int, CraftingOwnedEvidence> Owned(AccountCraftingSnapshot snapshot)
    {
        var rows = (snapshot.BankInventory.Value ?? []).Select(value => (value.ItemId, value.Quantity, value.Binding))
            .Concat((snapshot.MaterialStorage.Value ?? []).Select(value => (value.ItemId, value.Quantity, value.Binding)));
        return rows.Where(value => value.ItemId > 0 && value.Quantity > 0).GroupBy(value => value.ItemId).ToDictionary(group => group.Key,
            group => new CraftingOwnedEvidence(group.Select(value => new CraftingOwnedMaterial(value.Quantity,
                value.Binding == AccountItemBinding.Unspecified ? CraftingOwnedMaterialState.Unknown : CraftingOwnedMaterialState.Bound)).ToArray(), null));
    }
}
