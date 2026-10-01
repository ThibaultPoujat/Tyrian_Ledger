using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Gw2Tp.Application.AccountEvidence;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Time;
using Gw2Tp.Infrastructure.AccountConnection;
using Gw2Tp.Infrastructure.AccountEvidence;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed partial class AccountHoldingsGatewayTests
{
    private const string SyntheticCredential = "synthetic-holdings-credential";
    private static readonly DateTimeOffset Evaluation = DateTimeOffset.Parse("2026-10-01T10:00:00Z");
    private const string CharacterInventory = "{\"bags\":[null,{\"id\":910000,\"size\":3,\"inventory\":[null,{\"id\":910001,\"count\":2,\"binding\":\"Character\",\"bound_to\":\"Aé / One\",\"upgrades\":[910090],\"infusions\":[910091]},null]}]}";

    [Fact]
    public async Task Collects_all_locations_without_crediting_containers_components_or_delivery_as_stock()
    {
        var payloads = Payloads();
        payloads["/v2/account/bank"] = "[null,{\"id\":910002,\"count\":5,\"binding\":\"Account\"}]";
        payloads["/v2/account/inventory"] = "[{\"id\":910003,\"count\":6},null]";
        payloads["/v2/account/materials"] = "[{\"id\":910004,\"count\":0,\"category\":5},{\"id\":910005,\"count\":4,\"category\":6},{\"id\":910005,\"count\":4,\"category\":5}]";
        payloads["/v2/commerce/delivery"] = "{\"coins\":250,\"items\":[{\"id\":910006,\"count\":3},{\"id\":910006,\"count\":4}]}";
        var source = new MutableKeySource();
        var scheduler = new RecordingScheduler();
        using var handler = new Handler((request, _) => Task.FromResult(Json(PayloadFor(payloads, request.RequestUri!))));
        using var client = Client(handler);
        var result = await new AccountHoldingsGateway(source, client, scheduler, new StepClock()).CollectAsync(Evaluation);

        var capture = Capture(result);
        Assert.False(result.IsPartialData);
        Assert.Equal("synthetic-holdings-account", capture.AccountScope.AccountId);
        Assert.NotEqual(Guid.Empty, capture.RefreshId);
        Assert.Equal(Evaluation, capture.EvaluatedAtUtc);
        Assert.Equal(new CharacterHoldingsCoverage(EvidenceCompleteness.Complete, 2, 2), capture.CharacterCoverage);
        var actor = capture.Roster.Value![0];
        Assert.Equal("Aé / One", actor.DisplayName);
        var inventory = capture.Characters[0].Inventory.Value!;
        var item = Assert.Single(inventory.Items);
        Assert.Equal(910001, item.ItemId);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(actor, item.BoundActor);
        Assert.Equal(AccountItemBinding.CharacterBound, item.Binding);
        Assert.Equal(1, item.Location.BagIndex);
        Assert.Equal(1, item.Location.SlotIndex);
        Assert.Equal(actor.ActorId, item.Location.ActorId);
        Assert.Equal(AccountHoldingsSource.CharacterInventory, item.Location.Source);
        Assert.Equal(new InstalledBagObservation(1, 910000, 3), Assert.Single(inventory.InstalledBags));
        Assert.Equal([910090, 910091], item.AttachedComponents.Select(component => component.ItemId));
        Assert.Equal(new EvidenceSourceCoverage(3, 3), capture.Characters[0].Inventory.Coverage);
        Assert.Equal(1, Assert.Single(capture.Bank.Value!.Items).Location.SlotIndex);
        Assert.Equal(AccountHoldingsSource.SharedInventory, Assert.Single(capture.SharedInventory.Value!.Items).Location.Source);
        Assert.Equal(0, capture.MaterialStorage.Value!.Items[0].Quantity);
        var duplicateMaterial = capture.MaterialStorage.Value.Items[1];
        Assert.Equal(4, duplicateMaterial.Quantity);
        Assert.Equal([5, 6], duplicateMaterial.Location.MaterialCategoryIds);
        Assert.Equal([1, 2], duplicateMaterial.Location.SourceRowIndices);
        Assert.Equal(250L, capture.Delivery.Value!.CoinsCopper);
        Assert.Equal([0, 1], capture.Delivery.Value.Items.Select(row => row.RowIndex));
        Assert.Equal(new ObservedDeliveryItemTotal(910006, 7), Assert.Single(capture.Delivery.Value.ObservedTotals));
        Assert.DoesNotContain(LooseItems(capture), value => value.ItemId is 910000 or 910090 or 910091 or 910006);
        Assert.Equal(1, source.ReadCount);
        Assert.Equal(1, handler.Requests.Count(request => request.Path == "/v2/account"));
        Assert.Contains(handler.Requests, request => request.Path == "/v2/characters/A%C3%A9%20%2F%20One/inventory");
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("Bearer", request.Scheme);
            Assert.Equal(SyntheticCredential, request.Credential);
            Assert.Contains($"v={AccountHoldingsGateway.SchemaVersion}", request.Query, StringComparison.Ordinal);
            if (request.Query.Contains("tabs=", StringComparison.Ordinal)) Assert.Contains("tabs=all", request.Query, StringComparison.Ordinal);
        });
        Assert.All(scheduler.Keys, key =>
        {
            Assert.DoesNotContain("Aé", key.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("One", key.Value, StringComparison.Ordinal);
            Assert.DoesNotContain(SyntheticCredential, key.Value, StringComparison.Ordinal);
            Assert.Equal("account-holdings", key.Operation);
        });
        Assert.DoesNotContain(SyntheticCredential, JsonSerializer.Serialize(capture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Actor_permission_failure_retains_success_and_degrades_aggregate_coverage()
    {
        using var handler = new Handler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/v2/characters/Zed/inventory"
            ? new HttpResponseMessage(HttpStatusCode.Forbidden) : Json(PayloadFor(Payloads(), request.RequestUri!))));
        var result = await Gateway(handler).CollectAsync(Evaluation);
        var capture = Capture(result);
        Assert.True(result.IsPartialData);
        Assert.Equal(EvidenceCompleteness.Complete, capture.Roster.Completeness);
        Assert.Equal(new CharacterHoldingsCoverage(EvidenceCompleteness.Partial, 2, 1), capture.CharacterCoverage);
        Assert.Single(capture.Characters[0].Inventory.Value!.Items);
        Assert.Equal(EvidenceAvailability.MissingPermission, capture.Characters[1].Inventory.Availability);
        Assert.Equal(Gw2ApiErrorCategory.Forbidden, capture.Characters[1].Inventory.ErrorCategory);
        Assert.Null(capture.Characters[1].Inventory.Value);
        Assert.Equal(EvidenceCompleteness.Complete, capture.Bank.Completeness);
    }

    [Theory]
    [InlineData("[\"Zed\",\"Zed\"]")]
    [InlineData("[null]")]
    [InlineData("[\"\"]")]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task Invalid_roster_is_unknown_character_coverage_never_complete_zero_characters(string roster)
    {
        var payloads = Payloads();
        payloads["/v2/characters"] = roster;
        using var handler = FixtureHandler(payloads);
        var capture = Capture(await Gateway(handler).CollectAsync(Evaluation));
        Assert.Equal(Gw2ApiErrorCategory.InvalidPayload, capture.Roster.ErrorCategory);
        Assert.Equal(new CharacterHoldingsCoverage(EvidenceCompleteness.Unknown, null, 0), capture.CharacterCoverage);
        Assert.Empty(capture.Characters);
        Assert.Equal(EvidenceCompleteness.Complete, capture.Delivery.Completeness);
        Assert.DoesNotContain(handler.Requests, request => request.Path.EndsWith("/inventory", StringComparison.Ordinal) && request.Path.StartsWith("/v2/characters/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, Gw2ApiErrorCategory.Forbidden)]
    [InlineData(HttpStatusCode.PartialContent, Gw2ApiErrorCategory.IncompleteData)]
    [InlineData(HttpStatusCode.ServiceUnavailable, Gw2ApiErrorCategory.UpstreamUnavailable)]
    public async Task Unavailable_roster_does_not_fabricate_empty_account(HttpStatusCode status, Gw2ApiErrorCategory category)
    {
        using var handler = new Handler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/v2/characters"
            ? new HttpResponseMessage(status) : Json(PayloadFor(Payloads(), request.RequestUri!))));
        var capture = Capture(await Gateway(handler).CollectAsync(Evaluation));
        Assert.Equal(category, capture.Roster.ErrorCategory);
        Assert.Equal(EvidenceCompleteness.Unknown, capture.CharacterCoverage.Completeness);
        Assert.Null(capture.CharacterCoverage.ExpectedActorCount);
    }

    [Fact]
    public async Task Successful_empty_sources_are_complete_known_empties()
    {
        var payloads = Payloads();
        payloads["/v2/characters"] = "[]";
        using var handler = FixtureHandler(payloads);
        var capture = Capture(await Gateway(handler).CollectAsync(Evaluation));
        Assert.Equal(new CharacterHoldingsCoverage(EvidenceCompleteness.Complete, 0, 0), capture.CharacterCoverage);
        Assert.Empty(capture.Roster.Value!);
        Assert.Empty(capture.Bank.Value!.Items);
        Assert.Empty(capture.SharedInventory.Value!.Items);
        Assert.Empty(capture.MaterialStorage.Value!.Items);
        Assert.Equal(0L, capture.Delivery.Value!.CoinsCopper);
        Assert.Empty(capture.Delivery.Value.Items);
        Assert.Equal(new EvidenceSourceCoverage(0, 0), capture.Bank.Coverage);
    }

    [Theory]
    [InlineData("/v2/account/bank", "[{\"id\":1,\"count\":-1}]")]
    [InlineData("/v2/account/bank", "[{\"id\":1,\"count\":0}]")]
    [InlineData("/v2/account/bank", "[{\"id\":1,\"count\":2147483648}]")]
    [InlineData("/v2/account/bank", "[{\"id\":1,\"count\":2147483647},{\"id\":1,\"count\":1}]")]
    [InlineData("/v2/account/bank", "[{\"id\":1,\"count\":1,\"binding\":\"Unexpected\"}]")]
    [InlineData("/v2/account/bank", "[{\"id\":1,\"count\":1,\"binding\":\"Character\"}]")]
    [InlineData("/v2/account/bank", "[{\"id\":1,\"count\":1,\"binding\":\"Account\",\"bound_to\":\"Zed\"}]")]
    [InlineData("/v2/account/bank", "[null,{\"id\":1}]")]
    [InlineData("/v2/account/inventory", "[{\"id\":1,\"count\":1,\"binding\":\"Character\",\"bound_to\":\"Zed\"}]")]
    [InlineData("/v2/account/materials", "[{\"id\":1,\"count\":4,\"category\":5},{\"id\":1,\"count\":5,\"category\":6}]")]
    [InlineData("/v2/account/materials", "[{\"id\":1,\"count\":4,\"category\":5},{\"id\":1,\"count\":4,\"category\":6,\"binding\":\"Account\"}]")]
    [InlineData("/v2/account/materials", "[null]")]
    [InlineData("/v2/account/materials", "[{\"id\":1,\"count\":-1,\"category\":5}]")]
    [InlineData("/v2/commerce/delivery", "{\"coins\":-1,\"items\":[]}")]
    [InlineData("/v2/commerce/delivery", "{\"coins\":9223372036854775808,\"items\":[]}")]
    [InlineData("/v2/commerce/delivery", "{\"coins\":0,\"items\":[{\"id\":1,\"count\":2147483647},{\"id\":1,\"count\":1}]}")]
    [InlineData("/v2/commerce/delivery", "{\"coins\":0,\"items\":[null]}")]
    [InlineData("/v2/commerce/delivery", "{\"items\":[]}")]
    [InlineData("/v2/characters/Zed/inventory", "{\"bags\":[{\"id\":1,\"size\":2,\"inventory\":[null]}]}")]
    [InlineData("/v2/characters/Zed/inventory", "{\"bags\":[{\"id\":1,\"size\":1,\"inventory\":[{\"id\":2,\"count\":1,\"infusions\":[-1]}]}]}")]
    [InlineData("/v2/characters/Zed/inventory", "{}")]
    public async Task Malformed_source_fails_conservatively_without_erasing_unrelated_sources(string path, string payload)
    {
        var payloads = Payloads();
        payloads[path] = payload;
        using var handler = FixtureHandler(payloads);
        var capture = Capture(await Gateway(handler).CollectAsync(Evaluation));
        var error = path switch
        {
            "/v2/account/bank" => capture.Bank.ErrorCategory,
            "/v2/account/inventory" => capture.SharedInventory.ErrorCategory,
            "/v2/account/materials" => capture.MaterialStorage.ErrorCategory,
            "/v2/commerce/delivery" => capture.Delivery.ErrorCategory,
            _ => capture.Characters.Single(value => value.Actor.DisplayName == "Zed").Inventory.ErrorCategory,
        };
        Assert.Equal(Gw2ApiErrorCategory.InvalidPayload, error);
        Assert.Single(capture.Characters[0].Inventory.Value!.Items);
    }

    [Fact]
    public async Task Changing_credential_mid_capture_does_not_mix_accounts_or_child_credentials()
    {
        var source = new MutableKeySource();
        using var handler = new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/v2/account") source.Value = "synthetic-next-credential";
            return Task.FromResult(Json(PayloadFor(Payloads(), request.RequestUri!)));
        });
        using var client = Client(handler);
        var capture = Capture(await new AccountHoldingsGateway(source, client, new RecordingScheduler(), new StepClock()).CollectAsync(Evaluation));
        Assert.Equal(1, source.ReadCount);
        Assert.All(handler.Requests, request => Assert.Equal(SyntheticCredential, request.Credential));
        Assert.DoesNotContain(source.Value, JsonSerializer.Serialize(capture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Actor_ids_survive_roster_reordering_but_not_account_change_or_rename()
    {
        var payloads = Payloads();
        using var handler = FixtureHandler(payloads);
        var gateway = Gateway(handler);
        var first = Capture(await gateway.CollectAsync(Evaluation));
        payloads["/v2/characters"] = "[\"Aé / One\",\"Zed\"]";
        var reordered = Capture(await gateway.CollectAsync(Evaluation));
        Assert.Equal(first.Roster.Value, reordered.Roster.Value);
        payloads["/v2/account"] = "{\"id\":\"synthetic-other-account\"}";
        var otherAccount = Capture(await gateway.CollectAsync(Evaluation));
        Assert.NotEqual(first.Roster.Value![0].ActorId, otherAccount.Roster.Value![0].ActorId);
        payloads["/v2/characters"] = "[\"Renamed\"]";
        payloads["/v2/characters/Renamed/inventory"] = "{\"bags\":[]}";
        var renamed = Capture(await gateway.CollectAsync(Evaluation));
        Assert.DoesNotContain(otherAccount.Roster.Value, old => old.ActorId == renamed.Roster.Value![0].ActorId);
    }

    [Fact]
    public async Task Replay_preserves_local_actor_references_and_facts_but_not_invented_source_clock_or_coherence()
    {
        using var handler = FixtureHandler(Payloads());
        var clock = new StepClock();
        using var client = Client(handler);
        var gateway = new AccountHoldingsGateway(new MutableKeySource(), client, new RecordingScheduler(), clock);
        var first = Capture(await gateway.CollectAsync(Evaluation));
        var second = Capture(await gateway.CollectAsync(Evaluation));
        Assert.Equal(first.Roster.Value, second.Roster.Value);
        Assert.Equal(JsonSerializer.Serialize(first.Characters[0].Inventory.Value), JsonSerializer.Serialize(second.Characters[0].Inventory.Value));
        Assert.NotEqual(first.RefreshId, second.RefreshId);
        Assert.Equal(Evaluation, first.EvaluatedAtUtc);
        var intervals = new[] { first.AccountIdentityFetch, first.Roster.Fetch, first.Bank.Fetch,
            first.SharedInventory.Fetch, first.MaterialStorage.Fetch, first.Delivery.Fetch,
            first.Characters[0].Inventory.Fetch, first.Characters[1].Inventory.Fetch };
        Assert.All(intervals, interval =>
        {
            Assert.Null(interval.UpstreamObservedAtUtc);
            Assert.True(interval.StartedAtUtc < interval.CompletedAtUtc);
            Assert.NotEqual(first.EvaluatedAtUtc, interval.CompletedAtUtc);
        });
        Assert.Equal(8, intervals.Select(interval => interval.CompletedAtUtc).Distinct().Count());
        Assert.DoesNotContain("Aé", first.Roster.Value![0].ToString(), StringComparison.Ordinal);
        Assert.True(second.Bank.Fetch.StartedAtUtc > first.Bank.Fetch.CompletedAtUtc);
    }

    [Fact]
    public async Task Thirty_actors_have_four_active_reads_and_cancellation_stops_queued_work()
    {
        var payloads = Payloads();
        payloads["/v2/characters"] = JsonSerializer.Serialize(Enumerable.Range(0, 30).Select(index => $"Actor {index:D2}"));
        var fourStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var peak = 0;
        var starts = new ConcurrentQueue<string>();
        using var handler = new Handler(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (!path.StartsWith("/v2/characters/", StringComparison.Ordinal)) return Json(PayloadFor(payloads, request.RequestUri!));
            starts.Enqueue(path);
            var count = Interlocked.Increment(ref active);
            Interlocked.Exchange(ref peak, Math.Max(Volatile.Read(ref peak), count));
            if (count == 4) fourStarted.TrySetResult();
            try { await release.Task.WaitAsync(token); return Json("{\"bags\":[]}"); }
            finally { Interlocked.Decrement(ref active); }
        });
        using var cancellation = new CancellationTokenSource();
        var task = Gateway(handler).CollectAsync(Evaluation, cancellation.Token);
        await fourStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, peak);
        Assert.Equal(4, starts.Count);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(4, starts.Count);
        Assert.Equal(0, active);
        Assert.Equal(Enumerable.Range(0, 4).Select(index => $"/v2/characters/Actor%20{index:D2}/inventory"), starts);
    }

    [Fact]
    public async Task Thirty_actors_are_not_capped_and_stalled_actor_does_not_block_other_workers()
    {
        var payloads = Payloads();
        payloads["/v2/characters"] = JsonSerializer.Serialize(Enumerable.Range(0, 30).Select(index => $"Actor {index:D2}"));
        var otherActorsComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = 0;
        using var handler = new Handler(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (!path.StartsWith("/v2/characters/", StringComparison.Ordinal)) return Json(PayloadFor(payloads, request.RequestUri!));
            if (path == "/v2/characters/Actor%2000/inventory") await releaseFirst.Task.WaitAsync(token);
            else if (path.EndsWith("/inventory", StringComparison.Ordinal) && Interlocked.Increment(ref completed) == 29) otherActorsComplete.TrySetResult();
            return Json(path.EndsWith("/inventory", StringComparison.Ordinal) ? "{\"bags\":[]}" : PayloadFor(payloads, request.RequestUri!));
        });
        var task = Gateway(handler).CollectAsync(Evaluation);
        await otherActorsComplete.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(task.IsCompleted);
        releaseFirst.SetResult();
        var capture = Capture(await task);
        Assert.Equal(new CharacterHoldingsCoverage(EvidenceCompleteness.Complete, 30, 30), capture.CharacterCoverage);
        Assert.Equal(Enumerable.Range(0, 30).Select(index => $"Actor {index:D2}"), capture.Characters.Select(value => value.Actor.DisplayName));
    }

    [Fact]
    public async Task Uses_existing_scheduler_retry_policy_and_keeps_source_failure_isolated()
    {
        var attempts = 0;
        var delay = new RecordingDelay();
        using var scheduler = new Gw2RequestScheduler(new Gw2ApiSchedulerOptions(), delay);
        using var handler = new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/v2/account/bank")
            {
                var attempt = Interlocked.Increment(ref attempts);
                if (attempt == 1)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
                    return Task.FromResult(response);
                }
                if (attempt == 2) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
            }
            return Task.FromResult(Json(PayloadFor(Payloads(), request.RequestUri!)));
        });
        using var client = Client(handler);
        var capture = Capture(await new AccountHoldingsGateway(new MutableKeySource(), client, scheduler, new StepClock()).CollectAsync(Evaluation));
        Assert.Equal(3, attempts);
        Assert.Equal([TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(2)], delay.Delays);
        Assert.Equal(EvidenceCompleteness.Complete, capture.Bank.Completeness);
    }

    [Fact]
    public async Task Opt_in_factory_has_no_production_registration_and_never_logs_private_names_or_transport_detail()
    {
        using var logger = new CaptureLogger();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.ClearProviders().AddProvider(logger));
        services.AddTyrianLedgerAccountConnection(new TestEnvironment(), new ConfigurationBuilder().Build());
        using (var original = services.BuildServiceProvider()) Assert.Null(original.GetService<IAccountHoldingsCollector>());
        services.AddTyrianLedgerAccountHoldingsCollector();
        services.RemoveAll<IGw2ApiKeySource>();
        services.AddSingleton<IGw2ApiKeySource>(new MutableKeySource());
        services.RemoveAll<IGw2RequestScheduler>();
        services.AddSingleton<IGw2RequestScheduler>(new RecordingScheduler());
        var handler = new Handler((request, _) => request.RequestUri!.AbsolutePath.StartsWith("/v2/characters/", StringComparison.Ordinal)
            ? throw new HttpRequestException($"{SyntheticCredential} Aé / One")
            : Task.FromResult(Json(PayloadFor(Payloads(), request.RequestUri!))));
        services.Configure<HttpClientFactoryOptions>(AccountHoldingsGateway.HttpClientName,
            options => options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = handler));
        using var provider = services.BuildServiceProvider();
        var capture = Capture(await provider.GetRequiredService<IAccountHoldingsCollector>().CollectAsync(Evaluation));
        Assert.All(capture.Characters, actor => Assert.Equal(Gw2ApiErrorCategory.TransportFailure, actor.Inventory.ErrorCategory));
        var rendered = string.Join("\n", logger.Messages);
        Assert.DoesNotContain("Aé", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(SyntheticCredential, rendered, StringComparison.Ordinal);
        var options = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(AccountHoldingsGateway.HttpClientName);
        Assert.True(options.ShouldRedactHeaderValue("Authorization"));
        Assert.True(options.ShouldRedactHeaderValue("X-Any"));
        Assert.DoesNotContain(SyntheticCredential, JsonSerializer.Serialize(capture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Timeout_capacity_and_missing_credentials_return_safe_categories()
    {
        using var handler = new Handler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/v2/account/bank")
                await new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task.WaitAsync(token);
            return Json(PayloadFor(Payloads(), request.RequestUri!));
        });
        using var client = Client(handler);
        var capture = Capture(await new AccountHoldingsGateway(new MutableKeySource(), client, new RecordingScheduler(),
            new StepClock(), TimeSpan.FromMilliseconds(20)).CollectAsync(Evaluation));
        Assert.Equal(Gw2ApiErrorCategory.TransportFailure, capture.Bank.ErrorCategory);
        var unavailable = await new AccountHoldingsGateway(new MutableKeySource(), client, new CapacityScheduler(), new StepClock()).CollectAsync(Evaluation);
        Assert.Equal(Gw2ApiErrorCategory.UpstreamUnavailable, unavailable.ErrorCategory);
        var missing = await new AccountHoldingsGateway(new MutableKeySource { Value = "" }, client, new RecordingScheduler(), new StepClock()).CollectAsync(Evaluation);
        Assert.Equal(Gw2ApiErrorCategory.CredentialNotConfigured, missing.ErrorCategory);
    }

    [Fact]
    public async Task Large_delivery_coin_value_stays_integer_copper()
    {
        var payloads = Payloads();
        payloads["/v2/commerce/delivery"] = "{\"coins\":4294967295,\"items\":[]}";
        using var handler = FixtureHandler(payloads);
        var capture = Capture(await Gateway(handler).CollectAsync(Evaluation));
        Assert.Equal(4294967295L, capture.Delivery.Value!.CoinsCopper);
    }

    private static string PayloadFor(Dictionary<string, string> payloads, Uri uri)
    {
        if (uri.Query.Contains("tabs=all", StringComparison.Ordinal) && payloads.TryGetValue(uri.AbsolutePath + "?tabs=all", out var allTabs)) return allTabs;
        if (payloads.TryGetValue(uri.AbsolutePath, out var payload)) return payload;
        if (uri.AbsolutePath == "/v2/account/recipes") return "[]";
        if (uri.AbsolutePath.EndsWith("/crafting", StringComparison.Ordinal)) return "{\"crafting\":[]}";
        if (uri.AbsolutePath.EndsWith("/equipment", StringComparison.Ordinal)) return "{\"equipment\":[]}";
        if (uri.AbsolutePath.EndsWith("/equipmenttabs", StringComparison.Ordinal)) return uri.Query.Contains("tabs=all", StringComparison.Ordinal)
            ? "[{\"tab\":1,\"is_active\":true,\"equipment\":[]}]" : "[1]";
        throw new InvalidOperationException("Unexpected synthetic request.");
    }
    private static Dictionary<string, string> Payloads() => new()
    {
        ["/v2/account"] = "{\"id\":\"synthetic-holdings-account\"}",
        ["/v2/characters"] = "[\"Zed\",\"Aé / One\"]",
        ["/v2/account/bank"] = "[]", ["/v2/account/inventory"] = "[]",
        ["/v2/account/materials"] = "[]", ["/v2/commerce/delivery"] = "{\"coins\":0,\"items\":[]}",
        ["/v2/characters/A%C3%A9%20%2F%20One/inventory"] = CharacterInventory,
        ["/v2/characters/Zed/inventory"] = "{\"bags\":[]}",
    };
    private static Handler FixtureHandler(Dictionary<string, string> payloads) => new((request, _) => Task.FromResult(Json(PayloadFor(payloads, request.RequestUri!))));
    private static AccountHoldingsGateway Gateway(Handler handler) => new(new MutableKeySource(), Client(handler), new RecordingScheduler(), new StepClock());
    private static AccountHoldingsCapture Capture(Gw2ApiResult<AccountHoldingsCapture> result)
    {
        Assert.True(result.IsSuccess);
        return Assert.IsType<AccountHoldingsCapture>(result.Value);
    }
    private static IEnumerable<HoldingsItemObservation> LooseItems(AccountHoldingsCapture capture) =>
        capture.Bank.Value!.Items.Concat(capture.SharedInventory.Value!.Items).Concat(capture.MaterialStorage.Value!.Items)
            .Concat(capture.Characters.SelectMany(actor => actor.Inventory.Value!.Items));
    private static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://api.guildwars2.com/v2/") };
    private static HttpResponseMessage Json(string payload) => new(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
    private sealed class MutableKeySource : IGw2ApiKeySource
    {
        public string Value { get; set; } = SyntheticCredential;
        public int ReadCount;
        public ValueTask<Gw2ApiKeyReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref ReadCount);
            return ValueTask.FromResult(Gw2ApiKeyReadResult.FromValue(Value));
        }
    }
    private sealed class StepClock : IClock
    {
        private long tick;
        public DateTimeOffset UtcNow => Evaluation.AddHours(1).AddSeconds(Interlocked.Increment(ref tick));
    }
    private sealed record RequestObservation(string Path, string Query, string? Scheme, string? Credential);
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public ConcurrentQueue<RequestObservation> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(new(request.RequestUri!.AbsolutePath, request.RequestUri.Query,
                request.Headers.Authorization?.Scheme, request.Headers.Authorization?.Parameter));
            return send(request, cancellationToken);
        }
    }
    private sealed class RecordingScheduler : IGw2RequestScheduler
    {
        public ConcurrentQueue<Gw2RequestKey> Keys { get; } = new();
        public async Task<T> ScheduleAsync<T>(Gw2RequestKey key, Func<CancellationToken, Task<Gw2ScheduledResult<T>>> send, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Keys.Enqueue(key);
            return (await send(token)).Result;
        }
    }
    private sealed class CapacityScheduler : IGw2RequestScheduler
    {
        public Task<T> ScheduleAsync<T>(Gw2RequestKey key, Func<CancellationToken, Task<Gw2ScheduledResult<T>>> send, CancellationToken token) =>
            throw new Gw2RequestSchedulerCapacityExceededException();
    }
    private sealed class RecordingDelay : IGw2RequestDelay
    {
        public List<TimeSpan> Delays { get; } = [];
        public Task DelayAsync(TimeSpan delay, CancellationToken token) { Delays.Add(delay); return Task.CompletedTask; }
    }
    private sealed class CaptureLogger : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string category) => new Logger(Messages);
        public void Dispose() { }
        private sealed class Logger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception) + exception?.ToString());
        }
    }
    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "HoldingsTests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
