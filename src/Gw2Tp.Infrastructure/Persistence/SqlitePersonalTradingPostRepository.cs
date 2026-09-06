using Gw2Tp.Application.Persistence;
using Microsoft.Data.Sqlite;

namespace Gw2Tp.Infrastructure.Persistence;

internal sealed class SqlitePersonalTradingPostRepository(
    ISqliteConnectionFactory connectionFactory,
    ISqliteDatabaseGate? databaseGate = null) : IPersonalTradingPostRepository
{
    private readonly ISqliteDatabaseGate gate = databaseGate ?? new SqliteDatabaseGate();

    public async Task<AccountProfile> GetOrCreateAccountProfileAsync(
        string accountScopeId,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountScopeId))
        {
            throw new ArgumentException("An opaque account scope is required.", nameof(accountScopeId));
        }

        var observedAt = SqlitePersistenceValues.ToUtcTimestamp(observedAtUtc, nameof(observedAtUtc));
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO account_profiles (account_scope_id, created_at_utc)
                VALUES ($accountScopeId, $createdAtUtc)
                ON CONFLICT(account_scope_id) DO NOTHING;
                """;
            insert.Parameters.AddWithValue("$accountScopeId", accountScopeId);
            insert.Parameters.AddWithValue("$createdAtUtc", observedAt);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        AccountProfile accountProfile;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT id, account_scope_id, created_at_utc, last_successful_sync_at_utc
                FROM account_profiles
                WHERE account_scope_id = $accountScopeId;
                """;
            select.Parameters.AddWithValue("$accountScopeId", accountScopeId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException("The SQLite account profile was not available after insert.");
            }

            accountProfile = ReadAccountProfile(reader);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return accountProfile;
    }

    public async Task RecordSuccessfulSyncAsync(
        AccountProfile accountProfile,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        SqlitePersistenceValues.ValidateAccountProfile(accountProfile);
        var completedAt = SqlitePersistenceValues.ToUtcTimestamp(completedAtUtc, nameof(completedAtUtc));
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE account_profiles
            SET last_successful_sync_at_utc = CASE
                WHEN last_successful_sync_at_utc IS NULL OR last_successful_sync_at_utc < $completedAtUtc
                    THEN $completedAtUtc
                ELSE last_successful_sync_at_utc
            END
            WHERE id = $accountProfileId AND account_scope_id = $accountScopeId;
            """;
        command.Parameters.AddWithValue("$completedAtUtc", completedAt);
        command.Parameters.AddWithValue("$accountProfileId", accountProfile.Id);
        command.Parameters.AddWithValue("$accountScopeId", accountProfile.AccountScopeId);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("The account profile does not belong to this SQLite database.");
        }
    }

    public async Task UpsertCompletedTransactionsAsync(
        AccountProfile accountProfile,
        IReadOnlyCollection<CompletedPersonalTradingPostTransaction> transactions,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default)
    {
        SqlitePersistenceValues.ValidateAccountProfile(accountProfile);
        ArgumentNullException.ThrowIfNull(transactions);
        var observedAt = SqlitePersistenceValues.ToUtcTimestamp(observedAtUtc, nameof(observedAtUtc));
        foreach (var transaction in transactions)
        {
            SqlitePersistenceValues.ValidateCompletedTransaction(transaction);
        }

        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var databaseTransaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureAccountExistsAsync(connection, databaseTransaction, accountProfile, cancellationToken).ConfigureAwait(false);

        foreach (var transaction in transactions)
        {
            var existing = await GetCompletedTransactionAsync(
                connection,
                databaseTransaction,
                accountProfile.Id,
                transaction.ExternalTransactionId,
                cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                await InsertCompletedTransactionAsync(
                    connection,
                    databaseTransaction,
                    accountProfile.Id,
                    transaction,
                    observedAt,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (existing != transaction)
            {
                throw new InvalidOperationException("A completed Trading Post transaction cannot be mutated after import.");
            }
            else
            {
                await UpdateCompletedTransactionLastSeenAsync(
                    connection,
                    databaseTransaction,
                    accountProfile.Id,
                    transaction.ExternalTransactionId,
                    observedAt,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        await databaseTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StoredCompletedPersonalTradingPostTransaction>> GetCompletedTransactionsAsync(
        AccountProfile accountProfile,
        CancellationToken cancellationToken = default)
    {
        SqlitePersistenceValues.ValidateAccountProfile(accountProfile);
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT external_transaction_id, side, item_id, unit_price_in_copper, quantity,
                   created_at_utc, completed_at_utc, first_imported_at_utc, last_seen_at_utc
            FROM completed_tp_transactions
            WHERE account_profile_id = $accountProfileId
            ORDER BY completed_at_utc, external_transaction_id;
            """;
        command.Parameters.AddWithValue("$accountProfileId", accountProfile.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var transactions = new List<StoredCompletedPersonalTradingPostTransaction>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var transaction = new CompletedPersonalTradingPostTransaction(
                reader.GetInt64(0),
                ReadSide(reader.GetInt32(1)),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(5), "completed_tp_transactions.created_at_utc"),
                SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(6), "completed_tp_transactions.completed_at_utc"));
            transactions.Add(new StoredCompletedPersonalTradingPostTransaction(
                transaction,
                SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(7), "completed_tp_transactions.first_imported_at_utc"),
                SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(8), "completed_tp_transactions.last_seen_at_utc")));
        }

        return transactions;
    }

    public async Task ReplaceCurrentOrderSnapshotAsync(
        AccountProfile accountProfile,
        CurrentPersonalTradingPostOrderSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        SqlitePersistenceValues.ValidateAccountProfile(accountProfile);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Orders);
        var observedAt = SqlitePersistenceValues.ToUtcTimestamp(snapshot.ObservedAtUtc, nameof(snapshot.ObservedAtUtc));
        var orderIds = new HashSet<long>();
        foreach (var order in snapshot.Orders)
        {
            SqlitePersistenceValues.ValidateCurrentOrder(order);
            if (!orderIds.Add(order.ExternalOrderId))
            {
                throw new ArgumentException("A current-order snapshot cannot contain the same external order twice.", nameof(snapshot));
            }
        }

        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureAccountExistsAsync(connection, transaction, accountProfile, cancellationToken).ConfigureAwait(false);
        var syncBatchId = await InsertCurrentOrderSyncBatchAsync(
            connection,
            transaction,
            accountProfile.Id,
            observedAt,
            cancellationToken).ConfigureAwait(false);

        foreach (var order in snapshot.Orders)
        {
            await InsertCurrentOrderObservationAsync(
                connection,
                transaction,
                syncBatchId,
                accountProfile.Id,
                order,
                observedAt,
                cancellationToken).ConfigureAwait(false);
        }

        await using (var deleteCurrent = connection.CreateCommand())
        {
            deleteCurrent.Transaction = transaction;
            deleteCurrent.CommandText = "DELETE FROM current_tp_orders WHERE account_profile_id = $accountProfileId;";
            deleteCurrent.Parameters.AddWithValue("$accountProfileId", accountProfile.Id);
            await deleteCurrent.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var order in snapshot.Orders)
        {
            await InsertCurrentOrderStateAsync(
                connection,
                transaction,
                syncBatchId,
                accountProfile.Id,
                order,
                observedAt,
                cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CurrentPersonalTradingPostOrder>> GetCurrentOrdersAsync(
        AccountProfile accountProfile,
        CancellationToken cancellationToken = default)
    {
        SqlitePersistenceValues.ValidateAccountProfile(accountProfile);
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT external_order_id, side, item_id, unit_price_in_copper, quantity, created_at_utc
            FROM current_tp_orders
            WHERE account_profile_id = $accountProfileId
            ORDER BY external_order_id;
            """;
        command.Parameters.AddWithValue("$accountProfileId", accountProfile.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var orders = new List<CurrentPersonalTradingPostOrder>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            orders.Add(ReadCurrentOrder(reader));
        }

        return orders;
    }

    public async Task<IReadOnlyList<CurrentPersonalTradingPostOrderSnapshot>> GetCurrentOrderObservationsAsync(
        AccountProfile accountProfile,
        CancellationToken cancellationToken = default)
    {
        SqlitePersistenceValues.ValidateAccountProfile(accountProfile);
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT batch.id, batch.observed_at_utc, observation.external_order_id, observation.side,
                   observation.item_id, observation.unit_price_in_copper, observation.quantity,
                   observation.created_at_utc
            FROM current_order_sync_batches AS batch
            LEFT JOIN current_tp_order_observations AS observation ON observation.sync_batch_id = batch.id
            WHERE batch.account_profile_id = $accountProfileId
            ORDER BY batch.id, observation.external_order_id;
            """;
        command.Parameters.AddWithValue("$accountProfileId", accountProfile.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var snapshots = new List<CurrentPersonalTradingPostOrderSnapshot>();
        long? syncBatchId = null;
        DateTimeOffset? observedAtUtc = null;
        List<CurrentPersonalTradingPostOrder>? orders = null;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var rowSyncBatchId = reader.GetInt64(0);
            var rowObservedAtUtc = SqlitePersistenceValues.FromUtcTimestamp(
                reader.GetString(1),
                "current_order_sync_batches.observed_at_utc");
            if (syncBatchId != rowSyncBatchId)
            {
                if (syncBatchId is not null)
                {
                    snapshots.Add(new CurrentPersonalTradingPostOrderSnapshot(
                        observedAtUtc ?? throw new InvalidDataException("The SQLite sync batch is missing its observation timestamp."),
                        orders!));
                }

                syncBatchId = rowSyncBatchId;
                observedAtUtc = rowObservedAtUtc;
                orders = [];
            }

            if (!reader.IsDBNull(2))
            {
                orders!.Add(new CurrentPersonalTradingPostOrder(
                    reader.GetInt64(2),
                    ReadSide(reader.GetInt32(3)),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(7), "current_tp_order_observations.created_at_utc")));
            }
        }

        if (syncBatchId is not null)
        {
            snapshots.Add(new CurrentPersonalTradingPostOrderSnapshot(
                observedAtUtc ?? throw new InvalidDataException("The SQLite sync batch is missing its observation timestamp."),
                orders!));
        }

        return snapshots;
    }

    private static async Task EnsureAccountExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AccountProfile accountProfile,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM account_profiles
            WHERE id = $accountProfileId AND account_scope_id = $accountScopeId;
            """;
        command.Parameters.AddWithValue("$accountProfileId", accountProfile.Id);
        command.Parameters.AddWithValue("$accountScopeId", accountProfile.AccountScopeId);
        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
        {
            throw new InvalidOperationException("The account profile does not belong to this SQLite database.");
        }
    }

    private static async Task<CompletedPersonalTradingPostTransaction?> GetCompletedTransactionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long accountProfileId,
        long externalTransactionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT external_transaction_id, side, item_id, unit_price_in_copper, quantity,
                   created_at_utc, completed_at_utc
            FROM completed_tp_transactions
            WHERE account_profile_id = $accountProfileId AND external_transaction_id = $externalTransactionId;
            """;
        command.Parameters.AddWithValue("$accountProfileId", accountProfileId);
        command.Parameters.AddWithValue("$externalTransactionId", externalTransactionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new CompletedPersonalTradingPostTransaction(
            reader.GetInt64(0),
            ReadSide(reader.GetInt32(1)),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(5), "completed_tp_transactions.created_at_utc"),
            SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(6), "completed_tp_transactions.completed_at_utc"));
    }

    private static async Task InsertCompletedTransactionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long accountProfileId,
        CompletedPersonalTradingPostTransaction completedTransaction,
        string observedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO completed_tp_transactions (
                account_profile_id, external_transaction_id, side, item_id, unit_price_in_copper,
                quantity, created_at_utc, completed_at_utc, first_imported_at_utc, last_seen_at_utc)
            VALUES (
                $accountProfileId, $externalTransactionId, $side, $itemId, $unitPriceInCopper,
                $quantity, $createdAtUtc, $completedAtUtc, $observedAtUtc, $observedAtUtc);
            """;
        command.Parameters.AddWithValue("$accountProfileId", accountProfileId);
        command.Parameters.AddWithValue("$externalTransactionId", completedTransaction.ExternalTransactionId);
        command.Parameters.AddWithValue("$side", (int)completedTransaction.Side);
        command.Parameters.AddWithValue("$itemId", completedTransaction.ItemId);
        command.Parameters.AddWithValue("$unitPriceInCopper", completedTransaction.UnitPriceInCopper);
        command.Parameters.AddWithValue("$quantity", completedTransaction.Quantity);
        command.Parameters.AddWithValue("$createdAtUtc", SqlitePersistenceValues.ToUtcTimestamp(completedTransaction.CreatedAtUtc, nameof(completedTransaction.CreatedAtUtc)));
        command.Parameters.AddWithValue("$completedAtUtc", SqlitePersistenceValues.ToUtcTimestamp(completedTransaction.CompletedAtUtc, nameof(completedTransaction.CompletedAtUtc)));
        command.Parameters.AddWithValue("$observedAtUtc", observedAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpdateCompletedTransactionLastSeenAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long accountProfileId,
        long externalTransactionId,
        string observedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE completed_tp_transactions
            SET last_seen_at_utc = CASE
                WHEN last_seen_at_utc < $observedAtUtc THEN $observedAtUtc
                ELSE last_seen_at_utc
            END
            WHERE account_profile_id = $accountProfileId AND external_transaction_id = $externalTransactionId;
            """;
        command.Parameters.AddWithValue("$observedAtUtc", observedAtUtc);
        command.Parameters.AddWithValue("$accountProfileId", accountProfileId);
        command.Parameters.AddWithValue("$externalTransactionId", externalTransactionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<long> InsertCurrentOrderSyncBatchAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long accountProfileId,
        string observedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO current_order_sync_batches (account_profile_id, observed_at_utc)
            VALUES ($accountProfileId, $observedAtUtc);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$accountProfileId", accountProfileId);
        command.Parameters.AddWithValue("$observedAtUtc", observedAtUtc);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task InsertCurrentOrderObservationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long syncBatchId,
        long accountProfileId,
        CurrentPersonalTradingPostOrder order,
        string observedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO current_tp_order_observations (
                sync_batch_id, account_profile_id, external_order_id, side, item_id,
                unit_price_in_copper, quantity, created_at_utc, observed_at_utc)
            VALUES (
                $syncBatchId, $accountProfileId, $externalOrderId, $side, $itemId,
                $unitPriceInCopper, $quantity, $createdAtUtc, $observedAtUtc);
            """;
        AddCurrentOrderParameters(command, syncBatchId, accountProfileId, order, observedAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertCurrentOrderStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long syncBatchId,
        long accountProfileId,
        CurrentPersonalTradingPostOrder order,
        string observedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO current_tp_orders (
                account_profile_id, sync_batch_id, external_order_id, side, item_id,
                unit_price_in_copper, quantity, created_at_utc, observed_at_utc)
            VALUES (
                $accountProfileId, $syncBatchId, $externalOrderId, $side, $itemId,
                $unitPriceInCopper, $quantity, $createdAtUtc, $observedAtUtc);
            """;
        AddCurrentOrderParameters(command, syncBatchId, accountProfileId, order, observedAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddCurrentOrderParameters(
        SqliteCommand command,
        long syncBatchId,
        long accountProfileId,
        CurrentPersonalTradingPostOrder order,
        string observedAtUtc)
    {
        command.Parameters.AddWithValue("$syncBatchId", syncBatchId);
        command.Parameters.AddWithValue("$accountProfileId", accountProfileId);
        command.Parameters.AddWithValue("$externalOrderId", order.ExternalOrderId);
        command.Parameters.AddWithValue("$side", (int)order.Side);
        command.Parameters.AddWithValue("$itemId", order.ItemId);
        command.Parameters.AddWithValue("$unitPriceInCopper", order.UnitPriceInCopper);
        command.Parameters.AddWithValue("$quantity", order.Quantity);
        command.Parameters.AddWithValue("$createdAtUtc", SqlitePersistenceValues.ToUtcTimestamp(order.CreatedAtUtc, nameof(order.CreatedAtUtc)));
        command.Parameters.AddWithValue("$observedAtUtc", observedAtUtc);
    }

    private static AccountProfile ReadAccountProfile(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(2), "account_profiles.created_at_utc"),
        reader.IsDBNull(3)
            ? null
            : SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(3), "account_profiles.last_successful_sync_at_utc"));

    private static CurrentPersonalTradingPostOrder ReadCurrentOrder(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        ReadSide(reader.GetInt32(1)),
        reader.GetInt32(2),
        reader.GetInt32(3),
        reader.GetInt32(4),
        SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(5), "current_tp_orders.created_at_utc"));

    private static PersonalTradingPostSide ReadSide(int value) => value switch
    {
        (int)PersonalTradingPostSide.Buy => PersonalTradingPostSide.Buy,
        (int)PersonalTradingPostSide.Sell => PersonalTradingPostSide.Sell,
        _ => throw new InvalidDataException("The SQLite Trading Post side is invalid."),
    };
}
