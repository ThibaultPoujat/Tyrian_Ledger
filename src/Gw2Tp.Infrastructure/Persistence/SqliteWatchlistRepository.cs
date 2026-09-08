using Gw2Tp.Application.Persistence;

namespace Gw2Tp.Infrastructure.Persistence;

internal sealed class SqliteWatchlistRepository(
    ISqliteConnectionFactory connectionFactory,
    ISqliteDatabaseGate? databaseGate = null) : IWatchlistRepository
{
    private readonly ISqliteDatabaseGate gate = databaseGate ?? new SqliteDatabaseGate();

    public async Task<IReadOnlyList<WatchlistEntry>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT item_id, added_at_utc FROM watchlist_entries ORDER BY added_at_utc, item_id;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var entries = new List<WatchlistEntry>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new WatchlistEntry(
                reader.GetInt32(0),
                SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(1), "watchlist_entries.added_at_utc")));
        }

        return entries;
    }

    public async Task AddAsync(WatchlistEntry entry, CancellationToken cancellationToken = default)
    {
        Validate(entry);
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO watchlist_entries (item_id, added_at_utc)
            VALUES ($itemId, $addedAtUtc)
            ON CONFLICT(item_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$itemId", entry.ItemId);
        command.Parameters.AddWithValue("$addedAtUtc", SqlitePersistenceValues.ToUtcTimestamp(entry.AddedAtUtc, nameof(entry.AddedAtUtc)));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(int itemId, CancellationToken cancellationToken = default)
    {
        if (itemId <= 0) throw new ArgumentOutOfRangeException(nameof(itemId));
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM watchlist_entries WHERE item_id = $itemId;";
        command.Parameters.AddWithValue("$itemId", itemId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Validate(WatchlistEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.ItemId <= 0) throw new ArgumentOutOfRangeException(nameof(entry));
        _ = SqlitePersistenceValues.ToUtcTimestamp(entry.AddedAtUtc, nameof(entry.AddedAtUtc));
    }
}
