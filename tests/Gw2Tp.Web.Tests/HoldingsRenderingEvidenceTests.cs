using System.Text.Json;
using Gw2Tp.Application.AccountEvidence;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.Plans;
using Gw2Tp.Domain.Finance;
using Gw2Tp.Testing;
using Gw2Tp.Web.Hosting;
using Xunit;
using static Gw2Tp.Testing.HoldingsEvidenceFixture;

namespace Gw2Tp.Web.Tests;

public sealed class HoldingsRenderingEvidenceTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [Fact]
    public void Safe_mapper_displays_the_real_instruction_actor_without_exposing_private_capture_payloads()
    {
        var now = DateTimeOffset.UtcNow;
        var projection = new AccountHoldingsProjector().Project(Snapshot(now, [Item(10, 6)]), now, Generation, Incarnation);
        var recipe = Recipe();
        var candidate = new PlanCandidate("craft:1", 1, "craft:1", PlanAttention.Active,
            [new("craft", PlanStepAction.Craft, 100, "Insigne de test", 1, null, [], PlanStepState.Pending,
                CraftEffects: [new(PlanResourceKind.Inventory, "10", -1, Money.Zero)], RecipeId: 1)],
            [new(PlanResourceKind.Inventory, "10", 1, Money.Zero)], Money.Zero, new Money(700), 8000, 0, 1, 1, true, []);
        candidate = PlanHoldingsAdmission.Authorize(candidate, projection, A.ActorId, [recipe]);
        var plan = new PlanOrchestrationService().Start(candidate, now);
        var moved = new AccountHoldingsProjector().Project(Snapshot(now.AddMilliseconds(1), [Item(10, 6, AccountHoldingsSource.MaterialStorage)]),
            now.AddMilliseconds(1), Generation, Incarnation);
        var paused = PlanHoldingsAdmission.Revalidate(plan, moved);
        Assert.Equal("holdings_source_changed", paused.HoldingsEligibilityReason);
        var response = new { state = "ready", proposals = Array.Empty<object>(), plans = new[] { PlanEndpointService.ToResponse(paused) },
            accountCacheScope = "synthetic-view-scope" };
        var json = JsonSerializer.Serialize(response, Json);
        using var payload = JsonDocument.Parse(json);
        var mapped = payload.RootElement.GetProperty("plans")[0];
        Assert.True(mapped.GetProperty("isExecutionPaused").GetBoolean());
        Assert.Contains(A.DisplayName, mapped.GetProperty("holdingsExplanation")[0].GetString());
        Assert.DoesNotContain("opaque-a", json);
        Assert.DoesNotContain("storeIncarnation", json);
        Export("paused-location", json);

        var domain = Environment.GetEnvironmentVariable("TYRIAN_LEDGER_P02C_DOMAIN_DIR");
        if (string.IsNullOrEmpty(domain)) return;
        foreach (var scenario in new[] { "bank", "own-bag", "other-bag", "bound-other", "protected", "partial-bank" })
        {
            var result = JsonSerializer.Deserialize<CraftingPlannerResult>(File.ReadAllText(Path.Combine(domain, scenario + ".json")), Json)!;
            var safe = JsonSerializer.Serialize(CraftingOpportunityEndpoints.ToResponse(result), Json);
            Assert.DoesNotContain("accountScope", safe);
            Assert.DoesNotContain("opaque-", safe);
            Assert.DoesNotContain("capture", safe, StringComparison.OrdinalIgnoreCase);
            Export(scenario, safe);
        }
    }

    private static void Export(string scenario, string json)
    {
        var directory = Environment.GetEnvironmentVariable("TYRIAN_LEDGER_P02C_VISUAL_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, scenario + ".json"), json + Environment.NewLine);
    }
}
