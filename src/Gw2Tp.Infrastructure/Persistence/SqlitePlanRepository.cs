using System.Text.Json;
using Gw2Tp.Application.Plans;
using Gw2Tp.Domain.Finance;
using Microsoft.Data.Sqlite;

namespace Gw2Tp.Infrastructure.Persistence;

/// <summary>Account-scoped durable plan/shadow state. The JSON payload contains only normalized local plan state.</summary>
internal sealed class SqlitePlanRepository(
    ISqliteConnectionFactory connectionFactory,
    ISqliteDatabaseGate? databaseGate = null) : IPlanRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ISqliteDatabaseGate gate = databaseGate ?? new SqliteDatabaseGate();

    public async Task<IReadOnlyList<PlanRecord>> GetStartedAsync(long accountProfileId, CancellationToken cancellationToken = default)
        => (await ReadPlansAsync(accountProfileId, "state IN (2, 3, 4, 5, 6)", cancellationToken).ConfigureAwait(false))
            .Where(plan => !plan.IsReconciliationOnly).ToArray();

    public async Task<IReadOnlyList<PlanRecord>> GetReconciliationCandidatesAsync(long accountProfileId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var plans = await ReadPlansAsync(accountProfileId,
            "state IN (2, 3, 4, 5, 6) OR (state = 7 AND json_extract(payload_json, '$.cancellationReconciliationExpiresAtUtc') >= $reconciliationNowUtc)",
            cancellationToken, now).ConfigureAwait(false);
        return plans.Where(plan => plan.State != PlanState.Invalid || PlanOrchestrationService.IsCancellationReconciliationRetained(plan, now)).ToArray();
    }

    public async Task<IReadOnlyList<PlanRecord>> ApplyAccountReconciliationAsync(
        long accountProfileId,
        Func<IReadOnlyList<PlanRecord>, IReadOnlyList<PlanRecord>> reconcile,
        CancellationToken cancellationToken = default)
    {
        if (accountProfileId <= 0) throw new ArgumentOutOfRangeException(nameof(accountProfileId));
        ArgumentNullException.ThrowIfNull(reconcile);
        await using var lease = await gate.AcquirePrivateAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await gate.ValidateAccountProfileAsync(connection, accountProfileId, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var storedPlans = new List<PlanRecord>();
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT payload_json, revision FROM execution_plans WHERE account_profile_id = $accountProfileId ORDER BY plan_id;";
            read.Parameters.AddWithValue("$accountProfileId", accountProfileId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var plan = JsonSerializer.Deserialize<PlanRecord>(reader.GetString(0), SerializerOptions)
                    ?? throw new InvalidDataException("The stored plan payload is invalid.");
                storedPlans.Add(plan with { Revision = reader.GetInt64(1) });
            }
        }

        var transitioned = reconcile(storedPlans);
        ArgumentNullException.ThrowIfNull(transitioned);
        if (transitioned.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != transitioned.Count)
            throw new InvalidOperationException("An account reconciliation cannot return the same plan more than once.");
        var byId = storedPlans.ToDictionary(value => value.Id, StringComparer.Ordinal);
        var committed = new List<PlanRecord>(transitioned.Count);
        foreach (var plan in transitioned)
        {
            if (plan is null || !byId.TryGetValue(plan.Id, out var stored) || plan.Revision != stored.Revision)
                throw new InvalidOperationException("An account reconciliation must preserve each stored plan identity and revision.");
            if (PlanRecordSemantics.AreEqual(stored, plan))
            {
                committed.Add(stored);
                continue;
            }

            var updated = plan with { Revision = checked(stored.Revision + 1) };
            await using var write = connection.CreateCommand();
            write.Transaction = transaction;
            write.CommandText = """
                UPDATE execution_plans
                SET state = $state, payload_json = $payload, updated_at_utc = $updatedAtUtc, revision = $nextRevision
                WHERE plan_id = $planId AND account_profile_id = $accountProfileId AND revision = $expectedRevision;
                """;
            write.Parameters.AddWithValue("$state", (int)updated.State);
            write.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(updated, SerializerOptions));
            write.Parameters.AddWithValue("$updatedAtUtc", SqlitePersistenceValues.ToUtcTimestamp(DateTimeOffset.UtcNow, "updatedAtUtc"));
            write.Parameters.AddWithValue("$planId", stored.Id);
            write.Parameters.AddWithValue("$accountProfileId", accountProfileId);
            write.Parameters.AddWithValue("$expectedRevision", stored.Revision);
            write.Parameters.AddWithValue("$nextRevision", updated.Revision);
            if (await write.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new PlanConcurrencyException();
            committed.Add(updated);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return committed;
    }

    private async Task<IReadOnlyList<PlanRecord>> ReadPlansAsync(long accountProfileId, string statePredicate, CancellationToken cancellationToken,
        DateTimeOffset? reconciliationNowUtc = null)
    {
        if (accountProfileId <= 0) throw new ArgumentOutOfRangeException(nameof(accountProfileId));
        await using var lease = await gate.AcquirePrivateAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await gate.ValidateAccountProfileAsync(connection, accountProfileId, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT payload_json, revision FROM execution_plans WHERE account_profile_id = $accountProfileId AND {statePredicate} ORDER BY updated_at_utc, plan_id;";
        command.Parameters.AddWithValue("$accountProfileId", accountProfileId);
        if (reconciliationNowUtc is { } now)
            command.Parameters.AddWithValue("$reconciliationNowUtc", SqlitePersistenceValues.ToUtcTimestamp(now, nameof(reconciliationNowUtc)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var plans = new List<PlanRecord>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var plan = JsonSerializer.Deserialize<PlanRecord>(reader.GetString(0), SerializerOptions)
                ?? throw new InvalidDataException("The stored plan payload is invalid.");
            plans.Add(plan with { Revision = reader.GetInt64(1) });
        }
        return plans;
    }

    public async Task<PlanStartResult> TryStartAsync(
        long accountProfileId,
        PlanRecord plan,
        Gw2Tp.Domain.Finance.Money verifiedCash,
        Gw2Tp.Domain.Finance.Money hardReserve,
        IReadOnlyDictionary<string, long> verifiedQuantities,
        CancellationToken cancellationToken = default)
    {
        if (accountProfileId <= 0) throw new ArgumentOutOfRangeException(nameof(accountProfileId));
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(verifiedQuantities);
        await using var lease = await gate.AcquirePrivateAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await gate.ValidateAccountProfileAsync(connection, accountProfileId, cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var active = new List<PlanRecord>();
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT plan_id, payload_json FROM execution_plans WHERE account_profile_id = $accountProfileId AND state IN (2, 3, 4, 5, 6);";
            read.Parameters.AddWithValue("$accountProfileId", accountProfileId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var existing = JsonSerializer.Deserialize<PlanRecord>(reader.GetString(1), SerializerOptions)
                    ?? throw new InvalidDataException("The stored plan payload is invalid.");
                if (!existing.IsReconciliationOnly && existing.SourceOpportunityId == plan.SourceOpportunityId)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return PlanStartResult.AlreadyStarted;
                }
                active.Add(existing);
            }
        }

        if (verifiedCash.Copper < 0 || hardReserve.Copper < 0 || verifiedQuantities.Any(value => value.Value < 0))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return PlanStartResult.ResourcesUnavailable;
        }

        PlanEffectiveResources effective;
        IReadOnlyList<PlanResourceRequirement> reservations;
        IReadOnlyList<PlanResourceRequirement> candidateReservations;
        try
        {
            var events = active.SelectMany(value => value.Events).ToArray();
            effective = PlanOrchestrationService.ProjectEffectiveResources(verifiedCash, verifiedQuantities, events);
            reservations = active.SelectMany(PlanOrchestrationService.OutstandingReservations).ToArray();
            candidateReservations = PlanOrchestrationService.OutstandingReservations(plan).ToArray();
        }
        catch (OverflowException)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return PlanStartResult.ResourcesUnavailable;
        }

        if (!PlanOrchestrationService.TryAggregateResourceDemands(reservations, out var existingDemands) ||
            !PlanOrchestrationService.TryAggregateResourceDemands(candidateReservations, out var candidateDemands) ||
            !PlanOrchestrationService.TryAggregateResourceDemands(plan.Reservations, out var declaredDemands))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return PlanStartResult.ResourcesUnavailable;
        }

        var reservedCash = CashDemand(existingDemands);
        var candidateCash = CashDemand(declaredDemands);
        try
        {
            if ((effective.EffectiveCash - hardReserve - reservedCash - candidateCash).Copper < 0 ||
                HasResourceConflict(candidateDemands, existingDemands, effective.Quantities))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return PlanStartResult.ResourcesUnavailable;
            }
        }
        catch (OverflowException)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return PlanStartResult.ResourcesUnavailable;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO execution_plans (plan_id, account_profile_id, state, payload_json, updated_at_utc, revision)
            VALUES ($planId, $accountProfileId, $state, $payload, $updatedAtUtc, $revision);
            """;
        insert.Parameters.AddWithValue("$planId", plan.Id);
        insert.Parameters.AddWithValue("$accountProfileId", accountProfileId);
        insert.Parameters.AddWithValue("$state", (int)plan.State);
        var started = plan with { Revision = 1 };
        insert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(started, SerializerOptions));
        insert.Parameters.AddWithValue("$updatedAtUtc", SqlitePersistenceValues.ToUtcTimestamp(DateTimeOffset.UtcNow, "updatedAtUtc"));
        insert.Parameters.AddWithValue("$revision", 1L);
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return PlanStartResult.Started;
    }

    public async Task<PlanCompletionResult> CompleteStepAsync(
        long accountProfileId,
        PlanCompletionCommand command,
        Func<PlanRecord, PlanRecord> transition,
        CancellationToken cancellationToken = default)
    {
        if (accountProfileId <= 0) throw new ArgumentOutOfRangeException(nameof(accountProfileId));
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(transition);
        if (string.IsNullOrWhiteSpace(command.PlanId) || command.PlanId.Length > 256 ||
            string.IsNullOrWhiteSpace(command.CommandId) || command.CommandId.Length > 128)
            return new(PlanCompletionStatus.Invalid);

        await using var lease = await gate.AcquirePrivateAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await gate.ValidateAccountProfileAsync(connection, accountProfileId, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var existingReceipt = await FindReceiptAsync(connection, transaction, accountProfileId, command, cancellationToken).ConfigureAwait(false);
        if (existingReceipt is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(Matches(existingReceipt, command) ? PlanCompletionStatus.AlreadyApplied : PlanCompletionStatus.Conflict,
                Matches(existingReceipt, command) ? existingReceipt : null);
        }

        if (!IsValidCommand(command))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new(PlanCompletionStatus.Invalid, Reason: command.Operation == PlanCompletionOperation.ReportPerformed && command.Quantity <= 0
                ? PlanCompletionReason.InvalidQuantity : PlanCompletionReason.None);
        }

        PlanRecord? storedPlan;
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT payload_json, revision FROM execution_plans WHERE plan_id = $planId AND account_profile_id = $accountProfileId;";
            read.Parameters.AddWithValue("$planId", command.PlanId);
            read.Parameters.AddWithValue("$accountProfileId", accountProfileId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            storedPlan = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? (JsonSerializer.Deserialize<PlanRecord>(reader.GetString(0), SerializerOptions)
                    ?? throw new InvalidDataException("The stored plan payload is invalid.")) with { Revision = reader.GetInt64(1) }
                : null;
        }

        if (storedPlan is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new(PlanCompletionStatus.NotFound);
        }

        if (!CanAdmit(storedPlan, command))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new(PlanCompletionStatus.Conflict);
        }

        PlanRecord transitioned;
        try
        {
            transitioned = transition(storedPlan);
        }
        catch (PlanCompletionValidationException exception)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new(PlanCompletionStatus.Invalid, Reason: exception.Reason);
        }
        catch (OverflowException)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new(PlanCompletionStatus.Invalid, Reason: PlanCompletionReason.ResourceOverflow);
        }
        catch (InvalidOperationException)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new(PlanCompletionStatus.Invalid);
        }

        if (!string.Equals(transitioned.Id, storedPlan.Id, StringComparison.Ordinal) || transitioned.Revision != storedPlan.Revision)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new(PlanCompletionStatus.Invalid);
        }

        var committedRevision = checked(storedPlan.Revision + 1);
        var committedPlan = transitioned with { Revision = committedRevision };
        var updatedAt = SqlitePersistenceValues.ToUtcTimestamp(DateTimeOffset.UtcNow, "updatedAtUtc");
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE execution_plans
                SET state = $state, payload_json = $payload, updated_at_utc = $updatedAtUtc, revision = $nextRevision
                WHERE plan_id = $planId AND account_profile_id = $accountProfileId AND revision = $expectedRevision;
                """;
            update.Parameters.AddWithValue("$planId", command.PlanId);
            update.Parameters.AddWithValue("$accountProfileId", accountProfileId);
            update.Parameters.AddWithValue("$state", (int)committedPlan.State);
            update.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(committedPlan, SerializerOptions));
            update.Parameters.AddWithValue("$updatedAtUtc", updatedAt);
            update.Parameters.AddWithValue("$expectedRevision", command.ExpectedRevision);
            update.Parameters.AddWithValue("$nextRevision", committedRevision);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return new(PlanCompletionStatus.Conflict);
            }
        }

        var eventId = transitioned.Events.FirstOrDefault(value =>
            !storedPlan.Events.Any(previous => string.Equals(previous.Id, value.Id, StringComparison.Ordinal)))?.Id;
        var receipt = new PlanCompletionReceipt(command.PlanId, command.CommandId, command.StepId,
            command.ExpectedRevision, command.Operation, command.Quantity, command.UnitPrice, committedRevision,
            eventId, DateTimeOffset.UtcNow);
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO plan_completion_receipts
                    (account_profile_id, plan_id, command_id, step_id, expected_revision, operation, quantity,
                     unit_price_in_copper, committed_revision, event_id, created_at_utc)
                VALUES ($accountProfileId, $planId, $commandId, $stepId, $expectedRevision, $operation, $quantity,
                        $unitPrice, $committedRevision, $eventId, $createdAtUtc);
                """;
            insert.Parameters.AddWithValue("$accountProfileId", accountProfileId);
            insert.Parameters.AddWithValue("$planId", receipt.PlanId);
            insert.Parameters.AddWithValue("$commandId", receipt.CommandId);
            insert.Parameters.AddWithValue("$stepId", receipt.StepId);
            insert.Parameters.AddWithValue("$expectedRevision", receipt.ExpectedRevision);
            insert.Parameters.AddWithValue("$operation", (int)receipt.Operation);
            insert.Parameters.AddWithValue("$quantity", receipt.Quantity);
            insert.Parameters.AddWithValue("$unitPrice", (object?)receipt.UnitPrice?.Copper ?? DBNull.Value);
            insert.Parameters.AddWithValue("$committedRevision", receipt.CommittedRevision);
            insert.Parameters.AddWithValue("$eventId", (object?)receipt.EventId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$createdAtUtc", SqlitePersistenceValues.ToUtcTimestamp(receipt.CreatedAtUtc, "createdAtUtc"));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(PlanCompletionStatus.Applied, receipt);
    }

    public async Task SaveAsync(long accountProfileId, PlanRecord plan, CancellationToken cancellationToken = default)
    {
        if (accountProfileId <= 0) throw new ArgumentOutOfRangeException(nameof(accountProfileId));
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(plan.Id) || plan.StartedAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("The plan has invalid durable identity or time.", nameof(plan));
        var nextRevision = checked(plan.Revision + 1);
        var updated = plan with { Revision = nextRevision };
        var payload = JsonSerializer.Serialize(updated, SerializerOptions);
        await using var lease = await gate.AcquirePrivateAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await gate.ValidateAccountProfileAsync(connection, accountProfileId, cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE execution_plans
            SET state = $state, payload_json = $payload, updated_at_utc = $updatedAtUtc, revision = $nextRevision
            WHERE plan_id = $planId AND account_profile_id = $accountProfileId AND revision = $expectedRevision;
            """;
        command.Parameters.AddWithValue("$planId", plan.Id);
        command.Parameters.AddWithValue("$accountProfileId", accountProfileId);
        command.Parameters.AddWithValue("$state", (int)updated.State);
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$updatedAtUtc", SqlitePersistenceValues.ToUtcTimestamp(DateTimeOffset.UtcNow, "updatedAtUtc"));
        command.Parameters.AddWithValue("$expectedRevision", plan.Revision);
        command.Parameters.AddWithValue("$nextRevision", nextRevision);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new PlanConcurrencyException();
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PlanCompletionReceipt?> FindReceiptAsync(SqliteConnection connection,
        SqliteTransaction transaction, long accountProfileId, PlanCompletionCommand command,
        CancellationToken cancellationToken)
    {
        await using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = """
            SELECT plan_id, command_id, step_id, expected_revision, operation, quantity,
                   unit_price_in_copper, committed_revision, event_id, created_at_utc
            FROM plan_completion_receipts
            WHERE account_profile_id = $accountProfileId AND plan_id = $planId AND command_id = $commandId;
            """;
        read.Parameters.AddWithValue("$accountProfileId", accountProfileId);
        read.Parameters.AddWithValue("$planId", command.PlanId);
        read.Parameters.AddWithValue("$commandId", command.CommandId);
        await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new PlanCompletionReceipt(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetInt64(3), (PlanCompletionOperation)reader.GetInt32(4), reader.GetInt32(5),
            reader.IsDBNull(6) ? null : new Money(reader.GetInt64(6)), reader.GetInt64(7),
            reader.IsDBNull(8) ? null : reader.GetString(8), SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(9), "plan_completion_receipts.created_at_utc"));
    }

    private static bool Matches(PlanCompletionReceipt receipt, PlanCompletionCommand command) =>
        string.Equals(receipt.PlanId, command.PlanId, StringComparison.Ordinal) &&
        string.Equals(receipt.CommandId, command.CommandId, StringComparison.Ordinal) &&
        string.Equals(receipt.StepId, command.StepId, StringComparison.Ordinal) &&
        receipt.ExpectedRevision == command.ExpectedRevision && receipt.Operation == command.Operation &&
        receipt.Quantity == command.Quantity && receipt.UnitPrice == command.UnitPrice;

    private static bool IsValidCommand(PlanCompletionCommand command) =>
        !string.IsNullOrWhiteSpace(command.PlanId) && command.PlanId.Length <= 256 &&
        !string.IsNullOrWhiteSpace(command.StepId) && command.StepId.Length <= 256 &&
        !string.IsNullOrWhiteSpace(command.CommandId) && command.CommandId.Length <= 128 && command.ExpectedRevision >= 0 &&
        Enum.IsDefined(command.Operation) &&
        (command.Operation == PlanCompletionOperation.ReportPerformed
            ? command.Quantity > 0 && command.UnitPrice is not { Copper: < 0 }
            : command.Quantity == 0 && command.UnitPrice is null);

    private static bool CanAdmit(PlanRecord plan, PlanCompletionCommand command)
    {
        if (plan.Revision != command.ExpectedRevision || plan.State != PlanState.InProgress ||
            plan.CurrentStepOrdinal < 0 || plan.CurrentStepOrdinal >= plan.Steps.Count) return false;
        var current = plan.Steps[plan.CurrentStepOrdinal];
        return current.State == PlanStepState.Current &&
            string.Equals(current.Id, command.StepId, StringComparison.Ordinal);
    }

    private static bool HasResourceConflict(
        IReadOnlyDictionary<string, PlanResourceDemand> candidateDemands,
        IReadOnlyDictionary<string, PlanResourceDemand> existingDemands,
        IReadOnlyDictionary<string, long> verifiedQuantities)
    {
        foreach (var pair in candidateDemands)
        {
            var demand = pair.Value;
            if (demand.Kind == PlanResourceKind.Cash || demand.Quantity <= 0) continue;
            var used = existingDemands.GetValueOrDefault(pair.Key)?.Quantity ?? 0;
            if (verifiedQuantities.TryGetValue(pair.Key, out var capacity))
            {
                if (capacity < 0 || checked(used + demand.Quantity) > capacity) return true;
            }
            else if (demand.Kind == PlanResourceKind.Inventory || used > 0)
            {
                return true;
            }
        }
        return false;
    }

    private static Gw2Tp.Domain.Finance.Money CashDemand(IReadOnlyDictionary<string, PlanResourceDemand> demands) =>
        demands.Values.SingleOrDefault(value => value.Kind == PlanResourceKind.Cash)?.Cash ?? Gw2Tp.Domain.Finance.Money.Zero;
}
