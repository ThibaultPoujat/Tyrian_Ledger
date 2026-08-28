using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Analytics.OwnedItems;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.Crafting;

/// <summary>
/// Searches a caller-supplied recipe graph deterministically. It performs no
/// I/O and leaves all external data acquisition to the typed gateway layer.
/// </summary>
public sealed class CraftingPathAnalyzer
{
    private readonly OwnedItemOpportunityCostCalculator ownedItemCalculator;
    private readonly FlipProfitCalculator profitCalculator;
    private readonly OrderBookExecutionSimulator orderBookExecutionSimulator = new();

    public CraftingPathAnalyzer(TransactionFeePolicy feePolicy)
    {
        ArgumentNullException.ThrowIfNull(feePolicy);
        ownedItemCalculator = new OwnedItemOpportunityCostCalculator(feePolicy);
        profitCalculator = new FlipProfitCalculator(feePolicy);
    }

    public CraftingPathAnalysis Analyze(CraftingPathAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var recipesByOutput = request.Recipes
            .GroupBy(recipe => recipe.OutputItemId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CraftingRecipe>)group.OrderBy(recipe => recipe.RecipeId).ToArray());
        var state = new SearchState(request, recipesByOutput);
        var candidates = new List<CraftingPathCandidate>();

        if (recipesByOutput.TryGetValue(request.TargetItemId, out var rootRecipes))
        {
            for (var recipeIndex = 0; recipeIndex < rootRecipes.Count; recipeIndex++)
            {
                var recipe = rootRecipes[recipeIndex];
                var eligibility = EvaluateEligibility(recipe, request.AccountCapabilities, state);
                if (!eligibility.IsUsable)
                {
                    continue;
                }

                var candidatesBeforeRecipe = candidates.Count;
                var rootSteps = state.ExpandRecipe(
                    recipe,
                    request.RequestedQuantity,
                    currentDepth: 1,
                    ancestors: []);
                for (var rootStepIndex = 0; rootStepIndex < rootSteps.Count; rootStepIndex++)
                {
                    if (candidates.Count >= request.SearchLimits.MaximumCandidatePaths)
                    {
                        state.Record(CraftingAnalysisReason.CandidateLimitReached);
                        break;
                    }

                    candidates.Add(CreateCandidate(rootSteps[rootStepIndex], request));
                }

                if (candidates.Count >= request.SearchLimits.MaximumCandidatePaths)
                {
                    if (recipeIndex < rootRecipes.Count - 1 ||
                        rootSteps.Count > candidates.Count - candidatesBeforeRecipe)
                    {
                        state.Record(CraftingAnalysisReason.CandidateLimitReached);
                    }

                    break;
                }
            }
        }

        var diagnosticReasons = state.Reasons.Order().ToArray();
        return new CraftingPathAnalysis(
            request.TargetItemId,
            request.RequestedQuantity,
            candidates.AsReadOnly(),
            new CraftingSearchDiagnostics(
                state.ExpandedRecipeCandidates,
                state.MemoizedSubproblemHits,
                diagnosticReasons.Any(reason => reason is CraftingAnalysisReason.DepthLimitReached or CraftingAnalysisReason.CandidateLimitReached),
                Array.AsReadOnly(diagnosticReasons)));
    }

    private CraftingPathCandidate CreateCandidate(CraftingPathStep rootStep, CraftingPathAnalysisRequest request)
    {
        var terminalDemand = new Dictionary<int, int>();
        var outputsForSale = new Dictionary<int, int>();
        CollectEconomicQuantities(rootStep, isRoot: true, terminalDemand, outputsForSale);

        var reasons = CollectPathReasons(rootStep);
        var ingredientCosts = new List<CraftingIngredientCostAnalysis>();
        var totalAcquisitionCost = Money.Zero;
        var hasCompleteIngredientCosts = true;
        foreach (var demand in terminalDemand.OrderBy(entry => entry.Key))
        {
            if (!request.MarketEvidenceByItem.TryGetValue(demand.Key, out var marketEvidence))
            {
                ingredientCosts.Add(new CraftingIngredientCostAnalysis(demand.Key, demand.Value, null, null));
                reasons.Add(CraftingAnalysisReason.MissingIngredientMarketEvidence);
                hasCompleteIngredientCosts = false;
                continue;
            }

            var ownedLots = request.OwnedLotsByItem.TryGetValue(demand.Key, out var suppliedLots)
                ? suppliedLots
                : [];
            var opportunityCost = ownedItemCalculator.Analyze(new OwnedItemOpportunityCostRequest(
                demand.Key,
                demand.Value,
                ownedLots,
                marketEvidence,
                OwnedItemValuationRoute.ImmediateLiquidation));
            var selectedStrategy = opportunityCost.Strategies
                .Where(strategy => strategy.IsAvailable)
                .OrderBy(strategy => strategy.TotalEconomicCost!.Value.Copper)
                .ThenBy(strategy => strategy.Strategy)
                .FirstOrDefault();
            ingredientCosts.Add(new CraftingIngredientCostAnalysis(
                demand.Key,
                demand.Value,
                opportunityCost,
                selectedStrategy));

            if (selectedStrategy?.TotalEconomicCost is not { } selectedCost)
            {
                reasons.Add(CraftingAnalysisReason.IngredientCostUnavailable);
                hasCompleteIngredientCosts = false;
                continue;
            }

            totalAcquisitionCost += selectedCost;
        }

        var outputSales = new List<CraftingOutputSaleAnalysis>();
        var grossSaleValue = Money.Zero;
        var totalListingFee = Money.Zero;
        var totalExchangeFee = Money.Zero;
        var netSaleProceeds = Money.Zero;
        var hasCompleteOutputSales = true;
        foreach (var output in outputsForSale.OrderBy(entry => entry.Key))
        {
            if (!request.MarketEvidenceByItem.TryGetValue(output.Key, out var marketEvidence))
            {
                outputSales.Add(new CraftingOutputSaleAnalysis(output.Key, output.Value, null, null));
                reasons.Add(CraftingAnalysisReason.MissingOutputMarketEvidence);
                hasCompleteOutputSales = false;
                continue;
            }

            var liquidation = orderBookExecutionSimulator.SimulateLiquidation(marketEvidence.BuyLevels, output.Value);
            if (!liquidation.IsFullyFilled || liquidation.Fills.Any(fill => fill.UnitPrice.Copper <= 0))
            {
                outputSales.Add(new CraftingOutputSaleAnalysis(output.Key, output.Value, liquidation, null));
                reasons.Add(CraftingAnalysisReason.InsufficientOutputLiquidationDepth);
                hasCompleteOutputSales = false;
                continue;
            }

            var sale = profitCalculator.Calculate(Money.Zero, liquidation.TotalValue);
            outputSales.Add(new CraftingOutputSaleAnalysis(output.Key, output.Value, liquidation, sale));
            grossSaleValue += sale.GrossSaleValue;
            totalListingFee += sale.ListingFee;
            totalExchangeFee += sale.ExchangeFee;
            netSaleProceeds += sale.NetSaleProceeds;
        }

        var profit = hasCompleteIngredientCosts && hasCompleteOutputSales
            ? new CraftingProfitScenario(
                totalAcquisitionCost,
                grossSaleValue,
                totalListingFee,
                totalExchangeFee,
                netSaleProceeds,
                netSaleProceeds - totalAcquisitionCost)
            : null;
        return new CraftingPathCandidate(
            rootStep.RecipeId!.Value,
            rootStep,
            ingredientCosts,
            outputSales,
            profit,
            reasons.Order().ToArray());
    }

    private static void CollectEconomicQuantities(
        CraftingPathStep step,
        bool isRoot,
        IDictionary<int, int> terminalDemand,
        IDictionary<int, int> outputsForSale)
    {
        if (step.Kind == CraftingPathStepKind.Purchase)
        {
            AddQuantity(terminalDemand, step.ItemId, step.RequiredQuantity);
            return;
        }

        if (isRoot)
        {
            AddQuantity(outputsForSale, step.ItemId, step.ProducedQuantity);
        }
        else if (step.ProducedQuantity > step.RequiredQuantity)
        {
            AddQuantity(outputsForSale, step.ItemId, step.ProducedQuantity - step.RequiredQuantity);
        }

        foreach (var ingredient in step.Ingredients)
        {
            CollectEconomicQuantities(ingredient, isRoot: false, terminalDemand, outputsForSale);
        }
    }

    private static HashSet<CraftingAnalysisReason> CollectPathReasons(CraftingPathStep step)
    {
        var reasons = new HashSet<CraftingAnalysisReason>(step.Reasons);
        foreach (var ingredient in step.Ingredients)
        {
            reasons.UnionWith(CollectPathReasons(ingredient));
        }

        return reasons;
    }

    private static void AddQuantity(IDictionary<int, int> quantities, int itemId, int quantity)
    {
        quantities.TryGetValue(itemId, out var existingQuantity);
        quantities[itemId] = checked(existingQuantity + quantity);
    }

    private static RecipeEligibility EvaluateEligibility(
        CraftingRecipe recipe,
        CraftingAccountCapabilities capabilities,
        SearchState state)
    {
        var warnings = new List<CraftingAnalysisReason>();
        switch (recipe.Availability)
        {
            case CraftingRecipeAvailability.RequiresAccountUnlock when !capabilities.HasVerifiedUnlockedRecipes:
                warnings.Add(CraftingAnalysisReason.RecipeAvailabilityUnknown);
                state.Record(CraftingAnalysisReason.RecipeAvailabilityUnknown);
                break;
            case CraftingRecipeAvailability.RequiresAccountUnlock when !capabilities.UnlockedRecipeIds.Contains(recipe.RecipeId):
                state.Record(CraftingAnalysisReason.RecipeUnavailable);
                return RecipeEligibility.Unusable;
            case CraftingRecipeAvailability.Unknown:
                warnings.Add(CraftingAnalysisReason.RecipeAvailabilityUnknown);
                state.Record(CraftingAnalysisReason.RecipeAvailabilityUnknown);
                break;
        }

        if (recipe.DisciplineRequirements.Count == 0)
        {
            return new RecipeEligibility(true, warnings);
        }

        if (!capabilities.HasVerifiedDisciplines)
        {
            warnings.Add(CraftingAnalysisReason.DisciplineAvailabilityUnknown);
            state.Record(CraftingAnalysisReason.DisciplineAvailabilityUnknown);
            return new RecipeEligibility(true, warnings);
        }

        var disciplineCanCraft = recipe.DisciplineRequirements.Any(requirement => capabilities.Disciplines.Any(capability =>
            capability.IsActive &&
            capability.Rating >= requirement.MinimumRating &&
            string.Equals(capability.Discipline, requirement.Discipline, StringComparison.OrdinalIgnoreCase)));
        if (!disciplineCanCraft)
        {
            state.Record(CraftingAnalysisReason.DisciplineRequirementNotMet);
            return RecipeEligibility.Unusable;
        }

        return new RecipeEligibility(true, warnings);
    }

    private sealed class SearchState
    {
        private readonly CraftingPathAnalysisRequest request;
        private readonly IReadOnlyDictionary<int, IReadOnlyList<CraftingRecipe>> recipesByOutput;
        private readonly Dictionary<ExpansionKey, IReadOnlyList<CraftingPathStep>> memoizedOptions = [];

        public SearchState(
            CraftingPathAnalysisRequest request,
            IReadOnlyDictionary<int, IReadOnlyList<CraftingRecipe>> recipesByOutput)
        {
            this.request = request;
            this.recipesByOutput = recipesByOutput;
        }

        public HashSet<CraftingAnalysisReason> Reasons { get; } = [];

        public int ExpandedRecipeCandidates { get; private set; }

        public int MemoizedSubproblemHits { get; private set; }

        public void Record(CraftingAnalysisReason reason) => Reasons.Add(reason);

        public IReadOnlyList<CraftingPathStep> ExpandRecipe(
            CraftingRecipe recipe,
            int requiredQuantity,
            int currentDepth,
            IReadOnlyCollection<int> ancestors)
        {
            if (!TryBeginRecipeExpansion())
            {
                return [];
            }

            var batchCount = CalculateBatchCount(requiredQuantity, recipe.OutputQuantity);
            var producedQuantity = checked(batchCount * recipe.OutputQuantity);
            var nextAncestors = ancestors.Append(recipe.OutputItemId).Distinct().Order().ToArray();
            var combinations = new List<IReadOnlyList<CraftingPathStep>> { Array.Empty<CraftingPathStep>() };

            foreach (var ingredient in recipe.Ingredients)
            {
                var requiredIngredientQuantity = checked(batchCount * ingredient.Quantity);
                var options = ExpandItem(
                    ingredient.ItemId,
                    requiredIngredientQuantity,
                    currentDepth,
                    nextAncestors);
                combinations = Combine(combinations, options);
                if (combinations.Count == 0)
                {
                    break;
                }
            }

            var eligibility = EvaluateEligibility(recipe, request.AccountCapabilities, this);
            return combinations.Select(combination => (CraftingPathStep)new CraftingPathStep(
                CraftingPathStepKind.Craft,
                recipe.OutputItemId,
                requiredQuantity,
                producedQuantity,
                batchCount,
                recipe.RecipeId,
                combination,
                eligibility.Warnings)).ToArray();
        }

        private IReadOnlyList<CraftingPathStep> ExpandItem(
            int itemId,
            int requiredQuantity,
            int currentDepth,
            IReadOnlyCollection<int> ancestors)
        {
            if (ancestors.Contains(itemId))
            {
                Record(CraftingAnalysisReason.CycleDetected);
                return [CreatePurchaseStep(itemId, requiredQuantity, [CraftingAnalysisReason.CycleDetected])];
            }

            var key = new ExpansionKey(
                itemId,
                requiredQuantity,
                currentDepth,
                string.Join(',', ancestors));
            if (memoizedOptions.TryGetValue(key, out var memoized))
            {
                MemoizedSubproblemHits++;
                return memoized;
            }

            var options = new List<CraftingPathStep> { CreatePurchaseStep(itemId, requiredQuantity, []) };
            if (!recipesByOutput.TryGetValue(itemId, out var recipes))
            {
                return Cache(key, options);
            }

            if (currentDepth >= request.SearchLimits.MaximumDepth)
            {
                Record(CraftingAnalysisReason.DepthLimitReached);
                options[0] = CreatePurchaseStep(itemId, requiredQuantity, [CraftingAnalysisReason.DepthLimitReached]);
                return Cache(key, options);
            }

            foreach (var recipe in recipes)
            {
                var eligibility = EvaluateEligibility(recipe, request.AccountCapabilities, this);
                if (!eligibility.IsUsable)
                {
                    continue;
                }

                var recipeSteps = ExpandRecipe(recipe, requiredQuantity, checked(currentDepth + 1), ancestors);
                foreach (var recipeStep in recipeSteps)
                {
                    if (options.Count >= request.SearchLimits.MaximumCandidatePaths + 1)
                    {
                        Record(CraftingAnalysisReason.CandidateLimitReached);
                        return Cache(key, options);
                    }

                    options.Add(recipeStep);
                }
            }

            return Cache(key, options);
        }

        private List<IReadOnlyList<CraftingPathStep>> Combine(
            IReadOnlyList<IReadOnlyList<CraftingPathStep>> left,
            IReadOnlyList<CraftingPathStep> right)
        {
            var result = new List<IReadOnlyList<CraftingPathStep>>();
            foreach (var prefix in left)
            {
                foreach (var suffix in right)
                {
                    if (result.Count >= request.SearchLimits.MaximumCandidatePaths)
                    {
                        Record(CraftingAnalysisReason.CandidateLimitReached);
                        return result;
                    }

                    result.Add(prefix.Append(suffix).ToArray());
                }
            }

            return result;
        }

        private bool TryBeginRecipeExpansion()
        {
            if (ExpandedRecipeCandidates >= request.SearchLimits.MaximumCandidatePaths)
            {
                Record(CraftingAnalysisReason.CandidateLimitReached);
                return false;
            }

            ExpandedRecipeCandidates++;
            return true;
        }

        private IReadOnlyList<CraftingPathStep> Cache(ExpansionKey key, IReadOnlyList<CraftingPathStep> options)
        {
            var cached = Array.AsReadOnly(options.ToArray());
            memoizedOptions.Add(key, cached);
            return cached;
        }

        private static CraftingPathStep CreatePurchaseStep(
            int itemId,
            int requiredQuantity,
            IReadOnlyList<CraftingAnalysisReason> reasons) =>
            new(
                CraftingPathStepKind.Purchase,
                itemId,
                requiredQuantity,
                requiredQuantity,
                batchCount: 0,
                recipeId: null,
                ingredients: [],
                reasons);

        private static int CalculateBatchCount(int requiredQuantity, int outputQuantity)
        {
            var batchCount = ((long)requiredQuantity + outputQuantity - 1) / outputQuantity;
            return checked((int)batchCount);
        }

        private readonly record struct ExpansionKey(
            int ItemId,
            int RequiredQuantity,
            int CurrentDepth,
            string AncestorSignature);
    }

    private sealed record RecipeEligibility(bool IsUsable, IReadOnlyList<CraftingAnalysisReason> Warnings)
    {
        public static RecipeEligibility Unusable { get; } = new(false, []);
    }
}
