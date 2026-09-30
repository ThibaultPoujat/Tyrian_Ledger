using Gw2Tp.Application.LocalData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Application.Finance;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Application.Plans;
using Gw2Tp.Domain.Finance;
using Gw2Tp.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class SqlitePersistenceIntegrationTests
{
    private static readonly DateTimeOffset FirstObservedAtUtc = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondObservedAtUtc = new(2026, 9, 6, 12, 5, 0, TimeSpan.Zero);

    [Fact]
    public async Task Fresh_database_initializes_the_documented_schema_deterministically()
    {
        await using var database = await TestDatabase.CreateAsync(migrate: false);

        Assert.False(File.Exists(database.Path));
        await database.Migrator.MigrateAsync();
        await database.Migrator.MigrateAsync();

        Assert.True(File.Exists(database.Path));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], await database.GetMigrationVersionsAsync());
        Assert.Equal(
            [
                "account_crafting_bank_entries",
                "account_crafting_disciplines",
                "account_crafting_material_entries",
                "account_crafting_recipe_unlocks",
                "account_crafting_snapshots",
                "account_profiles",
                "completed_tp_transactions",
                "current_order_sync_batches",
                "current_tp_order_observations",
                "current_tp_orders",
                "execution_plans",
                "investment_position_exits",
                "investment_position_targets",
                "investment_positions",
                "item_metadata",
                "market_order_book_levels",
                "market_order_book_snapshots",
                "market_price_observations",
                "plan_completion_receipts",
                "schema_migrations",
                "user_settings",
                "watchlist_entries",
            ],
            await database.GetTableNamesAsync());
    }

    [Fact]
    public async Task Version_nine_crafting_entries_are_rebuilt_with_snapshot_foreign_keys_without_losing_rows()
    {
        await using var database = await TestDatabase.CreateAsync(migrate: false);
        await database.Migrator.MigrateToAsync(7);

        await using (var connection = await database.Factory.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO account_profiles (id, account_scope_id, created_at_utc)
                VALUES (1, 'historical-account', '2026-09-16T12:00:00.0000000+00:00');
                CREATE TABLE account_crafting_snapshots (
                    account_profile_id INTEGER PRIMARY KEY,
                    captured_at_utc TEXT NOT NULL,
                    bank_availability INTEGER NOT NULL CHECK (bank_availability BETWEEN 1 AND 3),
                    bank_error_category INTEGER NULL CHECK (bank_error_category BETWEEN 0 AND 11),
                    materials_availability INTEGER NOT NULL CHECK (materials_availability BETWEEN 1 AND 3),
                    materials_error_category INTEGER NULL CHECK (materials_error_category BETWEEN 0 AND 11),
                    recipes_availability INTEGER NOT NULL CHECK (recipes_availability BETWEEN 1 AND 3),
                    recipes_error_category INTEGER NULL CHECK (recipes_error_category BETWEEN 0 AND 11),
                    crafting_availability INTEGER NOT NULL CHECK (crafting_availability BETWEEN 1 AND 3),
                    crafting_error_category INTEGER NULL CHECK (crafting_error_category BETWEEN 0 AND 11),
                    FOREIGN KEY (account_profile_id) REFERENCES account_profiles(id) ON DELETE RESTRICT
                );
                CREATE TABLE account_crafting_bank_entries (
                    account_profile_id INTEGER NOT NULL,
                    item_id INTEGER NOT NULL,
                    binding INTEGER NOT NULL,
                    quantity INTEGER NOT NULL,
                    PRIMARY KEY (account_profile_id, item_id, binding),
                    FOREIGN KEY (account_profile_id) REFERENCES account_profiles(id) ON DELETE RESTRICT
                );
                CREATE TABLE account_crafting_material_entries (
                    account_profile_id INTEGER NOT NULL,
                    item_id INTEGER NOT NULL,
                    category_id INTEGER NOT NULL,
                    binding INTEGER NOT NULL,
                    quantity INTEGER NOT NULL,
                    PRIMARY KEY (account_profile_id, item_id),
                    FOREIGN KEY (account_profile_id) REFERENCES account_profiles(id) ON DELETE RESTRICT
                );
                CREATE TABLE account_crafting_recipe_unlocks (
                    account_profile_id INTEGER NOT NULL,
                    recipe_id INTEGER NOT NULL,
                    PRIMARY KEY (account_profile_id, recipe_id),
                    FOREIGN KEY (account_profile_id) REFERENCES account_profiles(id) ON DELETE RESTRICT
                );
                CREATE TABLE account_crafting_disciplines (
                    account_profile_id INTEGER NOT NULL,
                    discipline TEXT NOT NULL COLLATE BINARY,
                    rating INTEGER NOT NULL,
                    is_active INTEGER NOT NULL,
                    PRIMARY KEY (account_profile_id, discipline),
                    FOREIGN KEY (account_profile_id) REFERENCES account_profiles(id) ON DELETE RESTRICT
                );
                INSERT INTO account_crafting_snapshots VALUES (1, '2026-09-16T12:00:00.0000000+00:00', 1, NULL, 1, NULL, 1, NULL, 1, NULL);
                INSERT INTO account_crafting_bank_entries VALUES (1, 42, 0, 3);
                INSERT INTO account_crafting_material_entries VALUES (1, 43, 5, 1, 4);
                INSERT INTO account_crafting_recipe_unlocks VALUES (1, 44);
                INSERT INTO account_crafting_disciplines VALUES (1, 'Armorsmith', 500, 1);
                INSERT INTO schema_migrations (version, name, applied_at_utc)
                VALUES (8, 'account_crafting_snapshot_schema', '2026-09-16T12:00:00.0000000+00:00');
                """;
            await command.ExecuteNonQueryAsync();
        }

        await database.Migrator.MigrateToAsync(9);
        await database.Migrator.MigrateAsync();

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], await database.GetMigrationVersionsAsync());
        await using var validationConnection = await database.Factory.OpenConnectionAsync();
        await using var validationCommand = validationConnection.CreateCommand();
        validationCommand.CommandText = """
            SELECT COUNT(*) FROM account_crafting_bank_entries
            UNION ALL SELECT COUNT(*) FROM account_crafting_material_entries
            UNION ALL SELECT COUNT(*) FROM account_crafting_recipe_unlocks
            UNION ALL SELECT COUNT(*) FROM account_crafting_disciplines;
            """;
        var counts = new List<long>();
        await using (var reader = await validationCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) counts.Add(reader.GetInt64(0));
        }
        Assert.Equal([1, 1, 1, 1], counts);

        validationCommand.CommandText = "SELECT \"table\" FROM pragma_foreign_key_list('account_crafting_bank_entries');";
        Assert.Equal("account_crafting_snapshots", await validationCommand.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Plan_start_is_atomic_and_repeated_identity_is_idempotent()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("plan-account", FirstObservedAtUtc);
        var plan = new PlanRecord(
            "plan:atomic", 1, "opportunity:atomic", PlanAttention.Active, PlanState.InProgress,
            PlanReconciliationState.None, FirstObservedAtUtc,
            [new PlanResourceRequirement(PlanResourceKind.Cash, "cash", 0, new Money(100))],
            new Money(10), 0,
            [new PlanStep("plan:atomic:1", PlanStepAction.PlaceBuyOrder, 42, "Objet", 1, new Money(100), [], PlanStepState.Current)],
            [], 10, PlanHysteresisPolicy.Default);

        var outcomes = await Task.WhenAll(
            database.Plans.TryStartAsync(account.Id, plan, new Money(1_000), new Money(100), new Dictionary<string, long>()),
            database.Plans.TryStartAsync(account.Id, plan, new Money(1_000), new Money(100), new Dictionary<string, long>()));

        Assert.Equal(1, outcomes.Count(outcome => outcome == PlanStartResult.Started));
        Assert.Equal(1, outcomes.Count(outcome => outcome == PlanStartResult.AlreadyStarted));
        Assert.Single(await database.Plans.GetStartedAsync(account.Id));
    }

    [Fact]
    public async Task Reconciliation_snapshot_does_not_mix_a_sync_committed_between_logical_reads()
    {
        await using var database = await TestDatabase.CreateAsync();
        var accountId = "reconciliation-read-atomicity";
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            accountId, [CompletedTransaction(5001, PersonalTradingPostSide.Buy, 42, 100, 1)],
            new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc,
                [CurrentOrder(7001, PersonalTradingPostSide.Buy, 42, 100, 1)]), [],
            FirstObservedAtUtc, FirstObservedAtUtc, FirstObservedAtUtc));
        var profile = Assert.IsType<AccountProfile>(await database.PersonalTradingPost.FindAccountProfileAsync(accountId));
        var reader = new SqlitePersonalTradingPostRepository(database.Factory, new SqliteDatabaseGate());
        var concurrentWriter = new SqlitePersonalTradingPostSynchronizationStore(database.Factory, new SqliteDatabaseGate());
        var syncB = new PersonalTradingPostSuccessfulSync(accountId,
            [CompletedTransaction(5002, PersonalTradingPostSide.Buy, 42, 100, 1)],
            new CurrentPersonalTradingPostOrderSnapshot(SecondObservedAtUtc,
                [CurrentOrder(7002, PersonalTradingPostSide.Buy, 42, 100, 1)]), [],
            SecondObservedAtUtc, FirstObservedAtUtc, SecondObservedAtUtc);
        var writerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? writerTask = null;
        var writerCommittedBeforeSnapshotFinished = false;
        async Task InterleaveWriter(int logicalRead, CancellationToken cancellationToken)
        {
            if (logicalRead != 2) return;
            writerTask = Task.Run(async () =>
            {
                writerStarted.TrySetResult(true);
                await concurrentWriter.CommitSuccessfulSyncAsync(syncB, cancellationToken);
            }, cancellationToken);
            await writerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            await Task.Delay(75, cancellationToken);
            writerCommittedBeforeSnapshotFinished = writerTask.IsCompleted;
        }

        var snapshot = await reader.GetReconciliationSnapshotAsync(profile, CancellationToken.None, InterleaveWriter);
        await (writerTask ?? throw new InvalidOperationException("The concurrent sync writer did not start.")).WaitAsync(TimeSpan.FromSeconds(5));
        var latest = await database.PersonalTradingPost.GetReconciliationSnapshotAsync(profile);

        Assert.False(writerCommittedBeforeSnapshotFinished);
        Assert.Equal(FirstObservedAtUtc, snapshot.AccountProfile.LastSuccessfulSyncAtUtc);
        Assert.Equal(FirstObservedAtUtc, snapshot.CurrentOrders?.ObservedAtUtc);
        Assert.Equal([7001L], snapshot.CurrentOrders?.Orders.Select(value => value.ExternalOrderId));
        Assert.Equal([5001L], snapshot.CompletedTransactions.Select(value => value.Transaction.ExternalTransactionId));
        Assert.Equal(SecondObservedAtUtc, latest.AccountProfile.LastSuccessfulSyncAtUtc);
        Assert.Equal([7002L], latest.CurrentOrders?.Orders.Select(value => value.ExternalOrderId));
        Assert.Equal([5001L, 5002L], latest.CompletedTransactions.Select(value => value.Transaction.ExternalTransactionId));
    }

    [Fact]
    public async Task Sequential_retry_of_a_completed_step_acknowledges_its_sqlite_receipt_without_advancing_the_next_step()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("sequential-retry-account", FirstObservedAtUtc);
        var plan = new PlanRecord("plan:sequential-retry", 1, "opportunity:sequential-retry", PlanAttention.Active,
            PlanState.InProgress, PlanReconciliationState.None, FirstObservedAtUtc, [], Money.Zero, 0,
            [
                new PlanStep("step:a", PlanStepAction.BuyNow, 42, "Objet", 1, new Money(100), [], PlanStepState.Current),
                new PlanStep("step:b", PlanStepAction.SellNow, 42, "Objet", 1, new Money(150), [], PlanStepState.Pending),
            ], [], 0, PlanHysteresisPolicy.Default);
        Assert.Equal(PlanStartResult.Started,
            await database.Plans.TryStartAsync(account.Id, plan, new Money(1_000), Money.Zero, new Dictionary<string, long>()));
        var orchestration = new PlanOrchestrationService();
        var commandService = new PlanCompletionCommandService(database.Plans, orchestration);
        var started = Assert.Single(await database.Plans.GetStartedAsync(account.Id));
        var command = new PlanCompletionCommand(plan.Id, "step:a", started.Revision, "retry-command-a",
            PlanCompletionOperation.ReportPerformed, 1, new Money(100));
        var applied = await commandService.CompleteAsync(account.Id, command);
        var replayed = await commandService.CompleteAsync(account.Id, command);

        var final = Assert.Single(await database.Plans.GetStartedAsync(account.Id));
        Assert.Single(final.Events);
        Assert.Equal(PlanCompletionStatus.Applied, applied.Status);
        Assert.Equal(PlanCompletionStatus.AlreadyApplied, replayed.Status);
        Assert.Equal(applied.Receipt, replayed.Receipt);
        Assert.Equal("step:a", final.Events[0].StepId);
        Assert.Equal(1, final.CurrentStepOrdinal);
        Assert.Equal(2, final.Revision);
        Assert.Equal(2, replayed.Receipt!.CommittedRevision);
    }

    [Fact]
    public async Task Concurrent_identical_commands_share_one_receipt_and_competing_or_changed_commands_conflict()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("concurrent-command-account", FirstObservedAtUtc);
        var plan = TwoStepPlan("plan:concurrent-command");
        Assert.Equal(PlanStartResult.Started,
            await database.Plans.TryStartAsync(account.Id, plan, new Money(1_000), Money.Zero, new Dictionary<string, long>()));
        var service = new PlanCompletionCommandService(database.Plans, new PlanOrchestrationService());
        var command = new PlanCompletionCommand(plan.Id, "step:a", 1, "shared-command", PlanCompletionOperation.ReportPerformed, 1, new Money(100));

        var concurrent = await Task.WhenAll(service.CompleteAsync(account.Id, command), service.CompleteAsync(account.Id, command));
        Assert.Single(concurrent, result => result.Status == PlanCompletionStatus.Applied);
        Assert.Single(concurrent, result => result.Status == PlanCompletionStatus.AlreadyApplied);
        Assert.Equal(concurrent[0].Receipt, concurrent[1].Receipt);

        var changedPayload = await service.CompleteAsync(account.Id, command with { UnitPrice = new Money(101) });
        var changedQuantity = await service.CompleteAsync(account.Id, command with { Quantity = 2 });
        var changedStep = await service.CompleteAsync(account.Id, command with { StepId = "step:b" });
        var changedRevision = await service.CompleteAsync(account.Id, command with { ExpectedRevision = 2 });
        var changedOperation = await service.CompleteAsync(account.Id, command with
        {
            Operation = PlanCompletionOperation.NotPerformed,
            Quantity = 0,
            UnitPrice = null,
        });
        var invalidChangedOperation = await service.CompleteAsync(account.Id, command with
        {
            Operation = PlanCompletionOperation.NotPerformed,
            Quantity = 1,
            UnitPrice = new Money(100),
        });
        var competingCommand = await service.CompleteAsync(account.Id, command with { CommandId = "different-command" });
        Assert.Equal(PlanCompletionStatus.Conflict, changedPayload.Status);
        Assert.Equal(PlanCompletionStatus.Conflict, changedQuantity.Status);
        Assert.Equal(PlanCompletionStatus.Conflict, changedStep.Status);
        Assert.Equal(PlanCompletionStatus.Conflict, changedRevision.Status);
        Assert.Equal(PlanCompletionStatus.Conflict, changedOperation.Status);
        Assert.Equal(PlanCompletionStatus.Conflict, invalidChangedOperation.Status);
        Assert.Equal(PlanCompletionStatus.Conflict, competingCommand.Status);
        Assert.Equal(1, await database.GetTableCountAsync("plan_completion_receipts"));
        Assert.Single(Assert.Single(await database.Plans.GetStartedAsync(account.Id)).Events);

        var secondPlan = TwoStepPlan("plan:competing-command");
        Assert.Equal(PlanStartResult.Started, await database.Plans.TryStartAsync(account.Id, secondPlan,
            new Money(1_000), Money.Zero, new Dictionary<string, long>()));
        var firstDistinct = new PlanCompletionCommand(secondPlan.Id, "step:a", 1, "competing-command-a",
            PlanCompletionOperation.ReportPerformed, 1, new Money(100));
        var secondDistinct = firstDistinct with { CommandId = "competing-command-b" };
        var distinctResults = await Task.WhenAll(
            service.CompleteAsync(account.Id, firstDistinct),
            service.CompleteAsync(account.Id, secondDistinct));
        Assert.Single(distinctResults, result => result.Status == PlanCompletionStatus.Applied);
        Assert.Single(distinctResults, result => result.Status == PlanCompletionStatus.Conflict);
        Assert.Equal(2, await database.GetTableCountAsync("plan_completion_receipts"));
        Assert.Single((await database.Plans.GetStartedAsync(account.Id)).Single(plan => plan.Id == secondPlan.Id).Events);
    }

    [Fact]
    public async Task Stale_reconciliation_save_cannot_overwrite_a_concurrent_completion_or_its_receipt()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("reconciliation-completion-race", FirstObservedAtUtc);
        var plan = TwoStepPlan("plan:reconciliation-completion-race");
        Assert.Equal(PlanStartResult.Started,
            await database.Plans.TryStartAsync(account.Id, plan, new Money(1_000), Money.Zero, new Dictionary<string, long>()));

        var orchestration = new PlanOrchestrationService();
        var completions = new PlanCompletionCommandService(database.Plans, orchestration);
        var firstCommand = new PlanCompletionCommand(plan.Id, "step:a", 1, "race-first", PlanCompletionOperation.ReportPerformed, 1, new Money(100));
        Assert.Equal(PlanCompletionStatus.Applied, (await completions.CompleteAsync(account.Id, firstCommand)).Status);

        var stale = Assert.Single(await database.Plans.GetReconciliationCandidatesAsync(account.Id));
        var accountScope = new AccountScope(account.AccountScopeId);
        var staleReconciliation = orchestration.ReconcileWithVerifiedState(stale, accountScope,
            TradingPostFrame(accountScope, DateTimeOffset.UtcNow.AddMinutes(1), "race-capture", new Money(1_000)));
        Assert.False(PlanRecordSemantics.AreEqual(stale, staleReconciliation));
        Assert.Equal("race-capture", staleReconciliation.Events.Single(value => value.StepId == "step:a").LastNegativeEvidenceCaptureId);

        var secondCommand = new PlanCompletionCommand(plan.Id, "step:b", stale.Revision, "race-second", PlanCompletionOperation.ReportPerformed, 1, new Money(150));
        var secondCompletion = await completions.CompleteAsync(account.Id, secondCommand);
        Assert.Equal(PlanCompletionStatus.Applied, secondCompletion.Status);

        await Assert.ThrowsAsync<PlanConcurrencyException>(() => database.Plans.SaveAsync(account.Id, staleReconciliation));

        var latest = Assert.Single(await database.Plans.GetStartedAsync(account.Id));
        Assert.Equal(stale.Revision + 1, latest.Revision);
        Assert.Equal(2, latest.Events.Count);
        Assert.Contains(latest.Events, value => value.StepId == "step:b");
        Assert.Equal(PlanCompletionStatus.AlreadyApplied, (await completions.CompleteAsync(account.Id, secondCommand)).Status);
        Assert.Equal(2, await database.GetTableCountAsync("plan_completion_receipts"));
    }

    [Fact]
    public async Task Concurrent_account_reconciliation_consumes_one_transaction_identity_once_across_plans_and_restart()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("account-evidence-claim-race", FirstObservedAtUtc);
        var orchestration = new PlanOrchestrationService();
        foreach (var planId in new[] { "plan-a", "plan-b" })
        {
            var plan = TwoStepPlan(planId);
            Assert.Equal(PlanStartResult.Started, await database.Plans.TryStartAsync(account.Id, plan,
                new Money(1_000), Money.Zero, new Dictionary<string, long>()));
            var started = Assert.Single(await database.Plans.GetStartedAsync(account.Id), value => value.Id == plan.Id);
            await database.Plans.SaveAsync(account.Id,
                orchestration.ReportStep(started, 1, new Money(100), FirstObservedAtUtc.AddSeconds(10)));
        }

        var scope = new AccountScope(account.AccountScopeId);
        var evaluatedAt = FirstObservedAtUtc.AddMinutes(1);
        var frame = TradingPostFrame(scope, evaluatedAt, "one-real-purchase", new Money(800), completed:
        [
            new PlanVerifiedEvidence("CompletedBuy:123", PlanEvidenceKind.CompletedBuy, 42, 1, new Money(100),
                FirstObservedAtUtc.AddSeconds(5), evaluatedAt),
        ]);
        var firstRepository = new SqlitePlanRepository(new SqliteConnectionFactory(database.Path), new SqliteDatabaseGate());
        var secondRepository = new SqlitePlanRepository(new SqliteConnectionFactory(database.Path), new SqliteDatabaseGate());
        Task<IReadOnlyList<PlanRecord>> ReconcileAsync(IPlanRepository repository) => repository.ApplyAccountReconciliationAsync(
            account.Id, stored => orchestration.ReconcileAccountPlans(stored, scope, frame, evaluatedAt));

        await Task.WhenAll(ReconcileAsync(firstRepository), ReconcileAsync(secondRepository));

        var persisted = await database.Plans.GetReconciliationCandidatesAsync(account.Id);
        var alpha = Assert.Single(persisted, plan => plan.Id == "plan-a");
        var beta = Assert.Single(persisted, plan => plan.Id == "plan-b");
        Assert.Equal(PlanShadowEventState.Confirmed, alpha.Events.Single(value => value.StepId == "step:a").State);
        Assert.Equal(["CompletedBuy:123"], alpha.Events.Single(value => value.StepId == "step:a").VerifiedEvidenceIds);
        Assert.Equal(PlanShadowEventState.PendingConfirmation, beta.Events.Single(value => value.StepId == "step:a").State);
        Assert.Empty(beta.Events.Single(value => value.StepId == "step:a").VerifiedEvidenceIds ?? []);

        var reopened = new SqlitePlanRepository(new SqliteConnectionFactory(database.Path), new SqliteDatabaseGate());
        var beforeReplay = await reopened.GetReconciliationCandidatesAsync(account.Id);
        var replay = await reopened.ApplyAccountReconciliationAsync(account.Id,
            stored => orchestration.ReconcileAccountPlans(stored, scope, frame, evaluatedAt));
        Assert.Equal(beforeReplay.Select(value => value.Revision), replay.Select(value => value.Revision));
        Assert.Single(replay.SelectMany(value => value.Events).SelectMany(value => value.VerifiedEvidenceIds ?? []));
    }

    [Fact]
    public async Task Receipt_survives_repository_reopen_and_replay_after_undo_does_not_restore_the_effect()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("reopen-replay-account", FirstObservedAtUtc);
        var plan = TwoStepPlan("plan:reopen-replay");
        Assert.Equal(PlanStartResult.Started,
            await database.Plans.TryStartAsync(account.Id, plan, new Money(1_000), Money.Zero, new Dictionary<string, long>()));
        var orchestration = new PlanOrchestrationService();
        var service = new PlanCompletionCommandService(database.Plans, orchestration);
        var command = new PlanCompletionCommand(plan.Id, "step:a", 1, "report-a", PlanCompletionOperation.ReportPerformed, 1, new Money(100));
        Assert.Equal(PlanCompletionStatus.Applied, (await service.CompleteAsync(account.Id, command)).Status);

        var reported = Assert.Single(await database.Plans.GetStartedAsync(account.Id));
        await database.Plans.SaveAsync(account.Id, orchestration.UndoLastStep(reported, FirstObservedAtUtc.AddMinutes(2)));
        var reopenedRepository = new SqlitePlanRepository(database.Factory);
        var reopenedService = new PlanCompletionCommandService(reopenedRepository, orchestration);
        var replay = await reopenedService.CompleteAsync(account.Id, command);

        Assert.Equal(PlanCompletionStatus.AlreadyApplied, replay.Status);
        var afterReplay = Assert.Single(await reopenedRepository.GetStartedAsync(account.Id));
        Assert.Equal(3, afterReplay.Revision);
        Assert.Equal(0, afterReplay.CurrentStepOrdinal);
        Assert.Equal(PlanShadowEventState.Reversed, Assert.Single(afterReplay.Events).State);
        Assert.Equal(1, await database.GetTableCountAsync("plan_completion_receipts"));
    }

    [Fact]
    public async Task Undo_after_contradiction_persists_a_cleared_reason_across_repository_reopen()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("undo-clears-reason", FirstObservedAtUtc);
        var orchestration = new PlanOrchestrationService();
        var plan = TwoStepPlan("undo-clears-reason");
        Assert.Equal(PlanStartResult.Started, await database.Plans.TryStartAsync(account.Id, plan,
            new Money(1_000), Money.Zero, new Dictionary<string, long>()));
        var started = Assert.Single(await database.Plans.GetStartedAsync(account.Id));
        var reported = orchestration.ReportStep(started, 1, new Money(100), FirstObservedAtUtc.AddSeconds(10));
        await database.Plans.SaveAsync(account.Id, reported);
        var contradicted = reported with
        {
            State = PlanState.ReconciliationRequired,
            ReconciliationState = PlanReconciliationState.Contradicted,
            ConsecutiveContradictionCount = 2,
            ReconciliationReason = PlanReconciliationReason.CraftInventoryMismatch,
            Revision = reported.Revision + 1,
        };
        await database.Plans.SaveAsync(account.Id, contradicted);

        var reopened = new SqlitePlanRepository(new SqliteConnectionFactory(database.Path), new SqliteDatabaseGate());
        var afterReopen = Assert.Single(await reopened.GetReconciliationCandidatesAsync(account.Id));
        var undone = orchestration.UndoLastStep(afterReopen, FirstObservedAtUtc.AddMinutes(1));
        await reopened.SaveAsync(account.Id, undone);
        var persisted = Assert.Single(await reopened.GetStartedAsync(account.Id));

        Assert.Equal(PlanState.InProgress, persisted.State);
        Assert.Equal(PlanReconciliationState.None, persisted.ReconciliationState);
        Assert.Equal(PlanReconciliationReason.None, persisted.ReconciliationReason);
        Assert.Equal(PlanShadowEventState.Reversed, Assert.Single(persisted.Events).State);
    }

    [Fact]
    public async Task Receipt_insert_failure_rolls_back_transition_and_retry_can_commit_once()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("receipt-rollback-account", FirstObservedAtUtc);
        var plan = TwoStepPlan("plan:receipt-rollback");
        Assert.Equal(PlanStartResult.Started,
            await database.Plans.TryStartAsync(account.Id, plan, new Money(1_000), Money.Zero, new Dictionary<string, long>()));
        var service = new PlanCompletionCommandService(database.Plans, new PlanOrchestrationService());
        var command = new PlanCompletionCommand(plan.Id, "step:a", 1, "retry-after-abort", PlanCompletionOperation.ReportPerformed, 1, new Money(100));
        await using (var connection = await database.Factory.OpenConnectionAsync())
        await using (var trigger = connection.CreateCommand())
        {
            trigger.CommandText = "CREATE TRIGGER fail_plan_completion_receipt BEFORE INSERT ON plan_completion_receipts BEGIN SELECT RAISE(ABORT, 'simulated receipt write failure'); END;";
            await trigger.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<SqliteException>(() => service.CompleteAsync(account.Id, command));
        var unchanged = Assert.Single(await database.Plans.GetStartedAsync(account.Id));
        Assert.Equal(1, unchanged.Revision);
        Assert.Empty(unchanged.Events);
        Assert.Equal(0, await database.GetTableCountAsync("plan_completion_receipts"));

        await using (var connection = await database.Factory.OpenConnectionAsync())
        await using (var trigger = connection.CreateCommand())
        {
            trigger.CommandText = "DROP TRIGGER fail_plan_completion_receipt;";
            await trigger.ExecuteNonQueryAsync();
        }
        Assert.Equal(PlanCompletionStatus.Applied, (await service.CompleteAsync(account.Id, command)).Status);
        Assert.Single(Assert.Single(await database.Plans.GetStartedAsync(account.Id)).Events);
        Assert.Equal(1, await database.GetTableCountAsync("plan_completion_receipts"));
    }

    [Fact]
    public async Task Wrong_step_stale_revision_missing_identity_and_invalid_payload_write_no_effect_or_receipt()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("invalid-command-account", FirstObservedAtUtc);
        var plan = TwoStepPlan("plan:invalid-command");
        Assert.Equal(PlanStartResult.Started,
            await database.Plans.TryStartAsync(account.Id, plan, new Money(1_000), Money.Zero, new Dictionary<string, long>()));
        var service = new PlanCompletionCommandService(database.Plans, new PlanOrchestrationService());

        Assert.Equal(PlanCompletionStatus.Conflict, (await service.CompleteAsync(account.Id,
            new PlanCompletionCommand(plan.Id, "step:b", 1, "wrong-step", PlanCompletionOperation.ReportPerformed, 1, new Money(100)))).Status);
        Assert.Equal(PlanCompletionStatus.Conflict, (await service.CompleteAsync(account.Id,
            new PlanCompletionCommand(plan.Id, "step:a", 0, "stale-revision", PlanCompletionOperation.ReportPerformed, 1, new Money(100)))).Status);
        Assert.Equal(PlanCompletionStatus.Invalid, (await service.CompleteAsync(account.Id,
            new PlanCompletionCommand(plan.Id, "step:a", 1, "  ", PlanCompletionOperation.ReportPerformed, 1, new Money(100)))).Status);
        Assert.Equal(PlanCompletionStatus.Invalid, (await service.CompleteAsync(account.Id,
            new PlanCompletionCommand(plan.Id, "step:a", 1, "invalid-quantity", PlanCompletionOperation.ReportPerformed, 0, new Money(100)))).Status);

        var unchanged = Assert.Single(await database.Plans.GetStartedAsync(account.Id));
        Assert.Equal(1, unchanged.Revision);
        Assert.Empty(unchanged.Events);
        Assert.Equal(0, await database.GetTableCountAsync("plan_completion_receipts"));
    }

    [Fact]
    public async Task Not_performed_receipt_replays_after_terminal_cancellation_and_is_account_scoped()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("terminal-command-account", FirstObservedAtUtc);
        var otherAccount = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("other-terminal-command-account", FirstObservedAtUtc);
        var plan = TwoStepPlan("plan:terminal-command");
        Assert.Equal(PlanStartResult.Started,
            await database.Plans.TryStartAsync(account.Id, plan, new Money(1_000), Money.Zero, new Dictionary<string, long>()));
        var service = new PlanCompletionCommandService(database.Plans, new PlanOrchestrationService());
        var command = new PlanCompletionCommand(plan.Id, "step:a", 1, "cancel-a", PlanCompletionOperation.NotPerformed, 0, null);

        Assert.Equal(PlanCompletionStatus.NotFound, (await service.CompleteAsync(otherAccount.Id, command)).Status);
        Assert.Equal(PlanCompletionStatus.Applied, (await service.CompleteAsync(account.Id, command)).Status);
        Assert.Equal(PlanCompletionStatus.AlreadyApplied, (await service.CompleteAsync(account.Id, command)).Status);
        Assert.Equal(1, await database.GetTableCountAsync("plan_completion_receipts"));
        await using var connection = await database.Factory.OpenConnectionAsync();
        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT state, revision FROM execution_plans WHERE plan_id = $planId AND account_profile_id = $accountId;";
        read.Parameters.AddWithValue("$planId", plan.Id);
        read.Parameters.AddWithValue("$accountId", account.Id);
        await using var reader = await read.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal((int)PlanState.Invalid, reader.GetInt32(0));
        Assert.Equal(2, reader.GetInt64(1));
    }

    [Fact]
    public async Task Direct_start_rejects_duplicate_inventory_demands_without_persisting_a_partial_plan()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("duplicate-demand-account", FirstObservedAtUtc);
        var plan = InventoryPlan("plan:duplicate-demand", "opportunity:duplicate-demand", [6, 6]);

        var result = await database.Plans.TryStartAsync(account.Id, plan, new Money(1_000), Money.Zero,
            new Dictionary<string, long> { ["2:42"] = 10 });

        Assert.Equal(PlanStartResult.ResourcesUnavailable, result);
        Assert.Empty(await database.Plans.GetStartedAsync(account.Id));
        Assert.Equal(0, await database.GetTableCountAsync("execution_plans"));
    }

    [Fact]
    public async Task Direct_start_accepts_split_inventory_demands_exactly_at_capacity()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("split-demand-account", FirstObservedAtUtc);
        var plan = InventoryPlan("plan:split-demand", "opportunity:split-demand", [5, 5]);

        var result = await database.Plans.TryStartAsync(account.Id, plan, new Money(1_000), Money.Zero,
            new Dictionary<string, long> { ["2:42"] = 10 });

        Assert.Equal(PlanStartResult.Started, result);
        Assert.Single(await database.Plans.GetStartedAsync(account.Id));
    }

    [Fact]
    public async Task Competing_atomic_starts_reject_equivalent_split_generic_demands_after_one_reservation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("competing-generic-demand-account", FirstObservedAtUtc);
        var first = ExpectedIncomingPlan("plan:generic-a", "opportunity:generic-a", [4, 6]);
        var second = ExpectedIncomingPlan("plan:generic-b", "opportunity:generic-b", [6, 4]);

        Assert.Equal(PlanStartResult.Started,
            await database.Plans.TryStartAsync(account.Id, first, new Money(2_000), Money.Zero, new Dictionary<string, long>()));
        var active = Assert.Single(await database.Plans.GetStartedAsync(account.Id));
        Assert.Equal(10, PlanOrchestrationService.OutstandingReservations(active)
            .Where(value => value.Kind == PlanResourceKind.ExpectedIncoming).Sum(value => value.Quantity));
        Assert.Equal(PlanStartResult.ResourcesUnavailable,
            await database.Plans.TryStartAsync(account.Id, second, new Money(2_000), Money.Zero, new Dictionary<string, long>()));

        var stillActive = Assert.Single(await database.Plans.GetStartedAsync(account.Id));
        Assert.Equal(first.Id, stillActive.Id);
        Assert.Equal(10, PlanOrchestrationService.OutstandingReservations(stillActive)
            .Where(value => value.Kind == PlanResourceKind.ExpectedIncoming).Sum(value => value.Quantity));
        Assert.Equal(1, await database.GetTableCountAsync("execution_plans"));
    }

    [Fact]
    public async Task Competing_atomic_starts_cannot_both_commit_the_same_remaining_inventory()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("competing-demand-account", FirstObservedAtUtc);
        var first = InventoryPlan("plan:competing-a", "opportunity:competing-a", [6]);
        var second = InventoryPlan("plan:competing-b", "opportunity:competing-b", [6]);
        var capacity = new Dictionary<string, long> { ["2:42"] = 10 };

        var results = await Task.WhenAll(
            database.Plans.TryStartAsync(account.Id, first, new Money(1_000), Money.Zero, capacity),
            database.Plans.TryStartAsync(account.Id, second, new Money(1_000), Money.Zero, capacity));

        Assert.Equal(1, results.Count(result => result == PlanStartResult.Started));
        Assert.Equal(1, results.Count(result => result == PlanStartResult.ResourcesUnavailable));
        Assert.Single(await database.Plans.GetStartedAsync(account.Id));
    }

    [Fact]
    public async Task Existing_outstanding_inventory_reservation_is_combined_with_all_new_duplicate_demands()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("existing-reservation-account", FirstObservedAtUtc);
        var existing = InventoryPlan("plan:existing-reservation", "opportunity:existing-reservation", [4]);
        var replacement = InventoryPlan("plan:replacement", "opportunity:replacement", [4, 4]);
        var capacity = new Dictionary<string, long> { ["2:42"] = 10 };

        Assert.Equal(PlanStartResult.Started,
            await database.Plans.TryStartAsync(account.Id, existing, new Money(1_000), Money.Zero, capacity));
        Assert.Equal(PlanStartResult.ResourcesUnavailable,
            await database.Plans.TryStartAsync(account.Id, replacement, new Money(1_000), Money.Zero, capacity));

        var active = Assert.Single(await database.Plans.GetStartedAsync(account.Id));
        Assert.Equal(existing.Id, active.Id);
    }

    [Fact]
    public async Task Direct_start_aggregates_cash_amounts_and_rejects_crossing_the_hard_reserve()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("cash-demand-account", FirstObservedAtUtc);
        var plan = new PlanRecord("plan:cash-demand", 1, "opportunity:cash-demand", PlanAttention.Active, PlanState.InProgress,
            PlanReconciliationState.None, FirstObservedAtUtc,
            [
                new(PlanResourceKind.Cash, "first", long.MaxValue, new Money(450)),
                new(PlanResourceKind.Cash, "second", long.MaxValue, new Money(450)),
            ],
            Money.Zero, 0,
            [new PlanStep("plan:cash-demand:order", PlanStepAction.PlaceBuyOrder, 42, "Objet", 1, new Money(900), [], PlanStepState.Current)],
            [], 0, PlanHysteresisPolicy.Default);

        var result = await database.Plans.TryStartAsync(account.Id, plan, new Money(1_000), new Money(101),
            new Dictionary<string, long>());

        Assert.Equal(PlanStartResult.ResourcesUnavailable, result);
        Assert.Equal(0, await database.GetTableCountAsync("execution_plans"));
    }

    [Fact]
    public async Task Direct_start_rejects_checked_overflow_without_writing_any_plan()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("overflow-demand-account", FirstObservedAtUtc);
        var plan = new PlanRecord("plan:overflow-demand", 1, "opportunity:overflow-demand", PlanAttention.Active, PlanState.InProgress,
            PlanReconciliationState.None, FirstObservedAtUtc,
            [
                new(PlanResourceKind.Cash, "first", 0, new Money(long.MaxValue)),
                new(PlanResourceKind.Cash, "second", 0, new Money(1)),
            ],
            Money.Zero, 0, [], [], 0, PlanHysteresisPolicy.Default);

        var result = await database.Plans.TryStartAsync(account.Id, plan, new Money(1_000), Money.Zero,
            new Dictionary<string, long>());

        Assert.Equal(PlanStartResult.ResourcesUnavailable, result);
        Assert.Empty(await database.Plans.GetStartedAsync(account.Id));
        Assert.Equal(0, await database.GetTableCountAsync("execution_plans"));
    }

    [Fact]
    public async Task Direct_start_rejects_negative_resource_totals_without_writing_any_plan()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("negative-demand-account", FirstObservedAtUtc);
        var invalidPlans = new[]
        {
            new PlanRecord("plan:negative-cash", 1, "opportunity:negative-cash", PlanAttention.Active, PlanState.InProgress,
                PlanReconciliationState.None, FirstObservedAtUtc,
                [new PlanResourceRequirement(PlanResourceKind.Cash, "cash", 0, new Money(-1))], Money.Zero, 0,
                [new PlanStep("plan:negative-cash:order", PlanStepAction.PlaceBuyOrder, 42, "Objet", 1, Money.Zero, [], PlanStepState.Current)],
                [], 0, PlanHysteresisPolicy.Default),
            new PlanRecord("plan:negative-quantity", 1, "opportunity:negative-quantity", PlanAttention.Active, PlanState.InProgress,
                PlanReconciliationState.None, FirstObservedAtUtc,
                [new PlanResourceRequirement(PlanResourceKind.Inventory, "42", -1, Money.Zero)], Money.Zero, 0,
                [new PlanStep("plan:negative-quantity:order", PlanStepAction.PlaceBuyOrder, 42, "Objet", 1, Money.Zero, [], PlanStepState.Current)],
                [], 0, PlanHysteresisPolicy.Default),
        };

        foreach (var plan in invalidPlans)
        {
            var result = await database.Plans.TryStartAsync(account.Id, plan, new Money(1_000), Money.Zero,
                new Dictionary<string, long> { ["2:42"] = 10 });

            Assert.Equal(PlanStartResult.ResourcesUnavailable, result);
        }

        Assert.Empty(await database.Plans.GetStartedAsync(account.Id));
        Assert.Equal(0, await database.GetTableCountAsync("execution_plans"));
    }

    [Fact]
    public async Task Buy_craft_list_plan_starts_without_requiring_future_inventory_up_front()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("craft-dependency-account", FirstObservedAtUtc);
        var candidate = new PlanCandidate("craft:dependency", 1, "craft:dependency", PlanAttention.Active,
            [
                new PlanStep("craft:dependency:buy", PlanStepAction.BuyNow, 10, "Ingrédient", 1, new Money(100), [], PlanStepState.Pending),
                new PlanStep("craft:dependency:craft", PlanStepAction.Craft, 100, "Résultat", 1, null, ["craft:dependency:buy"], PlanStepState.Pending,
                    CraftEffects: [new(PlanResourceKind.Inventory, "10", -1, Money.Zero), new(PlanResourceKind.Inventory, "100", 1, Money.Zero)]),
                new PlanStep("craft:dependency:list", PlanStepAction.List, 100, "Résultat", 1, new Money(1_000), ["craft:dependency:craft"], PlanStepState.Pending),
            ],
            [new PlanResourceRequirement(PlanResourceKind.Cash, "cash", 0, new Money(250))], new Money(100), new Money(250), 8_000, 0, 1, 1, true, []);
        var plan = new PlanOrchestrationService().Start(candidate, FirstObservedAtUtc);

        var started = await database.Plans.TryStartAsync(account.Id, plan, new Money(1_000), Money.Zero, new Dictionary<string, long>());

        Assert.Equal(PlanStartResult.Started, started);
    }

    [Fact]
    public async Task Cancelling_an_unperformed_plan_allows_a_new_execution_for_the_same_opportunity_without_erasing_history()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("plan-restart-account", FirstObservedAtUtc);
        var candidate = new PlanCandidate("proposal:restart", 1, "opportunity:restart", PlanAttention.Active,
            [new PlanStep("proposal:restart:1", PlanStepAction.PlaceBuyOrder, 42, "Objet", 1, new Money(100), [], PlanStepState.Pending)],
            [new PlanResourceRequirement(PlanResourceKind.Cash, "cash", 0, new Money(100))], Money.Zero, new Money(100), 0, 0, 1, 1, true, []);
        var orchestration = new PlanOrchestrationService();
        var first = orchestration.Start(candidate, FirstObservedAtUtc);

        Assert.Equal(PlanStartResult.Started, await database.Plans.TryStartAsync(account.Id, first, new Money(1_000), Money.Zero, new Dictionary<string, long>()));
        var cancelled = orchestration.CancelUnperformedStep(first with { Revision = 1 });
        await database.Plans.SaveAsync(account.Id, cancelled);

        var restarted = orchestration.Start(candidate, SecondObservedAtUtc);
        Assert.NotEqual(first.Id, restarted.Id);
        Assert.Equal(PlanStartResult.Started, await database.Plans.TryStartAsync(account.Id, restarted, new Money(1_000), Money.Zero, new Dictionary<string, long>()));

        var active = Assert.Single(await database.Plans.GetStartedAsync(account.Id));
        Assert.Equal(restarted.Id, active.Id);
        Assert.Equal("opportunity:restart", active.SourceOpportunityId);
        Assert.Equal(2, await database.GetTableCountAsync("execution_plans"));
    }

    [Fact]
    public async Task Late_fill_on_a_terminal_cancelled_execution_does_not_block_or_duplicate_a_restarted_opportunity()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("plan-late-fill-restart", FirstObservedAtUtc);
        var candidate = new PlanCandidate("proposal:late-fill-restart", 1, "opportunity:late-fill-restart", PlanAttention.Active,
            [
                new PlanStep("proposal:late-fill-restart:cancel", PlanStepAction.CancelBuyOrder, 42, "Objet", 1, new Money(100), [], PlanStepState.Pending, "order-7"),
                new PlanStep("proposal:late-fill-restart:replace", PlanStepAction.PlaceBuyOrder, 42, "Objet", 1, new Money(100), [], PlanStepState.Pending),
            ],
            [new PlanResourceRequirement(PlanResourceKind.Cash, "cash", 0, new Money(100))], Money.Zero, new Money(100), 0, 0, 1, 1, true, []);
        var orchestration = new PlanOrchestrationService();
        var first = orchestration.Start(candidate, FirstObservedAtUtc);

        Assert.Equal(PlanStartResult.Started, await database.Plans.TryStartAsync(account.Id, first, new Money(1_000), Money.Zero, new Dictionary<string, long>()));
        var reported = orchestration.ReportStep(first with { Revision = 1 }, 1, new Money(100), FirstObservedAtUtc.AddSeconds(10));
        await database.Plans.SaveAsync(account.Id, reported);
        var cancelled = orchestration.CancelUnperformedStep(reported with { Revision = 2 });
        await database.Plans.SaveAsync(account.Id, cancelled);
        var accountScope = new AccountScope(account.AccountScopeId);
        var firstFrame = TradingPostFrame(accountScope, FirstObservedAtUtc.AddMinutes(1), "cancel-capture-1", new Money(1_000));
        var firstAbsence = orchestration.ReconcileWithVerifiedState(cancelled with { Revision = 3 }, accountScope, firstFrame);
        await database.Plans.SaveAsync(account.Id, firstAbsence);
        var reopenedRepository = new SqlitePlanRepository(new SqliteConnectionFactory(database.Path), new SqliteDatabaseGate());
        var afterRestart = Assert.Single(await reopenedRepository.GetReconciliationCandidatesAsync(account.Id));
        var replayed = orchestration.ReconcileWithVerifiedState(afterRestart, accountScope, firstFrame);
        Assert.True(PlanRecordSemantics.AreEqual(afterRestart, replayed));
        Assert.Equal(1, afterRestart.Events.Single(value => value.Action == PlanStepAction.CancelBuyOrder).NegativeEvidenceCaptureCount);
        Assert.Equal(4, afterRestart.Revision);

        var secondFrame = TradingPostFrame(accountScope, FirstObservedAtUtc.AddMinutes(16), "cancel-capture-2", new Money(1_000));
        var terminal = orchestration.ReconcileWithVerifiedState(afterRestart, accountScope, secondFrame);
        await database.Plans.SaveAsync(account.Id, terminal);

        var restarted = orchestration.Start(candidate, SecondObservedAtUtc);
        Assert.Equal(PlanStartResult.Started, await database.Plans.TryStartAsync(account.Id, restarted, new Money(1_000), Money.Zero, new Dictionary<string, long>()));

        var lateFill = new PlanVerifiedEvidence("CompletedBuy:7", PlanEvidenceKind.CompletedBuy, 42, 1, new Money(100),
            FirstObservedAtUtc.AddSeconds(5), FirstObservedAtUtc.AddMinutes(17), "order-7");
        var reopened = orchestration.ReconcileWithVerifiedState(terminal with { Revision = 5 }, accountScope,
            TradingPostFrame(accountScope, FirstObservedAtUtc.AddMinutes(17), "cancel-capture-late-fill", new Money(900), completed: [lateFill]));
        await database.Plans.SaveAsync(account.Id, reopened);

        Assert.True(reopened.IsReconciliationOnly);
        var active = Assert.Single(await database.Plans.GetStartedAsync(account.Id));
        Assert.Equal(restarted.Id, active.Id);
        Assert.Equal(PlanStartResult.AlreadyStarted, await database.Plans.TryStartAsync(account.Id, orchestration.Start(candidate, FirstObservedAtUtc.AddMinutes(18)), new Money(1_000), Money.Zero, new Dictionary<string, long>()));
    }

    [Fact]
    public async Task Recently_terminal_cancelled_execution_remains_available_only_for_bounded_reconciliation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("plan-reconciliation-retention", FirstObservedAtUtc);
        var retainedAt = DateTimeOffset.UtcNow;
        var retained = TerminalCancelledPlan("plan:retained", "opportunity:retained", retainedAt, retainedAt + PlanOrchestrationService.CancellationReconciliationRetentionWindow);
        var expired = TerminalCancelledPlan("plan:expired", "opportunity:expired", retainedAt, retainedAt - TimeSpan.FromSeconds(1));

        Assert.Equal(PlanStartResult.Started, await database.Plans.TryStartAsync(account.Id, retained, new Money(1_000), Money.Zero, new Dictionary<string, long>()));
        Assert.Equal(PlanStartResult.Started, await database.Plans.TryStartAsync(account.Id, expired, new Money(1_000), Money.Zero, new Dictionary<string, long>()));

        Assert.Empty(await database.Plans.GetStartedAsync(account.Id));
        var reconciliationCandidates = await database.Plans.GetReconciliationCandidatesAsync(account.Id);
        Assert.Equal("plan:retained", Assert.Single(reconciliationCandidates).Id);
    }

    [Fact]
    public async Task Version_two_database_upgrades_to_version_three_without_losing_completed_history()
    {
        await using var database = await TestDatabase.CreateAsync(migrate: false);
        await database.Migrator.MigrateToAsync(2);
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", FirstObservedAtUtc);
        var transaction = CompletedTransaction(1001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2);
        await database.PersonalTradingPost.UpsertCompletedTransactionsAsync(account, [transaction], FirstObservedAtUtc);

        await database.Migrator.MigrateAsync();

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], await database.GetMigrationVersionsAsync());
        var stored = Assert.Single(await database.PersonalTradingPost.GetCompletedTransactionsAsync(account));
        Assert.Equal(transaction, stored.Transaction);
        Assert.Contains("last_sync_outcome", await database.GetAccountProfileColumnNamesAsync());
    }

    [Fact]
    public async Task Local_watchlist_is_idempotent_and_survives_restart()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.Watchlist.AddAsync(new WatchlistEntry(42, FirstObservedAtUtc));
        await database.Watchlist.AddAsync(new WatchlistEntry(42, SecondObservedAtUtc));
        await database.Watchlist.AddAsync(new WatchlistEntry(84, SecondObservedAtUtc));

        Assert.Equal([42, 84], (await database.Watchlist.GetAllAsync()).Select(entry => entry.ItemId));
        await database.Watchlist.RemoveAsync(42);
        await database.Watchlist.RemoveAsync(42);

        var restarted = new SqliteWatchlistRepository(database.Factory, database.Gate);
        Assert.Equal([84], (await restarted.GetAllAsync()).Select(entry => entry.ItemId));
    }

    [Fact]
    public async Task Investment_positions_preserve_partial_exit_history_unknown_basis_and_targets_across_restart()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", FirstObservedAtUtc);
        var created = await database.Investments.CreateAsync(account, new CreateInvestmentPosition(
            42, 10, 1_000, "Seasonal", "Festival", FirstObservedAtUtc, "Supply may change; this is not a guarantee.", null,
            [new InvestmentTarget(0, 200, 4), new InvestmentTarget(1, 250, 6)]), FirstObservedAtUtc);

        var partial = await database.Investments.RecordExitAsync(account, created.Id, 4, SecondObservedAtUtc, "First stage");
        var unknown = await database.Investments.CreateAsync(account, new CreateInvestmentPosition(
            84, 2, null, "Speculative", "Other", FirstObservedAtUtc, "Unknown basis stays explicit.", null, []), FirstObservedAtUtc);
        var restarted = new SqliteInvestmentPositionRepository(database.Factory, database.Gate);

        Assert.NotNull(partial);
        Assert.Equal(6, partial.RemainingQuantity);
        Assert.Equal(400, Assert.Single(partial.Exits).AllocatedBasisInCopper);
        Assert.False(partial.IsClosed);
        Assert.Equal([250], partial.Targets.Select(target => target.UnitPriceInCopper));
        Assert.Equal(6, Assert.Single(partial.Targets).Quantity);
        var repeated = await database.Investments.RecordExitAsync(account, created.Id, 1, SecondObservedAtUtc.AddSeconds(1), "Second stage");
        Assert.Equal(5, repeated!.RemainingQuantity);
        Assert.Equal(5, Assert.Single(repeated.Targets).Quantity);
        Assert.Null((await restarted.GetAsync(account, unknown.Id))!.AcquisitionBasisInCopper);
        var closed = await restarted.RecordExitAsync(account, created.Id, 5, SecondObservedAtUtc.AddMinutes(1), null);
        Assert.True(closed!.IsClosed);
        Assert.Equal(0, closed.RemainingQuantity);
        Assert.Empty(closed.Targets);
        Assert.Equal(3, closed.Exits.Count);
        await database.Migrator.MigrateAndValidatePersistedDataAsync();
    }

    [Fact]
    public async Task Crafting_snapshots_are_account_scoped_replace_superseded_values_and_retain_no_raw_character_or_credential_data()
    {
        await using var database = await TestDatabase.CreateAsync();
        var accountA = new AccountScope("opaque-account-crafting-a");
        var accountB = new AccountScope("opaque-account-crafting-b");
        var first = new AccountCraftingSnapshot(
            accountA, FirstObservedAtUtc,
            CraftingFeatureResult<IReadOnlyList<AccountInventoryEntry>>.Available([new(42, 3, AccountItemBinding.AccountBound)]),
            CraftingFeatureResult<IReadOnlyList<AccountMaterialEntry>>.Available([new(84, 5, 250, AccountItemBinding.AccountBound)]),
            CraftingFeatureResult<IReadOnlyList<int>>.Available([9001]),
            CraftingFeatureResult<IReadOnlyList<CraftingDisciplineCapability>>.Available([new("Artificer", 500, true)]));
        await database.Crafting.ReplaceAsync(first);
        await database.Crafting.ReplaceAsync(new AccountCraftingSnapshot(
            accountA, SecondObservedAtUtc,
            CraftingFeatureResult<IReadOnlyList<AccountInventoryEntry>>.FromFailure(Gw2ApiErrorCategory.Forbidden),
            CraftingFeatureResult<IReadOnlyList<AccountMaterialEntry>>.Available([]),
            CraftingFeatureResult<IReadOnlyList<int>>.Available([9002]),
            CraftingFeatureResult<IReadOnlyList<CraftingDisciplineCapability>>.FromFailure(Gw2ApiErrorCategory.TransportFailure)));
        await database.Crafting.ReplaceAsync(new AccountCraftingSnapshot(
            accountB, SecondObservedAtUtc,
            CraftingFeatureResult<IReadOnlyList<AccountInventoryEntry>>.Available([new(42, 1, AccountItemBinding.Unspecified)]),
            CraftingFeatureResult<IReadOnlyList<AccountMaterialEntry>>.Available([]),
            CraftingFeatureResult<IReadOnlyList<int>>.Available([]),
            CraftingFeatureResult<IReadOnlyList<CraftingDisciplineCapability>>.Available([])));

        var stored = Assert.IsType<AccountCraftingSnapshot>(await database.Crafting.GetLatestAsync(accountA));
        Assert.Equal(SecondObservedAtUtc, stored.CapturedAtUtc);
        Assert.Equal(CraftingFeatureAvailability.MissingPermission, stored.BankInventory.Availability);
        Assert.Null(stored.BankInventory.Value);
        Assert.Empty(stored.MaterialStorage.Value!);
        Assert.Equal([9002], stored.RecipeUnlocks.Value);
        Assert.Equal(CraftingFeatureAvailability.Unavailable, stored.CharacterCrafting.Availability);
        Assert.Single((await database.Crafting.GetLatestAsync(accountB))!.BankInventory.Value!);
        await database.Migrator.MigrateAndValidatePersistedDataAsync();

        var bytes = await File.ReadAllBytesAsync(database.Path);
        var databaseText = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("synthetic-crafting-key", databaseText, StringComparison.Ordinal);
        Assert.DoesNotContain("Synthetic Crafter", databaseText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restore_rejects_crafting_rows_orphaned_from_their_snapshot_without_changing_live_data()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [original], new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []), [],
            FirstObservedAtUtc, FirstObservedAtUtc, FirstObservedAtUtc));
        await database.Crafting.ReplaceAsync(new AccountCraftingSnapshot(
            new AccountScope("opaque-account-crafting-a"), FirstObservedAtUtc,
            CraftingFeatureResult<IReadOnlyList<AccountInventoryEntry>>.Available([new(42, 3, AccountItemBinding.AccountBound)]),
            CraftingFeatureResult<IReadOnlyList<AccountMaterialEntry>>.Available([]),
            CraftingFeatureResult<IReadOnlyList<int>>.Available([]),
            CraftingFeatureResult<IReadOnlyList<CraftingDisciplineCapability>>.Available([])));
        var backup = await database.Recovery.CreateBackupAsync();
        var incompatiblePath = Path.Combine(Path.GetDirectoryName(database.Path)!, "orphaned-crafting-entry.db");
        File.Copy(Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, backup.FileName), incompatiblePath);

        await using (var connection = new SqliteConnection($"Data Source={incompatiblePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = OFF; DELETE FROM account_crafting_snapshots;";
            await command.ExecuteNonQueryAsync();
        }

        await using (var incompatible = File.OpenRead(incompatiblePath))
        {
            Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreAsync(incompatible)).Outcome);
        }

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Successful_sync_commits_history_current_orders_metadata_and_status_together()
    {
        await using var database = await TestDatabase.CreateAsync();
        var completed = CompletedTransaction(1001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2);
        var current = CurrentOrder(2001, PersonalTradingPostSide.Sell, itemId: 84, unitPrice: 456, quantity: 3);

        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a",
            [completed],
            new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, [current]),
            [new StoredItemMetadata(42, "First item", FirstObservedAtUtc), new StoredItemMetadata(84, "Second item", FirstObservedAtUtc)],
            FirstObservedAtUtc,
            FirstObservedAtUtc,
            FirstObservedAtUtc));

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([completed], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
        Assert.Equal([current], await database.PersonalTradingPost.GetCurrentOrdersAsync(account));
        Assert.Equal("First item", (await database.ItemMetadata.GetAsync(42))?.Name);
        Assert.Equal((FirstObservedAtUtc, 1L, null as long?, FirstObservedAtUtc, FirstObservedAtUtc), await database.GetAccountSyncStateAsync());
    }

    [Fact]
    public async Task Dashboard_persistence_reads_return_a_successful_empty_order_snapshot_and_no_missing_profile()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a",
            [CompletedTransaction(1001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2)],
            new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []),
            [new StoredItemMetadata(42, "First item", FirstObservedAtUtc)],
            FirstObservedAtUtc,
            FirstObservedAtUtc,
            FirstObservedAtUtc));

        var profile = await database.PersonalTradingPost.FindAccountProfileAsync("opaque-account-a");

        Assert.NotNull(profile);
        Assert.Equal(FirstObservedAtUtc, profile.LastSuccessfulSyncAtUtc);
        Assert.Equal(new PersonalTradingPostHistoryCoverage(FirstObservedAtUtc, FirstObservedAtUtc),
            await database.PersonalTradingPost.GetHistoryCoverageAsync(profile));
        var snapshot = await database.PersonalTradingPost.GetLatestCurrentOrderSnapshotAsync(profile);
        Assert.NotNull(snapshot);
        Assert.Equal(FirstObservedAtUtc, snapshot.ObservedAtUtc);
        Assert.Empty(snapshot.Orders);
        Assert.Null(await database.PersonalTradingPost.FindAccountProfileAsync("missing-account"));
    }

    [Fact]
    public async Task Failed_or_conflicting_sync_preserves_last_known_good_state()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2);
        var originalCurrent = CurrentOrder(2001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a",
            [original],
            new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, [originalCurrent]),
            [new StoredItemMetadata(42, "Original", FirstObservedAtUtc)],
            FirstObservedAtUtc,
            FirstObservedAtUtc,
            FirstObservedAtUtc));

        var conflicting = original with { Quantity = 3 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SynchronizationStore.CommitSuccessfulSyncAsync(
            new PersonalTradingPostSuccessfulSync(
                "opaque-account-a",
                [CompletedTransaction(1002, PersonalTradingPostSide.Sell, itemId: 84, unitPrice: 456, quantity: 1), conflicting],
                new CurrentPersonalTradingPostOrderSnapshot(SecondObservedAtUtc, [CurrentOrder(2002, PersonalTradingPostSide.Sell, 84, 456, 1)]),
                [new StoredItemMetadata(84, "New", SecondObservedAtUtc)],
                SecondObservedAtUtc,
                SecondObservedAtUtc,
                SecondObservedAtUtc)));

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
        Assert.Equal([originalCurrent], await database.PersonalTradingPost.GetCurrentOrdersAsync(account));
        Assert.Null(await database.ItemMetadata.GetAsync(84));

        await database.SynchronizationStore.RecordFailedSyncAsync("opaque-account-a", SecondObservedAtUtc, Gw2Tp.Application.MarketData.Gw2ApiErrorCategory.IncompleteData);
        Assert.Equal((FirstObservedAtUtc, 2L, 10L as long?, FirstObservedAtUtc, FirstObservedAtUtc), await database.GetAccountSyncStateAsync());
    }

    [Fact]
    public async Task Remote_history_aging_never_deletes_completed_history_or_claims_continuous_coverage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var completed = CompletedTransaction(1001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2);
        var current = CurrentOrder(2001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a",
            [completed],
            new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, [current]),
            [new StoredItemMetadata(42, "Original", FirstObservedAtUtc)],
            FirstObservedAtUtc,
            FirstObservedAtUtc,
            FirstObservedAtUtc));

        var effectiveCoverage = await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a",
            [],
            new CurrentPersonalTradingPostOrderSnapshot(SecondObservedAtUtc, []),
            [],
            SecondObservedAtUtc,
            null,
            null));

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        var stored = Assert.Single(await database.PersonalTradingPost.GetCompletedTransactionsAsync(account));
        Assert.Equal(completed, stored.Transaction);
        Assert.Equal(FirstObservedAtUtc, stored.FirstImportedAtUtc);
        Assert.Equal(FirstObservedAtUtc, stored.LastSeenAtUtc);
        Assert.Empty(await database.PersonalTradingPost.GetCurrentOrdersAsync(account));
        Assert.Equal(2, (await database.PersonalTradingPost.GetCurrentOrderObservationsAsync(account)).Count);
        Assert.Equal(new PersonalTradingPostHistoryCoverage(null, null), effectiveCoverage);
        Assert.Equal((SecondObservedAtUtc, 1L, null as long?, null as DateTimeOffset?, null as DateTimeOffset?), await database.GetAccountSyncStateAsync());
    }

    [Fact]
    public async Task Non_overlapping_history_snapshot_resets_coverage_without_deleting_prior_transactions()
    {
        await using var database = await TestDatabase.CreateAsync();
        var initial = CompletedTransaction(1001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a",
            [initial],
            new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []),
            [new StoredItemMetadata(42, "Initial", FirstObservedAtUtc)],
            FirstObservedAtUtc,
            FirstObservedAtUtc,
            FirstObservedAtUtc));

        var laterObservedAtUtc = FirstObservedAtUtc.AddDays(91);
        var later = initial with
        {
            ExternalTransactionId = 1002,
            CompletedAtUtc = laterObservedAtUtc.AddDays(-1),
            CreatedAtUtc = laterObservedAtUtc.AddDays(-1),
        };
        var effectiveCoverage = await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a",
            [later],
            new CurrentPersonalTradingPostOrderSnapshot(laterObservedAtUtc, []),
            [new StoredItemMetadata(42, "Later", laterObservedAtUtc)],
            laterObservedAtUtc,
            later.CompletedAtUtc,
            laterObservedAtUtc));

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", laterObservedAtUtc);
        Assert.Equal([initial, later], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
        Assert.Equal(new PersonalTradingPostHistoryCoverage(later.CompletedAtUtc, laterObservedAtUtc), effectiveCoverage);
        Assert.Equal((laterObservedAtUtc, 1L, null as long?, later.CompletedAtUtc, laterObservedAtUtc), await database.GetAccountSyncStateAsync());
    }

    [Fact]
    public async Task Overlapping_history_snapshots_merge_into_one_continuous_coverage_interval()
    {
        await using var database = await TestDatabase.CreateAsync();
        var initial = CompletedTransaction(1001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a",
            [initial],
            new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []),
            [new StoredItemMetadata(42, "Initial", FirstObservedAtUtc)],
            FirstObservedAtUtc,
            FirstObservedAtUtc,
            FirstObservedAtUtc));

        var later = initial with { ExternalTransactionId = 1002 };
        var effectiveCoverage = await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a",
            [initial, later],
            new CurrentPersonalTradingPostOrderSnapshot(SecondObservedAtUtc, []),
            [new StoredItemMetadata(42, "Repeated", SecondObservedAtUtc)],
            SecondObservedAtUtc,
            FirstObservedAtUtc,
            SecondObservedAtUtc));

        Assert.Equal(new PersonalTradingPostHistoryCoverage(FirstObservedAtUtc, SecondObservedAtUtc), effectiveCoverage);
        Assert.Equal((SecondObservedAtUtc, 1L, null as long?, FirstObservedAtUtc, SecondObservedAtUtc), await database.GetAccountSyncStateAsync());
    }

    [Fact]
    public async Task Wholly_earlier_history_snapshot_does_not_merge_without_interval_overlap()
    {
        await using var database = await TestDatabase.CreateAsync();
        var laterObservedAtUtc = FirstObservedAtUtc.AddDays(91);
        var later = CompletedTransaction(1002, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2) with
        {
            CreatedAtUtc = laterObservedAtUtc.AddDays(-1),
            CompletedAtUtc = laterObservedAtUtc.AddDays(-1),
        };
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a",
            [later],
            new CurrentPersonalTradingPostOrderSnapshot(laterObservedAtUtc, []),
            [new StoredItemMetadata(42, "Later", laterObservedAtUtc)],
            laterObservedAtUtc,
            later.CompletedAtUtc,
            laterObservedAtUtc));

        var earlier = CompletedTransaction(1001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2);
        var effectiveCoverage = await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a",
            [earlier],
            new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []),
            [new StoredItemMetadata(42, "Earlier", FirstObservedAtUtc)],
            FirstObservedAtUtc,
            FirstObservedAtUtc,
            FirstObservedAtUtc));

        Assert.Equal(new PersonalTradingPostHistoryCoverage(FirstObservedAtUtc, FirstObservedAtUtc), effectiveCoverage);
    }

    [Fact]
    public async Task Sync_store_isolates_accounts_even_when_external_transaction_ids_match()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a",
            [CompletedTransaction(1001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2)],
            new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []),
            [new StoredItemMetadata(42, "Shared item", FirstObservedAtUtc)],
            FirstObservedAtUtc,
            FirstObservedAtUtc,
            FirstObservedAtUtc));
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-b",
            [CompletedTransaction(1001, PersonalTradingPostSide.Sell, itemId: 84, unitPrice: 456, quantity: 1)],
            new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []),
            [new StoredItemMetadata(84, "Different item", FirstObservedAtUtc)],
            FirstObservedAtUtc,
            FirstObservedAtUtc,
            FirstObservedAtUtc));

        var accountA = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        var accountB = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-b", SecondObservedAtUtc);
        Assert.Equal(PersonalTradingPostSide.Buy, Assert.Single(await database.PersonalTradingPost.GetCompletedTransactionsAsync(accountA)).Transaction.Side);
        Assert.Equal(PersonalTradingPostSide.Sell, Assert.Single(await database.PersonalTradingPost.GetCompletedTransactionsAsync(accountB)).Transaction.Side);
    }

    [Fact]
    public async Task Completed_transactions_are_account_scoped_idempotent_and_never_mutated()
    {
        await using var database = await TestDatabase.CreateAsync();
        var accountA = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", FirstObservedAtUtc);
        var accountB = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-b", FirstObservedAtUtc);
        var accountATransaction = CompletedTransaction(1001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2);
        var accountBTransaction = CompletedTransaction(1001, PersonalTradingPostSide.Sell, itemId: 84, unitPrice: 456, quantity: 3);

        await database.PersonalTradingPost.UpsertCompletedTransactionsAsync(accountA, [accountATransaction], FirstObservedAtUtc);
        await database.PersonalTradingPost.UpsertCompletedTransactionsAsync(accountA, [accountATransaction], SecondObservedAtUtc);
        await database.PersonalTradingPost.UpsertCompletedTransactionsAsync(accountB, [accountBTransaction], FirstObservedAtUtc);

        var storedA = Assert.Single(await database.PersonalTradingPost.GetCompletedTransactionsAsync(accountA));
        var storedB = Assert.Single(await database.PersonalTradingPost.GetCompletedTransactionsAsync(accountB));
        Assert.Equal(accountATransaction, storedA.Transaction);
        Assert.Equal(FirstObservedAtUtc, storedA.FirstImportedAtUtc);
        Assert.Equal(SecondObservedAtUtc, storedA.LastSeenAtUtc);
        Assert.Equal(accountBTransaction, storedB.Transaction);

        var conflicting = accountATransaction with { Quantity = 4 };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.PersonalTradingPost.UpsertCompletedTransactionsAsync(accountA, [conflicting], SecondObservedAtUtc));
        Assert.Equal(accountATransaction, Assert.Single(await database.PersonalTradingPost.GetCompletedTransactionsAsync(accountA)).Transaction);
    }

    [Fact]
    public async Task A_failed_completed_transaction_batch_rolls_back_all_new_rows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", FirstObservedAtUtc);
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2);
        await database.PersonalTradingPost.UpsertCompletedTransactionsAsync(account, [original], FirstObservedAtUtc);

        var newTransaction = CompletedTransaction(1002, PersonalTradingPostSide.Sell, itemId: 84, unitPrice: 456, quantity: 3);
        var conflict = original with { UnitPriceInCopper = 124 };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.PersonalTradingPost.UpsertCompletedTransactionsAsync(account, [newTransaction, conflict], SecondObservedAtUtc));

        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Current_order_state_replaces_atomically_while_retaining_observations()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", FirstObservedAtUtc);
        var firstOrder = CurrentOrder(2001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 100, quantity: 3);
        await database.PersonalTradingPost.ReplaceCurrentOrderSnapshotAsync(
            account,
            new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, [firstOrder]));

        await Assert.ThrowsAsync<ArgumentException>(() => database.PersonalTradingPost.ReplaceCurrentOrderSnapshotAsync(
            account,
            new CurrentPersonalTradingPostOrderSnapshot(SecondObservedAtUtc, [firstOrder, firstOrder])));

        Assert.Equal([firstOrder], await database.PersonalTradingPost.GetCurrentOrdersAsync(account));
        Assert.Single(await database.PersonalTradingPost.GetCurrentOrderObservationsAsync(account));

        var secondOrder = CurrentOrder(2002, PersonalTradingPostSide.Sell, itemId: 84, unitPrice: 200, quantity: 1);
        await database.PersonalTradingPost.ReplaceCurrentOrderSnapshotAsync(
            account,
            new CurrentPersonalTradingPostOrderSnapshot(SecondObservedAtUtc, [secondOrder]));

        Assert.Equal([secondOrder], await database.PersonalTradingPost.GetCurrentOrdersAsync(account));
        var observations = await database.PersonalTradingPost.GetCurrentOrderObservationsAsync(account);
        Assert.Equal(2, observations.Count);
        Assert.Equal([firstOrder], observations[0].Orders);
        Assert.Equal([secondOrder], observations[1].Orders);
    }

    [Fact]
    public async Task Item_metadata_and_typed_non_secret_settings_round_trip()
    {
        await using var database = await TestDatabase.CreateAsync();
        var item = new StoredItemMetadata(42, "Test item", FirstObservedAtUtc);
        var settings = new UserSettings(1, 123, 250, 1500, FirstObservedAtUtc);

        await database.ItemMetadata.UpsertAsync([item]);
        await database.UserSettings.SaveAsync(settings);

        Assert.Equal(item, await database.ItemMetadata.GetAsync(42));
        Assert.Equal(settings, await database.UserSettings.GetAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => database.ItemMetadata.UpsertAsync(
            [item with { ObservedAtUtc = item.ObservedAtUtc.ToOffset(TimeSpan.FromHours(1)) }]));
        await Assert.ThrowsAsync<ArgumentException>(() => database.UserSettings.SaveAsync(settings with { CashReserveBasisPoints = 10001 }));
    }

    [Fact]
    public async Task Item_metadata_batch_read_returns_requested_retained_items_and_omits_missing_items()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.ItemMetadata.UpsertAsync(
        [
            new StoredItemMetadata(42, "First item", FirstObservedAtUtc),
            new StoredItemMetadata(84, "Second item", FirstObservedAtUtc),
        ]);

        var items = await database.ItemMetadata.GetManyAsync([84, 42, 84, 126]);

        Assert.Equal([42, 84], items.Select(item => item.ItemId).OrderBy(itemId => itemId));
        Assert.Equal(["First item", "Second item"], items.OrderBy(item => item.ItemId).Select(item => item.Name));
    }

    [Fact]
    public async Task Schema_has_no_credential_storage_path_and_documents_the_migrated_tables()
    {
        await using var database = await TestDatabase.CreateAsync();
        var columns = await database.GetAllColumnNamesAsync();
        var prohibitedColumnFragments = new[] { "api_key", "credential", "secret", "token", "authorization" };
        Assert.DoesNotContain(columns, column => prohibitedColumnFragments.Any(fragment =>
            column.Contains(fragment, StringComparison.OrdinalIgnoreCase)));

        var repositoryRoot = FindRepositoryRoot();
        var persistenceSource = Directory.EnumerateFiles(
                Path.Combine(repositoryRoot, "src", "Gw2Tp.Infrastructure", "Persistence"),
                "*.cs",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();
        Assert.DoesNotContain(persistenceSource, source => source.Contains("IGw2ApiKeySource", StringComparison.Ordinal));

        var documentation = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "architecture", "data-model.md"));
        foreach (var tableName in await database.GetTableNamesAsync())
        {
            Assert.Contains($"`{tableName}`", documentation, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Unknown_future_migration_is_rejected_before_repository_writes()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var connection = await database.Factory.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO schema_migrations (version, name, applied_at_utc) VALUES (99, 'future', '2026-09-06T12:00:00.0000000+00:00');";
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Migrator.MigrateAsync());
    }

    [Fact]
    public async Task Populated_database_backup_restore_round_trip_is_restart_safe()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a",
            [original],
            new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, [CurrentOrder(2001, PersonalTradingPostSide.Sell, 84, 456, 1)]),
            [new StoredItemMetadata(42, "Original item", FirstObservedAtUtc)],
            FirstObservedAtUtc,
            FirstObservedAtUtc,
            FirstObservedAtUtc));
        await database.UserSettings.SaveAsync(new UserSettings(1, 500, 250, 1500, FirstObservedAtUtc));

        var backup = await database.Recovery.CreateBackupAsync();
        Assert.Contains(database.Recovery.GetLocation().ManagedBackups, candidate => candidate.FileName == backup.FileName);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a",
            [CompletedTransaction(1002, PersonalTradingPostSide.Sell, 84, 999, 1)],
            new CurrentPersonalTradingPostOrderSnapshot(SecondObservedAtUtc, []),
            [new StoredItemMetadata(84, "Later item", SecondObservedAtUtc)],
            SecondObservedAtUtc,
            SecondObservedAtUtc,
            SecondObservedAtUtc));

        await using var backupContents = File.OpenRead(Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, backup.FileName));
        var result = await database.Recovery.RestoreAsync(backupContents);

        Assert.Equal(LocalDataRestoreOutcome.Restored, result.Outcome);
        Assert.NotNull(result.PreRestoreBackupFileName);
        Assert.True(File.Exists(Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, result.PreRestoreBackupFileName!)));

        var restartedGate = new SqliteDatabaseGate();
        var restartedFactory = new SqliteConnectionFactory(database.Path);
        var restartedMigrator = new SqliteSchemaMigrator(restartedFactory);
        await restartedMigrator.MigrateAsync();
        var restartedRepository = new SqlitePersonalTradingPostRepository(restartedFactory, restartedGate);
        var restartedAccount = await restartedRepository.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original], (await restartedRepository.GetCompletedTransactionsAsync(restartedAccount)).Select(item => item.Transaction));
        Assert.Equal("Original item", (await new SqliteItemMetadataRepository(restartedFactory, restartedGate).GetAsync(42))?.Name);
        Assert.Equal(500, (await new SqliteUserSettingsRepository(restartedFactory, restartedGate).GetAsync())?.MinimumProfitInCopper);
    }

    [Fact]
    public async Task Managed_backup_restore_accepts_only_application_created_backup_filenames()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [original], new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []), [],
            FirstObservedAtUtc, FirstObservedAtUtc, FirstObservedAtUtc));
        var backup = await database.Recovery.CreateBackupAsync();
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [CompletedTransaction(1002, PersonalTradingPostSide.Sell, 42, 456, 1)], new CurrentPersonalTradingPostOrderSnapshot(SecondObservedAtUtc, []), [],
            SecondObservedAtUtc, SecondObservedAtUtc, SecondObservedAtUtc));

        Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreManagedBackupAsync("../" + backup.FileName)).Outcome);
        Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreManagedBackupAsync("not-managed.db")).Outcome);

        var restored = await database.Recovery.RestoreManagedBackupAsync(backup.FileName);

        Assert.Equal(LocalDataRestoreOutcome.Restored, restored.Outcome);
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Invalid_or_incompatible_restore_never_changes_the_live_database()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [original], new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []), [],
            FirstObservedAtUtc, FirstObservedAtUtc, FirstObservedAtUtc));

        await using (var invalid = new MemoryStream("not a SQLite database"u8.ToArray()))
        {
            Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreAsync(invalid)).Outcome);
        }

        var incompatiblePath = Path.Combine(Path.GetDirectoryName(database.Path)!, "future-schema.db");
        await using (var connection = new SqliteConnection($"Data Source={incompatiblePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE schema_migrations (version INTEGER PRIMARY KEY, name TEXT NOT NULL, applied_at_utc TEXT NOT NULL); INSERT INTO schema_migrations VALUES (99, 'future', '2026-09-06T00:00:00.0000000+00:00');";
            await command.ExecuteNonQueryAsync();
        }

        await using (var incompatible = File.OpenRead(incompatiblePath))
        {
            Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreAsync(incompatible)).Outcome);
        }

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Structurally_incompatible_current_version_restore_never_changes_the_live_database()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [original], new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []), [],
            FirstObservedAtUtc, FirstObservedAtUtc, FirstObservedAtUtc));
        var backup = await database.Recovery.CreateBackupAsync();
        var structurallyIncompatiblePath = Path.Combine(Path.GetDirectoryName(database.Path)!, "missing-required-index.db");
        File.Copy(Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, backup.FileName), structurallyIncompatiblePath);

        await using (var connection = new SqliteConnection($"Data Source={structurallyIncompatiblePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP INDEX ix_completed_transactions_account_completed_at;";
            await command.ExecuteNonQueryAsync();
        }

        await using (var incompatible = File.OpenRead(structurallyIncompatiblePath))
        {
            Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreAsync(incompatible)).Outcome);
        }

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Restore_rejects_extra_restrictive_indexes_without_changing_live_data()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [original], new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []), [],
            FirstObservedAtUtc, FirstObservedAtUtc, FirstObservedAtUtc));
        var backup = await database.Recovery.CreateBackupAsync();
        var incompatiblePath = Path.Combine(Path.GetDirectoryName(database.Path)!, "extra-unique-index.db");
        File.Copy(Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, backup.FileName), incompatiblePath);

        await using (var connection = new SqliteConnection($"Data Source={incompatiblePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE UNIQUE INDEX unexpected_completed_transaction_item ON completed_tp_transactions (item_id);";
            await command.ExecuteNonQueryAsync();
        }

        await using (var incompatible = File.OpenRead(incompatiblePath))
        {
            Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreAsync(incompatible)).Outcome);
        }

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Restore_rejects_extra_tables_without_changing_live_data()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [original], new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []), [],
            FirstObservedAtUtc, FirstObservedAtUtc, FirstObservedAtUtc));
        var backup = await database.Recovery.CreateBackupAsync();
        var incompatiblePath = Path.Combine(Path.GetDirectoryName(database.Path)!, "extra-table.db");
        File.Copy(Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, backup.FileName), incompatiblePath);

        await using (var connection = new SqliteConnection($"Data Source={incompatiblePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE unrelated_private_data (value TEXT NOT NULL);";
            await command.ExecuteNonQueryAsync();
        }

        await using (var incompatible = File.OpenRead(incompatiblePath))
        {
            Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreAsync(incompatible)).Outcome);
        }

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Restore_rejects_a_case_insensitive_account_scope_index_without_changing_live_data()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [original], new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []), [],
            FirstObservedAtUtc, FirstObservedAtUtc, FirstObservedAtUtc));
        var backup = await database.Recovery.CreateBackupAsync();
        var incompatiblePath = Path.Combine(Path.GetDirectoryName(database.Path)!, "case-insensitive-account-scope.db");
        File.Copy(Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, backup.FileName), incompatiblePath);

        await using (var connection = new SqliteConnection($"Data Source={incompatiblePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA foreign_keys = OFF;
                CREATE TABLE account_profiles_replacement (
                    id INTEGER PRIMARY KEY,
                    account_scope_id TEXT NOT NULL COLLATE NOCASE,
                    created_at_utc TEXT NOT NULL,
                    last_successful_sync_at_utc TEXT NULL,
                    last_sync_attempted_at_utc TEXT NULL,
                    last_sync_outcome INTEGER NULL CHECK (last_sync_outcome IN (1, 2)),
                    last_sync_error_category INTEGER NULL CHECK (last_sync_error_category BETWEEN 0 AND 11),
                    history_coverage_start_utc TEXT NULL,
                    history_coverage_end_utc TEXT NULL,
                    CONSTRAINT uq_account_profiles_scope UNIQUE (account_scope_id)
                );
                INSERT INTO account_profiles_replacement SELECT * FROM account_profiles;
                DROP TABLE account_profiles;
                ALTER TABLE account_profiles_replacement RENAME TO account_profiles;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using (var incompatible = File.OpenRead(incompatiblePath))
        {
            Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreAsync(incompatible)).Outcome);
        }

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Restore_rejects_hidden_generated_columns_without_changing_live_data()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [original], new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []), [],
            FirstObservedAtUtc, FirstObservedAtUtc, FirstObservedAtUtc));
        var backup = await database.Recovery.CreateBackupAsync();
        var incompatiblePath = Path.Combine(Path.GetDirectoryName(database.Path)!, "generated-column.db");
        File.Copy(Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, backup.FileName), incompatiblePath);

        await using (var connection = new SqliteConnection($"Data Source={incompatiblePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE item_metadata ADD COLUMN generated_value INTEGER GENERATED ALWAYS AS (item_id * 2) VIRTUAL;";
            await command.ExecuteNonQueryAsync();
        }

        await using (var incompatible = File.OpenRead(incompatiblePath))
        {
            Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreAsync(incompatible)).Outcome);
        }

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Restore_rejects_unexpected_column_defaults_without_changing_live_data()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [original], new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []), [],
            FirstObservedAtUtc, FirstObservedAtUtc, FirstObservedAtUtc));
        var backup = await database.Recovery.CreateBackupAsync();
        var incompatiblePath = Path.Combine(Path.GetDirectoryName(database.Path)!, "column-default.db");
        File.Copy(Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, backup.FileName), incompatiblePath);

        await using (var connection = new SqliteConnection($"Data Source={incompatiblePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE item_metadata_replacement (
                    item_id INTEGER PRIMARY KEY CHECK (item_id > 0),
                    name TEXT NOT NULL CHECK (length(name) > 0),
                    observed_at_utc TEXT NOT NULL DEFAULT 'unexpected-default'
                );
                INSERT INTO item_metadata_replacement SELECT * FROM item_metadata;
                DROP TABLE item_metadata;
                ALTER TABLE item_metadata_replacement RENAME TO item_metadata;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using (var incompatible = File.OpenRead(incompatiblePath))
        {
            Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreAsync(incompatible)).Outcome);
        }

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Restore_rejects_foreign_key_update_actions_without_changing_live_data()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [original], new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []), [],
            FirstObservedAtUtc, FirstObservedAtUtc, FirstObservedAtUtc));
        var backup = await database.Recovery.CreateBackupAsync();
        var incompatiblePath = Path.Combine(Path.GetDirectoryName(database.Path)!, "foreign-key-update-action.db");
        File.Copy(Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, backup.FileName), incompatiblePath);

        await using (var connection = new SqliteConnection($"Data Source={incompatiblePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA writable_schema = ON;
                UPDATE sqlite_master
                SET sql = REPLACE(sql, 'ON DELETE RESTRICT', 'ON UPDATE CASCADE ON DELETE RESTRICT')
                WHERE type = 'table' AND name = 'completed_tp_transactions';
                PRAGMA writable_schema = OFF;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using (var incompatible = File.OpenRead(incompatiblePath))
        {
            Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreAsync(incompatible)).Outcome);
        }

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Restore_rejects_malformed_persisted_timestamps_without_changing_live_data()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [original], new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []), [],
            FirstObservedAtUtc, FirstObservedAtUtc, FirstObservedAtUtc));
        var backup = await database.Recovery.CreateBackupAsync();
        var incompatiblePath = Path.Combine(Path.GetDirectoryName(database.Path)!, "malformed-timestamp.db");
        File.Copy(Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, backup.FileName), incompatiblePath);

        await using (var connection = new SqliteConnection($"Data Source={incompatiblePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO item_metadata (item_id, name, observed_at_utc) VALUES (84, 'Malformed timestamp', 'not-a-timestamp');";
            await command.ExecuteNonQueryAsync();
        }

        await using (var incompatible = File.OpenRead(incompatiblePath))
        {
            Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreAsync(incompatible)).Outcome);
        }

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Restore_rejects_malformed_version_five_history_after_staged_migration_without_changing_live_data()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.Watchlist.AddAsync(new WatchlistEntry(42, FirstObservedAtUtc));
        await using var versionFiveDatabase = await TestDatabase.CreateAsync(migrate: false, databaseFileName: "version-five-history.db");
        await versionFiveDatabase.Migrator.MigrateToAsync(5);
        await using (var connection = await versionFiveDatabase.Factory.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO market_price_observations (
                    observed_at_utc, item_id, highest_buy_price_in_copper, lowest_sell_price_in_copper,
                    aggregate_buy_quantity, aggregate_sell_quantity, source_status, sampling_tier, sampling_policy_version)
                VALUES ('not-a-timestamp', 84, 100, 120, 10, 20, 1, 2, 1);
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var backup = File.OpenRead(versionFiveDatabase.Path);
        Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreAsync(backup)).Outcome);
        Assert.Equal([42], (await database.Watchlist.GetAllAsync()).Select(entry => entry.ItemId));
    }

    [Fact]
    public async Task Restore_rejects_domain_invalid_identifiers_without_changing_live_data()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [original], new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []), [],
            FirstObservedAtUtc, FirstObservedAtUtc, FirstObservedAtUtc));
        var backup = await database.Recovery.CreateBackupAsync();
        var incompatiblePath = Path.Combine(Path.GetDirectoryName(database.Path)!, "domain-invalid-identifier.db");
        File.Copy(Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, backup.FileName), incompatiblePath);

        await using (var connection = new SqliteConnection($"Data Source={incompatiblePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE completed_tp_transactions SET external_transaction_id = -1;";
            await command.ExecuteNonQueryAsync();
        }

        await using (var incompatible = File.OpenRead(incompatiblePath))
        {
            Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreAsync(incompatible)).Outcome);
        }

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Restore_rejects_watchlist_item_ids_outside_the_application_domain_without_changing_live_data()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.Watchlist.AddAsync(new WatchlistEntry(42, FirstObservedAtUtc));
        var backup = await database.Recovery.CreateBackupAsync();
        var incompatiblePath = Path.Combine(Path.GetDirectoryName(database.Path)!, "domain-invalid-watchlist.db");
        File.Copy(Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, backup.FileName), incompatiblePath);

        await using (var connection = new SqliteConnection($"Data Source={incompatiblePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE watchlist_entries SET item_id = {int.MaxValue + 1L};";
            await command.ExecuteNonQueryAsync();
        }

        await using (var incompatible = File.OpenRead(incompatiblePath))
        {
            Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreAsync(incompatible)).Outcome);
        }

        Assert.Equal([42], (await database.Watchlist.GetAllAsync()).Select(entry => entry.ItemId));
    }

    [Fact]
    public async Task Restore_rejects_whitespace_only_account_scopes_without_changing_live_data()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [original], new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []), [],
            FirstObservedAtUtc, FirstObservedAtUtc, FirstObservedAtUtc));
        var backup = await database.Recovery.CreateBackupAsync();
        var incompatiblePath = Path.Combine(Path.GetDirectoryName(database.Path)!, "whitespace-account-scope.db");
        File.Copy(Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, backup.FileName), incompatiblePath);

        await using (var connection = new SqliteConnection($"Data Source={incompatiblePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE account_profiles SET account_scope_id = char(9);";
            await command.ExecuteNonQueryAsync();
        }

        await using (var incompatible = File.OpenRead(incompatiblePath))
        {
            Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreAsync(incompatible)).Outcome);
        }

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Restore_rejects_whitespace_only_item_names_without_changing_live_data()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [original], new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []), [],
            FirstObservedAtUtc, FirstObservedAtUtc, FirstObservedAtUtc));
        var backup = await database.Recovery.CreateBackupAsync();
        var incompatiblePath = Path.Combine(Path.GetDirectoryName(database.Path)!, "whitespace-item-name.db");
        File.Copy(Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, backup.FileName), incompatiblePath);

        await using (var connection = new SqliteConnection($"Data Source={incompatiblePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO item_metadata (item_id, name, observed_at_utc) VALUES (84, char(9), '2026-09-06T12:00:00.0000000+00:00');";
            await command.ExecuteNonQueryAsync();
        }

        await using (var incompatible = File.OpenRead(incompatiblePath))
        {
            Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreAsync(incompatible)).Outcome);
        }

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Restore_rejects_inconsistent_history_coverage_without_changing_live_data()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [original], new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []), [],
            FirstObservedAtUtc, FirstObservedAtUtc, FirstObservedAtUtc));
        var backup = await database.Recovery.CreateBackupAsync();
        var incompatiblePath = Path.Combine(Path.GetDirectoryName(database.Path)!, "inconsistent-history-coverage.db");
        File.Copy(Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, backup.FileName), incompatiblePath);

        await using (var connection = new SqliteConnection($"Data Source={incompatiblePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE account_profiles SET history_coverage_start_utc = '2026-09-06T13:00:00.0000000+00:00', history_coverage_end_utc = '2026-09-06T12:00:00.0000000+00:00';";
            await command.ExecuteNonQueryAsync();
        }

        await using (var incompatible = File.OpenRead(incompatiblePath))
        {
            Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreAsync(incompatible)).Outcome);
        }

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Restore_rejects_one_sided_history_coverage_without_changing_live_data()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [original], new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []), [],
            FirstObservedAtUtc, FirstObservedAtUtc, FirstObservedAtUtc));
        var backup = await database.Recovery.CreateBackupAsync();
        var incompatiblePath = Path.Combine(Path.GetDirectoryName(database.Path)!, "one-sided-history-coverage.db");
        File.Copy(Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, backup.FileName), incompatiblePath);

        await using (var connection = new SqliteConnection($"Data Source={incompatiblePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE account_profiles SET history_coverage_start_utc = NULL, history_coverage_end_utc = '2026-09-06T12:00:00.0000000+00:00';";
            await command.ExecuteNonQueryAsync();
        }

        await using (var incompatible = File.OpenRead(incompatiblePath))
        {
            Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreAsync(incompatible)).Outcome);
        }

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Failed_restore_replacement_leaves_live_data_untouched()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2);
        var later = CompletedTransaction(1002, PersonalTradingPostSide.Sell, 84, 456, 1);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [original], new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []), [],
            FirstObservedAtUtc, FirstObservedAtUtc, FirstObservedAtUtc));
        var backup = await database.Recovery.CreateBackupAsync();
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [later], new CurrentPersonalTradingPostOrderSnapshot(SecondObservedAtUtc, []), [],
            SecondObservedAtUtc, SecondObservedAtUtc, SecondObservedAtUtc));
        var failingRecovery = new SqliteLocalDataRecoveryService(database.Factory, database.Gate, database.OperationGate, new FailingReplaceFileOperations());

        await using (var validBackup = File.OpenRead(Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, backup.FileName)))
        {
            Assert.Equal(LocalDataRestoreOutcome.RestoreFailed, (await failingRecovery.RestoreAsync(validBackup)).Outcome);
        }

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original, later], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Cancellation_after_pre_restore_backup_leaves_live_data_untouched()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2);
        var later = CompletedTransaction(1002, PersonalTradingPostSide.Sell, 84, 456, 1);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [original], new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []), [],
            FirstObservedAtUtc, FirstObservedAtUtc, FirstObservedAtUtc));
        var backup = await database.Recovery.CreateBackupAsync();
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [later], new CurrentPersonalTradingPostOrderSnapshot(SecondObservedAtUtc, []), [],
            SecondObservedAtUtc, SecondObservedAtUtc, SecondObservedAtUtc));
        using var cancellation = new CancellationTokenSource();
        var cancellingRecovery = new SqliteLocalDataRecoveryService(
            database.Factory,
            database.Gate,
            database.OperationGate,
            new CancelAfterPreRestoreBackupFileOperations(cancellation));

        await using (var validBackup = File.OpenRead(Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, backup.FileName)))
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => cancellingRecovery.RestoreAsync(validBackup, cancellation.Token));
        }

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original, later], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Interrupted_restore_copy_leaves_live_data_untouched()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a", [original], new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []), [],
            FirstObservedAtUtc, FirstObservedAtUtc, FirstObservedAtUtc));

        await using var interrupted = new InterruptedReadStream();
        Assert.Equal(LocalDataRestoreOutcome.InvalidBackup, (await database.Recovery.RestoreAsync(interrupted)).Outcome);

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Compatible_older_backup_is_migrated_in_staging_before_restore()
    {
        await using var database = await TestDatabase.CreateAsync();
        var olderBackupPath = Path.Combine(Path.GetDirectoryName(database.Path)!, "version-two-backup.db");
        var olderFactory = new SqliteConnectionFactory(olderBackupPath);
        await new SqliteSchemaMigrator(olderFactory).MigrateToAsync(2);

        await using var backup = File.OpenRead(olderBackupPath);
        Assert.Equal(LocalDataRestoreOutcome.Restored, (await database.Recovery.RestoreAsync(backup)).Outcome);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], await database.GetMigrationVersionsAsync());
    }

    [Fact]
    public async Task Clear_personal_data_removes_every_account_scope_but_keeps_shared_data_and_backups()
    {
        await using var database = await TestDatabase.CreateAsync();
        foreach (var accountScope in new[] { "opaque-account-a", "opaque-account-b" })
        {
            await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
                accountScope,
                [CompletedTransaction(1001, PersonalTradingPostSide.Buy, 42, 123, 2)],
                new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, [CurrentOrder(2001, PersonalTradingPostSide.Sell, 42, 456, 1)]),
                [new StoredItemMetadata(42, "Shared item", FirstObservedAtUtc)],
                FirstObservedAtUtc,
                FirstObservedAtUtc,
                FirstObservedAtUtc));
        }
        await database.UserSettings.SaveAsync(new UserSettings(1, 500, null, null, FirstObservedAtUtc));
        await database.Watchlist.AddAsync(new WatchlistEntry(84, FirstObservedAtUtc));
        await database.History.AppendPriceObservationAsync(new MarketPriceObservation(
            FirstObservedAtUtc, 42, 100, 120, 10, 20,
            MarketObservationSourceStatus.Complete, MarketSamplingTier.Watchlist, 1));
        await database.History.AppendOrderBookSnapshotAsync(new MarketOrderBookSnapshot(
            FirstObservedAtUtc, 42, MarketObservationSourceStatus.Complete, MarketSamplingTier.Watchlist, 1,
            [new MarketOrderBookLevel(MarketOrderBookSide.Buy, 0, 100, 10, 2)]));
        var planAccount = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", FirstObservedAtUtc);
        var completionPlan = TwoStepPlan("plan:clear-personal-data");
        Assert.Equal(PlanStartResult.Started, await database.Plans.TryStartAsync(planAccount.Id, completionPlan,
            new Money(1_000), Money.Zero, new Dictionary<string, long>()));
        var completion = new PlanCompletionCommandService(database.Plans, new PlanOrchestrationService());
        Assert.Equal(PlanCompletionStatus.Applied, (await completion.CompleteAsync(planAccount.Id,
            new PlanCompletionCommand(completionPlan.Id, "step:a", 1, "clear-personal-data-command",
                PlanCompletionOperation.ReportPerformed, 1, new Money(100)))).Status);
        Assert.Equal(1, await database.GetTableCountAsync("plan_completion_receipts"));
        var backup = await database.Recovery.CreateBackupAsync();
        var staleIncomingPath = Path.Combine(Path.GetDirectoryName(database.Path)!, $".tyrian-ledger-restore-{Guid.NewGuid():N}.incoming");
        var staleDatabasePath = Path.Combine(Path.GetDirectoryName(database.Path)!, $".tyrian-ledger-restore-{Guid.NewGuid():N}.db");
        var staleBackupPartialPath = Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, $"tyrian-ledger-backup-20260906T120000000Z.db.partial-{Guid.NewGuid():N}");
        var unrelatedRestorePath = Path.Combine(Path.GetDirectoryName(database.Path)!, ".tyrian-ledger-restore-notes.db");
        var unrelatedBackupPartialPath = Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, $"tyrian-ledger-notes.db.partial-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(staleIncomingPath, "stale personal data");
        await File.WriteAllTextAsync(staleDatabasePath, "stale personal data");
        await File.WriteAllTextAsync(staleBackupPartialPath, "stale personal data");
        await File.WriteAllTextAsync(unrelatedRestorePath, "user file");
        await File.WriteAllTextAsync(unrelatedBackupPartialPath, "user file");

        await database.Recovery.ClearPersonalDataAsync();

        Assert.Equal(0, await database.GetTableCountAsync("account_profiles"));
        Assert.Equal(0, await database.GetTableCountAsync("plan_completion_receipts"));
        Assert.Equal(0, await database.GetTableCountAsync("completed_tp_transactions"));
        Assert.Equal(0, await database.GetTableCountAsync("current_tp_orders"));
        Assert.Equal(0, await database.GetTableCountAsync("current_tp_order_observations"));
        Assert.Equal(0, await database.GetTableCountAsync("current_order_sync_batches"));
        Assert.Equal(1, await database.GetTableCountAsync("item_metadata"));
        Assert.Equal(1, await database.GetTableCountAsync("user_settings"));
        Assert.Equal(1, await database.GetTableCountAsync("market_price_observations"));
        Assert.Equal(1, await database.GetTableCountAsync("market_order_book_snapshots"));
        Assert.Equal(1, await database.GetTableCountAsync("market_order_book_levels"));
        Assert.Equal([84], (await database.Watchlist.GetAllAsync()).Select(entry => entry.ItemId));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], await database.GetMigrationVersionsAsync());
        Assert.True(File.Exists(Path.Combine(database.Recovery.GetLocation().BackupDirectoryPath, backup.FileName)));
        Assert.False(File.Exists(staleIncomingPath));
        Assert.False(File.Exists(staleDatabasePath));
        Assert.False(File.Exists(staleBackupPartialPath));
        Assert.True(File.Exists(unrelatedRestorePath));
        Assert.True(File.Exists(unrelatedBackupPartialPath));
    }

    [Fact]
    public async Task Cleanup_does_not_delete_a_live_database_named_like_a_restore_artifact()
    {
        await using var database = await TestDatabase.CreateAsync(databaseFileName: ".tyrian-ledger-restore-live.db");

        await database.Recovery.CleanupStaleRestoreArtifactsAsync();

        Assert.True(File.Exists(database.Path));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], await database.GetMigrationVersionsAsync());
    }

    private static CompletedPersonalTradingPostTransaction CompletedTransaction(
        long id,
        PersonalTradingPostSide side,
        int itemId,
        int unitPrice,
        int quantity) => new(
        id,
        side,
        itemId,
        unitPrice,
        quantity,
        FirstObservedAtUtc,
        FirstObservedAtUtc);

    private static CurrentPersonalTradingPostOrder CurrentOrder(
        long id,
        PersonalTradingPostSide side,
        int itemId,
        int unitPrice,
        int quantity) => new(id, side, itemId, unitPrice, quantity, FirstObservedAtUtc);

    private static PlanRecord TerminalCancelledPlan(string planId, string opportunityId, DateTimeOffset capturedAtUtc, DateTimeOffset retentionExpiresAtUtc)
    {
        var step = new PlanStep($"{planId}:cancel", PlanStepAction.CancelBuyOrder, 42, "Objet", 1, new Money(100), [], PlanStepState.Confirmed, "order-7");
        var execution = new PlanExecutionEvent($"{planId}:event", planId, step.Id, 1, capturedAtUtc, 1, new Money(100), [],
            PlanShadowEventState.Confirmed, null, [], Action: PlanStepAction.CancelBuyOrder);
        return new PlanRecord(planId, 1, opportunityId, PlanAttention.Active, PlanState.Invalid, PlanReconciliationState.Compatible,
            capturedAtUtc, [], Money.Zero, -1, [step], [execution], 0, PlanHysteresisPolicy.Default,
            LastEvidenceCapturedAtUtc: capturedAtUtc, IsCancelled: true, CancellationReconciliationExpiresAtUtc: retentionExpiresAtUtc);
    }

    private static PlanRecord InventoryPlan(string planId, string opportunityId, IReadOnlyList<int> quantities)
    {
        var steps = quantities.Select((quantity, index) =>
            new PlanStep($"{planId}:list:{index}", PlanStepAction.List, 42, "Objet", quantity, new Money(100), [],
                index == 0 ? PlanStepState.Current : PlanStepState.Pending)).ToArray();
        var reservations = steps.Select(step => new PlanResourceRequirement(PlanResourceKind.Cash, "cash", 0,
            Gw2TradingPostFeePolicy.Create().CalculateFees(new Money(checked(step.UnitPrice!.Value.Copper * step.Quantity))).ListingFee)).ToArray();
        return new PlanRecord(planId, 1, opportunityId, PlanAttention.Active, PlanState.InProgress,
            PlanReconciliationState.None, FirstObservedAtUtc, reservations, Money.Zero, 0, steps, [], 0, PlanHysteresisPolicy.Default);
    }

    private static PlanRecord TwoStepPlan(string planId) => new(
        planId, 1, $"opportunity:{planId}", PlanAttention.Active, PlanState.InProgress,
        PlanReconciliationState.None, FirstObservedAtUtc, [], Money.Zero, 0,
        [
            new PlanStep("step:a", PlanStepAction.BuyNow, 42, "Objet", 1, new Money(100), [], PlanStepState.Current),
            new PlanStep("step:b", PlanStepAction.SellNow, 42, "Objet", 1, new Money(150), [], PlanStepState.Pending),
        ], [], 0, PlanHysteresisPolicy.Default);

    private static PlanRecord ExpectedIncomingPlan(string planId, string opportunityId, IReadOnlyList<int> quantities)
    {
        var steps = new[]
        {
            new PlanStep($"{planId}:order", PlanStepAction.PlaceBuyOrder, 42, "Objet", quantities.Sum(), new Money(100), [], PlanStepState.Current),
        };
        var reservations = quantities.Select(quantity => new PlanResourceRequirement(PlanResourceKind.ExpectedIncoming, "42", quantity, Money.Zero))
            .Append(new PlanResourceRequirement(PlanResourceKind.Cash, "cash", 0, new Money(checked((long)quantities.Sum() * 100))))
            .ToArray();
        return new PlanRecord(planId, 1, opportunityId, PlanAttention.Active, PlanState.InProgress,
            PlanReconciliationState.None, FirstObservedAtUtc, reservations, Money.Zero, 0, steps, [], 0, PlanHysteresisPolicy.Default);
    }

    private static IReadOnlySet<PlanEvidenceKind> Complete(params PlanEvidenceKind[] kinds) => new HashSet<PlanEvidenceKind>(kinds);

    private static PlanEvidenceFrame TradingPostFrame(AccountScope scope, DateTimeOffset evaluatedAtUtc, string captureId,
        Money cash, IReadOnlyList<PlanVerifiedEvidence>? current = null, IReadOnlyList<PlanVerifiedEvidence>? completed = null)
    {
        var unknownPhysical = new PlanEvidenceProvenance(null, null, null, PlanEvidenceAvailability.Unavailable,
            PlanEvidenceCompleteness.Unknown, new HashSet<string>(StringComparer.Ordinal));
        var currentTp = new PlanEvidenceProvenance(captureId, evaluatedAtUtc, null, PlanEvidenceAvailability.Available,
            PlanEvidenceCompleteness.Complete, new HashSet<string>(new[] { "buy_orders", "sell_listings" }, StringComparer.Ordinal));
        var completedTp = new PlanEvidenceProvenance(captureId, evaluatedAtUtc, null, PlanEvidenceAvailability.Available,
            PlanEvidenceCompleteness.Complete, new HashSet<string>(new[] { "completed_buys", "completed_sells" }, StringComparer.Ordinal));
        var cashSource = new PlanEvidenceSource<Money>(new PlanEvidenceProvenance($"cash:{captureId}", evaluatedAtUtc, null,
            PlanEvidenceAvailability.Available, PlanEvidenceCompleteness.Complete,
            new HashSet<string>(new[] { "coin" }, StringComparer.Ordinal)), cash);
        return new PlanEvidenceFrame(scope, evaluatedAtUtc,
            new PlanEvidenceSource<IReadOnlyDictionary<string, long>>(unknownPhysical, null), cashSource,
            new PlanEvidenceSource<IReadOnlyList<PlanVerifiedEvidence>>(currentTp, current ?? Array.Empty<PlanVerifiedEvidence>()),
            new PlanEvidenceSource<IReadOnlyList<PlanVerifiedEvidence>>(completedTp, completed ?? Array.Empty<PlanVerifiedEvidence>()));
    }

    private static string FindRepositoryRoot()
    {
        foreach (var candidate in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var directory = new DirectoryInfo(candidate); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "TyrianLedger.slnx")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Tyrian Ledger repository root.");
    }

    private sealed class InterruptedReadStream : Stream
    {
        private bool hasRead;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (hasRead)
            {
                throw new IOException("Synthetic interrupted upload.");
            }

            hasRead = true;
            buffer.Span[0] = 0x53;
            return ValueTask.FromResult(1);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FailingReplaceFileOperations : ILocalDataFileOperations
    {
        public void MoveFile(string stagingPath, string backupPath) => File.Move(stagingPath, backupPath);

        public void ReplaceDatabase(string stagedDatabasePath, string liveDatabasePath) =>
            throw new IOException("Synthetic replacement failure.");
    }

    private sealed class CancelAfterPreRestoreBackupFileOperations(CancellationTokenSource cancellation)
        : ILocalDataFileOperations
    {
        public void MoveFile(string stagingPath, string backupPath)
        {
            File.Move(stagingPath, backupPath);
            if (Path.GetFileName(backupPath).Contains("pre-restore", StringComparison.Ordinal))
            {
                cancellation.Cancel();
            }
        }

        public void ReplaceDatabase(string stagedDatabasePath, string liveDatabasePath) =>
            File.Replace(stagedDatabasePath, liveDatabasePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string directory;

        private TestDatabase(string directory, SqliteConnectionFactory factory)
        {
            this.directory = directory;
            Factory = factory;
            Gate = new SqliteDatabaseGate();
            OperationGate = new PersonalDataOperationGate();
            Migrator = new SqliteSchemaMigrator(factory);
            PersonalTradingPost = new SqlitePersonalTradingPostRepository(factory, Gate);
            SynchronizationStore = new SqlitePersonalTradingPostSynchronizationStore(factory, Gate);
            ItemMetadata = new SqliteItemMetadataRepository(factory, Gate);
            UserSettings = new SqliteUserSettingsRepository(factory, Gate);
            Watchlist = new SqliteWatchlistRepository(factory, Gate);
            Investments = new SqliteInvestmentPositionRepository(factory, Gate);
            Crafting = new SqliteAccountCraftingSnapshotRepository(factory, Gate);
            Plans = new SqlitePlanRepository(factory, Gate);
            History = new SqliteMarketHistoryRepository(factory, Gate);
            Recovery = new SqliteLocalDataRecoveryService(factory, Gate, OperationGate);
        }

        public SqliteConnectionFactory Factory { get; }

        public ISqliteDatabaseGate Gate { get; }

        public IPersonalDataOperationGate OperationGate { get; }

        public SqliteSchemaMigrator Migrator { get; }

        public SqlitePersonalTradingPostRepository PersonalTradingPost { get; }

        public SqlitePersonalTradingPostSynchronizationStore SynchronizationStore { get; }

        public SqliteItemMetadataRepository ItemMetadata { get; }

        public SqliteUserSettingsRepository UserSettings { get; }

        public SqliteWatchlistRepository Watchlist { get; }

        public SqliteInvestmentPositionRepository Investments { get; }

        public SqliteAccountCraftingSnapshotRepository Crafting { get; }

        public SqlitePlanRepository Plans { get; }

        public SqliteMarketHistoryRepository History { get; }

        public SqliteLocalDataRecoveryService Recovery { get; }

        public string Path => Factory.DatabasePath;

        public static async Task<TestDatabase> CreateAsync(bool migrate = true, string databaseFileName = "tyrian-ledger.db")
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TyrianLedger.Persistence.Tests", Guid.NewGuid().ToString("N"));
            var database = new TestDatabase(directory, new SqliteConnectionFactory(System.IO.Path.Combine(directory, databaseFileName)));
            if (migrate)
            {
                await database.Migrator.MigrateAsync();
            }

            return database;
        }

        public async Task<IReadOnlyList<int>> GetMigrationVersionsAsync()
        {
            await using var connection = await Factory.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT version FROM schema_migrations ORDER BY version;";
            await using var reader = await command.ExecuteReaderAsync();
            var versions = new List<int>();
            while (await reader.ReadAsync())
            {
                versions.Add(reader.GetInt32(0));
            }

            return versions;
        }

        public async Task<IReadOnlyList<string>> GetTableNamesAsync()
        {
            await using var connection = await Factory.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync();
            var tableNames = new List<string>();
            while (await reader.ReadAsync())
            {
                tableNames.Add(reader.GetString(0));
            }

            return tableNames;
        }

        public async Task<int> GetTableCountAsync(string tableName)
        {
            await using var connection = await Factory.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
            return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }

        public async Task<IReadOnlyList<string>> GetAllColumnNamesAsync()
        {
            var columnNames = new List<string>();
            await using var connection = await Factory.OpenConnectionAsync();
            foreach (var tableName in await GetTableNamesAsync())
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"PRAGMA table_info({tableName});";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    columnNames.Add(reader.GetString(1));
                }
            }

            return columnNames;
        }

        public async Task<IReadOnlyList<string>> GetAccountProfileColumnNamesAsync()
        {
            await using var connection = await Factory.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(account_profiles);";
            await using var reader = await command.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1));
            }

            return columns;
        }

        public async Task<(DateTimeOffset? LastSuccessfulSyncAtUtc, long? Outcome, long? ErrorCategory, DateTimeOffset? HistoryStartUtc, DateTimeOffset? HistoryEndUtc)> GetAccountSyncStateAsync()
        {
            await using var connection = await Factory.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT last_successful_sync_at_utc, last_sync_outcome, last_sync_error_category,
                       history_coverage_start_utc, history_coverage_end_utc
                FROM account_profiles WHERE account_scope_id = 'opaque-account-a';
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (
                reader.IsDBNull(0) ? null : DateTimeOffset.Parse(reader.GetString(0), System.Globalization.CultureInfo.InvariantCulture),
                reader.IsDBNull(1) ? null : reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2),
                reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3), System.Globalization.CultureInfo.InvariantCulture),
                reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture));
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
