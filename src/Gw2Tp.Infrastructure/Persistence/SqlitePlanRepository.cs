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
    {
        if (accountProfileId <= 0) throw new ArgumentOutOfRangeException(nameof(accountProfileId));
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM execution_plans WHERE account_profile_id = $accountProfileId AND state IN (2, 3, 4, 5, 6) ORDER BY updated_at_utc, plan_id;";
        command.Parameters.AddWithValue("$accountProfileId", accountProfileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var plans = new List<PlanRecord>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var plan = JsonSerializer.Deserialize<PlanRecord>(reader.GetString(0), SerializerOptions)
                ?? throw new InvalidDataException("The stored plan payload is invalid.");
            plans.Add(plan);
        }
        return plans;
    }

    public async Task SaveAsync(long accountProfileId, PlanRecord plan, CancellationToken cancellationToken = default)
    {
        if (accountProfileId <= 0) throw new ArgumentOutOfRangeException(nameof(accountProfileId));
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(plan.Id) || plan.StartedAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("The plan has invalid durable identity or time.", nameof(plan));
        var payload = JsonSerializer.Serialize(plan, SerializerOptions);
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO execution_plans (plan_id, account_profile_id, state, payload_json, updated_at_utc)
            VALUES ($planId, $accountProfileId, $state, $payload, $updatedAtUtc)
            ON CONFLICT(plan_id) DO UPDATE SET state = excluded.state, payload_json = excluded.payload_json,
                updated_at_utc = excluded.updated_at_utc
            WHERE execution_plans.account_profile_id = excluded.account_profile_id;
            """;
        command.Parameters.AddWithValue("$planId", plan.Id);
        command.Parameters.AddWithValue("$accountProfileId", accountProfileId);
        command.Parameters.AddWithValue("$state", (int)plan.State);
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$updatedAtUtc", SqlitePersistenceValues.ToUtcTimestamp(DateTimeOffset.UtcNow, "updatedAtUtc"));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("The execution plan belongs to a different account scope.");
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
