using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Plans;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Crafting;

/// <summary>Hard bounds for the deliberately small crafting search.</summary>
public sealed record CraftingPlannerLimits(int MaximumRecipes, int MaximumDepth, int MaximumCandidates, int MaximumWork)
{
    public static CraftingPlannerLimits Default { get; } = new(48, 4, 6, 400);
    public void Validate()
    {
        if (MaximumRecipes is <= 0 or > 200 || MaximumDepth is <= 0 or > 12 || MaximumCandidates is <= 0 or > 24 || MaximumWork is <= 0)
            throw new ArgumentOutOfRangeException(nameof(CraftingPlannerLimits));
    }
}

public enum CraftingOpportunityState { Ready = 1, NoOpportunities, Degraded }
public enum CraftingSearchTruncationReason { RecipeLimit = 1, DepthLimit, CandidateLimit, WorkLimit, MarketDataLimit }
public enum CraftingOpportunityExclusion { CapabilityUnavailable = 1, RecipeLocked, InvalidRecipe, CycleDetected, MissingInputEvidence, InsufficientInputDepth, InsufficientOutputDepth, WeakHistory, StaleEvidence, NotProfitable, RawSaleSuperior, ResourceConflict }

public sealed record CraftingMarketEvidence(MarketListing Listing, MarketItemMetadata Item, bool IsFresh, bool HasSufficientHistory, int ConfidenceBasisPoints = 8_000);
public sealed record CraftingOwnedEvidence(IReadOnlyList<CraftingOwnedMaterial> Materials, CraftingExecutionEvidence? Liquidation);
public sealed record CraftingPlannerInput(
    IReadOnlyList<CraftingRecipe> Recipes,
    IReadOnlySet<int> UnlockedRecipeIds,
    IReadOnlyList<CraftingDisciplineCapability> Disciplines,
    IReadOnlyDictionary<int, CraftingMarketEvidence> Markets,
    IReadOnlyDictionary<int, CraftingOwnedEvidence> Owned,
    CraftingPlannerLimits Limits,
    bool AccountEvidenceFresh = true);

public sealed record CraftingOpportunity(
    string Id, CraftingRecipe Recipe, string OutputName, string? OutputIconUrl,
    CraftingEconomics Economics, PlanCandidate? Candidate,
    IReadOnlyList<CraftingOpportunityExclusion> Exclusions,
    IReadOnlyList<string> ProcurementExplanation,
    bool IsActionable,
    int ConfidenceBasisPoints = 0,
    IReadOnlyList<string>? EvidenceExplanation = null);

public sealed record CraftingPlannerResult(
    CraftingOpportunityState State,
    IReadOnlyList<CraftingOpportunity> Opportunities,
    IReadOnlyList<CraftingSearchTruncationReason> TruncationReasons,
    IReadOnlyList<CraftingOpportunityExclusion> SummaryExclusions,
    string? EvidenceFailureCode = null);

public interface ICraftingOpportunityPlanner
{
    CraftingPlannerResult Plan(CraftingPlannerInput input);
}

/// <summary>
/// Bounded, deterministic recipe-path evaluator. It intentionally considers only
/// complete market/account evidence and never treats an unexplored branch as a
/// negative profitability result.
/// </summary>
public sealed class CraftingOpportunityPlanner(ICraftingEconomicsCalculator economics) : ICraftingOpportunityPlanner
{
    private readonly ICraftingEconomicsCalculator economics = economics ?? throw new ArgumentNullException(nameof(economics));
    private readonly OrderBookExecutionSimulator executions = new();

    public CraftingPlannerResult Plan(CraftingPlannerInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Limits.Validate();
        if (!input.AccountEvidenceFresh)
            return new(CraftingOpportunityState.Degraded, [], [], [CraftingOpportunityExclusion.StaleEvidence]);
        if (input.Disciplines.Count == 0)
            return new(CraftingOpportunityState.Degraded, [], [], [CraftingOpportunityExclusion.CapabilityUnavailable]);

        var truncation = new HashSet<CraftingSearchTruncationReason>();
        var recipes = input.Recipes.OrderBy(recipe => recipe.RecipeId).ToArray();
        if (recipes.Length > input.Limits.MaximumRecipes)
        {
            recipes = recipes.Take(input.Limits.MaximumRecipes).ToArray();
            truncation.Add(CraftingSearchTruncationReason.RecipeLimit);
        }
        var byOutput = recipes.GroupBy(recipe => recipe.OutputItemId).ToDictionary(group => group.Key, group => group.OrderBy(recipe => recipe.RecipeId).ToArray());
        var work = 0;
        var memo = new Dictionary<SearchKey, IngredientPath>(SearchKeyComparer.Instance);
        var raw = new List<CraftingOpportunity>();
        foreach (var recipe in recipes)
        {
            if (++work > input.Limits.MaximumWork) { truncation.Add(CraftingSearchTruncationReason.WorkLimit); break; }
            if (!IsEligible(recipe, input)) continue;
            raw.Add(EvaluateRecipe(recipe, input, byOutput, memo, new HashSet<int>(), 0, ref work, truncation));
        }
        var ordered = raw.OrderByDescending(value => value.Economics.NetProfit?.Copper ?? long.MinValue)
            .ThenBy(value => value.Recipe.OutputItemId).ThenBy(value => value.Recipe.RecipeId).ToArray();
        var actionable = ordered.Where(value => value.IsActionable).ToArray();
        if (actionable.Length > input.Limits.MaximumCandidates)
        {
            truncation.Add(CraftingSearchTruncationReason.CandidateLimit);
            var allowed = actionable.Take(input.Limits.MaximumCandidates).Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
            ordered = ordered.Select(value => value.IsActionable && !allowed.Contains(value.Id)
                ? value with { Candidate = null, IsActionable = false, Exclusions = value.Exclusions.Append(CraftingOpportunityExclusion.ResourceConflict).ToArray() }
                : value).ToArray();
        }
        var state = ordered.Any(value => value.IsActionable) ? CraftingOpportunityState.Ready : CraftingOpportunityState.NoOpportunities;
        var visible = ordered.Where(value => value.IsActionable).Concat(ordered.Where(value => !value.IsActionable)).Take(input.Limits.MaximumCandidates).ToArray();
        return new(state, visible, truncation.OrderBy(value => value).ToArray(),
            ordered.SelectMany(value => value.Exclusions).Distinct().OrderBy(value => value).ToArray());
    }

    private CraftingOpportunity EvaluateRecipe(CraftingRecipe recipe, CraftingPlannerInput input,
        IReadOnlyDictionary<int, CraftingRecipe[]> byOutput, IDictionary<SearchKey, IngredientPath> memo, ISet<int> stack,
        int depth, ref int work, ISet<CraftingSearchTruncationReason> truncation)
    {
        var exclusions = new HashSet<CraftingOpportunityExclusion>();
        if (!input.Markets.TryGetValue(recipe.OutputItemId, out var output) || !output.IsFresh)
            exclusions.Add(CraftingOpportunityExclusion.StaleEvidence);
        if (output is null || !output.HasSufficientHistory || output.ConfidenceBasisPoints < 8_000) exclusions.Add(CraftingOpportunityExclusion.WeakHistory);
        if (output is not null && !CanLiquidate(output.Listing, recipe.OutputItemCount)) exclusions.Add(CraftingOpportunityExclusion.InsufficientOutputDepth);
        var ingredients = new List<CraftingIngredientEconomicsInput>();
        var steps = new List<PlanStep>();
        var requirements = new List<PlanResourceRequirement>();
        var explanation = new List<string>();
        foreach (var ingredient in recipe.Ingredients.Where(value => string.Equals(value.Type, "Item", StringComparison.Ordinal)).OrderBy(value => value.Id))
        {
            var path = FindIngredient(ingredient.Id, ingredient.Count, input, byOutput, memo, stack, depth + 1, ref work, truncation);
            ingredients.Add(path.Input);
            exclusions.UnionWith(path.Exclusions);
            steps.AddRange(path.Steps);
            requirements.AddRange(path.Requirements);
            explanation.AddRange(path.Explanation);
        }
        if (ingredients.Count != recipe.Ingredients.Count || ingredients.Count == 0) exclusions.Add(CraftingOpportunityExclusion.InvalidRecipe);
        var unitPrice = output is null ? null : BestSell(output.Listing);
        var result = economics.Calculate(new CraftingEconomicsInput(recipe.OutputItemId, recipe.OutputItemCount, unitPrice, ingredients));
        if (!result.IsProfitable) exclusions.Add(CraftingOpportunityExclusion.NotProfitable);
        if (result.Uncertainties.Contains(CraftingEconomicsUncertainty.InsufficientMarketDepth)) exclusions.Add(CraftingOpportunityExclusion.InsufficientInputDepth);
        if (result.State != CraftingEconomicsState.Available) exclusions.Add(CraftingOpportunityExclusion.MissingInputEvidence);
        var actionable = exclusions.Count == 0;
        PlanCandidate? candidate = null;
        if (actionable && output is not null && result.NetProfit is { } profit && result.TotalCost is { } totalCost)
        {
            var craftId = $"craft:{recipe.RecipeId}";
            foreach (var ingredient in result.Ingredients.OrderBy(value => value.ItemId))
            {
                if (ingredient.OwnedTradableQuantity > 0)
                    requirements.Add(new(PlanResourceKind.Inventory, ingredient.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture), ingredient.OwnedTradableQuantity, Money.Zero));
                if (ingredient.Acquisition is not { } acquisition || !input.Markets.TryGetValue(ingredient.ItemId, out var inputMarket)) continue;
                if (acquisition.Strategy != CraftingAcquisitionStrategy.CraftedIntermediate)
                    requirements.Add(new(PlanResourceKind.Cash, "cash", 0, acquisition.TotalCost));
                var stepId = $"{craftId}:acquire:{ingredient.ItemId}";
                if (acquisition.Strategy == CraftingAcquisitionStrategy.BuyOrder)
                {
                    Money? unit = acquisition.ExecutionEvidence is { RequestedQuantity: > 0 } evidence
                        ? new Money(evidence.TotalValue.Copper / evidence.RequestedQuantity) : null;
                    steps.Add(new(stepId, PlanStepAction.PlaceBuyOrder, ingredient.ItemId, inputMarket.Item.Name, acquisition.Quantity, unit, [], PlanStepState.Pending));
                }
                else if (acquisition.Strategy == CraftingAcquisitionStrategy.InstantBuy && acquisition.ExecutionEvidence?.SourceScenario is { } scenario)
                {
                    var prior = steps.Select(step => step.Id).ToArray();
                    var index = 0;
                    foreach (var fill in scenario.Fills)
                    {
                        var fillId = $"{stepId}:{++index}";
                        steps.Add(new(fillId, PlanStepAction.BuyNow, ingredient.ItemId, inputMarket.Item.Name, fill.Quantity, fill.UnitPrice, prior, PlanStepState.Pending));
                        prior = [fillId];
                    }
                }
            }
            var passive = steps.Any(step => step.Action == PlanStepAction.PlaceBuyOrder);
            if (passive)
            {
                var passiveSteps = steps.Where(step => step.Action == PlanStepAction.PlaceBuyOrder).ToArray();
                candidate = new(craftId, 1, craftId, PlanAttention.Passive, passiveSteps, requirements, profit, totalCost, output.ConfidenceBasisPoints, 0,
                    checked(passiveSteps.Length * 35), checked(profit.Copper * 1_000 / Math.Max(1, totalCost.Copper)), true, []);
                return new($"craft:{recipe.RecipeId}", recipe, output.Item.Name, output.Item.IconUrl, result, candidate,
                    exclusions.OrderBy(value => value).ToArray(), explanation.Distinct(StringComparer.Ordinal).ToArray(), true,
                    output.ConfidenceBasisPoints, ["Historique de marché et profondeur vérifiés avant la proposition."]);
            }
            var craftStepId = $"{craftId}:craft";
            steps.Add(new(craftStepId, PlanStepAction.Craft, recipe.OutputItemId, output.Item.Name, recipe.OutputItemCount, null, steps.Select(step => step.Id).ToArray(), PlanStepState.Pending,
                CraftEffects: CraftEffects(recipe, result)));
            var listId = $"{craftId}:list";
            steps.Add(new(listId, PlanStepAction.List, recipe.OutputItemId, output.Item.Name, recipe.OutputItemCount, unitPrice, [craftStepId], PlanStepState.Pending));
            requirements.Add(new(PlanResourceKind.Cash, "cash", 0, result.OutputSale!.ListingFee));
            requirements.Add(new(PlanResourceKind.ExpectedIncoming, recipe.OutputItemId.ToString(System.Globalization.CultureInfo.InvariantCulture), recipe.OutputItemCount, Money.Zero));
            candidate = new(craftId, 1, craftId, PlanAttention.Active, steps, requirements, profit, totalCost, output.ConfidenceBasisPoints, 0,
                checked(steps.Count * 45), checked(profit.Copper * 1_000 / Math.Max(1, totalCost.Copper)), true, []);
        }
        return new($"craft:{recipe.RecipeId}", recipe, output?.Item.Name ?? $"Objet {recipe.OutputItemId}", output?.Item.IconUrl, result, candidate,
            exclusions.OrderBy(value => value).ToArray(), explanation.Distinct(StringComparer.Ordinal).ToArray(), actionable,
            output?.ConfidenceBasisPoints ?? 0, output is null ? ["Les preuves de marché de sortie sont indisponibles."] : ["Historique de marché et profondeur vérifiés avant la proposition."]);
    }

    private IngredientPath FindIngredient(int itemId, int quantity, CraftingPlannerInput input, IReadOnlyDictionary<int, CraftingRecipe[]> byOutput,
        IDictionary<SearchKey, IngredientPath> memo, ISet<int> stack, int depth, ref int work, ISet<CraftingSearchTruncationReason> truncation)
    {
        var key = new SearchKey(itemId, quantity, depth);
        // Recursive eligibility depends on the current ancestor stack. Cache
        // only root calls, where there is no ancestor-specific exclusion.
        var cacheable = stack.Count == 0;
        if (cacheable && memo.TryGetValue(key, out var known)) return known;
        if (depth > input.Limits.MaximumDepth) { truncation.Add(CraftingSearchTruncationReason.DepthLimit); return IngredientPath.Unavailable(itemId, quantity, CraftingOpportunityExclusion.MissingInputEvidence); }
        if (!stack.Add(itemId)) return IngredientPath.Unavailable(itemId, quantity, CraftingOpportunityExclusion.CycleDetected);
        try
        {
            var owned = input.Owned.TryGetValue(itemId, out var value) ? value : new([], null);
            var tradableQuantity = Math.Min(quantity, owned.Materials.Where(material => material.State == CraftingOwnedMaterialState.Tradable).Sum(material => material.Quantity));
            var remainingAfterOwned = RemainingAfterOwned(quantity, owned.Materials);
            var exactLiquidation = tradableQuantity > 0 && input.Markets.TryGetValue(itemId, out var liquidationMarket)
                ? CraftingExecutionEvidence.FromOrderBookExecution(executions.SimulateLiquidation(ToLevels(liquidationMarket.Listing.Buys), tradableQuantity))
                : null;
            var alternatives = new List<CraftingAcquisitionAlternative>();
            input.Markets.TryGetValue(itemId, out var market);
            if (remainingAfterOwned > 0 && market is { IsFresh: true })
            {
                var acquisition = executions.SimulateAcquisition(ToLevels(market.Listing.Sells), remainingAfterOwned);
                alternatives.Add(CraftingAcquisitionAlternative.FromExecution(CraftingAcquisitionStrategy.InstantBuy, CraftingExecutionEvidence.FromOrderBookExecution(acquisition)));
                var bestBid = market.Listing.Buys.Where(level => level.Quantity > 0 && level.UnitPriceInCopper > 0)
                    .Select(level => level.UnitPriceInCopper).DefaultIfEmpty().Max();
                if (bestBid > 0 && bestBid < int.MaxValue)
                    alternatives.Add(CraftingAcquisitionAlternative.FromExecution(CraftingAcquisitionStrategy.BuyOrder,
                        CraftingExecutionEvidence.ForBoundedBuyOrder(remainingAfterOwned, new Money(bestBid + 1))));
            }
            var direct = new CraftingIngredientEconomicsInput(itemId, quantity, owned.Materials, exactLiquidation, alternatives);
            var best = new IngredientPath(direct, [], [], [], []);
            if (remainingAfterOwned > 0 && byOutput.TryGetValue(itemId, out var recipes))
            {
                foreach (var recipe in recipes.Where(recipe => IsEligible(recipe, input)))
                {
                    if (++work > input.Limits.MaximumWork) { truncation.Add(CraftingSearchTruncationReason.WorkLimit); break; }
                    if (remainingAfterOwned % recipe.OutputItemCount != 0) continue; // never create hidden surplus inventory
                    var batches = remainingAfterOwned / recipe.OutputItemCount;
                    var scaled = recipe with
                    {
                        OutputItemCount = remainingAfterOwned,
                        Ingredients = recipe.Ingredients.Select(ingredient => ingredient with { Count = checked(ingredient.Count * batches) }).ToArray(),
                    };
                    var inputs = new List<CraftingIngredientEconomicsInput>();
                    var craftSteps = new List<PlanStep>();
                    var craftRequirements = new List<PlanResourceRequirement>();
                    var branchFailed = false;
                    foreach (var ingredient in scaled.Ingredients.Where(value => string.Equals(value.Type, "Item", StringComparison.Ordinal)).OrderBy(value => value.Id))
                    {
                        var path = FindIngredient(ingredient.Id, ingredient.Count, input, byOutput, memo, stack, depth + 1, ref work, truncation);
                        if (path.Exclusions.Count > 0) { branchFailed = true; break; }
                        inputs.Add(path.Input);
                        craftSteps.AddRange(path.Steps);
                        craftRequirements.AddRange(path.Requirements);
                    }
                    if (branchFailed || inputs.Count != scaled.Ingredients.Count) continue; // a cyclic branch never poisons a usable direct fallback
                    // Intermediate cost is independent of whether the intermediate itself can be sold.
                    // A nominal output price is used solely to obtain the calculator's exact input-cost
                    // accounting; it is never surfaced as a sale opportunity or plan step.
                    var intermediateEconomics = economics.Calculate(new CraftingEconomicsInput(itemId, remainingAfterOwned, new Money(1), inputs));
                    if (intermediateEconomics.EconomicInputCost is not { } cost || intermediateEconomics.State != CraftingEconomicsState.Available) continue;
                    foreach (var ingredient in intermediateEconomics.Ingredients.OrderBy(value => value.ItemId))
                    {
                        if (ingredient.OwnedTradableQuantity > 0)
                            craftRequirements.Add(new(PlanResourceKind.Inventory, ingredient.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture), ingredient.OwnedTradableQuantity, Money.Zero));
                        if (ingredient.Acquisition is not { } acquisition || !input.Markets.TryGetValue(ingredient.ItemId, out var inputMarket)) continue;
                        if (acquisition.Strategy != CraftingAcquisitionStrategy.CraftedIntermediate)
                            craftRequirements.Add(new(PlanResourceKind.Cash, "cash", 0, acquisition.TotalCost));
                        if (acquisition.Strategy == CraftingAcquisitionStrategy.InstantBuy && acquisition.ExecutionEvidence?.SourceScenario is { } scenario)
                        {
                            var prior = craftSteps.Select(step => step.Id).ToArray();
                            var index = 0;
                            foreach (var fill in scenario.Fills)
                            {
                                var fillId = $"craft:{recipe.RecipeId}:acquire:{ingredient.ItemId}:{++index}";
                                craftSteps.Add(new(fillId, PlanStepAction.BuyNow, ingredient.ItemId, inputMarket.Item.Name, fill.Quantity, fill.UnitPrice, prior, PlanStepState.Pending));
                                prior = [fillId];
                            }
                        }
                        else if (acquisition.Strategy == CraftingAcquisitionStrategy.BuyOrder && acquisition.ExecutionEvidence is { RequestedQuantity: > 0 } evidence)
                        {
                            var unitPrice = new Money(evidence.TotalValue.Copper / evidence.RequestedQuantity);
                            craftSteps.Add(new($"craft:{recipe.RecipeId}:order:{ingredient.ItemId}", PlanStepAction.PlaceBuyOrder, ingredient.ItemId,
                                inputMarket.Item.Name, acquisition.Quantity, unitPrice, craftSteps.Select(step => step.Id).ToArray(), PlanStepState.Pending));
                        }
                    }
                    var withIntermediate = direct with { AcquisitionAlternatives = alternatives.Append(CraftingAcquisitionAlternative.FromCraftedIntermediate(remainingAfterOwned, cost)).ToArray() };
                    var chosen = economics.Calculate(new CraftingEconomicsInput(itemId, quantity, new Money(1), [withIntermediate]));
                    if (chosen.EconomicInputCost is { } chosenCost &&
                        chosen.Ingredients.Single().Acquisition?.Strategy == CraftingAcquisitionStrategy.CraftedIntermediate &&
                        (best.CraftedCost is null || chosenCost.Copper < best.CraftedCost.Value.Copper))
                    {
                        var craftId = $"craft:{recipe.RecipeId}:intermediate:{remainingAfterOwned}";
                        craftSteps.Add(new(craftId, PlanStepAction.Craft, itemId, market?.Item.Name ?? $"Objet {itemId}", remainingAfterOwned, null,
                            craftSteps.Select(step => step.Id).ToArray(), PlanStepState.Pending, CraftEffects: CraftEffects(scaled, intermediateEconomics)));
                        best = new(withIntermediate, craftSteps, craftRequirements, ["Un intermédiaire fabriqué a été retenu car son coût complet est inférieur."], [], chosenCost);
                    }
                }
            }
            if (cacheable) memo[key] = best;
            return best;
        }
        finally { stack.Remove(itemId); }
    }

    private static bool IsEligible(CraftingRecipe recipe, CraftingPlannerInput input) =>
        input.UnlockedRecipeIds.Contains(recipe.RecipeId) && recipe.OutputItemId > 0 && recipe.OutputItemCount > 0 && recipe.Ingredients.Count > 0 &&
        recipe.Disciplines.Any(discipline => input.Disciplines.Any(capability => string.Equals(capability.Discipline, discipline, StringComparison.OrdinalIgnoreCase) && capability.Rating >= recipe.MinRating));
    private static bool CanLiquidate(MarketListing listing, int quantity) => new OrderBookExecutionSimulator().SimulateLiquidation(ToLevels(listing.Buys), quantity).IsFullyFilled;
    private static Money? BestSell(MarketListing? listing) => listing?.Sells.Where(level => level.UnitPriceInCopper > 0).OrderBy(level => level.UnitPriceInCopper).Select(level => new Money(level.UnitPriceInCopper)).FirstOrDefault();
    private static IReadOnlyList<OrderBookLevel> ToLevels(IEnumerable<MarketOrderLevel> levels) => levels.Where(level => level.Quantity > 0 && level.UnitPriceInCopper > 0).Select(level => new OrderBookLevel(level.Quantity, new Money(level.UnitPriceInCopper))).ToArray();
    private static int RemainingAfterOwned(int quantity, IReadOnlyList<CraftingOwnedMaterial> materials)
    {
        var remaining = quantity;
        foreach (var state in new[] { CraftingOwnedMaterialState.Tradable, CraftingOwnedMaterialState.Bound, CraftingOwnedMaterialState.Unknown })
            foreach (var material in materials.Where(material => material.State == state))
            {
                remaining -= Math.Min(remaining, material.Quantity);
                if (remaining == 0) return 0;
            }
        return remaining;
    }
    private static IReadOnlyList<PlanResourceRequirement> CraftEffects(CraftingRecipe recipe, CraftingEconomics economics) =>
        economics.Ingredients.Select(ingredient => new PlanResourceRequirement(PlanResourceKind.Inventory,
                ingredient.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture), -ingredient.RequiredQuantity, Money.Zero))
            .Append(new PlanResourceRequirement(PlanResourceKind.Inventory, recipe.OutputItemId.ToString(System.Globalization.CultureInfo.InvariantCulture), recipe.OutputItemCount, Money.Zero)).ToArray();

    private sealed record SearchKey(int ItemId, int Quantity, int Depth);
    private sealed class SearchKeyComparer : IEqualityComparer<SearchKey> { public static SearchKeyComparer Instance { get; } = new(); public bool Equals(SearchKey? x, SearchKey? y) => x == y; public int GetHashCode(SearchKey value) => value.GetHashCode(); }
    private sealed record IngredientPath(CraftingIngredientEconomicsInput Input, IReadOnlyList<PlanStep> Steps, IReadOnlyList<PlanResourceRequirement> Requirements, IReadOnlyList<string> Explanation, IReadOnlyList<CraftingOpportunityExclusion> Exclusions, Money? CraftedCost = null)
    { public static IngredientPath Unavailable(int itemId, int quantity, CraftingOpportunityExclusion exclusion) => new(new(itemId, quantity, [], null, []), [], [], [], [exclusion]); }
}
