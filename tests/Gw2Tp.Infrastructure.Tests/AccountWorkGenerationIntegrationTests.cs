using Gw2Tp.Application.LocalData;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Application.AccountConnection;
using Gw2Tp.Infrastructure.AccountConnection;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Application.Plans;
using Gw2Tp.Domain.Finance;
using Gw2Tp.Infrastructure.Persistence;
using Gw2Tp.Infrastructure.Secrets;
using Gw2Tp.Infrastructure.PersonalTradingPost;
using Gw2Tp.Infrastructure.Crafting;
using Gw2Tp.Infrastructure.AccountEvidence;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Testing;
using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class AccountWorkGenerationIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("clear")]
    [InlineData("managed-restore")]
    [InlineData("uploaded-restore")]
    public async Task Recovery_does_not_wait_on_an_old_read_groups_operation_lease(string operation)
    {
        await using var db = await Database.CreateAsync();
        await db.Run("A", () => db.Profiles.GetOrCreateAccountProfileAsync("A", Now));
        var backup = await db.Recovery.CreateBackupAsync();
        var entered = Barrier(); var release = Barrier();
        var oldRead = db.Fence.RunAsync(async token =>
        {
            await db.Fence.BindAccountAsync(new("A"), token);
            await using var group = await db.OperationGate.AcquireAsync(token);
            entered.SetResult(); await release.Task;
            return await db.Profiles.FindAccountProfileAsync("A", token);
        });
        await entered.Task;
        using var recoveryDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            if (operation == "clear") await db.Recovery.ClearPersonalDataAsync(recoveryDeadline.Token);
            else if (operation == "managed-restore")
                Assert.Equal(LocalDataRestoreOutcome.Restored,
                    (await db.Recovery.RestoreManagedBackupAsync(backup.FileName, recoveryDeadline.Token)).Outcome);
            else
            {
                await using var contents = File.OpenRead(Path.Combine(db.Recovery.GetLocation().BackupDirectoryPath, backup.FileName));
                Assert.Equal(LocalDataRestoreOutcome.Restored,
                    (await db.Recovery.RestoreAsync(contents, recoveryDeadline.Token)).Outcome);
            }
        }
        finally { release.SetResult(); }
        await Assert.ThrowsAsync<AccountWorkRejectedException>(() => oldRead);
        Assert.NotNull(await db.Run("A", () => db.Profiles.GetOrCreateAccountProfileAsync("A", Now)));
    }

    [Fact]
    public async Task Holdings_identity_conflict_rejects_before_any_child_read()
    {
        await using var db = await Database.CreateAsync();
        using var transport = new BlockedIdentityTransport("B");
        transport.Release.SetResult();
        using var client = new HttpClient(transport) { BaseAddress = new Uri("https://fixture.invalid/v2/") };
        var gateway = new AccountHoldingsGateway(new CapturedGw2ApiKeySource(db.Fence), client,
            new ImmediateScheduler(), new FrozenClock(Now), fence: db.Fence);
        await Assert.ThrowsAsync<AccountWorkRejectedException>(() => db.Run("A", () => gateway.CollectAsync(Now)));
        Assert.Equal(1, transport.Requests);
    }

    [Fact]
    public async Task Queued_old_validation_cannot_be_retagged_or_cached_after_credential_switch()
    {
        await using var db = await Database.CreateAsync();
        var inner = new BlockedValidation(new CapturedGw2ApiKeySource(db.Fence));
        var cache = new CachedAccountConnectionStatusService(inner, fence: db.Fence);
        var first = cache.GetStatusAsync();
        await inner.Entered.Task;
        var queued = cache.GetStatusAsync();
        db.Credential.Value = "synthetic-B";
        await db.Run("B", () => db.Profiles.GetOrCreateAccountProfileAsync("B", Now));
        inner.Release.SetResult();
        await Assert.ThrowsAsync<AccountWorkRejectedException>(() => first);
        await Assert.ThrowsAsync<AccountWorkRejectedException>(() => queued);
        Assert.Equal(1, inner.Calls);
        var current = await cache.GetStatusAsync();
        Assert.Equal(AccountConnectionState.InsufficientPermissions, current.State);
        Assert.Equal(current, await cache.GetStatusAsync());
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Coalesced_account_reads_bind_each_callers_context_before_private_access()
    {
        await using var db = await Database.CreateAsync();
        using var transport = new BlockedIdentityTransport();
        using var client = new HttpClient(transport) { BaseAddress = new Uri("https://fixture.invalid/v2/") };
        using var scheduler = new Gw2RequestScheduler(new Gw2ApiSchedulerOptions
        {
            RateLimit = new Gw2RateLimitOptions { BurstSize = 20, RefillTokensPerSecond = 20, MaxConcurrentRequests = 5, MaxQueuedRequests = 20 },
            Retry = new Gw2RetryOptions
            {
                On429 = new Gw2BackoffOptions { InitialBackoffMs = 1, MaxBackoffMs = 1, MaxAttempts = 1 },
                On5xx = new Gw2BackoffOptions { InitialBackoffMs = 1, MaxBackoffMs = 1, MaxAttempts = 1 },
            },
            RequestTimeoutMs = 10_000,
        }, new NoDelay());
        var gateway = new PersonalTradingPostGateway(new CapturedGw2ApiKeySource(db.Fence), client, scheduler, fence: db.Fence);
        Task<AccountProfile> Read() => db.Fence.RunAsync(async token =>
        {
            Assert.Null(db.Fence.Current!.AccountScope);
            var result = await gateway.GetAccountScopeAsync(token);
            Assert.True(result.IsSuccess);
            Assert.Equal("A", db.Fence.Current!.AccountScope!.AccountId);
            return await db.Profiles.GetOrCreateAccountProfileAsync("A", Now, token);
        });
        var first = Read();
        await transport.Entered.Task;
        var second = Read();
        transport.Release.SetResult();
        var profiles = await Task.WhenAll(first, second);
        Assert.Equal(profiles[0].Id, profiles[1].Id);
        Assert.Equal(1, transport.Requests);
    }

    [Theory]
    [InlineData("tp", false)]
    [InlineData("tp", true)]
    [InlineData("crafting", false)]
    [InlineData("holdings", false)]
    public async Task Authenticated_bundles_keep_one_credential_and_reject_late_success_or_failure(string producer, bool fail)
    {
        await using var db = await Database.CreateAsync();
        var transport = new BlockedTransport(fail);
        using var client = new HttpClient(transport) { BaseAddress = new Uri("https://fixture.invalid/v2/") };
        var source = new CapturedGw2ApiKeySource(db.Fence);
        var scheduler = new ImmediateScheduler();
        Task<bool> refresh;
        if (producer == "tp")
        {
            var gateway = new PersonalTradingPostGateway(source, client, scheduler, fence: db.Fence);
            var store = new SqlitePersonalTradingPostSynchronizationStore(db.Factory, db.Gate);
            var service = new PersonalTradingPostSynchronizationService(gateway, new MetadataMarket(), store, new FrozenClock(Now), fence: db.Fence);
            refresh = ObserveSync(service);
        }
        else if (producer == "crafting")
        {
            var gateway = new AccountCraftingGateway(source, client, scheduler, fence: db.Fence);
            var service = new AccountCraftingSnapshotService(gateway, new SqliteAccountCraftingSnapshotRepository(db.Factory, db.Gate), db.Fence);
            refresh = ObserveCrafting(service);
        }
        else
        {
            var gateway = new AccountHoldingsGateway(source, client, scheduler, new FrozenClock(Now), fence: db.Fence);
            refresh = ObserveHoldings(gateway);
        }
        await transport.Entered.Task;
        db.Credential.Value = "synthetic-B";
        await db.Run("B", () => db.Profiles.GetOrCreateAccountProfileAsync("B", Now));
        transport.Release.SetResult();
        Assert.False(await refresh);
        Assert.All(transport.Credentials, value => Assert.Equal("synthetic-A", value));
        Assert.True(transport.Credentials.Count > 1);
        if (producer == "tp" && !fail) Assert.Contains(transport.Paths, value => value.Contains("page=1", StringComparison.Ordinal));
        await using var connection = await db.Factory.OpenConnectionAsync();
        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT count(*) FROM account_profiles WHERE account_scope_id = 'A'";
        Assert.Equal(0L, await query.ExecuteScalarAsync());
        Assert.NotNull(await db.Run("B", () => db.Profiles.FindAccountProfileAsync("B")));
    }

    private static async Task<bool> ObserveSync(PersonalTradingPostSynchronizationService service) => (await service.SynchronizeAsync()).IsSuccess;
    private static async Task<bool> ObserveCrafting(AccountCraftingSnapshotService service) => (await service.RefreshAsync()).IsSuccess;
    private static async Task<bool> ObserveHoldings(AccountHoldingsGateway gateway)
    {
        try { return (await gateway.CollectAsync(Now)).IsSuccess; }
        catch (AccountWorkRejectedException) { return false; }
    }

    [Fact]
    public async Task Private_storage_requires_admission_and_rejects_mixed_account_targets()
    {
        await using var db = await Database.CreateAsync();
        await Assert.ThrowsAsync<AccountWorkRejectedException>(() => db.Profiles.GetOrCreateAccountProfileAsync("A", Now));
        await Assert.ThrowsAsync<AccountWorkRejectedException>(() => db.Run("A", () => db.Profiles.GetOrCreateAccountProfileAsync("B", Now)));
        Assert.NotNull(await db.Run("A", () => db.Profiles.GetOrCreateAccountProfileAsync("A", Now)));
    }

    [Fact]
    public async Task Completion_transaction_finishes_before_a_waiting_transition_without_torn_receipt()
    {
        await using var db = await Database.CreateAsync();
        var profile = await db.Run("A", () => db.Profiles.GetOrCreateAccountProfileAsync("A", Now));
        var plan = new PlanRecord("race-plan", 1, "race-opportunity", PlanAttention.Active, PlanState.InProgress,
            PlanReconciliationState.None, Now, [], Money.Zero, 0,
            [new("step", PlanStepAction.BuyNow, 42, "Objet", 1, new Money(100), [], PlanStepState.Current)],
            [], 1, PlanHysteresisPolicy.Default);
        var repository = new SqlitePlanRepository(db.Factory, db.Gate);
        await db.Run("A", () => repository.TryStartAsync(profile.Id, plan, new Money(1000), Money.Zero, new Dictionary<string, long>()));
        var entered = Barrier(); var release = Barrier();
        var command = new PlanCompletionCommand(plan.Id, "step", 1, "race-command", PlanCompletionOperation.ReportPerformed, 1, new Money(100));
        var completion = Task.Run(() => db.Run("A", () => repository.CompleteStepAsync(profile.Id, command, current =>
        {
            entered.SetResult(); release.Task.GetAwaiter().GetResult();
            return new PlanOrchestrationService().ReportStep(current, 1, new Money(100), Now);
        })));
        await entered.Task;
        var transition = db.Fence.QuiesceAsync().AsTask();
        Assert.False(transition.IsCompleted);
        release.SetResult();
        Assert.Equal(PlanCompletionStatus.Applied, (await completion).Status);
        await using (var lease = await transition) await lease.PublishAsync();
        var replay = await db.Run("A", () => new PlanCompletionCommandService(repository, new PlanOrchestrationService()).CompleteAsync(profile.Id, command));
        Assert.Equal(PlanCompletionStatus.AlreadyApplied, replay.Status);
        Assert.Equal("race-command", replay.Receipt!.CommandId);
    }

    [Fact]
    public async Task Corrupt_incarnation_never_falls_back_to_ready_private_operations()
    {
        await using var db = await Database.CreateAsync();
        await File.WriteAllTextAsync(db.Factory.DatabasePath + ".incarnation", "invalid");
        var restarted = new AccountWorkFence(new(db.Credential), new FileStoreIncarnationStore(db.Factory));
        await Assert.ThrowsAsync<InvalidDataException>(() => restarted.InitializeAsync());
        await Assert.ThrowsAsync<AccountWorkRejectedException>(() => restarted.RunAsync(_ => Task.FromResult(true)));
    }

    [Fact]
    public async Task Late_account_A_profile_cannot_commit_after_B_admission()
    {
        await using var db = await Database.CreateAsync();
        var entered = Barrier(); var release = Barrier();
        var late = db.Fence.RunAsync(async token =>
        {
            await db.Fence.BindAccountAsync(new("A"), token);
            entered.SetResult(); await release.Task;
            await db.Profiles.GetOrCreateAccountProfileAsync("A", Now, token);
            return true;
        });
        await entered.Task;
        db.Credential.Value = "synthetic-B";
        await db.Run("B", () => db.Profiles.GetOrCreateAccountProfileAsync("B", Now));
        release.SetResult();
        await Assert.ThrowsAsync<AccountWorkRejectedException>(() => late);
        await Assert.ThrowsAsync<AccountWorkRejectedException>(() => db.Run("B", () => db.Profiles.FindAccountProfileAsync("A")));
        Assert.NotNull(await db.Run("B", () => db.Profiles.FindAccountProfileAsync("B")));
    }

    [Fact]
    public async Task Clear_while_fetch_is_blocked_rejects_late_writes_and_allows_a_new_context()
    {
        await using var db = await Database.CreateAsync();
        await db.Run("A", () => db.Profiles.GetOrCreateAccountProfileAsync("A", Now));
        var entered = Barrier(); var release = Barrier();
        var late = db.Fence.RunAsync(async token =>
        {
            await db.Fence.BindAccountAsync(new("A"), token);
            entered.SetResult(); await release.Task;
            return await db.Profiles.GetOrCreateAccountProfileAsync("A", Now, token);
        });
        await entered.Task;
        var previous = await File.ReadAllTextAsync(db.Factory.DatabasePath + ".incarnation");
        await db.Recovery.ClearPersonalDataAsync();
        Assert.NotEqual(previous, await File.ReadAllTextAsync(db.Factory.DatabasePath + ".incarnation"));
        release.SetResult();
        await Assert.ThrowsAsync<AccountWorkRejectedException>(() => late);
        Assert.Null(await db.Run("A", () => db.Profiles.FindAccountProfileAsync("A")));
        Assert.NotNull(await db.Run("A", () => db.Profiles.GetOrCreateAccountProfileAsync("A", Now)));
    }

    [Fact]
    public async Task Older_backup_gets_a_fresh_incarnation_and_cannot_be_overwritten_by_a_late_result()
    {
        await using var db = await Database.CreateAsync();
        await db.Run("A", () => db.Profiles.GetOrCreateAccountProfileAsync("A", Now));
        var backup = await db.Recovery.CreateBackupAsync();
        var profile = (await db.Run("A", () => db.Profiles.FindAccountProfileAsync("A")))!;
        await db.Run("A", async () => { await db.Profiles.RecordSuccessfulSyncAsync(profile, Now); return true; });
        var entered = Barrier(); var release = Barrier();
        var late = db.Fence.RunAsync(async token =>
        {
            await db.Fence.BindAccountAsync(new("A"), token);
            entered.SetResult(); await release.Task;
            await db.Profiles.RecordSuccessfulSyncAsync(profile, Now.AddMinutes(1), token);
            return true;
        });
        await entered.Task;
        var oldGeneration = db.Fence.Generation;
        var result = await db.Recovery.RestoreManagedBackupAsync(backup.FileName);
        Assert.Equal(LocalDataRestoreOutcome.Restored, result.Outcome);
        Assert.NotNull(result.PreRestoreBackupFileName);
        Assert.NotEqual(oldGeneration, db.Fence.Generation);
        release.SetResult();
        await Assert.ThrowsAsync<AccountWorkRejectedException>(() => late);
        Assert.Null((await db.Run("A", () => db.Profiles.FindAccountProfileAsync("A")))!.LastSuccessfulSyncAtUtc);
        Assert.NotNull(await db.Run("A", () => db.Profiles.FindAccountProfileAsync("A")));
        var restarted = new AccountWorkFence(new(db.Credential), new FileStoreIncarnationStore(db.Factory));
        await restarted.InitializeAsync();
        Assert.NotEqual(db.Fence.Generation, restarted.Generation);
    }

    [Fact]
    public async Task Invalid_restore_preserves_live_data_and_publishes_a_new_usable_generation()
    {
        await using var db = await Database.CreateAsync();
        await db.Run("A", () => db.Profiles.GetOrCreateAccountProfileAsync("A", Now));
        var old = db.Fence.Generation;
        using var invalid = new MemoryStream([1, 2, 3]);
        Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await db.Recovery.RestoreAsync(invalid)).Outcome);
        Assert.NotEqual(old, db.Fence.Generation);
        Assert.NotNull(await db.Run("A", () => db.Profiles.FindAccountProfileAsync("A")));
    }

    [Fact]
    public async Task Failed_replace_preserves_live_data_and_backup_but_keeps_private_work_unavailable()
    {
        await using var db = await Database.CreateAsync();
        await db.Run("A", () => db.Profiles.GetOrCreateAccountProfileAsync("A", Now));
        var backup = await db.Recovery.CreateBackupAsync();
        var recovery = new SqliteLocalDataRecoveryService(db.Factory, db.Gate, fileOperations: new FailedReplacement(), fence: db.Fence);
        Assert.Equal(LocalDataRestoreOutcome.RestoreFailed, (await recovery.RestoreManagedBackupAsync(backup.FileName)).Outcome);
        await Assert.ThrowsAsync<AccountWorkRejectedException>(() => db.Run("A", () => db.Profiles.FindAccountProfileAsync("A")));
        await using var connection = await db.Factory.OpenConnectionAsync();
        await using var query = connection.CreateCommand(); query.CommandText = "SELECT count(*) FROM account_profiles";
        Assert.Equal(1L, await query.ExecuteScalarAsync());
        Assert.True(File.Exists(Path.Combine(db.Recovery.GetLocation().BackupDirectoryPath, backup.FileName)));
        Assert.Contains(db.Recovery.GetLocation().ManagedBackups, value => value.FileName.Contains("pre-restore", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Incarnation_publish_failure_leaves_cleared_data_unavailable_and_restart_is_fresh()
    {
        await using var db = await Database.CreateAsync();
        var failing = new FailingStore(new FileStoreIncarnationStore(db.Factory));
        var fence = new AccountWorkFence(new(db.Credential), failing);
        await fence.InitializeAsync();
        var gate = new SqliteDatabaseGate(fence);
        var recovery = new SqliteLocalDataRecoveryService(db.Factory, gate, fence: fence);
        await Assert.ThrowsAsync<IOException>(() => recovery.ClearPersonalDataAsync());
        await Assert.ThrowsAsync<AccountWorkRejectedException>(() => fence.RunAsync(_ => Task.FromResult(true)));
        var restarted = new AccountWorkFence(new(db.Credential), new FileStoreIncarnationStore(db.Factory));
        await restarted.InitializeAsync();
        Assert.NotEqual(fence.Generation, restarted.Generation);
        Assert.True(await restarted.RunAsync(_ => Task.FromResult(true)));
    }

    [Fact]
    public async Task Restore_generation_publication_failure_is_safe_and_restart_reads_only_valid_restored_data()
    {
        await using var db = await Database.CreateAsync();
        var profile = await db.Run("A", () => db.Profiles.GetOrCreateAccountProfileAsync("A", Now));
        var backup = await db.Recovery.CreateBackupAsync();
        await db.Run("A", async () => { await db.Profiles.RecordSuccessfulSyncAsync(profile, Now); return true; });
        var fence = new AccountWorkFence(new(db.Credential), new FailingStore(new FileStoreIncarnationStore(db.Factory)));
        await fence.InitializeAsync();
        var recovery = new SqliteLocalDataRecoveryService(db.Factory, new SqliteDatabaseGate(fence), fence: fence);
        var result = await recovery.RestoreManagedBackupAsync(backup.FileName);
        Assert.Equal(LocalDataRestoreOutcome.RestoreFailed, result.Outcome);
        Assert.NotNull(result.PreRestoreBackupFileName);
        await Assert.ThrowsAsync<AccountWorkRejectedException>(() => fence.RunAsync(_ => Task.FromResult(true)));
        var restarted = new AccountWorkFence(new(db.Credential), new FileStoreIncarnationStore(db.Factory));
        await restarted.InitializeAsync();
        var profiles = new SqlitePersonalTradingPostRepository(db.Factory, new SqliteDatabaseGate(restarted));
        var restored = await restarted.RunAsync(async token =>
        {
            await restarted.BindAccountAsync(new("A"), token);
            return await profiles.FindAccountProfileAsync("A", token);
        });
        Assert.Null(restored!.LastSuccessfulSyncAtUtc);
        Assert.NotEqual(fence.Generation, restarted.Generation);
    }

    [Fact]
    public async Task Completion_receipt_replays_under_new_scope_after_restart_but_old_work_cannot_lookup_it()
    {
        await using var db = await Database.CreateAsync();
        var profile = await db.Run("A", () => db.Profiles.GetOrCreateAccountProfileAsync("A", Now));
        var plan = new PlanRecord("plan", 1, "opportunity", PlanAttention.Active, PlanState.InProgress,
            PlanReconciliationState.None, Now, [], Money.Zero, 0,
            [new("step", PlanStepAction.BuyNow, 42, "Objet", 1, new Money(100), [], PlanStepState.Current)],
            [], 1, PlanHysteresisPolicy.Default);
        var plans = new SqlitePlanRepository(db.Factory, db.Gate);
        Assert.Equal(PlanStartResult.Started, await db.Run("A", () => plans.TryStartAsync(profile.Id, plan,
            new Money(1000), Money.Zero, new Dictionary<string, long>())));
        var command = new PlanCompletionCommand("plan", "step", 1, "known-command", PlanCompletionOperation.ReportPerformed, 1, new Money(100));
        var commands = new PlanCompletionCommandService(plans, new PlanOrchestrationService());
        Assert.Equal(PlanCompletionStatus.Applied, (await db.Run("A", () => commands.CompleteAsync(profile.Id, command))).Status);
        var entered = Barrier(); var release = Barrier();
        var stale = db.Fence.RunAsync(async token =>
        {
            await db.Fence.BindAccountAsync(new("A"), token);
            entered.SetResult(); await release.Task;
            return await commands.CompleteAsync(profile.Id, command, token);
        });
        await entered.Task;
        await using (var transition = await db.Fence.QuiesceAsync()) await transition.PublishAsync();
        release.SetResult();
        await Assert.ThrowsAsync<AccountWorkRejectedException>(() => stale);
        var restarted = new AccountWorkFence(new(db.Credential), new FileStoreIncarnationStore(db.Factory));
        await restarted.InitializeAsync();
        var restartedPlans = new SqlitePlanRepository(db.Factory, new SqliteDatabaseGate(restarted));
        var replay = await restarted.RunAsync(async token =>
        {
            await restarted.BindAccountAsync(new("A"), token);
            return await new PlanCompletionCommandService(restartedPlans, new PlanOrchestrationService()).CompleteAsync(profile.Id, command, token);
        });
        Assert.Equal(PlanCompletionStatus.AlreadyApplied, replay.Status);
        Assert.Equal("known-command", replay.Receipt!.CommandId);
    }

    [Fact]
    public async Task Credential_bundle_remains_captured_but_changed_source_rejects_commit_and_ABA_never_revives_work()
    {
        await using var db = await Database.CreateAsync();
        var captured = new CapturedGw2ApiKeySource(db.Fence);
        var entered = Barrier(); var release = Barrier();
        var stale = db.Fence.RunAsync(async token =>
        {
            var first = await captured.ReadAsync(token);
            var originalGeneration = db.Fence.Current!.Generation;
            entered.SetResult(); await release.Task;
            Assert.Equal(first.Value, (await captured.ReadAsync(token)).Value);
            Assert.Equal(originalGeneration, db.Fence.Current!.Generation);
            await using var commit = await db.Fence.AcquireCommitAsync(token);
            return true;
        });
        await entered.Task;
        db.Credential.Value = "synthetic-B";
        await db.Run("B", () => db.Profiles.GetOrCreateAccountProfileAsync("B", Now));
        db.Credential.Value = "synthetic-A";
        await db.Run("A", () => db.Profiles.GetOrCreateAccountProfileAsync("A", Now));
        release.SetResult();
        await Assert.ThrowsAsync<AccountWorkRejectedException>(() => stale);
    }

    private static TaskCompletionSource Barrier() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class BlockedValidation(IGw2ApiKeySource source) : IAccountConnectionStatusService
    {
        internal TaskCompletionSource Entered { get; } = Barrier();
        internal TaskCompletionSource Release { get; } = Barrier();
        internal int Calls;
        public async Task<AccountConnectionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            var credential = await source.ReadAsync(cancellationToken);
            if (credential.Value == "synthetic-A")
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
                return new(AccountConnectionState.Valid, ["account", "tradingpost", "wallet"], []);
            }
            return new(AccountConnectionState.InsufficientPermissions, ["account"], ["tradingpost", "wallet"]);
        }
    }
    private sealed class NoDelay : IGw2RequestDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class BlockedIdentityTransport(string account = "A") : HttpMessageHandler
    {
        internal TaskCompletionSource Entered { get; } = Barrier();
        internal TaskCompletionSource Release { get; } = Barrier();
        internal int Requests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            Entered.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent($"{{\"id\":\"{account}\"}}", Encoding.UTF8, "application/json") };
        }
    }
    private sealed class BlockedTransport(bool fail) : HttpMessageHandler
    {
        internal TaskCompletionSource Entered { get; } = Barrier();
        internal TaskCompletionSource Release { get; } = Barrier();
        internal System.Collections.Concurrent.ConcurrentBag<string> Credentials { get; } = [];
        internal System.Collections.Concurrent.ConcurrentBag<string> Paths { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Credentials.Add(request.Headers.Authorization!.Parameter!);
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(request.RequestUri.PathAndQuery);
            if (path.EndsWith("/account", StringComparison.Ordinal)) return Json("{\"id\":\"A\"}");
            Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken);
            if (fail) return new(HttpStatusCode.Forbidden);
            var response = Json(path.EndsWith("/delivery", StringComparison.Ordinal) ? "{\"coins\":0,\"items\":[]}" : "[]");
            if (path.Contains("/transactions/", StringComparison.Ordinal))
            {
                var buys = path.EndsWith("/current/buys", StringComparison.Ordinal);
                if (buys)
                {
                    response.Dispose();
                    var id = request.RequestUri.Query.Contains("page=1", StringComparison.Ordinal) ? 2 : 1;
                    response = Json($"[{{\"id\":{id},\"item_id\":42,\"price\":100,\"quantity\":1,\"created\":\"2026-10-01T12:00:00Z\"}}]");
                }
                response.Headers.Add("X-Page-Size", "1"); response.Headers.Add("X-Page-Total", buys ? "2" : "0");
                response.Headers.Add("X-Result-Count", buys ? "1" : "0"); response.Headers.Add("X-Result-Total", buys ? "2" : "0");
            }
            return response;
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
    private sealed class ImmediateScheduler : IGw2RequestScheduler
    {
        public async Task<T> ScheduleAsync<T>(Gw2RequestKey requestKey, Func<CancellationToken, Task<Gw2ScheduledResult<T>>> sendAsync, CancellationToken cancellationToken) => (await sendAsync(cancellationToken)).Result;
    }
    private sealed class MetadataMarket : IGw2ApiClient
    {
        public Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>.Success(itemIds.Select(id => new MarketItemMetadata(id, "Objet", 250)).ToArray()));
    }
    private sealed class Credential : IGw2ApiKeySource
    {
        internal string Value { get; set; } = "synthetic-A";
        public ValueTask<Gw2ApiKeyReadResult> ReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Gw2ApiKeyReadResult.FromValue(Value));
    }
    private sealed class FailedReplacement : ILocalDataFileOperations
    {
        public void MoveFile(string stagingPath, string backupPath) => File.Move(stagingPath, backupPath);
        public void ReplaceDatabase(string stagedDatabasePath, string liveDatabasePath) => throw new IOException("Synthetic replacement failure.");
    }
    private sealed class FailingStore(IStoreIncarnationStore inner) : IStoreIncarnationStore
    {
        public Task<string> ReadOrCreateAsync(CancellationToken token) => inner.ReadOrCreateAsync(token);
        public Task<string> RotateAsync(CancellationToken token) => throw new IOException("Synthetic publication failure.");
    }
    private sealed class Database : IAsyncDisposable
    {
        internal Credential Credential { get; } = new();
        internal SqliteConnectionFactory Factory { get; }
        internal AccountWorkFence Fence { get; }
        internal SqliteDatabaseGate Gate { get; }
        internal PersonalDataOperationGate OperationGate { get; } = new();
        internal SqlitePersonalTradingPostRepository Profiles { get; }
        internal SqliteLocalDataRecoveryService Recovery { get; }
        private Database(string path)
        {
            Factory = new(path);
            Fence = new(new(Credential), new FileStoreIncarnationStore(Factory));
            Gate = new(Fence); Profiles = new(Factory, Gate); Recovery = new(Factory, Gate, OperationGate, fence: Fence);
        }
        internal static async Task<Database> CreateAsync()
        {
            var db = new Database(Path.Combine(Path.GetTempPath(), "TyrianLedger.Generation.Tests", Guid.NewGuid().ToString("N"), "test.db"));
            await new SqliteSchemaMigrator(db.Factory).MigrateAsync(); await db.Fence.InitializeAsync(); return db;
        }
        internal Task<T> Run<T>(string account, Func<Task<T>> work) => Fence.RunAsync(async token =>
        {
            await Fence.BindAccountAsync(new(account), token); return await work();
        });
        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools(); Directory.Delete(Path.GetDirectoryName(Factory.DatabasePath)!, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
