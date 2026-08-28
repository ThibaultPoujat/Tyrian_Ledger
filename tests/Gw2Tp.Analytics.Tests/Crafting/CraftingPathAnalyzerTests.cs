using Gw2Tp.Analytics.Crafting;
using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Analytics.OwnedItems;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Analytics.Tests.Crafting;

public sealed class CraftingPathAnalyzerTests
{
    private readonly CraftingPathAnalyzer analyzer = new(new TransactionFeePolicy(
        new FeeRule(0, FeeRounding.Down),
        new FeeRule(0, FeeRounding.Down)));

    [Fact]
    public void Analyzes_a_simple_recipe_with_purchased_ingredients()
    {
        var analysis = analyzer.Analyze(Request(
            targetItemId: 10,
            recipes: [Recipe(100, 10, 1, [Ingredient(1, 2)])],
            marketEvidence: Evidence((1, 5, 10), (10, 100, 110))));

        var candidate = Assert.Single(analysis.Candidates);
        Assert.Equal(100, candidate.RootRecipeId);
        Assert.Equal(CraftingPathStepKind.Purchase, Assert.Single(candidate.RootStep.Ingredients).Kind);
        Assert.Equal(new Money(20), candidate.Profit!.AcquisitionCost);
        Assert.Equal(new Money(100), candidate.Profit.NetSaleProceeds);
        Assert.Equal(new Money(80), candidate.Profit.NetProfit);
        Assert.Equal(OwnedItemStrategy.BuyAll, Assert.Single(candidate.IngredientCosts).SelectedStrategy!.Strategy);
    }

    [Fact]
    public void Includes_a_multi_step_path_alongside_the_direct_purchase_fallback()
    {
        var analysis = analyzer.Analyze(Request(
            targetItemId: 10,
            recipes:
            [
                Recipe(100, 10, 1, [Ingredient(20, 1)]),
                Recipe(200, 20, 1, [Ingredient(1, 3)]),
            ],
            marketEvidence: Evidence((1, 5, 10), (20, 25, 30), (10, 100, 110))));

        var multiStepCandidate = Assert.Single(analysis.Candidates, candidate =>
            Assert.Single(candidate.RootStep.Ingredients).Kind == CraftingPathStepKind.Craft);
        var intermediate = Assert.Single(multiStepCandidate.RootStep.Ingredients);

        Assert.Equal(200, intermediate.RecipeId);
        Assert.Equal(new Money(30), multiStepCandidate.Profit!.AcquisitionCost);
        Assert.Equal(new Money(70), multiStepCandidate.Profit.NetProfit);
        Assert.Equal(2, analysis.Diagnostics.ExpandedRecipeCandidates);
    }

    [Fact]
    public void Detects_a_cycle_and_keeps_a_purchasable_fallback()
    {
        var analysis = analyzer.Analyze(Request(
            targetItemId: 10,
            recipes:
            [
                Recipe(100, 10, 1, [Ingredient(20, 1)]),
                Recipe(200, 20, 1, [Ingredient(10, 1)]),
            ],
            marketEvidence: Evidence((10, 100, 110), (20, 25, 30))));

        var cyclicCandidate = Assert.Single(analysis.Candidates, candidate =>
            Assert.Single(candidate.RootStep.Ingredients).Kind == CraftingPathStepKind.Craft);
        var cyclePurchase = Assert.Single(Assert.Single(cyclicCandidate.RootStep.Ingredients).Ingredients);

        Assert.Equal(CraftingPathStepKind.Purchase, cyclePurchase.Kind);
        Assert.Contains(CraftingAnalysisReason.CycleDetected, cyclePurchase.Reasons);
        Assert.Contains(CraftingAnalysisReason.CycleDetected, analysis.Diagnostics.Reasons);
        Assert.Contains(CraftingAnalysisReason.CycleDetected, cyclicCandidate.Reasons);
    }

    [Fact]
    public void Reports_depth_truncation_and_purchases_the_capped_intermediate()
    {
        var analysis = analyzer.Analyze(Request(
            targetItemId: 10,
            recipes:
            [
                Recipe(100, 10, 1, [Ingredient(20, 1)]),
                Recipe(200, 20, 1, [Ingredient(1, 1)]),
            ],
            marketEvidence: Evidence((1, 5, 10), (20, 25, 30), (10, 100, 110)),
            maximumDepth: 1));

        var candidate = Assert.Single(analysis.Candidates);
        var intermediate = Assert.Single(candidate.RootStep.Ingredients);

        Assert.Equal(CraftingPathStepKind.Purchase, intermediate.Kind);
        Assert.Contains(CraftingAnalysisReason.DepthLimitReached, intermediate.Reasons);
        Assert.True(analysis.Diagnostics.IsTruncated);
        Assert.Contains(CraftingAnalysisReason.DepthLimitReached, analysis.Diagnostics.Reasons);
    }

    [Fact]
    public void Returns_alternative_root_recipes_in_recipe_id_order()
    {
        var analysis = analyzer.Analyze(Request(
            targetItemId: 10,
            recipes:
            [
                Recipe(200, 10, 1, [Ingredient(2, 1)]),
                Recipe(100, 10, 1, [Ingredient(1, 1)]),
            ],
            marketEvidence: Evidence((1, 5, 10), (2, 5, 15), (10, 100, 110))));

        Assert.Equal([100, 200], analysis.Candidates.Select(candidate => candidate.RootRecipeId));
    }

    [Fact]
    public void Chooses_mixed_owned_and_purchased_ingredient_cost_without_treating_stock_as_free()
    {
        var analysis = analyzer.Analyze(Request(
            targetItemId: 10,
            recipes: [Recipe(100, 10, 1, [Ingredient(1, 5)])],
            marketEvidence: Evidence((1, 90, 100), (10, 1_000, 1_100)),
            ownedLots: new Dictionary<int, IReadOnlyList<OwnedItemLot>> { [1] = [new OwnedItemLot(2)] }));

        var candidate = Assert.Single(analysis.Candidates);
        var cost = Assert.Single(candidate.IngredientCosts);

        Assert.Equal(OwnedItemStrategy.Mixed, cost.SelectedStrategy!.Strategy);
        Assert.Equal(new Money(480), cost.SelectedStrategy.TotalEconomicCost);
        Assert.Equal(new Money(520), candidate.Profit!.NetProfit);
    }

    [Fact]
    public void Liquidates_target_and_intermediate_batch_surplus()
    {
        var analysis = analyzer.Analyze(Request(
            targetItemId: 10,
            recipes:
            [
                Recipe(100, 10, 1, [Ingredient(20, 1)]),
                Recipe(200, 20, 2, [Ingredient(1, 1)]),
            ],
            marketEvidence: Evidence((1, 5, 10), (20, 30, 40), (10, 100, 110))));

        var candidate = Assert.Single(analysis.Candidates, path =>
            Assert.Single(path.RootStep.Ingredients).Kind == CraftingPathStepKind.Craft);

        Assert.Equal(
            [
                (10, 1),
                (20, 1),
            ],
            candidate.OutputSales.Select(sale => (sale.ItemId, sale.Quantity)));
        Assert.Equal(new Money(10), candidate.Profit!.AcquisitionCost);
        Assert.Equal(new Money(130), candidate.Profit.GrossSaleValue);
        Assert.Equal(new Money(120), candidate.Profit.NetProfit);
    }

    [Fact]
    public void Liquidates_every_output_from_a_whole_target_batch()
    {
        var analysis = analyzer.Analyze(Request(
            targetItemId: 10,
            recipes: [Recipe(100, 10, 2, [Ingredient(1, 1)])],
            marketEvidence: Evidence((1, 5, 10), (10, 100, 110))));

        var candidate = Assert.Single(analysis.Candidates);

        Assert.Equal(2, Assert.Single(candidate.OutputSales).Quantity);
        Assert.Equal(new Money(200), candidate.Profit!.GrossSaleValue);
        Assert.Equal(new Money(190), candidate.Profit.NetProfit);
    }

    [Fact]
    public void Reports_candidate_limit_truncation()
    {
        var analysis = analyzer.Analyze(Request(
            targetItemId: 10,
            recipes:
            [
                Recipe(100, 10, 1, [Ingredient(1, 1)]),
                Recipe(200, 10, 1, [Ingredient(2, 1)]),
            ],
            marketEvidence: Evidence((1, 5, 10), (2, 5, 15), (10, 100, 110)),
            maximumCandidatePaths: 1));

        Assert.Equal([100], analysis.Candidates.Select(candidate => candidate.RootRecipeId));
        Assert.True(analysis.Diagnostics.IsTruncated);
        Assert.Contains(CraftingAnalysisReason.CandidateLimitReached, analysis.Diagnostics.Reasons);
    }

    [Fact]
    public void Memoizes_repeated_intermediate_subproblems()
    {
        var analysis = analyzer.Analyze(Request(
            targetItemId: 10,
            recipes:
            [
                Recipe(100, 10, 1, [Ingredient(20, 1)]),
                Recipe(101, 10, 1, [Ingredient(20, 1)]),
                Recipe(200, 20, 1, [Ingredient(1, 1)]),
            ],
            marketEvidence: Evidence((1, 5, 10), (20, 25, 30), (10, 100, 110))));

        Assert.True(analysis.Diagnostics.MemoizedSubproblemHits > 0);
    }

    [Fact]
    public void Applies_verified_recipe_and_active_discipline_constraints()
    {
        var lockedRecipe = Recipe(
            100,
            10,
            1,
            [Ingredient(1, 1)],
            CraftingRecipeAvailability.RequiresAccountUnlock,
            [new CraftingDisciplineRequirement("Artificer", 400)]);
        var locked = analyzer.Analyze(Request(
            10,
            [lockedRecipe],
            Evidence((1, 5, 10), (10, 100, 110)),
            capabilities: Capabilities(unlocked: [], disciplines: [new CraftingDisciplineCapability("Artificer", 500, true)])));
        var inactive = analyzer.Analyze(Request(
            10,
            [Recipe(101, 10, 1, [Ingredient(1, 1)], CraftingRecipeAvailability.AlwaysAvailable, [new CraftingDisciplineRequirement("Artificer", 400)])],
            Evidence((1, 5, 10), (10, 100, 110)),
            capabilities: Capabilities(unlocked: [], disciplines: [new CraftingDisciplineCapability("Artificer", 500, false)])));

        Assert.Empty(locked.Candidates);
        Assert.Contains(CraftingAnalysisReason.RecipeUnavailable, locked.Diagnostics.Reasons);
        Assert.Empty(inactive.Candidates);
        Assert.Contains(CraftingAnalysisReason.DisciplineRequirementNotMet, inactive.Diagnostics.Reasons);
    }

    [Fact]
    public void Retains_unknown_recipe_and_discipline_facts_as_explicit_warnings()
    {
        var analysis = analyzer.Analyze(Request(
            10,
            [Recipe(
                100,
                10,
                1,
                [Ingredient(1, 1)],
                CraftingRecipeAvailability.Unknown,
                [new CraftingDisciplineRequirement("Artificer", 400)])],
            Evidence((1, 5, 10), (10, 100, 110)),
            capabilities: Capabilities(
                hasVerifiedUnlockedRecipes: false,
                hasVerifiedDisciplines: false,
                unlocked: [],
                disciplines: [])));

        var candidate = Assert.Single(analysis.Candidates);
        Assert.Contains(CraftingAnalysisReason.RecipeAvailabilityUnknown, candidate.Reasons);
        Assert.Contains(CraftingAnalysisReason.DisciplineAvailabilityUnknown, candidate.Reasons);
        Assert.Contains(CraftingAnalysisReason.RecipeAvailabilityUnknown, analysis.Diagnostics.Reasons);
        Assert.Contains(CraftingAnalysisReason.DisciplineAvailabilityUnknown, analysis.Diagnostics.Reasons);
    }

    [Fact]
    public void Marks_a_candidate_unvalued_when_output_liquidation_evidence_is_missing()
    {
        var analysis = analyzer.Analyze(Request(
            10,
            [Recipe(100, 10, 1, [Ingredient(1, 1)])],
            Evidence((1, 5, 10))));

        var candidate = Assert.Single(analysis.Candidates);
        Assert.Null(candidate.Profit);
        Assert.Contains(CraftingAnalysisReason.MissingOutputMarketEvidence, candidate.Reasons);
    }

    [Fact]
    public void Marks_a_candidate_unvalued_when_output_depth_cannot_liquidate_the_full_batch()
    {
        var analysis = analyzer.Analyze(Request(
            10,
            [Recipe(100, 10, 2, [Ingredient(1, 1)])],
            new Dictionary<int, OwnedItemMarketEvidence>
            {
                [1] = new OwnedItemMarketEvidence([new OrderBookLevel(20, new Money(5))], [new OrderBookLevel(20, new Money(10))]),
                [10] = new OwnedItemMarketEvidence([new OrderBookLevel(1, new Money(100))], [new OrderBookLevel(20, new Money(110))]),
            }));

        var candidate = Assert.Single(analysis.Candidates);

        Assert.Null(candidate.Profit);
        Assert.Contains(CraftingAnalysisReason.InsufficientOutputLiquidationDepth, candidate.Reasons);
        Assert.False(Assert.Single(candidate.OutputSales).Liquidation!.IsFullyFilled);
    }

    [Fact]
    public void Validates_recipe_and_search_contracts()
    {
        Assert.Throws<ArgumentException>(() => Recipe(100, 10, 1, [Ingredient(1, 1), Ingredient(1, 2)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CraftingSearchLimits(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CraftingDisciplineCapability("Chef", -1, true));
    }

    private static CraftingPathAnalysisRequest Request(
        int targetItemId,
        IReadOnlyList<CraftingRecipe> recipes,
        IReadOnlyDictionary<int, OwnedItemMarketEvidence> marketEvidence,
        IReadOnlyDictionary<int, IReadOnlyList<OwnedItemLot>>? ownedLots = null,
        CraftingAccountCapabilities? capabilities = null,
        int maximumDepth = 4,
        int maximumCandidatePaths = 20) =>
        new(
            targetItemId,
            requestedQuantity: 1,
            recipes,
            marketEvidence,
            ownedLots ?? new Dictionary<int, IReadOnlyList<OwnedItemLot>>(),
            capabilities ?? Capabilities(),
            new CraftingSearchLimits(maximumDepth, maximumCandidatePaths));

    private static CraftingRecipe Recipe(
        int recipeId,
        int outputItemId,
        int outputQuantity,
        IReadOnlyList<CraftingIngredient> ingredients,
        CraftingRecipeAvailability availability = CraftingRecipeAvailability.AlwaysAvailable,
        IReadOnlyList<CraftingDisciplineRequirement>? requirements = null) =>
        new(recipeId, outputItemId, outputQuantity, ingredients, availability, requirements ?? []);

    private static CraftingIngredient Ingredient(int itemId, int quantity) => new(itemId, quantity);

    private static CraftingAccountCapabilities Capabilities(
        bool hasVerifiedUnlockedRecipes = true,
        bool hasVerifiedDisciplines = true,
        IReadOnlyList<int>? unlocked = null,
        IReadOnlyList<CraftingDisciplineCapability>? disciplines = null) =>
        new(
            hasVerifiedUnlockedRecipes,
            unlocked ?? [],
            hasVerifiedDisciplines,
            disciplines ?? []);

    private static IReadOnlyDictionary<int, OwnedItemMarketEvidence> Evidence(params (int ItemId, long Buy, long Sell)[] values) =>
        values.ToDictionary(
            value => value.ItemId,
            value => new OwnedItemMarketEvidence(
                [new OrderBookLevel(20, new Money(value.Buy))],
                [new OrderBookLevel(20, new Money(value.Sell))]));
}
