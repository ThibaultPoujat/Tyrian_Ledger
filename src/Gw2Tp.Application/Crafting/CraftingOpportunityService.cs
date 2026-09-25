using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.PersonalTradingPost;
using System.Diagnostics;

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
    ICraftingOpportunityPlanner planner,
    ICraftingEvidenceDiagnostics? diagnostics = null) : ICraftingOpportunityService
{
    public async Task<CraftingPlannerResult> GetAsync(CancellationToken cancellationToken = default)
    {
        var totalTimer = Stopwatch.StartNew();
        var preparationTimer = Stopwatch.StartNew();
        var scope = await personalTradingPost.GetAccountScopeAsync(cancellationToken).ConfigureAwait(false);
        if (!scope.IsSuccess || scope.Value is null)
            return Timed(new(CraftingOpportunityState.Degraded, [], [], [CraftingOpportunityExclusion.StaleEvidence]), preparationTimer, totalTimer);
        var snapshot = await snapshots.GetLatestAsync(scope.Value, cancellationToken).ConfigureAwait(false);
        if (snapshot is null || DateTimeOffset.UtcNow - snapshot.CapturedAtUtc > TimeSpan.FromMinutes(15) ||
            snapshot.RecipeUnlocks.Availability != CraftingFeatureAvailability.Available ||
            snapshot.CharacterCrafting.Availability != CraftingFeatureAvailability.Available ||
            snapshot.BankInventory.Availability != CraftingFeatureAvailability.Available ||
            snapshot.MaterialStorage.Availability != CraftingFeatureAvailability.Available)
            return Timed(new(CraftingOpportunityState.Degraded, [], [], [CraftingOpportunityExclusion.CapabilityUnavailable]), preparationTimer, totalTimer);

        preparationTimer.Stop();

        var limits = CraftingPlannerLimits.Default;
        var unlocked = (snapshot.RecipeUnlocks.Value ?? []).OrderBy(id => id).ToArray();
        var recipeLimited = unlocked.Length > limits.MaximumRecipes;
        var requested = unlocked.Take(limits.MaximumRecipes).ToArray();
        var recipesTimer = Stopwatch.StartNew();
        var definitions = await recipes.GetRecipesAsync(requested, cancellationToken).ConfigureAwait(false);
        recipesTimer.Stop();
        if (!definitions.IsSuccess || definitions.Value is null || definitions.IsPartialData)
            return Timed(new(CraftingOpportunityState.Degraded, [], [], [CraftingOpportunityExclusion.MissingInputEvidence], "recipe_definitions_unavailable"), preparationTimer, totalTimer, recipesTimer: recipesTimer);
        var allItemIds = definitions.Value.Select(recipe => recipe.OutputItemId)
            .Concat(definitions.Value.SelectMany(recipe => recipe.Ingredients.Where(ingredient => ingredient.Type == "Item").Select(ingredient => ingredient.Id)))
            .Where(id => id > 0).Distinct().OrderBy(id => id).ToArray();
        var marketLimited = allItemIds.Length > 200;
        var itemIds = allItemIds.Take(200).ToArray();
        var listingTimer = Stopwatch.StartNew();
        var listingTask = market.GetListingsAsync(itemIds, cancellationToken);
        var metadataTimer = Stopwatch.StartNew();
        var metadataTask = market.GetItemMetadataAsync(itemIds, cancellationToken);
        await Task.WhenAll(listingTask, metadataTask).ConfigureAwait(false);
        listingTimer.Stop();
        metadataTimer.Stop();
        var listings = await listingTask.ConfigureAwait(false);
        var metadata = await metadataTask.ConfigureAwait(false);
        if (!listings.IsSuccess || listings.Value is null || listings.IsPartialData)
        {
            RecordMarketDiagnostic(itemIds.Length, listings, metadata, listingTimer.Elapsed, metadataTimer.Elapsed, "market_listings_unavailable");
            return Timed(new(CraftingOpportunityState.Degraded, [], [], [CraftingOpportunityExclusion.MissingInputEvidence], "market_listings_unavailable"), preparationTimer, totalTimer, recipesTimer, listingTimer, metadataTimer);
        }
        if (!metadata.IsSuccess || metadata.Value is null || metadata.IsPartialData)
        {
            RecordMarketDiagnostic(itemIds.Length, listings, metadata, listingTimer.Elapsed, metadataTimer.Elapsed, "market_metadata_unavailable");
            return Timed(new(CraftingOpportunityState.Degraded, [], [], [CraftingOpportunityExclusion.MissingInputEvidence], "market_metadata_unavailable"), preparationTimer, totalTimer, recipesTimer, listingTimer, metadataTimer);
        }
        RecordMarketDiagnostic(itemIds.Length, listings, metadata, listingTimer.Elapsed, metadataTimer.Elapsed, "complete");
        var listingByItem = listings.Value.ToDictionary(value => value.ItemId);
        var metadataByItem = metadata.Value.ToDictionary(value => value.ItemId);
        // History is relevant only for a bounded output that already has full
        // current market evidence. Recipes outside the market budget, or with
        // no usable evidence, are excluded by the planner; reading their
        // history cannot make them safe and previously dominated this phase.
        var marketItemIds = itemIds.Where(id => listingByItem.ContainsKey(id) && metadataByItem.ContainsKey(id)).ToArray();
        var outputIds = definitions.Value.Select(recipe => recipe.OutputItemId)
            .Where(marketItemIds.Contains).Distinct().OrderBy(id => id).ToArray();
        var historyTimer = Stopwatch.StartNew();
        var histories = await Task.WhenAll(outputIds.Select(async itemId => new { ItemId = itemId, Value = await history.GetAsync(itemId, cancellationToken).ConfigureAwait(false) })).ConfigureAwait(false);
        historyTimer.Stop();
        var historyByItem = histories.ToDictionary(value => value.ItemId, value => value.Value);
        var markets = marketItemIds.ToDictionary(id => id,
            id => new CraftingMarketEvidence(listingByItem[id], metadataByItem[id], true,
                historyByItem.TryGetValue(id, out var analytics) && analytics.Windows.Count(window => window.State == HistoricalMarketWindowState.Available) >= 2,
                historyByItem.TryGetValue(id, out analytics) ? HistoryConfidence(analytics) : 0));
        var planningTimer = Stopwatch.StartNew();
        var result = planner.Plan(new(definitions.Value, unlocked.ToHashSet(), snapshot.CharacterCrafting.Value ?? [], markets, Owned(snapshot, markets), limits));
        planningTimer.Stop();
        var extra = (recipeLimited ? new[] { CraftingSearchTruncationReason.RecipeLimit } : [])
            .Concat(marketLimited ? new[] { CraftingSearchTruncationReason.MarketDataLimit } : []).Distinct().OrderBy(value => value).ToArray();
        result = extra.Length == 0 ? result : result with { TruncationReasons = result.TruncationReasons.Concat(extra).Distinct().OrderBy(value => value).ToArray() };
        return Timed(result, preparationTimer, totalTimer, recipesTimer, listingTimer, metadataTimer, historyTimer, planningTimer);
    }

    private static CraftingPlannerResult Timed(CraftingPlannerResult result, Stopwatch preparationTimer, Stopwatch totalTimer,
        Stopwatch? recipesTimer = null, Stopwatch? listingTimer = null, Stopwatch? metadataTimer = null,
        Stopwatch? historyTimer = null, Stopwatch? planningTimer = null)
    {
        if (preparationTimer.IsRunning) preparationTimer.Stop();
        if (recipesTimer?.IsRunning == true) recipesTimer.Stop();
        if (listingTimer?.IsRunning == true) listingTimer.Stop();
        if (metadataTimer?.IsRunning == true) metadataTimer.Stop();
        if (historyTimer?.IsRunning == true) historyTimer.Stop();
        if (planningTimer?.IsRunning == true) planningTimer.Stop();
        totalTimer.Stop();
        return result with { Timing = new(Milliseconds(preparationTimer.Elapsed), Milliseconds(recipesTimer?.Elapsed),
            Milliseconds(listingTimer?.Elapsed), Milliseconds(metadataTimer?.Elapsed), Milliseconds(historyTimer?.Elapsed),
            Milliseconds(planningTimer?.Elapsed), Milliseconds(totalTimer.Elapsed)) };
    }

    private static long Milliseconds(TimeSpan? elapsed) => elapsed is { } value ? Math.Max(0, (long)value.TotalMilliseconds) : 0;

    private void RecordMarketDiagnostic(
        int requestedItemIdCount,
        Gw2ApiResult<IReadOnlyList<MarketListing>> listings,
        Gw2ApiResult<IReadOnlyList<MarketItemMetadata>> metadata,
        TimeSpan listingsElapsed,
        TimeSpan metadataElapsed,
        string outcome) =>
        diagnostics?.Record(new(
            requestedItemIdCount,
            Milliseconds(listingsElapsed),
            listings.ErrorCategory,
            listings.IsPartialData,
            listings.Value?.Count,
            0,
            Milliseconds(metadataElapsed),
            metadata.ErrorCategory,
            metadata.IsPartialData,
            metadata.Value?.Count,
            outcome));

    private static long Milliseconds(TimeSpan elapsed) => Math.Max(0, (long)elapsed.TotalMilliseconds);

    private static IReadOnlyDictionary<int, CraftingOwnedEvidence> Owned(AccountCraftingSnapshot snapshot,
        IReadOnlyDictionary<int, CraftingMarketEvidence> markets)
    {
        var rows = (snapshot.BankInventory.Value ?? []).Select(value => (value.ItemId, value.Quantity, value.Binding))
            .Concat((snapshot.MaterialStorage.Value ?? []).Select(value => (value.ItemId, value.Quantity, value.Binding)));
        return rows.Where(value => value.ItemId > 0 && value.Quantity > 0).GroupBy(value => value.ItemId).ToDictionary(group => group.Key,
            group =>
            {
                // The planner subsequently simulates the exact consumed subset,
                // rather than incorrectly valuing the complete owned stack.
                var tradable = markets.TryGetValue(group.Key, out var market) && market.IsFresh &&
                    market.Listing.Buys.Any(level => level.Quantity > 0 && level.UnitPriceInCopper > 0);
                return new CraftingOwnedEvidence(group.Select(value => new CraftingOwnedMaterial(value.Quantity,
                    value.Binding == AccountItemBinding.Unspecified && tradable ? CraftingOwnedMaterialState.Tradable :
                    value.Binding == AccountItemBinding.Unspecified ? CraftingOwnedMaterialState.Unknown : CraftingOwnedMaterialState.Bound)).ToArray(), null);
            });
    }

    private static int HistoryConfidence(HistoricalMarketAnalytics analytics)
    {
        var available = analytics.Windows.Count(window => window.State == HistoricalMarketWindowState.Available);
        return available >= 3 ? 9_000 : available == 2 ? 8_000 : available == 1 ? 5_000 : 0;
    }
}
