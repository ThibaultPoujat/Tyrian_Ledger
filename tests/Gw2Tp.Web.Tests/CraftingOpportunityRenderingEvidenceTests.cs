using System.Text.Json;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Web.Hosting;
using Xunit;

namespace Gw2Tp.Web.Tests;

public sealed class CraftingOpportunityRenderingEvidenceTests
{
    [Fact]
    public void Real_planner_and_response_mapper_export_safe_capability_transition_vectors()
    {
        // The mixed legacy gateway regression establishes this actual active-100 tuple;
        // it cannot be combined with another actor's inactive-500 rating.
        Check("rejected-mixed", new("Artificer", 100, true), CraftingOpportunityState.NoOpportunities);
        Check("rejected-inactive", new("Artificer", 500, false), CraftingOpportunityState.NoOpportunities);
        Check("eligible-single-actor", new("Artificer", 500, true), CraftingOpportunityState.Ready);
    }

    private static void Check(string scenario, CraftingDisciplineCapability capability, CraftingOpportunityState expected)
    {
        var recipe = new CraftingRecipe(1, 100, 1, ["Artificer"], 400, [], [new("Item", 10, 1)]);
        var markets = new Dictionary<int, CraftingMarketEvidence>
        {
            [10] = Market(10, "Minerai de test", 100),
            [100] = Market(100, "Insigne de test", 1000),
        };
        var input = new CraftingPlannerInput([recipe], new HashSet<int> { 1 }, [capability], markets,
            new Dictionary<int, CraftingOwnedEvidence>(), CraftingPlannerLimits.Default);
        var result = new CraftingOpportunityPlanner(new CraftingEconomicsCalculator()).Plan(input);
        Assert.Equal(expected, result.State);
        if (expected == CraftingOpportunityState.Ready) Assert.True(Assert.Single(result.Opportunities).IsActionable);
        else Assert.Empty(result.Opportunities);
        var json = JsonSerializer.Serialize(CraftingOpportunityEndpoints.ToResponse(result), new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        using var payload = JsonDocument.Parse(json);
        Assert.Equal(expected.ToString(), payload.RootElement.GetProperty("state").GetString());
        Assert.Equal(expected == CraftingOpportunityState.Ready ? 1 : 0, payload.RootElement.GetProperty("opportunities").GetArrayLength());
        // Optional, explicit evidence export. Normal CI tests never write fixtures.
        var directory = Environment.GetEnvironmentVariable("TYRIAN_LEDGER_P02B_VISUAL_DIR");
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, scenario + ".json"), json + Environment.NewLine);
        }
    }

    private static CraftingMarketEvidence Market(int id, string name, int price) => new(
        new MarketListing(id, id == 100 ? [new MarketOrderLevel(1, 10, price)] : [], [new MarketOrderLevel(1, 10, price)]),
        new MarketItemMetadata(id, name, 250), true, true);
}
