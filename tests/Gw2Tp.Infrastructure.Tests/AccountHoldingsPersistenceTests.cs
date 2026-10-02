using System.Text.Json;
using Gw2Tp.Application.AccountEvidence;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.LocalData;
using Gw2Tp.Application.Plans;
using Gw2Tp.Domain.Finance;
using Gw2Tp.Infrastructure.Persistence;
using Gw2Tp.Infrastructure.Crafting;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Infrastructure.Secrets;
using Microsoft.Data.Sqlite;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Time;
using Xunit;
using static Gw2Tp.Testing.HoldingsEvidenceFixture;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class AccountHoldingsPersistenceTests
{
    [Fact]
    public async Task Real_holdings_and_crafting_consumers_share_public_references_across_refresh_and_recovery()
    {
        await using var db = await Database.Create();
        var now = DateTimeOffset.UtcNow;
        var collector = new Collector(Snapshot(now, [Item(10, 6)]).Capture);
        var clock = new PublicReferenceCacheTests.Clock { Now = now };
        var cache = PublicReferenceCacheTests.Cache(clock);
        using var handler = new PublicReferenceCacheTests.ReferenceHandler();
        using var http = PublicReferenceCacheTests.Client(handler);
        using var scheduler = PublicReferenceCacheTests.Scheduler();
        var market = PublicReferenceCacheTests.Market(http, scheduler, cache);
        var holdings = new AccountHoldingsSnapshotService(collector, db.Holdings, market,
            new DefaultAccountHoldingsRulesProvider(), clock, db.Fence);
        var snapshots = new AccountCraftingSnapshotService(null!, null!, db.Fence, holdings);
        var planner = new CraftingOpportunityService(new PublicReferenceConsumerScope(), snapshots,
            new CraftingReferenceGateway(http, scheduler, cache), market, new PublicReferenceConsumerHistory(now),
            new CraftingOpportunityPlanner(new CraftingEconomicsCalculator()), holdings: holdings);
        Assert.True((await holdings.RefreshAsync()).IsSuccess);
        var first = await db.Run(() => planner.GetAsync());
        Assert.Equal(new[] { 1, 2 }, first.Opportunities.Select(value => value.Recipe.RecipeId));
        Assert.All(first.Opportunities, value => Assert.False(value.IsActionable));
        Assert.True((await holdings.RefreshAsync()).IsSuccess);
        var second = await db.Run(() => planner.GetAsync());
        Assert.Equal(JsonSerializer.Serialize(first.Opportunities), JsonSerializer.Serialize(second.Opportunities));
        Assert.Equal(first.State, second.State);
        Assert.Equal(first.SummaryExclusions, second.SummaryExclusions);
        Assert.Equal(2, collector.Calls); // Private captures still happen on every refresh.
        Assert.Equal(new[] { "/v2/items:10", "/v2/items:100" }, handler.Reads.Where(read => read.StartsWith("/v2/items:", StringComparison.Ordinal)));
        Assert.Single(handler.Reads, read => read.StartsWith("/v2/recipes:", StringComparison.Ordinal));
        Assert.Equal(2, handler.Reads.Count(read => read.StartsWith("/v2/commerce/listings:", StringComparison.Ordinal)));
        var backup = await db.Recovery.CreateBackupAsync();
        await db.Recovery.ClearPersonalDataAsync();
        Assert.Null(await db.Run(() => holdings.GetProjectionAsync(Scope, now)));
        Assert.Equal(LocalDataRestoreOutcome.Restored, (await db.Recovery.RestoreManagedBackupAsync(backup.FileName)).Outcome);
        var restored = await db.Run(() => holdings.GetProjectionAsync(Scope, now));
        Assert.Empty(restored!.Quantities); // Public hits do not grant restored private-generation eligibility.
        Assert.True((await holdings.RefreshAsync()).IsSuccess);
        Assert.Equal(2, handler.Reads.Count(read => read.StartsWith("/v2/items:", StringComparison.Ordinal)));
        Assert.Equal(3, collector.Calls);
        db.Credential.Value = "synthetic-other-account-key";
        await db.Fence.RunAsync(async token => {
            var other = new Gw2Tp.Application.PersonalTradingPost.AccountScope("synthetic-other-account");
            await db.Fence.BindAccountAsync(other, token);
            Assert.Null(await db.Holdings.GetLatestAsync(other, token));
            Assert.True((await market.GetItemMetadataAsync([10], token)).IsSuccess);
            return true;
        });
        Assert.Equal(2, handler.Reads.Count(read => read.StartsWith("/v2/items:", StringComparison.Ordinal)));
        await db.Migrator.ValidatePersistedDataAsync();
    }

    private sealed class PublicReferenceConsumerScope : Gw2Tp.Application.PersonalTradingPost.IPersonalTradingPostGateway
    {
        public Task<Gw2ApiResult<Gw2Tp.Application.PersonalTradingPost.AccountScope>> GetAccountScopeAsync(CancellationToken token = default) =>
            Task.FromResult(Gw2ApiResult<Gw2Tp.Application.PersonalTradingPost.AccountScope>.Success(Scope));
        public Task<Gw2ApiResult<Gw2Tp.Application.PersonalTradingPost.PersonalTransactionPage>> GetCurrentBuyOrdersAsync(int page, CancellationToken token = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<Gw2Tp.Application.PersonalTradingPost.PersonalTransactionPage>> GetCurrentSellListingsAsync(int page, CancellationToken token = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<Gw2Tp.Application.PersonalTradingPost.PersonalTransactionPage>> GetCompletedBuyHistoryAsync(int page, CancellationToken token = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<Gw2Tp.Application.PersonalTradingPost.PersonalTransactionPage>> GetCompletedSellHistoryAsync(int page, CancellationToken token = default) => throw new NotSupportedException();
    }
    private sealed class PublicReferenceConsumerHistory(DateTimeOffset now) : IHistoricalMarketAnalyticsService
    {
        public Task<HistoricalMarketAnalytics> GetAsync(int id, CancellationToken token = default) => GetAtAsync(id, now, token);
        public Task<HistoricalMarketAnalytics> GetAtAsync(int id, DateTimeOffset at, CancellationToken token = default) =>
            Task.FromResult(new HistoricalMarketAnalytics(id, at, false, HistoricalMarketAnalyticsSettings.Default, null, []));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Guarded_refresh_collects_and_publishes_normalized_sources_with_bounded_category_reads(bool metadataFails)
    {
        await using var db = await Database.Create();
        var now = DateTimeOffset.UtcNow;
        var capture = Snapshot(now, Enumerable.Range(1, 201).Select(id => Item(id, 1, slot: id)).ToArray()).Capture;
        var collector = new Collector(capture);
        var market = new Metadata(metadataFails);
        var service = new AccountHoldingsSnapshotService(collector, db.Holdings, market,
            new DefaultAccountHoldingsRulesProvider(), new Clock(now), db.Fence);
        var result = await service.RefreshAsync();
        Assert.True(result.IsSuccess);
        Assert.Equal(1, collector.Calls);
        Assert.Equal(new[] { 200, 1 }, market.BatchSizes);
        Assert.Equal(capture.RefreshId, result.Value!.Capture.RefreshId);
        var projection = await db.Run(() => service.GetProjectionAsync(Scope, now));
        Assert.Equal(metadataFails ? 0 : 201, projection!.Quantities.Count);
        Assert.Equal(PlanEvidenceCompleteness.Partial, AccountHoldingsProjector.PhysicalEvidence(projection).Provenance.Completeness);
        await db.Migrator.ValidatePersistedDataAsync();
    }

    [Fact]
    public async Task Refresh_adapter_uses_the_guarded_holdings_collector_and_preserves_permission_failure()
    {
        await using var db = await Database.Create();
        var now = DateTimeOffset.UtcNow;
        var capture = Snapshot(now, [Item(10, 1, AccountHoldingsSource.MaterialStorage)]).Capture;
        capture = capture with { Bank = capture.Bank with { Value = null, Availability = EvidenceAvailability.MissingPermission,
            Completeness = EvidenceCompleteness.Unknown, ErrorCategory = Gw2ApiErrorCategory.Forbidden } };
        var collector = new Collector(capture);
        var holdings = new AccountHoldingsSnapshotService(collector, db.Holdings, new Metadata(false),
            new DefaultAccountHoldingsRulesProvider(), new Clock(now), db.Fence);
        // Null legacy seams prove the production adapter cannot call the old inventory authority.
        var adapter = new AccountCraftingSnapshotService(null!, null!, db.Fence, holdings);
        var result = await adapter.RefreshWithOutcomeAsync();
        Assert.True(result.Result.IsSuccess);
        Assert.Equal(1, collector.Calls);
        Assert.Equal(CraftingFeatureAvailability.MissingPermission, result.Result.Value!.BankInventory.Availability);
        Assert.Equal(Gw2ApiErrorCategory.Forbidden, result.Result.Value.BankInventory.ErrorCategory);
        Assert.Equal(CraftingFeatureAvailability.Available, result.Result.Value.MaterialStorage.Availability);
        var read = (await db.Run(() => adapter.GetLatestAsync(Scope)))!;
        Assert.Equal(result.Result.Value.CapturedAtUtc, read.CapturedAtUtc);
        Assert.Equal(result.Result.Value.BankInventory, read.BankInventory);
        Assert.Equal(result.Result.Value.MaterialStorage.Value, read.MaterialStorage.Value);
        Assert.Equal(result.Result.Value.RecipeUnlocks.Value, read.RecipeUnlocks.Value);
        Assert.Equal(result.Result.Value.CharacterCrafting.Value, read.CharacterCrafting.Value);
    }

    [Fact]
    public async Task Normalized_sources_floor_and_actor_facts_survive_restart_but_old_generation_cannot_admit()
    {
        await using var db = await Database.Create();
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, [Item(10, 6), Item(10, 4, AccountHoldingsSource.CharacterInventory, A.ActorId)], deliveryQuantity: 7);
        await db.Run(async () => Assert.True(await db.Holdings.ReplaceAsync(db.Current(snapshot))));
        var stored = await db.Run(() => db.Holdings.GetLatestAsync(Scope));
        Assert.Equal(6, Project(stored!, db).Quantities["2:10"]);
        var restarted = new AccountWorkFence(new(db.Credential), new FileStoreIncarnationStore(db.Factory));
        await restarted.InitializeAsync();
        var repo = new SqliteAccountHoldingsSnapshotRepository(db.Factory, new SqliteDatabaseGate(restarted), restarted);
        await restarted.RunAsync(async token =>
        {
            await restarted.BindAccountAsync(Scope, token);
            var evidence = (await repo.GetLatestAsync(Scope, token))!;
            Assert.Equal(snapshot.Capture.RefreshId, evidence.Capture.RefreshId);
            Assert.Equal(2, new AccountHoldingsProjector().Project(evidence, now, evidence.Generation, evidence.StoreIncarnation).Rows.Count);
            Assert.Empty(new AccountHoldingsProjector().Project(evidence, now, restarted.Current!.Generation, restarted.Current.StoreIncarnation).Quantities);
            Assert.Equal(7, Assert.Single(evidence.Capture.Delivery.Value!.ObservedTotals).Quantity);
            Assert.Equal(A, Assert.Single(evidence.Capture.Characters).Actor);
            return true;
        });
        await db.Migrator.ValidatePersistedDataAsync();
    }

    [Fact]
    public async Task Partial_source_keeps_stale_rows_and_floor_without_granting_stock_and_replays_do_not_grow_them()
    {
        await using var db = await Database.Create();
        var now = DateTimeOffset.UtcNow;
        var first = Snapshot(now, [Item(10, 10)], floor: new HashSet<int> { 11 });
        await db.Run(async () => Assert.True(await db.Holdings.ReplaceAsync(db.Current(first))));
        var second = Snapshot(now.AddSeconds(1));
        second = second with { Capture = second.Capture with { Bank = second.Capture.Bank with
            { Availability = EvidenceAvailability.Unavailable, Completeness = EvidenceCompleteness.Unknown, Value = null,
                ErrorCategory = Gw2Tp.Application.MarketData.Gw2ApiErrorCategory.TransportFailure } } };
        await db.Run(async () => Assert.True(await db.Holdings.ReplaceAsync(db.Current(second))));
        await db.Run(async () => Assert.True(await db.Holdings.ReplaceAsync(db.Current(second))));
        var stored = (await db.Run(() => db.Holdings.GetLatestAsync(Scope)))!;
        Assert.Equal(10, Assert.Single(stored.StaleObservations).Quantity);
        Assert.Contains(11, stored.ProtectionFloor.ItemIds);
        Assert.Empty(Project(stored, db, now.AddSeconds(2)).Quantities);
        Assert.Equal(EvidenceAvailability.Unavailable, stored.Capture.Bank.Availability);
        await db.Run(async () => Assert.False(await db.Holdings.ReplaceAsync(db.Current(first))));
        Assert.Equal(second.Capture.RefreshId, (await db.Run(() => db.Holdings.GetLatestAsync(Scope)))!.Capture.RefreshId);
    }

    [Fact]
    public async Task Snapshot_failure_rolls_back_both_evidence_and_floor_and_preserves_previous_valid_projection()
    {
        await using var db = await Database.Create();
        var now = DateTimeOffset.UtcNow;
        var first = Snapshot(now, [Item(10, 6)]);
        await db.Run(async () => Assert.True(await db.Holdings.ReplaceAsync(db.Current(first))));
        await db.Sql("CREATE TRIGGER test_abort BEFORE UPDATE ON account_holdings_snapshots BEGIN SELECT RAISE(ABORT, 'synthetic rollback'); END;");
        var next = Snapshot(now.AddSeconds(1), [Item(10, 50)], floor: new HashSet<int> { 10 });
        await Assert.ThrowsAsync<SqliteException>(() => db.Run(() => db.Holdings.ReplaceAsync(db.Current(next))));
        await db.Sql("DROP TRIGGER test_abort;");
        var stored = (await db.Run(() => db.Holdings.GetLatestAsync(Scope)))!;
        Assert.Equal(first.Capture.RefreshId, stored.Capture.RefreshId);
        Assert.Empty(stored.ProtectionFloor.ItemIds);
        Assert.Equal(6, Project(stored, db).Quantities["2:10"]);
        await db.Migrator.ValidatePersistedDataAsync();
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("restore")]
    public async Task Recovery_covers_holdings_and_rejects_late_old_generation_publication(string operation)
    {
        await using var db = await Database.Create();
        var now = DateTimeOffset.UtcNow;
        var original = Snapshot(now, [Item(10, 10)]);
        await db.Run(async () => Assert.True(await db.Holdings.ReplaceAsync(db.Current(original))));
        var backup = await db.Recovery.CreateBackupAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = db.Run(async () =>
        {
            var next = db.Current(Snapshot(now.AddSeconds(1), [Item(10, 99)]));
            entered.SetResult(); await release.Task;
            return await db.Holdings.ReplaceAsync(next);
        });
        await entered.Task;
        if (operation == "clear") await db.Recovery.ClearPersonalDataAsync();
        else Assert.Equal(Gw2Tp.Application.LocalData.LocalDataRestoreOutcome.Restored,
            (await db.Recovery.RestoreManagedBackupAsync(backup.FileName)).Outcome);
        release.SetResult();
        await Assert.ThrowsAsync<AccountWorkRejectedException>(() => late);
        var stored = await db.Run(() => db.Holdings.GetLatestAsync(Scope));
        if (operation == "clear") Assert.Null(stored);
        else
        {
            Assert.Equal(original.Capture.RefreshId, stored!.Capture.RefreshId);
            Assert.Empty(Project(stored, db).Quantities);
        }
        await db.Migrator.ValidatePersistedDataAsync();
    }

    [Theory]
    [InlineData("negative")]
    [InlineData("ownership")]
    [InlineData("unknown-field")]
    public async Task Integrity_and_restore_reject_domain_invalid_or_unrecognized_normalized_documents(string corruption)
    {
        await using var db = await Database.Create();
        var now = DateTimeOffset.UtcNow;
        await db.Run(async () => Assert.True(await db.Holdings.ReplaceAsync(db.Current(Snapshot(now, [Item(10, 6)])))));
        var valid = await db.Recovery.CreateBackupAsync();
        var sql = corruption switch
        {
            "negative" => "UPDATE account_holdings_snapshots SET payload_json=json_set(payload_json, '$.capture.bank.value.items[0].quantity', -1);",
            "ownership" => "UPDATE account_holdings_snapshots SET payload_json=json_set(payload_json, '$.capture.accountScope.accountId', 'different');",
            _ => "UPDATE account_holdings_snapshots SET payload_json=json_set(payload_json, '$.unrecognizedValue', 'synthetic');",
        };
        await db.Sql(sql);
        await Assert.ThrowsAsync<InvalidDataException>(() => db.Migrator.ValidatePersistedDataAsync());
        var damagedPath = Path.Combine(Path.GetDirectoryName(db.Factory.DatabasePath)!, "damaged.db");
        File.Copy(db.Factory.DatabasePath, damagedPath);
        await Assert.ThrowsAsync<InvalidDataException>(() => SqliteSchemaMigrator.ValidateBackupCandidateAsync(damagedPath));
        Assert.Equal(Gw2Tp.Application.LocalData.LocalDataRestoreOutcome.Restored,
            (await db.Recovery.RestoreManagedBackupAsync(valid.FileName)).Outcome);
    }

    [Fact]
    public async Task Atomic_start_uses_latest_stored_location_and_global_item_reservations_instead_of_caller_totals()
    {
        await using var db = await Database.Create();
        var now = DateTimeOffset.UtcNow;
        var first = Snapshot(now, [Item(10, 10)]);
        var profile = await db.Run(() => db.Profiles.GetOrCreateAccountProfileAsync(Scope.AccountId, now));
        await db.Run(async () => Assert.True(await db.Holdings.ReplaceAsync(db.Current(first))));
        var projection = Project((await db.Run(() => db.Holdings.GetLatestAsync(Scope)))!, db);
        var candidate = Sale("one", 7);
        var orchestration = new PlanOrchestrationService();
        var plan = orchestration.Start(PlanHoldingsAdmission.Authorize(candidate, projection), now);
        var inflated = new Dictionary<string, long> { ["2:10"] = 200 };
        Assert.Equal(PlanStartResult.Started, await db.Run(() => db.Plans.TryStartAsync(profile.Id, plan, new Money(1000), Money.Zero, inflated)));
        var second = orchestration.Start(PlanHoldingsAdmission.Authorize(Sale("two", 7), projection), now);
        Assert.Equal(PlanStartResult.ResourcesUnavailable, await db.Run(() => db.Plans.TryStartAsync(profile.Id, second, new Money(1000), Money.Zero, inflated)));
        await db.Run(async () => Assert.True(await db.Holdings.ReplaceAsync(db.Current(Snapshot(now.AddMilliseconds(1), [Item(10, 10, AccountHoldingsSource.MaterialStorage)])))));
        var moved = orchestration.Start(PlanHoldingsAdmission.Authorize(Sale("three", 1), projection), now);
        Assert.Equal(PlanStartResult.ResourcesUnavailable, await db.Run(() => db.Plans.TryStartAsync(profile.Id, moved, new Money(1000), Money.Zero, inflated)));
    }

    [Fact]
    public async Task Completion_receipt_replay_and_concurrent_commands_never_swap_a_location_or_tear_effects()
    {
        await using var db = await Database.Create();
        var now = DateTimeOffset.UtcNow;
        var profile = await db.Run(() => db.Profiles.GetOrCreateAccountProfileAsync(Scope.AccountId, now));
        await db.Run(async () => Assert.True(await db.Holdings.ReplaceAsync(db.Current(Snapshot(now, [Item(10, 10)])))));
        var projection = Project((await db.Run(() => db.Holdings.GetLatestAsync(Scope)))!, db);
        var orchestration = new PlanOrchestrationService();
        var plan = orchestration.Start(PlanHoldingsAdmission.Authorize(Sale("one", 4), projection), now);
        Assert.Equal(PlanStartResult.Started, await db.Run(() => db.Plans.TryStartAsync(profile.Id, plan, new Money(1000), Money.Zero, projection.Quantities)));
        var stored = Assert.Single(await db.Run(() => db.Plans.GetStartedAsync(profile.Id)));
        var command = new PlanCompletionCommand(stored.Id, stored.Steps[0].Id, stored.Revision, "one-command", PlanCompletionOperation.ReportPerformed, 4, new Money(100));
        var completion = new PlanCompletionCommandService(db.Plans, orchestration);
        var commands = Enumerable.Range(0, 2).Select(_ => db.Run(() => completion.CompleteAsync(profile.Id, command))).ToArray();
        var outcomes = await Task.WhenAll(commands);
        Assert.Single(outcomes, result => result.Status == PlanCompletionStatus.Applied);
        Assert.Single(outcomes, result => result.Status == PlanCompletionStatus.AlreadyApplied);
        await db.Run(async () => Assert.True(await db.Holdings.ReplaceAsync(db.Current(Snapshot(now.AddMilliseconds(1), [Item(10, 10, AccountHoldingsSource.MaterialStorage)])))));
        Assert.Equal(PlanCompletionStatus.AlreadyApplied, (await db.Run(() => completion.CompleteAsync(profile.Id, command))).Status);
        var final = Assert.Single(await db.Run(() => db.Plans.GetStartedAsync(profile.Id)));
        Assert.Single(final.Events);
        Assert.Equal(stored.Revision + 1, final.Revision);
        Assert.Equal(AccountHoldingsSource.Bank, Assert.Single(final.HoldingsAuthority!.Commitments).Location.Source);
    }

    [Fact]
    public async Task Consuming_completion_rechecks_all_account_reservations_and_latest_quantity_in_one_transaction()
    {
        await using var db = await Database.Create();
        var now = DateTimeOffset.UtcNow;
        var profile = await db.Run(() => db.Profiles.GetOrCreateAccountProfileAsync(Scope.AccountId, now));
        await db.Run(async () => Assert.True(await db.Holdings.ReplaceAsync(db.Current(Snapshot(now, [Item(10, 10)])))));
        var physical = Project((await db.Run(() => db.Holdings.GetLatestAsync(Scope)))!, db);
        var orchestration = new PlanOrchestrationService();
        foreach (var pair in new[] { ("one", 4), ("two", 6) })
        {
            var plan = orchestration.Start(PlanHoldingsAdmission.Authorize(Sale(pair.Item1, pair.Item2), physical), now);
            Assert.Equal(PlanStartResult.Started, await db.Run(() => db.Plans.TryStartAsync(profile.Id, plan, new Money(1000), Money.Zero, physical.Quantities)));
        }
        var first = (await db.Run(() => db.Plans.GetStartedAsync(profile.Id))).Single(plan => plan.SourceOpportunityId == "one");
        await db.Run(async () => Assert.True(await db.Holdings.ReplaceAsync(db.Current(Snapshot(now.AddMilliseconds(1), [Item(10, 7)])))));
        var command = new PlanCompletionCommand(first.Id, first.Steps[0].Id, first.Revision, "consume", PlanCompletionOperation.ReportPerformed, 4, new Money(100));
        var outcome = await db.Run(() => new PlanCompletionCommandService(db.Plans, orchestration).CompleteAsync(profile.Id, command));
        Assert.Equal(PlanCompletionStatus.Conflict, outcome.Status);
        var unchanged = (await db.Run(() => db.Plans.GetStartedAsync(profile.Id))).Single(plan => plan.Id == first.Id);
        Assert.Empty(unchanged.Events);
        Assert.Equal(first.Revision, unchanged.Revision);
        var reconciled = await db.Run(() => db.Plans.ApplyAccountReconciliationAsync(profile.Id, plans => plans));
        Assert.All(reconciled, plan => Assert.Equal(PlanState.RecheckRequired, plan.State));
        Assert.Equal(10, reconciled.SelectMany(PlanOrchestrationService.OutstandingReservations).Sum(value => value.Quantity));
    }

    private static PlanCandidate Sale(string id, int quantity) => new(id, 1, id, PlanAttention.Active,
        [new(id + ":sell", PlanStepAction.SellNow, 10, "Objet", quantity, new Money(100), [], PlanStepState.Pending)],
        [new(PlanResourceKind.Inventory, "10", quantity, Money.Zero)], Money.Zero, Money.Zero, 8000, 0, 1, 1, true, []);
    private static AccountHoldingsProjection Project(AccountHoldingsSnapshot value, Database db, DateTimeOffset? at = null) =>
        new AccountHoldingsProjector().Project(value, at ?? DateTimeOffset.UtcNow, db.Fence.Generation, db.Incarnation);

    private sealed class Credential : IGw2ApiKeySource
    {
        public string Value { get; set; } = "synthetic-holdings-test-key";
        public ValueTask<Gw2ApiKeyReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Gw2ApiKeyReadResult.FromValue(Value));
    }
    private sealed class Clock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }
    private sealed class Collector(AccountHoldingsCapture capture) : IAccountHoldingsCollector
    {
        public int Calls { get; private set; }
        public Task<Gw2ApiResult<AccountHoldingsCapture>> CollectAsync(DateTimeOffset at, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(Gw2ApiResult<AccountHoldingsCapture>.Success(capture)); }
    }
    private sealed class Metadata(bool fails) : IGw2ApiClient
    {
        public List<int> BatchSizes { get; } = [];
        public Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
        {
            BatchSizes.Add(ids.Count);
            return Task.FromResult(fails ? Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>.Failure(Gw2ApiErrorCategory.IncompleteData) :
                Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>.Success(ids.Select(id => new MarketItemMetadata(id, "Objet synthétique", 250, ItemType: "CraftingMaterial")).ToArray()));
        }
        public Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Database : IAsyncDisposable
    {
        public Credential Credential { get; } = new();
        public SqliteConnectionFactory Factory { get; }
        public AccountWorkFence Fence { get; }
        public SqliteSchemaMigrator Migrator { get; }
        public SqliteAccountHoldingsSnapshotRepository Holdings { get; }
        public SqlitePersonalTradingPostRepository Profiles { get; }
        public SqlitePlanRepository Plans { get; }
        public SqliteLocalDataRecoveryService Recovery { get; }
        public string Incarnation { get; private set; } = "";
        private Database(string path)
        {
            Factory = new(path); Fence = new(new(Credential), new FileStoreIncarnationStore(Factory)); Migrator = new(Factory);
            var gate = new SqliteDatabaseGate(Fence);
            Holdings = new(Factory, gate, Fence); Profiles = new(Factory, gate); Plans = new(Factory, gate, Fence);
            Recovery = new(Factory, gate, new PersonalDataOperationGate(), fence: Fence);
        }
        public static async Task<Database> Create()
        {
            var value = new Database(Path.Combine(Path.GetTempPath(), "TyrianLedger.Holdings.Tests", Guid.NewGuid().ToString("N"), "test.db"));
            await value.Migrator.MigrateAsync(); await value.Fence.InitializeAsync();
            await value.Run(() => Task.CompletedTask); return value;
        }
        public AccountHoldingsSnapshot Current(AccountHoldingsSnapshot value) => value with
            { StoreIncarnation = Fence.Current!.StoreIncarnation, Generation = Fence.Current.Generation };
        public Task<T> Run<T>(Func<Task<T>> work) => Fence.RunAsync(async token =>
        {
            await Fence.BindAccountAsync(Scope, token); Incarnation = Fence.Current!.StoreIncarnation; return await work();
        });
        public Task Run(Func<Task> work) => Run(async () => { await work(); return true; });
        public async Task Sql(string sql)
        {
            await using var connection = await Factory.OpenConnectionAsync();
            await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
        }
        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools(); Directory.Delete(Path.GetDirectoryName(Factory.DatabasePath)!, true); return ValueTask.CompletedTask;
        }
    }
}
