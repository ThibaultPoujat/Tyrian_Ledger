using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class MarketHistoryPersistenceTests
{
    private static readonly DateTimeOffset FirstObservedAtUtc = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondObservedAtUtc = new(2026, 9, 8, 12, 15, 0, TimeSpan.Zero);

    [Fact]
    public async Task Fresh_and_upgrade_migrations_create_market_history_schema_without_losing_prior_data()
    {
        await using var database = await TestDatabase.CreateAsync(migrate: false);
        await database.Migrator.MigrateToAsync(4);
        await database.Migrator.MigrateToAsync(5);
        await database.History.AppendOrderBookSnapshotAsync(new MarketOrderBookSnapshot(
            FirstObservedAtUtc, 42, MarketObservationSourceStatus.Complete, MarketSamplingTier.Watchlist, 1,
            [new MarketOrderBookLevel(MarketOrderBookSide.Buy, 0, 100, 5, 2)]));
        await database.Migrator.MigrateAsync();
        await database.Migrator.MigrateAsync();

        Assert.Equal([1, 2, 3, 4, 5, 6], await database.GetMigrationVersionsAsync());
        Assert.Equal(
        [
            "market_order_book_levels",
            "market_order_book_snapshots",
            "market_price_observations",
        ],
        (await database.GetTableNamesAsync()).Where(name => name.StartsWith("market_", StringComparison.Ordinal)).ToArray());
        Assert.Equal(
            [new MarketOrderBookLevel(MarketOrderBookSide.Buy, 0, 100, 5, 2)],
            await database.GetOrderBookLevelsAsync());
    }

    [Fact]
    public async Task Price_observations_are_immutable_unique_and_queryable_by_item_and_time_window()
    {
        await using var database = await TestDatabase.CreateAsync();
        var first = Price(42, FirstObservedAtUtc);
        var second = Price(42, SecondObservedAtUtc) with { HighestBuyPriceInCopper = 105 };
        await database.History.AppendPriceObservationAsync(first);
        await database.History.AppendPriceObservationAsync(second);

        Assert.Equal([first, second], await database.History.GetPriceObservationsAsync(42, FirstObservedAtUtc, SecondObservedAtUtc));
        Assert.Empty(await database.History.GetPriceObservationsAsync(84, FirstObservedAtUtc, SecondObservedAtUtc));
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.History.AppendPriceObservationAsync(first));
    }

    [Fact]
    public async Task History_repository_rejects_non_utc_invalid_values_and_only_persists_explicit_complete_books()
    {
        await using var database = await TestDatabase.CreateAsync();
        var localTimestamp = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.FromHours(2));
        await Assert.ThrowsAsync<ArgumentException>(() => database.History.AppendPriceObservationAsync(Price(42, localTimestamp)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => database.History.AppendPriceObservationAsync(Price(42, FirstObservedAtUtc) with { AggregateBuyQuantity = -1 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => database.History.AppendOrderBookSnapshotAsync(new MarketOrderBookSnapshot(
            FirstObservedAtUtc, 42, MarketObservationSourceStatus.Complete, MarketSamplingTier.Watchlist, 1,
            [new MarketOrderBookLevel(MarketOrderBookSide.Buy, 0, 100, 0, 1)])));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => database.History.AppendOrderBookSnapshotAsync(new MarketOrderBookSnapshot(
            FirstObservedAtUtc, 42, MarketObservationSourceStatus.Complete, MarketSamplingTier.Watchlist, 1,
            [new MarketOrderBookLevel(MarketOrderBookSide.Buy, 0, 0, 1, 1)])));

        await database.History.AppendOrderBookSnapshotAsync(new MarketOrderBookSnapshot(
            FirstObservedAtUtc, 42, MarketObservationSourceStatus.Complete, MarketSamplingTier.Watchlist, 1,
            [
                new MarketOrderBookLevel(MarketOrderBookSide.Buy, 0, 100, 5, 2),
                new MarketOrderBookLevel(MarketOrderBookSide.Sell, 0, 120, 7, 3),
            ]));

        Assert.Equal(
        [
            new MarketOrderBookLevel(MarketOrderBookSide.Buy, 0, 100, 5, 2),
            new MarketOrderBookLevel(MarketOrderBookSide.Sell, 0, 120, 7, 3),
        ],
        await database.GetOrderBookLevelsAsync());
        Assert.Equal(0, await database.GetTableCountAsync("market_price_observations"));
    }

    [Fact]
    public async Task Backup_validation_rejects_malformed_market_history_without_rewriting_existing_raw_rows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var observation = Price(42, FirstObservedAtUtc);
        await database.History.AppendPriceObservationAsync(observation);
        await database.History.AppendOrderBookSnapshotAsync(new MarketOrderBookSnapshot(
            FirstObservedAtUtc, 42, MarketObservationSourceStatus.Complete, MarketSamplingTier.Watchlist, 1,
            [new MarketOrderBookLevel(MarketOrderBookSide.Buy, 0, 100, 5, 2)]));
        var malformedPath = Path.Combine(database.DirectoryPath, "malformed-history.db");
        File.Copy(database.Path, malformedPath);

        await using (var connection = new SqliteConnection($"Data Source={malformedPath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA ignore_check_constraints = ON; UPDATE market_order_book_levels SET unit_price_in_copper = 0;";
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => SqliteSchemaMigrator.ValidateBackupCandidateAsync(malformedPath));
        Assert.Equal([observation], await database.History.GetPriceObservationsAsync(42, FirstObservedAtUtc, FirstObservedAtUtc));
    }

    private static MarketPriceObservation Price(int itemId, DateTimeOffset observedAtUtc) => new(
        observedAtUtc, itemId, 100, 120, 10, 20,
        MarketObservationSourceStatus.Complete, MarketSamplingTier.Watchlist, 1);

    private sealed class TestDatabase : IAsyncDisposable
    {
        private TestDatabase(string directoryPath, SqliteConnectionFactory factory, ISqliteDatabaseGate gate)
        {
            DirectoryPath = directoryPath;
            Factory = factory;
            Migrator = new SqliteSchemaMigrator(factory);
            History = new SqliteMarketHistoryRepository(factory, gate);
        }

        public string DirectoryPath { get; }
        public SqliteConnectionFactory Factory { get; }
        public SqliteSchemaMigrator Migrator { get; }
        public SqliteMarketHistoryRepository History { get; }
        public string Path => Factory.DatabasePath;

        public static async Task<TestDatabase> CreateAsync(bool migrate = true)
        {
            var directoryPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TyrianLedger.MarketHistory.Tests", Guid.NewGuid().ToString("N"));
            var database = new TestDatabase(directoryPath, new SqliteConnectionFactory(System.IO.Path.Combine(directoryPath, "tyrian-ledger.db")), new SqliteDatabaseGate());
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
            while (await reader.ReadAsync()) versions.Add(reader.GetInt32(0));
            return versions;
        }

        public async Task<IReadOnlyList<string>> GetTableNamesAsync()
        {
            await using var connection = await Factory.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync();
            var names = new List<string>();
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));
            return names;
        }

        public async Task<int> GetTableCountAsync(string tableName)
        {
            await using var connection = await Factory.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
            return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }

        public async Task<IReadOnlyList<MarketOrderBookLevel>> GetOrderBookLevelsAsync()
        {
            await using var connection = await Factory.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT side, level_ordinal, unit_price_in_copper, quantity, listings FROM market_order_book_levels ORDER BY side, level_ordinal;";
            await using var reader = await command.ExecuteReaderAsync();
            var levels = new List<MarketOrderBookLevel>();
            while (await reader.ReadAsync())
            {
                levels.Add(new MarketOrderBookLevel(
                    (MarketOrderBookSide)reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)));
            }

            return levels;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
