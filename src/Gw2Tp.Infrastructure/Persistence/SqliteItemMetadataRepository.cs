using Gw2Tp.Application.Persistence;
using Microsoft.Data.Sqlite;

namespace Gw2Tp.Infrastructure.Persistence;

internal sealed class SqliteItemMetadataRepository(
    ISqliteConnectionFactory connectionFactory,
    ISqliteDatabaseGate? databaseGate = null) : IItemMetadataRepository
{
    private readonly ISqliteDatabaseGate gate = databaseGate ?? new SqliteDatabaseGate();

    public async Task UpsertAsync(
        IReadOnlyCollection<StoredItemMetadata> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            ValidateItem(item);
        }

        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var item in items)
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
            command.Parameters.AddWithValue("$itemId", item.ItemId);
            command.Parameters.AddWithValue("$name", item.Name);
            command.Parameters.AddWithValue("$observedAtUtc", SqlitePersistenceValues.ToUtcTimestamp(item.ObservedAtUtc, nameof(item.ObservedAtUtc)));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<StoredItemMetadata?> GetAsync(int itemId, CancellationToken cancellationToken = default)
    {
        if (itemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId));
        }

        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT item_id, name, observed_at_utc
            FROM item_metadata
            WHERE item_id = $itemId;
            """;
        command.Parameters.AddWithValue("$itemId", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new StoredItemMetadata(
            reader.GetInt32(0),
            reader.GetString(1),
            SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(2), "item_metadata.observed_at_utc"));
    }

    public async Task<IReadOnlyList<StoredItemMetadata>> GetManyAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        var uniqueIds = itemIds.Distinct().OrderBy(itemId => itemId).ToArray();
        if (uniqueIds.Any(itemId => itemId <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(itemIds));
        }

        if (uniqueIds.Length == 0)
        {
            return [];
        }

        var items = new List<StoredItemMetadata>();
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var itemIdBatch in uniqueIds.Chunk(900))
        {
            await using var command = connection.CreateCommand();
            var parameterNames = new List<string>(itemIdBatch.Length);
            for (var index = 0; index < itemIdBatch.Length; index++)
            {
                var parameterName = $"$itemId{index}";
                parameterNames.Add(parameterName);
                command.Parameters.AddWithValue(parameterName, itemIdBatch[index]);
            }

            command.CommandText = $"""
                SELECT item_id, name, observed_at_utc
                FROM item_metadata
                WHERE item_id IN ({string.Join(", ", parameterNames)});
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(new StoredItemMetadata(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(2), "item_metadata.observed_at_utc")));
            }
        }

        return items;
    }

    private static void ValidateItem(StoredItemMetadata item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.ItemId <= 0 || string.IsNullOrWhiteSpace(item.Name))
        {
            throw new ArgumentException("Item metadata requires a positive item ID and non-empty name.", nameof(item));
        }

        _ = SqlitePersistenceValues.ToUtcTimestamp(item.ObservedAtUtc, nameof(item.ObservedAtUtc));
    }
}
