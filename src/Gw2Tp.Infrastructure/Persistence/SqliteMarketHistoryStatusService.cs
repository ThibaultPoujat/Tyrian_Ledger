using Gw2Tp.Application.MarketHistory;
using Microsoft.Data.Sqlite;

namespace Gw2Tp.Infrastructure.Persistence;

/// <summary>
/// Read-only governance queries for durable public-market evidence. Retention
/// policy version one intentionally reports rather than deletes or rewrites
/// raw history.
/// </summary>
internal sealed class SqliteMarketHistoryStatusService(
    SqliteConnectionFactory connectionFactory,
    SqliteSchemaMigrator schemaMigrator,
    ISqliteDatabaseGate databaseGate) : IMarketHistoryStatusService
{
    public async Task<MarketHistoryStatus> GetStatusAsync(
        MarketHistoryCoverageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();

        await using var lease = await databaseGate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        var integrityState = await GetIntegrityStateAsync(cancellationToken).ConfigureAwait(false);
        if (integrityState == MarketHistoryIntegrityState.Failed)
        {
            return new MarketHistoryStatus(
                MarketHistoryRetentionPolicies.Current,
                GetDatabaseFileBytes(),
                new MarketHistoryCoverage(0, null, null, 0, 0, null, null),
                integrityState);
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var coverage = await GetCoverageAsync(connection, query, cancellationToken).ConfigureAwait(false);

        return new MarketHistoryStatus(
            MarketHistoryRetentionPolicies.Current,
            GetDatabaseFileBytes(),
            coverage,
            integrityState);
    }

    private async Task<MarketHistoryIntegrityState> GetIntegrityStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await schemaMigrator.ValidatePersistedDataAsync(cancellationToken).ConfigureAwait(false);
            return MarketHistoryIntegrityState.Passed;
        }
        catch (InvalidDataException)
        {
            return MarketHistoryIntegrityState.Failed;
        }
    }

    private long GetDatabaseFileBytes() => File.Exists(connectionFactory.DatabasePath)
        ? new FileInfo(connectionFactory.DatabasePath).Length
        : 0;

    private static async Task<MarketHistoryCoverage> GetCoverageAsync(
        SqliteConnection connection,
        MarketHistoryCoverageQuery query,
        CancellationToken cancellationToken)
    {
        var (priceFilter, priceParameters) = CreateFilter(query, "price");
        await using var priceCommand = connection.CreateCommand();
        priceCommand.CommandText = $"""
            SELECT COUNT(*), MIN(price.observed_at_utc), MAX(price.observed_at_utc)
            FROM market_price_observations AS price
            {priceFilter};
            """;
        AddParameters(priceCommand, priceParameters);
        await using var priceReader = await priceCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await priceReader.ReadAsync(cancellationToken).ConfigureAwait(false);

        var (bookFilter, bookParameters) = CreateFilter(query, "snapshot");
        await using var bookCommand = connection.CreateCommand();
        bookCommand.CommandText = $"""
            SELECT COUNT(DISTINCT snapshot.id), COUNT(level.id), MIN(snapshot.observed_at_utc), MAX(snapshot.observed_at_utc)
            FROM market_order_book_snapshots AS snapshot
            LEFT JOIN market_order_book_levels AS level ON level.snapshot_id = snapshot.id
            {bookFilter};
            """;
        AddParameters(bookCommand, bookParameters);
        await using var bookReader = await bookCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await bookReader.ReadAsync(cancellationToken).ConfigureAwait(false);

        return new MarketHistoryCoverage(
            priceReader.GetInt64(0),
            ReadNullableUtc(priceReader, 1, "market_price_observations.observed_at_utc"),
            ReadNullableUtc(priceReader, 2, "market_price_observations.observed_at_utc"),
            bookReader.GetInt64(0),
            bookReader.GetInt64(1),
            ReadNullableUtc(bookReader, 2, "market_order_book_snapshots.observed_at_utc"),
            ReadNullableUtc(bookReader, 3, "market_order_book_snapshots.observed_at_utc"));
    }

    private static (string Clause, IReadOnlyDictionary<string, object> Parameters) CreateFilter(
        MarketHistoryCoverageQuery query,
        string tableAlias)
    {
        var clauses = new List<string>();
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        if (query.ItemId is { } itemId)
        {
            clauses.Add($"{tableAlias}.item_id = $itemId");
            parameters.Add("$itemId", itemId);
        }

        if (query.FromInclusiveUtc is { } from && query.ToInclusiveUtc is { } to)
        {
            clauses.Add($"{tableAlias}.observed_at_utc >= $fromInclusiveUtc");
            clauses.Add($"{tableAlias}.observed_at_utc <= $toInclusiveUtc");
            parameters.Add("$fromInclusiveUtc", SqlitePersistenceValues.ToUtcTimestamp(from, nameof(query.FromInclusiveUtc)));
            parameters.Add("$toInclusiveUtc", SqlitePersistenceValues.ToUtcTimestamp(to, nameof(query.ToInclusiveUtc)));
        }

        return (clauses.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", clauses)}", parameters);
    }

    private static void AddParameters(SqliteCommand command, IReadOnlyDictionary<string, object> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
    }

    private static DateTimeOffset? ReadNullableUtc(SqliteDataReader reader, int ordinal, string columnName) =>
        reader.IsDBNull(ordinal) ? null : SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(ordinal), columnName);
}
