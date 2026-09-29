using System.Text.Json;
using Gw2Tp.Application.Plans;

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

    private async Task<IReadOnlyList<PlanRecord>> ReadPlansAsync(long accountProfileId, string statePredicate, CancellationToken cancellationToken,
        DateTimeOffset? reconciliationNowUtc = null)
    {
        if (accountProfileId <= 0) throw new ArgumentOutOfRangeException(nameof(accountProfileId));
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
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
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
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

    public async Task SaveAsync(long accountProfileId, PlanRecord plan, CancellationToken cancellationToken = default)
    {
        if (accountProfileId <= 0) throw new ArgumentOutOfRangeException(nameof(accountProfileId));
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(plan.Id) || plan.StartedAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("The plan has invalid durable identity or time.", nameof(plan));
        var nextRevision = checked(plan.Revision + 1);
        var updated = plan with { Revision = nextRevision };
        var payload = JsonSerializer.Serialize(updated, SerializerOptions);
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
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
