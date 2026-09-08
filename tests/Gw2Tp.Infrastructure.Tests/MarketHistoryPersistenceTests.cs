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
        await using (var connection = await database.Factory.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE market_order_book_levels SET unit_price_in_copper = 0;";
            await command.ExecuteNonQueryAsync();
        }

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
            [new MarketOrderBookLevel(MarketOrderBookSide.Buy, 0, 0, 5, 2)],
            await database.GetOrderBookLevelsAsync());
        await SqliteSchemaMigrator.ValidateBackupCandidateAsync(database.Path);
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
        var latest = await database.History.GetLatestPriceObservationsAsync([42, 84]);
        Assert.Equal(second, latest[42]);
        Assert.False(latest.ContainsKey(84));
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.History.AppendPriceObservationAsync(first));
    }

    [Fact]
    public async Task Latest_observation_lookup_batches_a_broad_item_set_on_one_connection()
    {
        await using var database = await TestDatabase.CreateAsync();
        var itemIds = Enumerable.Range(1, 201).ToArray();
        foreach (var itemId in itemIds)
        {
            await database.History.AppendPriceObservationAsync(Price(itemId, FirstObservedAtUtc));
        }

        var latest = await database.History.GetLatestPriceObservationsAsync(itemIds);

        Assert.Equal(201, latest.Count);
        Assert.Equal(Price(201, FirstObservedAtUtc), latest[201]);
    }

    [Fact]
    public async Task Latest_eligible_observation_lookup_uses_bounded_structural_filters()
    {
        await using var database = await TestDatabase.CreateAsync();
        var eligible = Price(42, FirstObservedAtUtc);
        var newerZeroDepth = Price(42, SecondObservedAtUtc) with { AggregateBuyQuantity = 0 };
        await database.History.AppendPriceObservationsAsync([eligible, newerZeroDepth]);

        var latest = await database.History.GetLatestPriceObservationAsync(
            42,
            new LatestMarketPriceObservationQuery(int.MaxValue - 1, 2, 1, 1));

        Assert.Equal(eligible, latest);
    }

    [Fact]
    public async Task Price_observation_batch_is_atomic_and_never_partially_commits_a_duplicate()
    {
        await using var database = await TestDatabase.CreateAsync();
        var existing = Price(42, FirstObservedAtUtc);
        await database.History.AppendPriceObservationAsync(existing);

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.History.AppendPriceObservationsAsync(
        [
            Price(84, FirstObservedAtUtc),
            existing,
        ]));

        Assert.Equal([existing], await database.History.GetPriceObservationsAsync(42, FirstObservedAtUtc, FirstObservedAtUtc));
        Assert.Empty(await database.History.GetPriceObservationsAsync(84, FirstObservedAtUtc, FirstObservedAtUtc));
    }

    [Fact]
    public async Task Schema_validation_accepts_the_briefly_published_strict_version_six_order_book_shape()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var connection = await database.Factory.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                ALTER TABLE market_order_book_levels RENAME TO market_order_book_levels_current;
                CREATE TABLE market_order_book_levels (
                    id INTEGER PRIMARY KEY,
                    snapshot_id INTEGER NOT NULL CHECK (snapshot_id > 0),
                    side INTEGER NOT NULL CHECK (side IN (1, 2)),
                    level_ordinal INTEGER NOT NULL CHECK (level_ordinal >= 0),
                    unit_price_in_copper INTEGER NOT NULL CHECK (unit_price_in_copper > 0),
                    quantity INTEGER NOT NULL CHECK (quantity > 0),
                    listings INTEGER NOT NULL CHECK (listings > 0),
                    CONSTRAINT fk_market_order_book_levels_snapshot FOREIGN KEY (snapshot_id)
                        REFERENCES market_order_book_snapshots(id) ON DELETE RESTRICT,
                    CONSTRAINT uq_market_order_book_levels_snapshot_side_ordinal
                        UNIQUE (snapshot_id, side, level_ordinal)
                );
                DROP TABLE market_order_book_levels_current;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await database.Migrator.MigrateAsync();
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
            command.CommandText = "PRAGMA ignore_check_constraints = ON; UPDATE market_order_book_snapshots SET sampling_tier = 9;";
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => SqliteSchemaMigrator.ValidateBackupCandidateAsync(malformedPath));
        Assert.Equal([observation], await database.History.GetPriceObservationsAsync(42, FirstObservedAtUtc, FirstObservedAtUtc));
    }

    [Fact]
    public async Task Startup_schema_validation_does_not_scan_raw_history_but_backup_validation_does()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.History.AppendPriceObservationAsync(Price(42, FirstObservedAtUtc));
        await using (var connection = await database.Factory.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE market_price_observations SET observed_at_utc = 'not-a-timestamp';";
            await command.ExecuteNonQueryAsync();
        }

        await database.Migrator.MigrateAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => SqliteSchemaMigrator.ValidateBackupCandidateAsync(database.Path));
    }

    [Fact]
    public async Task Status_reports_versioned_non_destructive_policy_storage_coverage_and_integrity()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.History.AppendPriceObservationAsync(Price(42, FirstObservedAtUtc));
        await database.History.AppendPriceObservationAsync(Price(42, SecondObservedAtUtc));
        await database.History.AppendOrderBookSnapshotAsync(new MarketOrderBookSnapshot(
            FirstObservedAtUtc, 42, MarketObservationSourceStatus.Complete, MarketSamplingTier.Watchlist, 1,
            [
                new MarketOrderBookLevel(MarketOrderBookSide.Buy, 0, 100, 5, 2),
                new MarketOrderBookLevel(MarketOrderBookSide.Sell, 0, 120, 7, 3),
            ]));

        var status = await database.Status.GetStatusAsync(new MarketHistoryCoverageQuery(42, FirstObservedAtUtc, FirstObservedAtUtc));

        Assert.Equal(MarketHistoryIntegrityState.Passed, status.IntegrityState);
        Assert.True(status.DatabaseFileBytes > 0);
        Assert.Equal(
        [
            new MarketHistoryRetentionPolicy(1, MarketHistoryEvidenceKind.AggregatePrices, MarketHistoryRetentionMode.PreserveAllRawEvidence),
            new MarketHistoryRetentionPolicy(1, MarketHistoryEvidenceKind.DetailedOrderBooks, MarketHistoryRetentionMode.PreserveAllRawEvidence),
        ], status.RetentionPolicies);
        Assert.Equal(1, status.Coverage.AggregateObservationCount);
        Assert.Equal(FirstObservedAtUtc, status.Coverage.AggregateFirstObservedAtUtc);
        Assert.Equal(FirstObservedAtUtc, status.Coverage.AggregateLastObservedAtUtc);
        Assert.Equal(1, status.Coverage.DetailedBookSnapshotCount);
        Assert.Equal(2, status.Coverage.DetailedBookLevelCount);
        Assert.Equal(FirstObservedAtUtc, status.Coverage.DetailedBookFirstObservedAtUtc);
        Assert.Equal(FirstObservedAtUtc, status.Coverage.DetailedBookLastObservedAtUtc);
    }

    [Fact]
    public async Task Status_marks_malformed_retained_history_as_failed_without_rewriting_evidence()
    {
        await using var database = await TestDatabase.CreateAsync();
        var observation = Price(42, FirstObservedAtUtc);
        await database.History.AppendPriceObservationAsync(observation);
        await using (var connection = await database.Factory.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE market_price_observations SET observed_at_utc = 'not-a-timestamp';";
            await command.ExecuteNonQueryAsync();
        }

        var status = await database.Status.GetStatusAsync(new MarketHistoryCoverageQuery(null, null, null));

        Assert.Equal(MarketHistoryIntegrityState.Failed, status.IntegrityState);
        Assert.Equal(0, status.Coverage.AggregateObservationCount);
    }

    [Fact]
    public async Task Status_marks_inconsistent_current_migration_history_as_failed()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var connection = await database.Factory.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM schema_migrations WHERE version = 6;";
            await command.ExecuteNonQueryAsync();
        }

        var status = await database.Status.GetStatusAsync(new MarketHistoryCoverageQuery(null, null, null));

        Assert.Equal(MarketHistoryIntegrityState.Failed, status.IntegrityState);
    }

    [Fact]
    public async Task Status_marks_a_missing_migration_history_table_as_failed()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var connection = await database.Factory.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "DROP TABLE schema_migrations;";
            await command.ExecuteNonQueryAsync();
        }

        var status = await database.Status.GetStatusAsync(new MarketHistoryCoverageQuery(null, null, null));

        Assert.Equal(MarketHistoryIntegrityState.Failed, status.IntegrityState);
    }

    [Fact]
    public async Task Status_maps_an_unreadable_sqlite_database_to_failed_integrity()
    {
        await using var database = await TestDatabase.CreateAsync();
        SqliteConnection.ClearAllPools();
        await File.WriteAllTextAsync(database.Path, "not a SQLite database");

        var status = await database.Status.GetStatusAsync(new MarketHistoryCoverageQuery(null, null, null));

        Assert.Equal(MarketHistoryIntegrityState.Failed, status.IntegrityState);
    }

    [Fact]
    public async Task Status_maps_a_64_bit_out_of_range_migration_version_to_failed_integrity()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var connection = await database.Factory.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO schema_migrations (version, name, applied_at_utc) VALUES (2147483648, 'out_of_range', '2026-09-08T12:00:00.0000000+00:00');";
            await command.ExecuteNonQueryAsync();
        }

        var status = await database.Status.GetStatusAsync(new MarketHistoryCoverageQuery(null, null, null));

        Assert.Equal(MarketHistoryIntegrityState.Failed, status.IntegrityState);
        await Assert.ThrowsAsync<InvalidDataException>(() => SqliteSchemaMigrator.ValidateBackupCandidateAsync(database.Path));
    }

    [Fact]
    public async Task Backup_and_restore_round_trip_preserves_market_history_without_personal_data_clear_behavior()
    {
        await using var database = await TestDatabase.CreateAsync();
        var first = Price(42, FirstObservedAtUtc);
        await database.History.AppendPriceObservationAsync(first);
        await database.History.AppendOrderBookSnapshotAsync(new MarketOrderBookSnapshot(
            FirstObservedAtUtc, 42, MarketObservationSourceStatus.Complete, MarketSamplingTier.Watchlist, 1,
            [new MarketOrderBookLevel(MarketOrderBookSide.Buy, 0, 100, 5, 2)]));
        var backup = await database.Recovery.CreateBackupAsync();
        await database.History.AppendPriceObservationAsync(Price(42, SecondObservedAtUtc));

        var restored = await database.Recovery.RestoreManagedBackupAsync(backup.FileName);

        Assert.Equal(Gw2Tp.Application.LocalData.LocalDataRestoreOutcome.Restored, restored.Outcome);
        Assert.Equal([first], await database.History.GetPriceObservationsAsync(42, FirstObservedAtUtc, SecondObservedAtUtc));
        Assert.Equal([new MarketOrderBookLevel(MarketOrderBookSide.Buy, 0, 100, 5, 2)], await database.GetOrderBookLevelsAsync());
        Assert.Equal(MarketHistoryIntegrityState.Passed, (await database.Status.GetStatusAsync(new MarketHistoryCoverageQuery(null, null, null))).IntegrityState);
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
            Status = new SqliteMarketHistoryStatusService(factory, Migrator, gate);
            Recovery = new SqliteLocalDataRecoveryService(factory, gate);
        }

        public string DirectoryPath { get; }
        public SqliteConnectionFactory Factory { get; }
        public SqliteSchemaMigrator Migrator { get; }
        public SqliteMarketHistoryRepository History { get; }
        public SqliteMarketHistoryStatusService Status { get; }
        public SqliteLocalDataRecoveryService Recovery { get; }
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
