using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Persistence;
using Microsoft.Data.Sqlite;

namespace Gw2Tp.Infrastructure.Persistence;

/// <summary>
/// SQLite implementation of the synchronization unit of work. The current
/// materialization, its observation, completed history, metadata, and sync
/// status are committed together so an incomplete local write cannot replace
/// last-known-good current orders.
/// </summary>
internal sealed class SqlitePersonalTradingPostSynchronizationStore(
    ISqliteConnectionFactory connectionFactory,
    ISqliteDatabaseGate? databaseGate = null)
    : IPersonalTradingPostSynchronizationStore
{
    private const int SuccessfulOutcome = 1;
    private const int FailedOutcome = 2;
    private readonly ISqliteDatabaseGate gate = databaseGate ?? new SqliteDatabaseGate();

    public async Task<PersonalTradingPostHistoryCoverage> CommitSuccessfulSyncAsync(
        PersonalTradingPostSuccessfulSync sync,
        CancellationToken cancellationToken = default)
    {
        ValidateSuccessfulSync(sync);
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var accountProfileId = await GetOrCreateAccountProfileIdAsync(
            connection,
            transaction,
            sync.AccountScopeId,
            sync.CompletedAtUtc,
            cancellationToken).ConfigureAwait(false);
        var existingHistoryCoverage = await GetHistoryCoverageAsync(
            connection,
            transaction,
            accountProfileId,
            cancellationToken).ConfigureAwait(false);
        var effectiveHistoryCoverage = MergeHistoryCoverage(existingHistoryCoverage, sync);

        foreach (var completedTransaction in sync.CompletedTransactions)
        {
            await UpsertCompletedTransactionAsync(
                connection,
                transaction,
                accountProfileId,
                completedTransaction,
                sync.CompletedAtUtc,
                cancellationToken).ConfigureAwait(false);
        }

        var syncBatchId = await InsertCurrentOrderSyncBatchAsync(
            connection,
            transaction,
            accountProfileId,
            sync.CurrentOrders.ObservedAtUtc,
            cancellationToken).ConfigureAwait(false);
        foreach (var order in sync.CurrentOrders.Orders)
        {
            await InsertCurrentOrderObservationAsync(
                connection,
                transaction,
                syncBatchId,
                accountProfileId,
                order,
                sync.CurrentOrders.ObservedAtUtc,
                cancellationToken).ConfigureAwait(false);
        }

        await using (var deleteCurrentOrders = connection.CreateCommand())
        {
            deleteCurrentOrders.Transaction = transaction;
            deleteCurrentOrders.CommandText = "DELETE FROM current_tp_orders WHERE account_profile_id = $accountProfileId;";
            deleteCurrentOrders.Parameters.AddWithValue("$accountProfileId", accountProfileId);
            await deleteCurrentOrders.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var order in sync.CurrentOrders.Orders)
        {
            await InsertCurrentOrderAsync(
                connection,
                transaction,
                syncBatchId,
                accountProfileId,
                order,
                sync.CurrentOrders.ObservedAtUtc,
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var itemMetadata in sync.ItemMetadata)
        {
            await UpsertItemMetadataAsync(connection, transaction, itemMetadata, cancellationToken).ConfigureAwait(false);
        }

        await using (var updateStatus = connection.CreateCommand())
        {
            updateStatus.Transaction = transaction;
            updateStatus.CommandText = """
                UPDATE account_profiles
                SET last_successful_sync_at_utc = $completedAtUtc,
                    last_sync_attempted_at_utc = $completedAtUtc,
                    last_sync_outcome = $successfulOutcome,
                    last_sync_error_category = NULL,
                    history_coverage_start_utc = $historyCoverageStartUtc,
                    history_coverage_end_utc = $historyCoverageEndUtc
                WHERE id = $accountProfileId;
                """;
            updateStatus.Parameters.AddWithValue("$completedAtUtc", ToUtc(sync.CompletedAtUtc, nameof(sync.CompletedAtUtc)));
            updateStatus.Parameters.AddWithValue("$successfulOutcome", SuccessfulOutcome);
            updateStatus.Parameters.AddWithValue("$historyCoverageStartUtc", ToNullableUtc(effectiveHistoryCoverage.StartUtc, nameof(effectiveHistoryCoverage.StartUtc)));
            updateStatus.Parameters.AddWithValue("$historyCoverageEndUtc", ToNullableUtc(effectiveHistoryCoverage.EndUtc, nameof(effectiveHistoryCoverage.EndUtc)));
            updateStatus.Parameters.AddWithValue("$accountProfileId", accountProfileId);
            if (await updateStatus.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidDataException("The SQLite account profile was unavailable during sync commit.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return effectiveHistoryCoverage;
    }

    public async Task RecordFailedSyncAsync(
        string accountScopeId,
        DateTimeOffset attemptedAtUtc,
        Gw2ApiErrorCategory errorCategory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountScopeId))
        {
            throw new ArgumentException("An opaque account scope is required.", nameof(accountScopeId));
        }

        _ = ToUtc(attemptedAtUtc, nameof(attemptedAtUtc));
        if (!Enum.IsDefined(errorCategory))
        {
            throw new ArgumentOutOfRangeException(nameof(errorCategory));
        }

        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var accountProfileId = await GetOrCreateAccountProfileIdAsync(
            connection,
            transaction,
            accountScopeId,
            attemptedAtUtc,
            cancellationToken).ConfigureAwait(false);
        await using (var updateStatus = connection.CreateCommand())
        {
            updateStatus.Transaction = transaction;
            updateStatus.CommandText = """
                UPDATE account_profiles
                SET last_sync_attempted_at_utc = $attemptedAtUtc,
                    last_sync_outcome = $failedOutcome,
                    last_sync_error_category = $errorCategory
                WHERE id = $accountProfileId;
                """;
            updateStatus.Parameters.AddWithValue("$attemptedAtUtc", ToUtc(attemptedAtUtc, nameof(attemptedAtUtc)));
            updateStatus.Parameters.AddWithValue("$failedOutcome", FailedOutcome);
            updateStatus.Parameters.AddWithValue("$errorCategory", (int)errorCategory);
            updateStatus.Parameters.AddWithValue("$accountProfileId", accountProfileId);
            if (await updateStatus.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidDataException("The SQLite account profile was unavailable during failed sync recording.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateSuccessfulSync(PersonalTradingPostSuccessfulSync sync)
    {
        ArgumentNullException.ThrowIfNull(sync);
        if (string.IsNullOrWhiteSpace(sync.AccountScopeId) || sync.CompletedTransactions is null ||
            sync.CurrentOrders is null || sync.CurrentOrders.Orders is null || sync.ItemMetadata is null ||
            (sync.HistoryCoverageStartUtc is null) != (sync.HistoryCoverageEndUtc is null))
        {
            throw new ArgumentException("The successful synchronization is incomplete.", nameof(sync));
        }

        _ = ToUtc(sync.CompletedAtUtc, nameof(sync.CompletedAtUtc));
        _ = ToUtc(sync.CurrentOrders.ObservedAtUtc, nameof(sync.CurrentOrders.ObservedAtUtc));
        if (sync.HistoryCoverageStartUtc is { } startUtc && sync.HistoryCoverageEndUtc is { } endUtc)
        {
            _ = ToUtc(startUtc, nameof(sync.HistoryCoverageStartUtc));
            _ = ToUtc(endUtc, nameof(sync.HistoryCoverageEndUtc));
            if (startUtc > endUtc)
            {
                throw new ArgumentException("History coverage start must not be after its end.", nameof(sync));
            }
        }

        if (sync.CompletedTransactions.Select(transaction => transaction.ExternalTransactionId).Distinct().Count() != sync.CompletedTransactions.Count ||
            sync.CurrentOrders.Orders.Select(order => order.ExternalOrderId).Distinct().Count() != sync.CurrentOrders.Orders.Count ||
            sync.ItemMetadata.Select(item => item.ItemId).Distinct().Count() != sync.ItemMetadata.Count)
        {
            throw new ArgumentException("A successful synchronization contains duplicate external identifiers.", nameof(sync));
        }

        foreach (var completedTransaction in sync.CompletedTransactions)
        {
            SqlitePersistenceValues.ValidateCompletedTransaction(completedTransaction);
        }

        foreach (var order in sync.CurrentOrders.Orders)
        {
            SqlitePersistenceValues.ValidateCurrentOrder(order);
        }

        foreach (var item in sync.ItemMetadata)
        {
            if (item is null || item.ItemId <= 0 || string.IsNullOrWhiteSpace(item.Name))
            {
                throw new ArgumentException("Item metadata must include a positive ID and non-empty name.", nameof(sync));
            }

            _ = ToUtc(item.ObservedAtUtc, nameof(item.ObservedAtUtc));
        }
    }

    private static async Task<PersonalTradingPostHistoryCoverage> GetHistoryCoverageAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long accountProfileId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT history_coverage_start_utc, history_coverage_end_utc
            FROM account_profiles
            WHERE id = $accountProfileId;
            """;
        command.Parameters.AddWithValue("$accountProfileId", accountProfileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("The SQLite account profile was unavailable while reading history coverage.");
        }

        var startUtc = ReadNullableUtc(reader, 0, "history_coverage_start_utc");
        var endUtc = ReadNullableUtc(reader, 1, "history_coverage_end_utc");
        if ((startUtc is null) != (endUtc is null) || (startUtc is not null && startUtc > endUtc))
        {
            throw new InvalidDataException("The SQLite account profile contains invalid history coverage.");
        }

        return new PersonalTradingPostHistoryCoverage(startUtc, endUtc);
    }

    private static PersonalTradingPostHistoryCoverage MergeHistoryCoverage(
        PersonalTradingPostHistoryCoverage existing,
        PersonalTradingPostSuccessfulSync sync)
    {
        var incoming = new PersonalTradingPostHistoryCoverage(sync.HistoryCoverageStartUtc, sync.HistoryCoverageEndUtc);
        if (incoming.StartUtc is null || existing.StartUtc is null)
        {
            return incoming;
        }

        return incoming.StartUtc <= existing.EndUtc && existing.StartUtc <= incoming.EndUtc
            ? new PersonalTradingPostHistoryCoverage(
                incoming.StartUtc < existing.StartUtc ? incoming.StartUtc : existing.StartUtc,
                incoming.EndUtc > existing.EndUtc ? incoming.EndUtc : existing.EndUtc)
            : incoming;
    }

    private static DateTimeOffset? ReadNullableUtc(SqliteDataReader reader, int ordinal, string columnName)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(
                reader.GetString(ordinal),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var value) || value.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException($"The SQLite {columnName} value is not UTC.");
        }

        return value;
    }

    private static async Task<long> GetOrCreateAccountProfileIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string accountScopeId,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO account_profiles (account_scope_id, created_at_utc)
                VALUES ($accountScopeId, $createdAtUtc)
                ON CONFLICT(account_scope_id) DO NOTHING;
                """;
            insert.Parameters.AddWithValue("$accountScopeId", accountScopeId);
            insert.Parameters.AddWithValue("$createdAtUtc", ToUtc(observedAtUtc, nameof(observedAtUtc)));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = "SELECT id FROM account_profiles WHERE account_scope_id = $accountScopeId;";
        select.Parameters.AddWithValue("$accountScopeId", accountScopeId);
        var accountProfileId = await select.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return accountProfileId is long id && id > 0
            ? id
            : throw new InvalidDataException("The SQLite account profile was not available after insert.");
    }

    private static async Task UpsertCompletedTransactionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long accountProfileId,
        CompletedPersonalTradingPostTransaction completedTransaction,
        DateTimeOffset observedAtUtc,
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
                $quantity, $createdAtUtc, $completedAtUtc, $observedAtUtc, $observedAtUtc)
            ON CONFLICT(account_profile_id, external_transaction_id) DO UPDATE SET
                last_seen_at_utc = excluded.last_seen_at_utc
            WHERE completed_tp_transactions.side = excluded.side
                AND completed_tp_transactions.item_id = excluded.item_id
                AND completed_tp_transactions.unit_price_in_copper = excluded.unit_price_in_copper
                AND completed_tp_transactions.quantity = excluded.quantity
                AND completed_tp_transactions.created_at_utc = excluded.created_at_utc
                AND completed_tp_transactions.completed_at_utc = excluded.completed_at_utc;
            """;
        command.Parameters.AddWithValue("$accountProfileId", accountProfileId);
        command.Parameters.AddWithValue("$externalTransactionId", completedTransaction.ExternalTransactionId);
        command.Parameters.AddWithValue("$side", (int)completedTransaction.Side);
        command.Parameters.AddWithValue("$itemId", completedTransaction.ItemId);
        command.Parameters.AddWithValue("$unitPriceInCopper", completedTransaction.UnitPriceInCopper);
        command.Parameters.AddWithValue("$quantity", completedTransaction.Quantity);
        command.Parameters.AddWithValue("$createdAtUtc", ToUtc(completedTransaction.CreatedAtUtc, nameof(completedTransaction.CreatedAtUtc)));
        command.Parameters.AddWithValue("$completedAtUtc", ToUtc(completedTransaction.CompletedAtUtc, nameof(completedTransaction.CompletedAtUtc)));
        command.Parameters.AddWithValue("$observedAtUtc", ToUtc(observedAtUtc, nameof(observedAtUtc)));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("A completed Trading Post transaction cannot be mutated after import.");
        }
    }

    private static async Task<long> InsertCurrentOrderSyncBatchAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long accountProfileId,
        DateTimeOffset observedAtUtc,
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
        command.Parameters.AddWithValue("$observedAtUtc", ToUtc(observedAtUtc, nameof(observedAtUtc)));
        var syncBatchId = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return syncBatchId is long id && id > 0
            ? id
            : throw new InvalidDataException("The SQLite current-order sync batch was not created.");
    }

    private static async Task InsertCurrentOrderObservationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long syncBatchId,
        long accountProfileId,
        CurrentPersonalTradingPostOrder order,
        DateTimeOffset observedAtUtc,
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
        AddOrderParameters(command, syncBatchId, accountProfileId, order, observedAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertCurrentOrderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long syncBatchId,
        long accountProfileId,
        CurrentPersonalTradingPostOrder order,
        DateTimeOffset observedAtUtc,
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
        AddOrderParameters(command, syncBatchId, accountProfileId, order, observedAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddOrderParameters(
        SqliteCommand command,
        long syncBatchId,
        long accountProfileId,
        CurrentPersonalTradingPostOrder order,
        DateTimeOffset observedAtUtc)
    {
        command.Parameters.AddWithValue("$syncBatchId", syncBatchId);
        command.Parameters.AddWithValue("$accountProfileId", accountProfileId);
        command.Parameters.AddWithValue("$externalOrderId", order.ExternalOrderId);
        command.Parameters.AddWithValue("$side", (int)order.Side);
        command.Parameters.AddWithValue("$itemId", order.ItemId);
        command.Parameters.AddWithValue("$unitPriceInCopper", order.UnitPriceInCopper);
        command.Parameters.AddWithValue("$quantity", order.Quantity);
        command.Parameters.AddWithValue("$createdAtUtc", ToUtc(order.CreatedAtUtc, nameof(order.CreatedAtUtc)));
        command.Parameters.AddWithValue("$observedAtUtc", ToUtc(observedAtUtc, nameof(observedAtUtc)));
    }

    private static async Task UpsertItemMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        StoredItemMetadata itemMetadata,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO item_metadata (item_id, name, observed_at_utc)
            VALUES ($itemId, $name, $observedAtUtc)
            ON CONFLICT(item_id) DO UPDATE SET
                name = excluded.name,
                observed_at_utc = excluded.observed_at_utc;
            """;
        command.Parameters.AddWithValue("$itemId", itemMetadata.ItemId);
        command.Parameters.AddWithValue("$name", itemMetadata.Name);
        command.Parameters.AddWithValue("$observedAtUtc", ToUtc(itemMetadata.ObservedAtUtc, nameof(itemMetadata.ObservedAtUtc)));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string ToUtc(DateTimeOffset value, string parameterName) =>
        SqlitePersistenceValues.ToUtcTimestamp(value, parameterName);

    private static object ToNullableUtc(DateTimeOffset? value, string parameterName) =>
        value is null ? DBNull.Value : ToUtc(value.Value, parameterName);
}
