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
    public void Detects_indirect_cycles_without_treating_them_as_profitable_paths()
    {
        var result = planner.Plan(Input([Recipe(1, 100, 1, (10, 1)), Recipe(2, 10, 1, (100, 1))], Markets((10, 100, 0), (100, 1_000, 1_000))));

        Assert.Contains(result.Opportunities.SelectMany(value => value.Exclusions), value => value == CraftingOpportunityExclusion.CycleDetected);
        Assert.DoesNotContain(result.Opportunities, value => value.IsActionable && value.Recipe.RecipeId == 1 && value.ProcurementExplanation.Any(line => line.Contains("intermédiaire", StringComparison.OrdinalIgnoreCase)));
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
