using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gw2Tp.Application.AccountEvidence;
using Gw2Tp.Application.LocalData;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Application.Plans;
using Gw2Tp.Application.Time;
using Gw2Tp.Domain.Finance;
using Gw2Tp.Testing;
using Gw2Tp.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Gw2Tp.Web.Tests;

public sealed class LocalPlanCommandTests
{
    [Fact]
    public async Task Actual_host_complete_and_undo_commit_before_account_and_market_HTTP_barriers_release()
    {
        await using var host = await Host.Create();
        var stored = await host.Seed();
        var scope = await host.ReadScope();
        host.Account.Block = true;
        var accountRead = host.App.Services.GetRequiredService<IPersonalTradingPostSynchronizationService>().SynchronizeAsync();
        await host.Account.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var marketRead = host.App.Services.GetRequiredService<IGw2ApiClient>().GetItemMetadataAsync([10]);
        await host.Transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var accountCalls = host.Account.Calls;
        var transportCalls = host.Transport.Calls;
        var secondAccountRead = host.App.Services.GetRequiredService<IPersonalTradingPostSynchronizationService>().SynchronizeAsync();
        Assert.Equal(accountCalls, host.Account.Calls);
        Assert.False(secondAccountRead.IsCompleted);
        var samples = new List<object>();
        await using (var availableOperation = await host.App.Services.GetRequiredService<IPersonalDataOperationGate>().AcquireAsync()) { }
        try
        {
            for (var index = 0; index < 12; index++)
            {
                var commandId = $"local-{index}";
                var watch = Stopwatch.StartNew();
                using var complete = await host.Post("complete", Completion(stored, commandId), scope).WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
                samples.Add(Sample("complete", watch.Elapsed.TotalMilliseconds, complete));
                if (index == 0) await ExportFixture(host, scope, "reported");
                var committedRevision = stored.Revision + 1;
                // Same command replay has one receipt/effect, even though its displayed revision is old.
                using var replay = await host.Post("complete", Completion(stored, commandId), scope);
                Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
                using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
                Assert.Equal("already_applied", replayJson.RootElement.GetProperty("state").GetString());
                using var staleUndo = await host.Post("undo", new { expectedRevision = stored.Revision.ToString() }, scope);
                Assert.Equal(HttpStatusCode.Conflict, staleUndo.StatusCode);
                watch.Restart();
                using var undo = await host.Post("undo", new { expectedRevision = committedRevision.ToString() }, scope).WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
                samples.Add(Sample("undo", watch.Elapsed.TotalMilliseconds, undo));
                if (index == 0) await ExportFixture(host, scope, "undone");
                stored = await host.ReadPlan();
                Assert.Equal(committedRevision + 1, stored.Revision);
                Assert.All(stored.Events, value => Assert.Equal(PlanShadowEventState.Reversed, value.State));
                Assert.Equal(accountCalls, host.Account.Calls);
                Assert.Equal(transportCalls, host.Transport.Calls);
                Assert.False(accountRead.IsCompleted);
                Assert.False(marketRead.IsCompleted);
            }
            var destination = Environment.GetEnvironmentVariable("TYRIAN_LEDGER_LOCAL_COMMAND_EVIDENCE");
            if (!string.IsNullOrWhiteSpace(destination))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
                await File.WriteAllTextAsync(destination, JsonSerializer.Serialize(new
                {
                    environment = new { os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                        architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                        runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                        processors = Environment.ProcessorCount },
                    evidence = "Actual TestServer host, production command services/fence/SQLite; synthetic account gateway and actual public HTTP transport barrier",
                    commandAccountReads = host.Account.Calls - accountCalls,
                    commandHTTPAttempts = host.Transport.Calls - transportCalls,
                    committedBeforeBothBarrierReleases = true, samples,
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        finally
        {
            host.Account.Release.TrySetResult(); host.Transport.Release.TrySetResult();
            await accountRead; await secondAccountRead; await marketRead;
        }
    }

    [Theory]
    [InlineData("transition")]
    [InlineData("clear")]
    [InlineData("restore")]
    public async Task Unbound_scope_rejects_known_receipts_and_undo_before_lookup_without_HTTP(string transition)
    {
        await using var host = await Host.Create();
        var plan = await host.Seed();
        var scope = await host.ReadScope();
        using var applied = await host.Post("complete", Completion(plan, "known"), scope);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        if (transition == "transition")
        {
            await using var boundary = await host.Fence.QuiesceAsync(); await boundary.PublishAsync();
        }
        else
        {
            string? backup = null;
            if (transition == "restore")
            {
                using var response = await host.PostPath("/api/local-data/backup", new { });
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                backup = json.RootElement.GetProperty("fileName").GetString();
            }
            using var response2 = await host.PostPath(transition == "clear" ? "/api/local-data/clear-personal" : "/api/local-data/restore-managed",
                transition == "clear" ? new { confirmation = "CLEAR PERSONAL DATA" } : (object)new { confirmation = "RESTORE LOCAL DATA", backupFileName = backup });
            response2.EnsureSuccessStatusCode();
        }
        var reads = host.Account.Calls;
        using var replay = await host.Post("complete", Completion(plan, "known"), scope);
        using var undo = await host.Post("undo", new { expectedRevision = "2" }, scope);
        foreach (var response in new[] { replay, undo })
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("account_context_unavailable", body);
            Assert.DoesNotContain("receiptId", body); Assert.DoesNotContain("acknowledgement", body);
        }
        Assert.Equal(reads, host.Account.Calls); Assert.Equal(0, host.Transport.Calls);
    }

    [Fact]
    public async Task Actual_host_concurrent_revision_has_one_effect_and_undo_retry_cannot_reverse_another_event()
    {
        await using var host = await Host.Create();
        var plan = await host.Seed(); var scope = await host.ReadScope();
        var attempts = await Task.WhenAll(host.Post("complete", Completion(plan, "one"), scope), host.Post("complete", Completion(plan, "two"), scope));
        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in attempts) response.Dispose();
        var reported = await host.ReadPlan(); Assert.Single(reported.Events);
        using var undo = await host.Post("undo", new { expectedRevision = reported.Revision.ToString() }, scope);
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
        using var retry = await host.Post("undo", new { expectedRevision = reported.Revision.ToString() }, scope);
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Equal(reported.Revision + 1, (await host.ReadPlan()).Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_clear_restore_rejects_a_captured_queued_command_without_torn_effects(bool restore)
    {
        await using var host = await Host.Create();
        var stored = await host.Seed(); var scope = await host.ReadScope();
        string? backup = null;
        if (restore)
        {
            using var response = await host.PostPath("/api/local-data/backup", new { }); response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); backup = json.RootElement.GetProperty("fileName").GetString();
        }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var endpoint = host.App.Services.GetRequiredService<PlanEndpointService>();
        var queued = host.Fence.RunAsync(async token =>
        {
            entered.TrySetResult(); await release.Task;
            return await endpoint.CompleteAsync(stored.Id, new(stored.Steps[0].Id, stored.Revision.ToString(), "queued", "ReportPerformed", 4, "100"), scope, token);
        });
        await entered.Task;
        using var transition = await host.PostPath(restore ? "/api/local-data/restore-managed" : "/api/local-data/clear-personal",
            restore ? new { confirmation = "RESTORE LOCAL DATA", backupFileName = backup } : (object)new { confirmation = "CLEAR PERSONAL DATA" });
        transition.EnsureSuccessStatusCode(); release.TrySetResult();
        await Assert.ThrowsAsync<AccountWorkRejectedException>(() => queued);
        await host.ReadScope();
        if (restore)
        {
            var retained = await host.ReadPlan(); Assert.Equal(stored.Revision, retained.Revision); Assert.Empty(retained.Events);
        }
        else await host.Fence.RunAsync(async token =>
        {
            Assert.Null(await host.App.Services.GetRequiredService<IPersonalTradingPostRepository>().FindAccountProfileAsync(HoldingsEvidenceFixture.Scope.AccountId, token));
            return true;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_SQLite_durable_command_invalidation_survives_browser_cancellation(bool undo)
    {
        await using var host = await Host.Create();
        var stored = await host.Seed(); var scope = await host.ReadScope();
        if (undo)
        {
            using var report = await host.Post("complete", Completion(stored, "before-undo"), scope);
            report.EnsureSuccessStatusCode(); stored = await host.ReadPlan();
        }
        using var browser = new CancellationTokenSource();
        var services = host.App.Services;
        var projections = services.GetRequiredService<PlanDecisionProjectionStore>();
        var endpoint = new PlanEndpointService(null!, null!, null!, null!, null!,
            services.GetRequiredService<IPersonalTradingPostRepository>(), services.GetRequiredService<IPlanRepository>(),
            services.GetRequiredService<IPlanOrchestrationService>(), projections,
            services.GetRequiredService<AccountViewScopeTokenService>(),
            new CancelCompletion(services.GetRequiredService<IPlanCompletionCommandService>(), browser), host.Fence,
            undoCommands: new CancelUndo(services.GetRequiredService<IPlanUndoCommandService>(), browser));
        var invalidations = 0; endpoint.LoopDecisionInvalidated += _ => invalidations++;
        endpoint.BeginLoopDecision(); Assert.True(projections.TryGetActive(out var active));
        var result = await host.Fence.RunAsync(token => undo
            ? endpoint.UndoAsync(stored.Id, new(stored.Revision.ToString()), scope, token)
            : endpoint.CompleteAsync(stored.Id, new(stored.Steps[0].Id, stored.Revision.ToString(), "cancelled-browser", "ReportPerformed", 4, "100"), scope, token), browser.Token);
        Assert.True(browser.IsCancellationRequested); Assert.Equal(1, invalidations);
        Assert.True(active!.IsCompleted); Assert.False(projections.TryGetActive(out _));
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(((Microsoft.AspNetCore.Http.IValueHttpResult)result).Value));
        Assert.Equal(undo ? "undone" : "reported", payload.RootElement.GetProperty("state").GetString());
        var persisted = await host.ReadPlan(); Assert.Equal(stored.Revision + 1, persisted.Revision);
        Assert.Equal(undo ? PlanShadowEventState.Reversed : PlanShadowEventState.PendingConfirmation, Assert.Single(persisted.Events).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_after_commit_recovers_invalidation_by_receipt_or_revision_replay_without_second_effect(bool undo)
    {
        await using var host = await Host.Create();
        var stored = await host.Seed(); var scope = await host.ReadScope();
        if (undo)
        {
            using var applied = await host.Post("complete", Completion(stored, "prior"), scope); applied.EnsureSuccessStatusCode();
            stored = await host.ReadPlan();
        }
        var endpoint = host.App.Services.GetRequiredService<PlanEndpointService>();
        var failOnce = true; var recovered = 0;
        endpoint.LoopDecisionInvalidated += _ =>
        {
            if (failOnce) { failOnce = false; throw new InvalidOperationException("Synthetic post-commit interruption"); }
            recovered++;
        };
        Task<Microsoft.AspNetCore.Http.IResult> Send() => host.Fence.RunAsync(token => undo
            ? endpoint.UndoAsync(stored.Id, new(stored.Revision.ToString()), scope, token)
            : endpoint.CompleteAsync(stored.Id, new(stored.Steps[0].Id, stored.Revision.ToString(), "recover", "ReportPerformed", 4, "100"), scope, token));
        await Assert.ThrowsAsync<InvalidOperationException>(Send);
        var committed = await host.ReadPlan(); Assert.Equal(stored.Revision + 1, committed.Revision);
        endpoint.BeginLoopDecision();
        var replay = await Send(); Assert.Equal(1, recovered);
        Assert.False(host.App.Services.GetRequiredService<PlanDecisionProjectionStore>().TryGetActive(out _));
        Assert.Equal(committed.Revision, (await host.ReadPlan()).Revision);
        if (undo) Assert.Equal(409, ((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)replay).StatusCode);
        else
        {
            using var payload = JsonDocument.Parse(JsonSerializer.Serialize(((Microsoft.AspNetCore.Http.IValueHttpResult)replay).Value));
            Assert.Equal("already_applied", payload.RootElement.GetProperty("state").GetString());
        }
    }

    private sealed class CancelCompletion(IPlanCompletionCommandService service, CancellationTokenSource browser) : IPlanCompletionCommandService
    {
        public async Task<PlanCompletionResult> CompleteAsync(long profile, PlanCompletionCommand command, CancellationToken token = default)
        { var result = await service.CompleteAsync(profile, command, token); browser.Cancel(); return result; }
    }
    private sealed class CancelUndo(IPlanUndoCommandService service, CancellationTokenSource browser) : IPlanUndoCommandService
    {
        public async Task<PlanUndoResult> UndoAsync(long profile, string plan, long revision, CancellationToken token = default)
        { var result = await service.UndoAsync(profile, plan, revision, token); browser.Cancel(); return result; }
    }

    private static async Task ExportFixture(Host host, string scope, string state)
    {
        var directory = Environment.GetEnvironmentVariable("TYRIAN_LEDGER_LOCAL_COMMAND_FIXTURES");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var plan = await host.ReadPlan();
        await File.WriteAllTextAsync(Path.Combine(directory, state + ".json"), JsonSerializer.Serialize(new
        { state = "ready", proposals = Array.Empty<object>(), plans = new[] { PlanEndpointService.ToResponse(plan) }, accountCacheScope = scope }));
    }

    private static object Completion(PlanRecord plan, string id) => new { stepId = plan.Steps[0].Id,
        expectedRevision = plan.Revision.ToString(), commandId = id, operation = "ReportPerformed", quantity = 4, unitPriceCopper = "100" };
    private static object Sample(string operation, double totalMilliseconds, HttpResponseMessage response) =>
        new { operation, totalMilliseconds, phases = response.Headers.GetValues("X-Tyrian-Plan-Command-Timing").Single() };

    private sealed class Host : IAsyncDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "TyrianLedger.LocalCommand.Tests", Guid.NewGuid().ToString("N"));
        public readonly AccountGateway Account = new();
        public readonly BarrierTransport Transport = new();
        public WebApplication App = null!;
        public HttpClient Client = null!;
        public string PlanId = "";
        public IAccountWorkFence Fence => App.Services.GetRequiredService<IAccountWorkFence>();
        private readonly DateTimeOffset now = DateTimeOffset.UtcNow;
        public static async Task<Host> Create()
        {
            var host = new Host();
            host.App = Program.CreateApplication([], builder =>
            {
                builder.Environment.EnvironmentName = "Testing"; builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                { ["TyrianLedger:Database:Path"] = Path.Combine(host.directory, "test.db") });
            }, services =>
            {
                services.AddSingleton<IPersonalTradingPostGateway>(host.Account);
                services.AddSingleton<IClock>(new Clock(host.now));
                services.AddHttpClient("TyrianLedger.Gw2Api").ConfigurePrimaryHttpMessageHandler(() => host.Transport);
                foreach (var worker in services.Where(value => value.ServiceType == typeof(IHostedService) &&
                    value.ImplementationType is { } type && (type == typeof(ContinuousDecisionLoopHostedService) || type == typeof(MarketHistoryCollectorHostedService))).ToArray()) services.Remove(worker);
            });
            await host.App.StartAsync(); host.Client = host.App.GetTestClient(); return host;
        }
        public Task<PlanRecord> Seed() => Fence.RunAsync(async token =>
        {
            await Fence.BindAccountAsync(HoldingsEvidenceFixture.Scope, token);
            var profile = await App.Services.GetRequiredService<IPersonalTradingPostRepository>().GetOrCreateAccountProfileAsync(HoldingsEvidenceFixture.Scope.AccountId, now, token);
            var snapshot = HoldingsEvidenceFixture.Snapshot(now, [HoldingsEvidenceFixture.Item(10, 10)]) with
            { StoreIncarnation = Fence.Current!.StoreIncarnation, Generation = Fence.Current.Generation };
            Assert.True(await App.Services.GetRequiredService<IAccountHoldingsSnapshotRepository>().ReplaceAsync(snapshot, token));
            var physical = new AccountHoldingsProjector().Project(snapshot, now, Fence.Current.Generation, Fence.Current.StoreIncarnation);
            var candidate = new PlanCandidate("local", 1, "local", PlanAttention.Active,
                [new("sell", PlanStepAction.SellNow, 10, "Objet de test", 4, new Money(100), [], PlanStepState.Pending)],
                [new(PlanResourceKind.Inventory, "10", 4, Money.Zero)], Money.Zero, Money.Zero, 0, 0, 1, 1, true, []);
            var plan = App.Services.GetRequiredService<IPlanOrchestrationService>().Start(PlanHoldingsAdmission.Authorize(candidate, physical), now);
            Assert.Equal(PlanStartResult.Started, await App.Services.GetRequiredService<IPlanRepository>().TryStartAsync(profile.Id, plan, new Money(1000), Money.Zero, physical.Quantities, token));
            PlanId = plan.Id;
            return (await App.Services.GetRequiredService<IPlanRepository>().GetStartedAsync(profile.Id, token)).Single();
        });
        public Task<PlanRecord> ReadPlan() => Fence.RunAsync(async token =>
        {
            var profile = await App.Services.GetRequiredService<IPersonalTradingPostRepository>().FindAccountProfileAsync(HoldingsEvidenceFixture.Scope.AccountId, token);
            return (await App.Services.GetRequiredService<IPlanRepository>().GetStartedAsync(profile!.Id, token)).Single();
        });
        public async Task<string> ReadScope()
        {
            using var response = await Client.GetAsync("/api/plans/context"); response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("accountCacheScope").GetString()!;
        }
        public Task<HttpResponseMessage> Post(string operation, object body, string scope) => PostPath($"/api/plans/{PlanId}/{operation}", body, scope);
        public Task<HttpResponseMessage> PostPath(string path, object body, string? scope = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
            request.Headers.Add("Origin", "http://localhost");
            request.Headers.Add(LocalRequestOriginProtectionMiddleware.RequestHeader, LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
            if (scope is not null) request.Headers.Add(PlanEndpointService.AccountViewScopeHeader, scope);
            return Client.SendAsync(request);
        }
        public async ValueTask DisposeAsync()
        {
            Client.Dispose(); await App.StopAsync(); await App.DisposeAsync(); SqliteConnection.ClearAllPools(); Directory.Delete(directory, true);
        }
    }
    private sealed class Clock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }
    private sealed class BarrierTransport : HttpMessageHandler
    {
        public int Calls;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls); Entered.TrySetResult(); await Release.Task.WaitAsync(token);
            return new(HttpStatusCode.OK) { Content = new StringContent("[]") };
        }
    }
    private sealed class AccountGateway : IPersonalTradingPostGateway
    {
        public int Calls; public bool Block;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<Gw2ApiResult<AccountScope>> GetAccountScopeAsync(CancellationToken token = default)
        {
            Interlocked.Increment(ref Calls);
            if (Block) { Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            return Gw2ApiResult<AccountScope>.Success(HoldingsEvidenceFixture.Scope);
        }
        private Task<Gw2ApiResult<PersonalTransactionPage>> Empty(int page)
        { Interlocked.Increment(ref Calls); return Task.FromResult(Gw2ApiResult<PersonalTransactionPage>.Success(new([], page, 200, 1, 0, 0))); }
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCurrentBuyOrdersAsync(int page, CancellationToken token = default) => Empty(page);
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCurrentSellListingsAsync(int page, CancellationToken token = default) => Empty(page);
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCompletedBuyHistoryAsync(int page, CancellationToken token = default) => Empty(page);
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCompletedSellHistoryAsync(int page, CancellationToken token = default) => Empty(page);
    }
}
