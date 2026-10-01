using System.Net;
using System.Text.Json;
using Gw2Tp.Application.AccountEvidence;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Infrastructure.AccountEvidence;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed partial class AccountHoldingsGatewayTests
{
    private const string EncodedActor = "/v2/characters/A%C3%A9%20%2F%20One";

    [Fact]
    public async Task Collects_per_actor_tuples_all_tabs_armory_and_recipe_coverage_separately()
    {
        var payloads = Payloads();
        payloads["/v2/account/recipes"] = "[2,1]";
        payloads[EncodedActor + "/crafting"] = "{\"crafting\":[{\"discipline\":\"Artificer\",\"rating\":500,\"active\":false}]}";
        payloads["/v2/characters/Zed/crafting"] = "{\"crafting\":[{\"discipline\":\"Artificer\",\"rating\":100,\"active\":true}]}";
        payloads[EncodedActor + "/equipment"] = "{\"equipment\":[{\"id\":910010,\"location\":\"EquippedFromLegendaryArmory\",\"slot\":\"Coat\",\"tabs\":[1,2],\"binding\":\"Character\",\"bound_to\":\"Aé / One\",\"infusions\":[910090]}, {\"id\":910011,\"count\":2,\"location\":\"LegendaryArmory\",\"tabs\":[2]}]}";
        payloads[EncodedActor + "/equipmenttabs"] = "[2,1]";
        payloads[EncodedActor + "/equipmenttabs?tabs=all"] = "[{\"tab\":2,\"is_active\":false,\"equipment\":[{\"id\":910012,\"location\":\"Armory\",\"upgrades\":[910091]}]}, {\"tab\":1,\"is_active\":true,\"equipment\":[]}]";
        using var handler = FixtureHandler(payloads);
        var capture = Capture(await Gateway(handler).CollectAsync(Evaluation));
        var first = capture.Characters[0];
        var second = capture.Characters[1];
        Assert.Equal(new CraftingDisciplineCapability("Artificer", 500, false), Assert.Single(first.Crafting.Value!));
        Assert.Equal(new CraftingDisciplineCapability("Artificer", 100, true), Assert.Single(second.Crafting.Value!));
        Assert.Equal([1, 2], capture.RecipeUnlocks.Value);
        Assert.Equal([1, 2], first.EquipmentTabRoster.Value);
        Assert.Equal([1, 2], first.EquipmentTabs.Value!.Select(tab => tab.TabId));
        Assert.False(first.EquipmentTabs.Value![1].IsActive);
        Assert.Equal(EquipmentObservedLocation.LegendaryArmory, first.Equipment.Value![1].Location);
        Assert.Equal(2, first.Equipment.Value[1].UnlockCount);
        Assert.Null(first.Equipment.Value[1].Slot);
        Assert.Equal(first.Actor, first.Equipment.Value[0].BoundActor);
        Assert.Equal(910090, Assert.Single(first.Equipment.Value[0].AttachedComponents).ItemId);
        Assert.Equal(910091, Assert.Single(first.EquipmentTabs.Value[1].Equipment[0].AttachedComponents).ItemId);
        Assert.DoesNotContain(LooseItems(capture), row => row.ItemId is 910010 or 910011 or 910012 or 910090 or 910091);
        Assert.Equal(CraftingActorSelectionFailure.CapabilityUnavailable, new CraftingActorSelector().Select(capture,
            [new(1, 100, 1, ["Artificer"], 400, [], [new("Item", 1, 1)])]).Failure);
        Assert.Contains(handler.Requests, request => request.Path == EncodedActor + "/equipmenttabs" && request.Query.Contains("tabs=all", StringComparison.Ordinal));
        var intervals = new[] { first.Crafting.Fetch, first.Equipment.Fetch, first.EquipmentTabs.Fetch, capture.RecipeUnlocks.Fetch };
        Assert.All(intervals, interval => Assert.Null(interval.UpstreamObservedAtUtc));
        Assert.Equal(4, intervals.Select(interval => interval.CompletedAtUtc).Distinct().Count());
    }

    [Fact]
    public async Task Thirty_actors_are_bounded_during_new_child_reads_and_cancellation_stops_the_queue()
    {
        var payloads = Payloads();
        payloads["/v2/characters"] = JsonSerializer.Serialize(Enumerable.Range(0, 30).Select(index => $"Actor {index:D2}"));
        var fourCrafting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var active = 0;
        using var handler = new Handler(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/v2/characters/", StringComparison.Ordinal)) starts.Enqueue(path);
            if (path.EndsWith("/inventory", StringComparison.Ordinal) && path.StartsWith("/v2/characters/", StringComparison.Ordinal))
                return Json("{\"bags\":[]}");
            if (!path.EndsWith("/crafting", StringComparison.Ordinal)) return Json(PayloadFor(payloads, request.RequestUri));
            if (Interlocked.Increment(ref active) == 4) fourCrafting.TrySetResult();
            try { await release.Task.WaitAsync(token); return Json("{\"crafting\":[]}"); }
            finally { Interlocked.Decrement(ref active); }
        });
        using var cancellation = new CancellationTokenSource();
        var task = Gateway(handler).CollectAsync(Evaluation, cancellation.Token);
        await fourCrafting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, Volatile.Read(ref active));
        Assert.Equal(8, starts.Count); // exactly four inventories + four crafting reads
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(8, starts.Count);
        Assert.Equal(0, active);
    }

    [Theory]
    [InlineData("FutureArmory", null)]
    [InlineData(null, "FutureBinding")]
    public async Task Unknown_equipment_location_or_binding_retains_protective_ids_and_marks_partial(string? location, string? binding)
    {
        var payloads = Payloads();
        payloads[EncodedActor + "/equipment"] = JsonSerializer.Serialize(new { equipment = new[] { new { id = 910010, location, binding, upgrades = new[] { 910090 } } } });
        using var handler = FixtureHandler(payloads);
        var capture = Capture(await Gateway(handler).CollectAsync(Evaluation));
        var source = capture.Characters[0].Equipment;
        Assert.Equal(EvidenceCompleteness.Partial, source.Completeness);
        Assert.Equal(EvidenceAvailability.Available, source.Availability);
        Assert.Equal(910010, Assert.Single(source.Value!).ItemId);
        var result = new AccountHoldingsProtectionPolicy().Evaluate(capture,
            new(capture.AccountScope, new HashSet<int>(), new Dictionary<int, int>()), new Dictionary<int, HoldingsItemCategory>());
        Assert.Contains(910010, result.Floor.ItemIds);
        Assert.Contains(910090, result.Floor.ItemIds);
        Assert.False(result.HasCompleteEquipmentCoverage);
    }

    [Theory]
    [InlineData("[1,2]", "[{\"tab\":1,\"is_active\":true,\"equipment\":[]}]")]
    [InlineData("[1]", "[]")]
    [InlineData("[1,2]", "[{\"tab\":1,\"is_active\":true,\"equipment\":[]},{\"tab\":2,\"is_active\":true,\"equipment\":[]}]")]
    public async Task Active_tab_only_or_inconsistent_tab_set_never_proves_template_coverage(string roster, string tabs)
    {
        var payloads = Payloads();
        payloads[EncodedActor + "/equipmenttabs"] = roster;
        payloads[EncodedActor + "/equipmenttabs?tabs=all"] = tabs;
        using var handler = FixtureHandler(payloads);
        var capture = Capture(await Gateway(handler).CollectAsync(Evaluation));
        Assert.Equal(EvidenceCompleteness.Partial, capture.Characters[0].EquipmentTabs.Completeness);
        Assert.NotNull(capture.Characters[0].EquipmentTabs.Value);
        Assert.Equal(EvidenceCompleteness.Complete, capture.Characters[1].EquipmentTabs.Completeness);
    }

    [Fact]
    public async Task Missing_optional_equipment_grant_isolated_from_other_actors_and_retained_tabs()
    {
        using var handler = new Handler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == EncodedActor + "/equipment"
            ? new HttpResponseMessage(HttpStatusCode.Forbidden) : Json(PayloadFor(Payloads(), request.RequestUri))));
        var result = await Gateway(handler).CollectAsync(Evaluation);
        var capture = Capture(result);
        Assert.True(result.IsPartialData);
        Assert.Equal(EvidenceAvailability.MissingPermission, capture.Characters[0].Equipment.Availability);
        Assert.Equal(EvidenceCompleteness.Complete, capture.Characters[0].Inventory.Completeness);
        Assert.Equal(EvidenceCompleteness.Complete, capture.Characters[0].EquipmentTabs.Completeness);
        Assert.Equal(EvidenceCompleteness.Complete, capture.Characters[1].Equipment.Completeness);
    }

    [Theory]
    [InlineData("/crafting", "{\"crafting\":[{\"discipline\":\"Chef\",\"active\":true}]}")]
    [InlineData("/crafting", "{\"crafting\":[{\"discipline\":\"Chef\",\"rating\":-1,\"active\":true}]}")]
    [InlineData("/crafting", "{\"crafting\":[{\"discipline\":\"Chef\",\"rating\":10,\"active\":true},{\"discipline\":\"chef\",\"rating\":20,\"active\":false}]}")]
    [InlineData("/equipment", "{\"equipment\":[{\"id\":0,\"location\":\"Equipped\"}]}")]
    [InlineData("/equipment", "{\"equipment\":[{\"id\":10,\"count\":-1,\"location\":\"LegendaryArmory\"}]}")]
    [InlineData("/equipment", "{\"equipment\":[{\"id\":10,\"tabs\":[0]}]}")]
    [InlineData("/equipment", "{\"equipment\":[{\"id\":10,\"upgrades\":[-1]}]}")]
    [InlineData("/equipmenttabs", "[1,1]")]
    public async Task Malformed_new_sources_fail_conservatively_without_erasing_other_facts(string suffix, string payload)
    {
        var payloads = Payloads();
        payloads[EncodedActor + suffix] = payload;
        using var handler = FixtureHandler(payloads);
        var capture = Capture(await Gateway(handler).CollectAsync(Evaluation));
        var actor = capture.Characters[0];
        var error = suffix switch
        {
            "/crafting" => actor.Crafting.ErrorCategory,
            "/equipment" => actor.Equipment.ErrorCategory,
            _ => actor.EquipmentTabRoster.ErrorCategory,
        };
        Assert.Equal(Gw2ApiErrorCategory.InvalidPayload, error);
        Assert.Equal(EvidenceCompleteness.Complete, actor.Inventory.Completeness);
        Assert.Equal(EvidenceCompleteness.Complete, capture.Characters[1].Equipment.Completeness);
    }

    [Fact]
    public async Task Unknown_tab_roster_keeps_all_tab_references_as_a_partial_protective_floor()
    {
        var payloads = Payloads();
        payloads[EncodedActor + "/equipmenttabs?tabs=all"] = "[{\"tab\":1,\"is_active\":true,\"equipment\":[{\"id\":910010,\"location\":\"Armory\"}]}]";
        using var handler = new Handler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == EncodedActor + "/equipmenttabs" && !request.RequestUri.Query.Contains("tabs=all", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.Forbidden) : Json(PayloadFor(payloads, request.RequestUri))));
        var capture = Capture(await Gateway(handler).CollectAsync(Evaluation));
        Assert.Equal(EvidenceAvailability.MissingPermission, capture.Characters[0].EquipmentTabRoster.Availability);
        Assert.Equal(EvidenceCompleteness.Partial, capture.Characters[0].EquipmentTabs.Completeness);
        Assert.Equal(910010, Assert.Single(capture.Characters[0].EquipmentTabs.Value![0].Equipment).ItemId);
    }
}
