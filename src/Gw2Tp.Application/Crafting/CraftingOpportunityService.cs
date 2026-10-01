using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.AccountEvidence;
using Gw2Tp.Application.Plans;
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
    ICraftingEvidenceDiagnostics? diagnostics = null,
    IAccountHoldingsSnapshotService? holdings = null) : ICraftingOpportunityService
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
            holdings is null && (snapshot.BankInventory.Availability != CraftingFeatureAvailability.Available ||
            snapshot.MaterialStorage.Availability != CraftingFeatureAvailability.Available))
            return Timed(new(CraftingOpportunityState.Degraded, [], [], [CraftingOpportunityExclusion.CapabilityUnavailable]), preparationTimer, totalTimer);

        var projection = holdings is null ? null : await holdings.GetProjectionAsync(scope.Value, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        if (holdings is not null && projection?.IsCurrentGeneration != true)
            return Timed(new(CraftingOpportunityState.Degraded, [], [], [CraftingOpportunityExclusion.StaleEvidence], "holdings_evidence_unavailable"), preparationTimer, totalTimer);
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
        var result = projection is null
            ? planner.Plan(new(definitions.Value, unlocked.ToHashSet(), snapshot.CharacterCrafting.Value ?? [], markets, new Dictionary<int, CraftingOwnedEvidence>(), limits))
            : PlanForActors(projection, definitions.Value, markets, limits);
        planningTimer.Stop();
        var extra = (recipeLimited ? new[] { CraftingSearchTruncationReason.RecipeLimit } : [])
            .Concat(marketLimited ? new[] { CraftingSearchTruncationReason.MarketDataLimit } : []).Distinct().OrderBy(value => value).ToArray();
        result = extra.Length == 0 ? result : result with { TruncationReasons = result.TruncationReasons.Concat(extra).Distinct().OrderBy(value => value).ToArray() };
        return Timed(result with { AccountEvidenceCapturedAtUtc = snapshot.CapturedAtUtc }, preparationTimer, totalTimer, recipesTimer, listingTimer, metadataTimer, historyTimer, planningTimer);
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

    private CraftingPlannerResult PlanForActors(AccountHoldingsProjection projection, IReadOnlyList<CraftingRecipe> definitions,
        IReadOnlyDictionary<int, CraftingMarketEvidence> markets, CraftingPlannerLimits limits)
    {
        var capture = projection.FreshCapture;
        if (!AccountEvidencePolicyFacts.Complete(capture.RecipeUnlocks, AccountHoldingsSource.RecipeUnlocks) ||
            AccountEvidencePolicyFacts.CurrentActors(capture) is null)
            return new(CraftingOpportunityState.Degraded, [], [], [CraftingOpportunityExclusion.CapabilityUnavailable]);
        var evaluated = new List<(string ActorId, CraftingPlannerResult Result)>();
        foreach (var actor in capture.Characters.OrderBy(actor => actor.Actor.ActorId, StringComparer.Ordinal))
        {
            if (!AccountEvidencePolicyFacts.Complete(actor.Crafting, AccountHoldingsSource.CharacterCrafting, actor.Actor.ActorId)) continue;
            var owned = projection.Items.Where(item => item.Admission is { AdmissionQuantity: > 0 } && projection.CanAccess(item.Admission, actor.Actor.ActorId))
                .ToDictionary(item => item.ItemId, item =>
                {
                    var admission = item.Admission!;
                    var tradable = markets.TryGetValue(item.ItemId, out var value) && value.IsFresh && value.Listing.Buys.Any(level => level.Quantity > 0 && level.UnitPriceInCopper > 0);
                    var state = admission.TradeableQuantity > 0 ? tradable ? CraftingOwnedMaterialState.Tradable : CraftingOwnedMaterialState.Unknown : CraftingOwnedMaterialState.Bound;
                    return new CraftingOwnedEvidence([new(admission.AdmissionQuantity, state)], null);
                });
            var result = planner.Plan(new(definitions, capture.RecipeUnlocks.Value!.ToHashSet(), actor.Crafting.Value!, markets, owned, limits));
            var opportunities = result.Opportunities.Select(opportunity =>
            {
                if (opportunity.Candidate is not { } candidate) return opportunity;
                var recipeIds = candidate.Steps.Where(step => step.Action == PlanStepAction.Craft).Select(step => step.RecipeId ?? opportunity.Recipe.RecipeId).Distinct().ToHashSet();
                // Passive-only procurement still has a real intended actor and recipe prerequisite.
                if (recipeIds.Count == 0) recipeIds.Add(opportunity.Recipe.RecipeId);
                var chain = definitions.Where(recipe => recipeIds.Contains(recipe.RecipeId)).ToArray();
                var inputs = candidate.Requirements.Where(requirement => requirement.Kind == PlanResourceKind.Inventory && requirement.Quantity > 0)
                    .Select(requirement => int.TryParse(requirement.ResourceId, out var id) ? projection.Items.SingleOrDefault(item => item.ItemId == id)?.Admission?.Observation : null).ToArray();
                var restricted = capture with { Roster = capture.Roster with { Value = new[] { actor.Actor } }, Characters = [actor] };
                // The full roster is retained when testing input access; the chosen actor was already checked independently by its planner input.
                var feasible = new CraftingActorSelector().Select(capture, chain, inputs.Where(input => input is not null).Cast<HoldingsItemObservation>().ToArray());
                var capable = new CraftingActorSelector().Select(restricted, chain);
                var authorized = PlanHoldingsAdmission.Authorize(candidate, projection, actor.Actor.ActorId, chain);
                var unsupported = inputs.Any(input => input is null) || chain.Length != recipeIds.Count ||
                    capable.Failure != CraftingActorSelectionFailure.None || feasible.Failure != CraftingActorSelectionFailure.None ||
                    inputs.Any(input => input is not null && input.Location.Source == AccountHoldingsSource.CharacterInventory && input.Location.ActorId != actor.Actor.ActorId) ||
                    chain.SelectMany(recipe => recipe.Ingredients).Any(ingredient =>
                        projection.Items.SingleOrDefault(item => item.ItemId == ingredient.Id)?.Admission is { AdmissionQuantity: > 0 } selected &&
                        !projection.CanAccess(selected, actor.Actor.ActorId)) ||
                    chain.SelectMany(recipe => recipe.Ingredients).Any(ingredient => projection.Snapshot.ProtectionFloor.ItemIds.Contains(ingredient.Id));
                return unsupported || !authorized.IsHardEligible
                    ? opportunity with { Candidate = null, IsActionable = false,
                        Exclusions = opportunity.Exclusions.Append(CraftingOpportunityExclusion.UnsupportedPrerequisite).Distinct().ToArray() }
                    : opportunity with { Candidate = authorized };
            }).ToArray();
            evaluated.Add((actor.Actor.ActorId, result with { Opportunities = opportunities }));
        }
        if (evaluated.Count == 0) return new(CraftingOpportunityState.Degraded, [], [], [CraftingOpportunityExclusion.CapabilityUnavailable]);
        // Same recipe ties use the actual opaque actor ID; no aggregate capability or economic ranking change.
        var combined = evaluated.SelectMany(value => value.Result.Opportunities.Select(opportunity => (value.ActorId, Opportunity: opportunity)))
            .GroupBy(value => value.Opportunity.Recipe.RecipeId).Select(group => group.OrderByDescending(value => value.Opportunity.IsActionable)
                .ThenBy(value => value.ActorId, StringComparer.Ordinal).First().Opportunity)
            .OrderByDescending(value => value.IsActionable).ThenByDescending(value => value.Economics.NetProfit?.Copper ?? long.MinValue)
            .ThenBy(value => value.Recipe.RecipeId).Take(limits.MaximumCandidates).ToArray();
        return new(combined.Any(value => value.IsActionable) ? CraftingOpportunityState.Ready : CraftingOpportunityState.NoOpportunities,
            combined, evaluated.SelectMany(value => value.Result.TruncationReasons).Distinct().Order().ToArray(),
            combined.SelectMany(value => value.Exclusions).Distinct().Order().ToArray());
    }

    private static int HistoryConfidence(HistoricalMarketAnalytics analytics)
    {
        var available = analytics.Windows.Count(window => window.State == HistoricalMarketWindowState.Available);
        return available >= 3 ? 9_000 : available == 2 ? 8_000 : available == 1 ? 5_000 : 0;
    }
}
