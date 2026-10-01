using System.Text.Json;
using System.Text.Json.Serialization;
using Gw2Tp.Application.AccountEvidence;
using Gw2Tp.Application.LocalData;
using Gw2Tp.Application.PersonalTradingPost;
using Microsoft.Data.Sqlite;

namespace Gw2Tp.Infrastructure.Persistence;

/// <summary>One normalized typed document per account, plus relational ownership and monotone capture time.</summary>
internal sealed class SqliteAccountHoldingsSnapshotRepository(
    ISqliteConnectionFactory connectionFactory, ISqliteDatabaseGate? databaseGate = null,
    IAccountWorkFence? fence = null) : IAccountHoldingsSnapshotRepository
{
    internal static readonly JsonSerializerOptions SerializerOptions = CreateOptions();
    private readonly ISqliteDatabaseGate gate = databaseGate ?? new SqliteDatabaseGate();

    public async Task<bool> ReplaceAsync(AccountHoldingsSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        AccountHoldingsSnapshotValidation.Validate(snapshot);
        await using var lease = await gate.AcquirePrivateAsync(cancellationToken).ConfigureAwait(false);
        gate.ValidateAccountScope(snapshot.Capture.AccountScope.AccountId);
        if (fence is not null && (snapshot.Generation != fence.Current?.Generation || snapshot.StoreIncarnation != fence.Current?.StoreIncarnation))
            throw new AccountWorkRejectedException();
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var previous = await ReadAsync(connection, transaction, snapshot.Capture.AccountScope, cancellationToken).ConfigureAwait(false);
        if (previous is not null && previous.StoreIncarnation == snapshot.StoreIncarnation && previous.Generation == snapshot.Generation &&
            previous.Capture.AccountIdentityFetch.StartedAtUtc >= snapshot.Capture.AccountIdentityFetch.StartedAtUtc)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return previous.Capture.RefreshId == snapshot.Capture.RefreshId;
        }
        snapshot = MergeProtectiveEvidence(snapshot, previous);
        var profileId = await SqliteAccountCraftingSnapshotRepository.GetOrCreateProfileIdAsync(connection, transaction,
            snapshot.Capture.AccountScope.AccountId, snapshot.Capture.EvaluatedAtUtc, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO account_holdings_snapshots (account_profile_id, capture_started_at_utc, payload_json)
            VALUES ($profile, $started, $payload)
            ON CONFLICT(account_profile_id) DO UPDATE SET capture_started_at_utc = excluded.capture_started_at_utc, payload_json = excluded.payload_json;
            """;
        command.Parameters.AddWithValue("$profile", profileId);
        command.Parameters.AddWithValue("$started", SqlitePersistenceValues.ToUtcTimestamp(snapshot.Capture.AccountIdentityFetch.StartedAtUtc, "captureStartedAtUtc"));
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(snapshot, SerializerOptions));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<AccountHoldingsSnapshot?> GetLatestAsync(AccountScope scope, CancellationToken cancellationToken = default)
    {
        await using var lease = await gate.AcquirePrivateAsync(cancellationToken).ConfigureAwait(false);
        gate.ValidateAccountScope(scope.AccountId);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, null, scope, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<AccountHoldingsSnapshot?> ReadAsync(SqliteConnection connection, SqliteTransaction? transaction,
        AccountScope scope, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT s.payload_json, s.capture_started_at_utc FROM account_holdings_snapshots s JOIN account_profiles p ON p.id=s.account_profile_id WHERE p.account_scope_id=$scope";
        command.Parameters.AddWithValue("$scope", scope.AccountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return Deserialize(reader.GetString(0), scope.AccountId, reader.GetString(1));
    }

    internal static AccountHoldingsSnapshot Deserialize(string json, string scopeId, string startedAtUtc)
    {
        try
        {
            var value = JsonSerializer.Deserialize<AccountHoldingsSnapshot>(json, SerializerOptions)
                ?? throw new InvalidDataException("Invalid holdings document.");
            AccountHoldingsSnapshotValidation.Validate(value);
            if (value.Capture.AccountScope.AccountId != scopeId ||
                value.Capture.AccountIdentityFetch.StartedAtUtc != SqlitePersistenceValues.FromUtcTimestamp(startedAtUtc, "holdings capture"))
                throw new InvalidDataException("Holdings ownership/provenance mismatch.");
            return value;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NullReferenceException or NotSupportedException or OverflowException)
        {
            throw new InvalidDataException("Invalid normalized holdings evidence.", exception);
        }
    }

    internal static AccountHoldingsSnapshot MergeProtectiveEvidence(AccountHoldingsSnapshot next, AccountHoldingsSnapshot? previous)
    {
        var observedFloor = new AccountHoldingsProtectionPolicy().Evaluate(next.Capture, next.Rules, next.Categories,
            previousFloor: next.ProtectionFloor).Floor;
        if (previous is null) return next with { ProtectionFloor = observedFloor };
        var floor = previous.ProtectionFloor.ItemIds.Concat(observedFloor.ItemIds).ToHashSet();
        var sources = new[] { next.Capture.Bank, next.Capture.SharedInventory, next.Capture.MaterialStorage }
            .Concat(next.Capture.Characters.Select(actor => actor.Inventory)).ToArray();
        bool Replaced(Gw2Tp.Application.AccountEvidence.HoldingsItemObservation row) => sources.Any(source =>
            source.Source == row.Location.Source && source.ActorId == row.Location.ActorId && source.Availability == EvidenceAvailability.Available &&
            source.Completeness == EvidenceCompleteness.Complete && source.Value is not null);
        var oldRows = new[] { previous.Capture.Bank, previous.Capture.SharedInventory, previous.Capture.MaterialStorage }
            .Concat(previous.Capture.Characters.Select(actor => actor.Inventory)).SelectMany(source => source.Value?.Items ?? [])
            .Concat(previous.StaleObservations).Where(row => !Replaced(row))
            .DistinctBy(row => JsonSerializer.Serialize(row, SerializerOptions)).ToArray();
        // These rows have no admission path. Persisted stale facts can only explain uncertainty/protective history.
        return next with { ProtectionFloor = new(next.Capture.AccountScope, floor), StaleObservations = oldRows };
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.Converters.Add(new IntSetConverter());
        return options;
    }

    private sealed class IntSetConverter : JsonConverter<IReadOnlySet<int>>
    {
        public override IReadOnlySet<int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            (JsonSerializer.Deserialize<int[]>(ref reader, options) ?? throw new JsonException()).ToHashSet();
        public override void Write(Utf8JsonWriter writer, IReadOnlySet<int> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.Order().ToArray(), options);
    }
}
