using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Plans;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class CraftingOpportunityPlannerTests
{
    private readonly CraftingOpportunityPlanner planner = new(new CraftingEconomicsCalculator());

    [Fact]
    public void Produces_a_deterministic_active_buy_craft_list_path_from_complete_depth_and_history()
    {
        var result = planner.Plan(Input([Recipe(1, 100, 1, (10, 1))], Markets((10, 100, 0), (100, 1_000, 1_000))));

        var opportunity = Assert.Single(result.Opportunities);
        Assert.Equal(CraftingOpportunityState.Ready, result.State);
        Assert.True(opportunity.IsActionable);
        Assert.Equal(new[] { PlanStepAction.BuyNow, PlanStepAction.Craft, PlanStepAction.List }, opportunity.Candidate!.Steps.Select(step => step.Action));
        var craft = Assert.Single(opportunity.Candidate.Steps, step => step.Action == PlanStepAction.Craft);
        Assert.Equal(-1, Assert.Single(craft.CraftEffects!, effect => effect.ResourceId == "10").Quantity);
        Assert.Equal(1, Assert.Single(craft.CraftEffects!, effect => effect.ResourceId == "100").Quantity);
        Assert.Equal(750, opportunity.Economics.NetProfit!.Value.Copper); // independently: 1000 - 15% fees - 100 input
        Assert.Equal(opportunity.Economics.TotalCost!.Value, opportunity.Candidate.Requirements.Aggregate(Gw2Tp.Domain.Finance.Money.Zero, (sum, value) => sum + value.Cash));
    }

    [Fact]
    public void Uses_a_terminal_passive_buy_order_when_it_is_the_cheapest_complete_modeled_procurement()
    {
        var result = planner.Plan(Input([Recipe(1, 100, 1, (10, 1))], Markets((10, 100, 50), (100, 1_000, 1_000))));

        var candidate = Assert.Single(result.Opportunities).Candidate!;
        Assert.Equal(PlanAttention.Passive, candidate.Attention);
        Assert.Single(candidate.Steps);
        Assert.Equal(PlanStepAction.PlaceBuyOrder, candidate.Steps[0].Action);
    }

    [Fact]
    public void Selects_a_cheaper_crafted_intermediate_without_listing_it_before_the_final_craft()
    {
        var result = planner.Plan(Input(
            [Recipe(1, 100, 1, (10, 1)), Recipe(2, 10, 1, (20, 1))],
            Markets((20, 100, 0), (10, 1_000, 1_000), (100, 2_000, 2_000))));

        var final = Assert.Single(result.Opportunities, value => value.Recipe.RecipeId == 1).Candidate!;
        Assert.Contains(final.Steps, step => step.Action == PlanStepAction.Craft && step.ItemId == 10);
        Assert.DoesNotContain(final.Steps, step => step.Action == PlanStepAction.List && step.ItemId == 10);
        Assert.Equal(200, final.Requirements.Where(value => value.Kind == PlanResourceKind.Cash).Sum(value => value.Cash.Copper));
    }

    [Fact]
    public void Falls_back_to_direct_acquisition_when_an_intermediate_branch_is_cyclic()
    {
        var result = planner.Plan(Input([Recipe(1, 100, 1, (10, 1)), Recipe(2, 10, 1, (100, 1))], Markets((10, 100, 0), (100, 1_000, 1_000))));

        var final = Assert.Single(result.Opportunities, value => value.Recipe.RecipeId == 1);
        Assert.True(final.IsActionable);
        Assert.DoesNotContain(final.ProcurementExplanation, line => line.Contains("intermédiaire", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scales_an_internal_recipe_to_the_exact_required_quantity_without_requiring_it_to_be_saleable()
    {
        var result = planner.Plan(Input(
            [Recipe(1, 100, 1, (10, 2)), Recipe(2, 10, 1, (20, 1))],
            Markets((20, 100, 0), (100, 2_000, 2_000))));

        var final = Assert.Single(result.Opportunities, value => value.Recipe.RecipeId == 1).Candidate!;
        var intermediateCraft = Assert.Single(final.Steps, step => step.Action == PlanStepAction.Craft && step.ItemId == 10);
        Assert.Equal(2, intermediateCraft.Quantity);
        Assert.DoesNotContain(final.Steps, step => step.Action == PlanStepAction.List && step.ItemId == 10);
    }

    [Fact]
    public void Values_only_the_consumed_subset_of_a_larger_owned_stack()
    {
        var owned = new Dictionary<int, CraftingOwnedEvidence>
        {
            [10] = new([new CraftingOwnedMaterial(10, CraftingOwnedMaterialState.Tradable)], null),
        };
        var result = planner.Plan(new CraftingPlannerInput([Recipe(1, 100, 1, (10, 1))], new HashSet<int> { 1 }, [new("Artificer", 500, true)],
            Markets((10, 100, 100), (100, 1_000, 1_000)), owned, CraftingPlannerLimits.Default));

        var ingredient = Assert.Single(Assert.Single(result.Opportunities).Economics.Ingredients);
        Assert.Equal(CraftingInputStrategy.Owned, ingredient.Strategy);
        Assert.Equal(1, ingredient.OwnedTradableQuantity);
        Assert.Equal(CraftingEconomicsState.Available, Assert.Single(result.Opportunities).Economics.State);
    }

    [Fact]
    public void Uses_owned_material_then_buys_only_the_missing_quantity()
    {
        var owned = new Dictionary<int, CraftingOwnedEvidence>
        {
            [10] = new([new CraftingOwnedMaterial(1, CraftingOwnedMaterialState.Tradable)], null),
        };
        var result = planner.Plan(new CraftingPlannerInput([Recipe(1, 100, 1, (10, 2))], new HashSet<int> { 1 }, [new("Artificer", 500, true)],
            Markets((10, 100, 90), (100, 1_000, 1_000)), owned, CraftingPlannerLimits.Default));

        var opportunity = Assert.Single(result.Opportunities);
        Assert.Equal(CraftingInputStrategy.Mixed, Assert.Single(opportunity.Economics.Ingredients).Strategy);
        Assert.Contains(opportunity.Candidate!.Steps, step => (step.Action is PlanStepAction.BuyNow or PlanStepAction.PlaceBuyOrder) && step.ItemId == 10 && step.Quantity == 1);
    }

    [Fact]
    public void Makes_the_root_passive_when_a_recursive_intermediate_needs_a_buy_order()
    {
        var result = planner.Plan(Input(
            [Recipe(1, 100, 1, (10, 1)), Recipe(2, 10, 1, (20, 1))],
            Markets((20, 100, 50), (10, 1_000, 1_000), (100, 2_000, 2_000))));

        var candidate = Assert.Single(result.Opportunities, value => value.Recipe.RecipeId == 1).Candidate!;
        Assert.Equal(PlanAttention.Passive, candidate.Attention);
        Assert.All(candidate.Steps, step => Assert.Equal(PlanStepAction.PlaceBuyOrder, step.Action));
    }

    [Fact]
    public void Does_not_surface_an_opportunity_below_the_minimum_history_confidence()
    {
        var markets = Markets((10, 100, 0), (100, 1_000, 1_000)).ToDictionary(pair => pair.Key, pair => pair.Value);
        markets[100] = markets[100] with { ConfidenceBasisPoints = 5_000 };

        var result = planner.Plan(Input([Recipe(1, 100, 1, (10, 1))], markets));

        var opportunity = Assert.Single(result.Opportunities);
        Assert.False(opportunity.IsActionable);
        Assert.Contains(CraftingOpportunityExclusion.WeakHistory, opportunity.Exclusions);
    }

    [Fact]
    public void Prioritizes_actionable_paths_over_higher_margin_excluded_recipes_when_display_is_bounded()
    {
        var limits = new CraftingPlannerLimits(8, 4, 1, 100);
        var result = planner.Plan(Input(
            [Recipe(1, 100, 1, (10, 1)), Recipe(2, 200, 1, (20, 1))],
            Markets((10, 100, 0, true), (20, 100, 0, true), (100, 1_000, 1_000, true), (200, 10_000, 10_000, false)), limits));

        Assert.Equal(100, Assert.Single(result.Opportunities).Recipe.OutputItemId);
        Assert.True(Assert.Single(result.Opportunities).IsActionable);
    }

    [Fact]
    public void Reports_depth_and_candidate_truncation_explicitly_with_stable_ties()
    {
        var limits = new CraftingPlannerLimits(8, 1, 1, 100);
        var recipes = new[] { Recipe(3, 300, 1, (30, 1)), Recipe(2, 200, 1, (20, 1)), Recipe(1, 100, 1, (10, 1)) };
        var result = planner.Plan(Input(recipes, Markets((10, 100, 0), (20, 100, 0), (30, 100, 0), (100, 1_000, 1_000), (200, 1_000, 1_000), (300, 1_000, 1_000)), limits));

        Assert.Contains(CraftingSearchTruncationReason.CandidateLimit, result.TruncationReasons);
        Assert.Equal(100, Assert.Single(result.Opportunities).Recipe.OutputItemId);
    }

    [Fact]
    public void Rejects_nominal_margin_when_output_history_or_depth_is_incomplete()
    {
        var markets = Markets((10, 100, 0, true), (100, 1_000, 1_000, false));
        var result = planner.Plan(Input([Recipe(1, 100, 2, (10, 1))], markets));

        var opportunity = Assert.Single(result.Opportunities);
        Assert.False(opportunity.IsActionable);
        Assert.Contains(CraftingOpportunityExclusion.WeakHistory, opportunity.Exclusions);
    }

    private static CraftingPlannerInput Input(IReadOnlyList<CraftingRecipe> recipes, IReadOnlyDictionary<int, CraftingMarketEvidence> markets, CraftingPlannerLimits? limits = null) =>
        new(recipes, recipes.Select(recipe => recipe.RecipeId).ToHashSet(), [new("Artificer", 500, true)], markets, new Dictionary<int, CraftingOwnedEvidence>(), limits ?? CraftingPlannerLimits.Default);

    private static CraftingRecipe Recipe(int id, int output, int count, params (int Item, int Count)[] ingredients) =>
        new(id, output, count, ["Artificer"], 1, [], ingredients.Select(value => new CraftingRecipeIngredient("Item", value.Item, value.Count)).ToArray());

    private static IReadOnlyDictionary<int, CraftingMarketEvidence> Markets(params (int Item, int Sell, int Buy, bool History)[] values) => values.ToDictionary(value => value.Item, value =>
        new CraftingMarketEvidence(new MarketListing(value.Item,
            value.Buy > 0 ? [new MarketOrderLevel(1, 10, value.Buy)] : [],
            value.Sell > 0 ? [new MarketOrderLevel(1, 10, value.Sell)] : []), new MarketItemMetadata(value.Item, $"Item {value.Item}", 250), true, value.History));

    private static IReadOnlyDictionary<int, CraftingMarketEvidence> Markets(params (int Item, int Sell, int Buy)[] values) =>
        Markets(values.Select(value => (value.Item, value.Sell, value.Buy, true)).ToArray());
}
