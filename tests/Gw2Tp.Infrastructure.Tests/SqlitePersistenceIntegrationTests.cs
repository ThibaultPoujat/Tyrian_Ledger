using Gw2Tp.Application.Persistence;
using Gw2Tp.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class SqlitePersistenceIntegrationTests
{
    private static readonly DateTimeOffset FirstObservedAtUtc = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondObservedAtUtc = new(2026, 9, 6, 12, 5, 0, TimeSpan.Zero);

    [Fact]
    public async Task Fresh_database_initializes_the_documented_schema_deterministically()
    {
        await using var database = await TestDatabase.CreateAsync(migrate: false);

        Assert.False(File.Exists(database.Path));
        await database.Migrator.MigrateAsync();
        await database.Migrator.MigrateAsync();

        Assert.True(File.Exists(database.Path));
        Assert.Equal([1, 2, 3], await database.GetMigrationVersionsAsync());
        Assert.Equal(
            [
                "account_profiles",
                "completed_tp_transactions",
                "current_order_sync_batches",
                "current_tp_order_observations",
                "current_tp_orders",
                "item_metadata",
                "schema_migrations",
                "user_settings",
            ],
            await database.GetTableNamesAsync());
    }

    [Fact]
    public async Task Version_two_database_upgrades_to_version_three_without_losing_completed_history()
    {
        await using var database = await TestDatabase.CreateAsync(migrate: false);
        await database.Migrator.MigrateToAsync(2);
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", FirstObservedAtUtc);
        var transaction = CompletedTransaction(1001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2);
        await database.PersonalTradingPost.UpsertCompletedTransactionsAsync(account, [transaction], FirstObservedAtUtc);

        await database.Migrator.MigrateAsync();

        Assert.Equal([1, 2, 3], await database.GetMigrationVersionsAsync());
        var stored = Assert.Single(await database.PersonalTradingPost.GetCompletedTransactionsAsync(account));
        Assert.Equal(transaction, stored.Transaction);
        Assert.Contains("last_sync_outcome", await database.GetAccountProfileColumnNamesAsync());
    }

    [Fact]
    public async Task Successful_sync_commits_history_current_orders_metadata_and_status_together()
    {
        await using var database = await TestDatabase.CreateAsync();
        var completed = CompletedTransaction(1001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2);
        var current = CurrentOrder(2001, PersonalTradingPostSide.Sell, itemId: 84, unitPrice: 456, quantity: 3);

        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a",
            [completed],
            new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, [current]),
            [new StoredItemMetadata(42, "First item", FirstObservedAtUtc), new StoredItemMetadata(84, "Second item", FirstObservedAtUtc)],
            FirstObservedAtUtc,
            FirstObservedAtUtc,
            FirstObservedAtUtc));

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([completed], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
        Assert.Equal([current], await database.PersonalTradingPost.GetCurrentOrdersAsync(account));
        Assert.Equal("First item", (await database.ItemMetadata.GetAsync(42))?.Name);
        Assert.Equal((FirstObservedAtUtc, 1L, null as long?, FirstObservedAtUtc, FirstObservedAtUtc), await database.GetAccountSyncStateAsync());
    }

    [Fact]
    public async Task Failed_or_conflicting_sync_preserves_last_known_good_state()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2);
        var originalCurrent = CurrentOrder(2001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a",
            [original],
            new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, [originalCurrent]),
            [new StoredItemMetadata(42, "Original", FirstObservedAtUtc)],
            FirstObservedAtUtc,
            FirstObservedAtUtc,
            FirstObservedAtUtc));

        var conflicting = original with { Quantity = 3 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SynchronizationStore.CommitSuccessfulSyncAsync(
            new PersonalTradingPostSuccessfulSync(
                "opaque-account-a",
                [CompletedTransaction(1002, PersonalTradingPostSide.Sell, itemId: 84, unitPrice: 456, quantity: 1), conflicting],
                new CurrentPersonalTradingPostOrderSnapshot(SecondObservedAtUtc, [CurrentOrder(2002, PersonalTradingPostSide.Sell, 84, 456, 1)]),
                [new StoredItemMetadata(84, "New", SecondObservedAtUtc)],
                SecondObservedAtUtc,
                SecondObservedAtUtc,
                SecondObservedAtUtc)));

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
        Assert.Equal([originalCurrent], await database.PersonalTradingPost.GetCurrentOrdersAsync(account));
        Assert.Null(await database.ItemMetadata.GetAsync(84));

        await database.SynchronizationStore.RecordFailedSyncAsync("opaque-account-a", SecondObservedAtUtc, Gw2Tp.Application.MarketData.Gw2ApiErrorCategory.IncompleteData);
        Assert.Equal((FirstObservedAtUtc, 2L, 10L as long?, FirstObservedAtUtc, FirstObservedAtUtc), await database.GetAccountSyncStateAsync());
    }

    [Fact]
    public async Task Repeated_sync_is_idempotent_and_remote_history_aging_never_deletes_completed_history()
    {
        await using var database = await TestDatabase.CreateAsync();
        var completed = CompletedTransaction(1001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2);
        var current = CurrentOrder(2001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2);
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a",
            [completed],
            new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, [current]),
            [new StoredItemMetadata(42, "Original", FirstObservedAtUtc)],
            FirstObservedAtUtc,
            FirstObservedAtUtc,
            FirstObservedAtUtc));

        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a",
            [],
            new CurrentPersonalTradingPostOrderSnapshot(SecondObservedAtUtc, []),
            [],
            SecondObservedAtUtc,
            null,
            null));

        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        var stored = Assert.Single(await database.PersonalTradingPost.GetCompletedTransactionsAsync(account));
        Assert.Equal(completed, stored.Transaction);
        Assert.Equal(FirstObservedAtUtc, stored.FirstImportedAtUtc);
        Assert.Equal(FirstObservedAtUtc, stored.LastSeenAtUtc);
        Assert.Empty(await database.PersonalTradingPost.GetCurrentOrdersAsync(account));
        Assert.Equal(2, (await database.PersonalTradingPost.GetCurrentOrderObservationsAsync(account)).Count);
    }

    [Fact]
    public async Task Sync_store_isolates_accounts_even_when_external_transaction_ids_match()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-a",
            [CompletedTransaction(1001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2)],
            new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []),
            [new StoredItemMetadata(42, "Shared item", FirstObservedAtUtc)],
            FirstObservedAtUtc,
            FirstObservedAtUtc,
            FirstObservedAtUtc));
        await database.SynchronizationStore.CommitSuccessfulSyncAsync(new PersonalTradingPostSuccessfulSync(
            "opaque-account-b",
            [CompletedTransaction(1001, PersonalTradingPostSide.Sell, itemId: 84, unitPrice: 456, quantity: 1)],
            new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, []),
            [new StoredItemMetadata(84, "Different item", FirstObservedAtUtc)],
            FirstObservedAtUtc,
            FirstObservedAtUtc,
            FirstObservedAtUtc));

        var accountA = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", SecondObservedAtUtc);
        var accountB = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-b", SecondObservedAtUtc);
        Assert.Equal(PersonalTradingPostSide.Buy, Assert.Single(await database.PersonalTradingPost.GetCompletedTransactionsAsync(accountA)).Transaction.Side);
        Assert.Equal(PersonalTradingPostSide.Sell, Assert.Single(await database.PersonalTradingPost.GetCompletedTransactionsAsync(accountB)).Transaction.Side);
    }

    [Fact]
    public async Task Completed_transactions_are_account_scoped_idempotent_and_never_mutated()
    {
        await using var database = await TestDatabase.CreateAsync();
        var accountA = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", FirstObservedAtUtc);
        var accountB = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-b", FirstObservedAtUtc);
        var accountATransaction = CompletedTransaction(1001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2);
        var accountBTransaction = CompletedTransaction(1001, PersonalTradingPostSide.Sell, itemId: 84, unitPrice: 456, quantity: 3);

        await database.PersonalTradingPost.UpsertCompletedTransactionsAsync(accountA, [accountATransaction], FirstObservedAtUtc);
        await database.PersonalTradingPost.UpsertCompletedTransactionsAsync(accountA, [accountATransaction], SecondObservedAtUtc);
        await database.PersonalTradingPost.UpsertCompletedTransactionsAsync(accountB, [accountBTransaction], FirstObservedAtUtc);

        var storedA = Assert.Single(await database.PersonalTradingPost.GetCompletedTransactionsAsync(accountA));
        var storedB = Assert.Single(await database.PersonalTradingPost.GetCompletedTransactionsAsync(accountB));
        Assert.Equal(accountATransaction, storedA.Transaction);
        Assert.Equal(FirstObservedAtUtc, storedA.FirstImportedAtUtc);
        Assert.Equal(SecondObservedAtUtc, storedA.LastSeenAtUtc);
        Assert.Equal(accountBTransaction, storedB.Transaction);

        var conflicting = accountATransaction with { Quantity = 4 };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.PersonalTradingPost.UpsertCompletedTransactionsAsync(accountA, [conflicting], SecondObservedAtUtc));
        Assert.Equal(accountATransaction, Assert.Single(await database.PersonalTradingPost.GetCompletedTransactionsAsync(accountA)).Transaction);
    }

    [Fact]
    public async Task A_failed_completed_transaction_batch_rolls_back_all_new_rows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", FirstObservedAtUtc);
        var original = CompletedTransaction(1001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 123, quantity: 2);
        await database.PersonalTradingPost.UpsertCompletedTransactionsAsync(account, [original], FirstObservedAtUtc);

        var newTransaction = CompletedTransaction(1002, PersonalTradingPostSide.Sell, itemId: 84, unitPrice: 456, quantity: 3);
        var conflict = original with { UnitPriceInCopper = 124 };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.PersonalTradingPost.UpsertCompletedTransactionsAsync(account, [newTransaction, conflict], SecondObservedAtUtc));

        Assert.Equal([original], (await database.PersonalTradingPost.GetCompletedTransactionsAsync(account)).Select(item => item.Transaction));
    }

    [Fact]
    public async Task Current_order_state_replaces_atomically_while_retaining_observations()
    {
        await using var database = await TestDatabase.CreateAsync();
        var account = await database.PersonalTradingPost.GetOrCreateAccountProfileAsync("opaque-account-a", FirstObservedAtUtc);
        var firstOrder = CurrentOrder(2001, PersonalTradingPostSide.Buy, itemId: 42, unitPrice: 100, quantity: 3);
        await database.PersonalTradingPost.ReplaceCurrentOrderSnapshotAsync(
            account,
            new CurrentPersonalTradingPostOrderSnapshot(FirstObservedAtUtc, [firstOrder]));

        await Assert.ThrowsAsync<ArgumentException>(() => database.PersonalTradingPost.ReplaceCurrentOrderSnapshotAsync(
            account,
            new CurrentPersonalTradingPostOrderSnapshot(SecondObservedAtUtc, [firstOrder, firstOrder])));

        Assert.Equal([firstOrder], await database.PersonalTradingPost.GetCurrentOrdersAsync(account));
        Assert.Single(await database.PersonalTradingPost.GetCurrentOrderObservationsAsync(account));

        var secondOrder = CurrentOrder(2002, PersonalTradingPostSide.Sell, itemId: 84, unitPrice: 200, quantity: 1);
        await database.PersonalTradingPost.ReplaceCurrentOrderSnapshotAsync(
            account,
            new CurrentPersonalTradingPostOrderSnapshot(SecondObservedAtUtc, [secondOrder]));

        Assert.Equal([secondOrder], await database.PersonalTradingPost.GetCurrentOrdersAsync(account));
        var observations = await database.PersonalTradingPost.GetCurrentOrderObservationsAsync(account);
        Assert.Equal(2, observations.Count);
        Assert.Equal([firstOrder], observations[0].Orders);
        Assert.Equal([secondOrder], observations[1].Orders);
    }

    [Fact]
    public async Task Item_metadata_and_typed_non_secret_settings_round_trip()
    {
        await using var database = await TestDatabase.CreateAsync();
        var item = new StoredItemMetadata(42, "Test item", FirstObservedAtUtc);
        var settings = new UserSettings(1, 123, 250, 1500, FirstObservedAtUtc);

        await database.ItemMetadata.UpsertAsync([item]);
        await database.UserSettings.SaveAsync(settings);

        Assert.Equal(item, await database.ItemMetadata.GetAsync(42));
        Assert.Equal(settings, await database.UserSettings.GetAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => database.ItemMetadata.UpsertAsync(
            [item with { ObservedAtUtc = item.ObservedAtUtc.ToOffset(TimeSpan.FromHours(1)) }]));
        await Assert.ThrowsAsync<ArgumentException>(() => database.UserSettings.SaveAsync(settings with { CashReserveBasisPoints = 10001 }));
    }

    [Fact]
    public async Task Schema_has_no_credential_storage_path_and_documents_the_migrated_tables()
    {
        await using var database = await TestDatabase.CreateAsync();
        var columns = await database.GetAllColumnNamesAsync();
        var prohibitedColumnFragments = new[] { "api_key", "credential", "secret", "token", "authorization" };
        Assert.DoesNotContain(columns, column => prohibitedColumnFragments.Any(fragment =>
            column.Contains(fragment, StringComparison.OrdinalIgnoreCase)));

        var repositoryRoot = FindRepositoryRoot();
        var persistenceSource = Directory.EnumerateFiles(
                Path.Combine(repositoryRoot, "src", "Gw2Tp.Infrastructure", "Persistence"),
                "*.cs",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();
        Assert.DoesNotContain(persistenceSource, source => source.Contains("IGw2ApiKeySource", StringComparison.Ordinal));

        var documentation = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "architecture", "data-model.md"));
        foreach (var tableName in await database.GetTableNamesAsync())
        {
            Assert.Contains($"`{tableName}`", documentation, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Unknown_future_migration_is_rejected_before_repository_writes()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var connection = await database.Factory.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO schema_migrations (version, name, applied_at_utc) VALUES (99, 'future', '2026-09-06T12:00:00.0000000+00:00');";
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Migrator.MigrateAsync());
    }

    private static CompletedPersonalTradingPostTransaction CompletedTransaction(
        long id,
        PersonalTradingPostSide side,
        int itemId,
        int unitPrice,
        int quantity) => new(
        id,
        side,
        itemId,
        unitPrice,
        quantity,
        FirstObservedAtUtc,
        FirstObservedAtUtc);

    private static CurrentPersonalTradingPostOrder CurrentOrder(
        long id,
        PersonalTradingPostSide side,
        int itemId,
        int unitPrice,
        int quantity) => new(id, side, itemId, unitPrice, quantity, FirstObservedAtUtc);

    private static string FindRepositoryRoot()
    {
        foreach (var candidate in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var directory = new DirectoryInfo(candidate); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "TyrianLedger.slnx")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Tyrian Ledger repository root.");
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string directory;

        private TestDatabase(string directory, SqliteConnectionFactory factory)
        {
            this.directory = directory;
            Factory = factory;
            Migrator = new SqliteSchemaMigrator(factory);
            PersonalTradingPost = new SqlitePersonalTradingPostRepository(factory);
            SynchronizationStore = new SqlitePersonalTradingPostSynchronizationStore(factory);
            ItemMetadata = new SqliteItemMetadataRepository(factory);
            UserSettings = new SqliteUserSettingsRepository(factory);
        }

        public SqliteConnectionFactory Factory { get; }

        public SqliteSchemaMigrator Migrator { get; }

        public SqlitePersonalTradingPostRepository PersonalTradingPost { get; }

        public SqlitePersonalTradingPostSynchronizationStore SynchronizationStore { get; }

        public SqliteItemMetadataRepository ItemMetadata { get; }

        public SqliteUserSettingsRepository UserSettings { get; }

        public string Path => Factory.DatabasePath;

        public static async Task<TestDatabase> CreateAsync(bool migrate = true)
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TyrianLedger.Persistence.Tests", Guid.NewGuid().ToString("N"));
            var database = new TestDatabase(directory, new SqliteConnectionFactory(System.IO.Path.Combine(directory, "tyrian-ledger.db")));
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
            while (await reader.ReadAsync())
            {
                versions.Add(reader.GetInt32(0));
            }

            return versions;
        }

        public async Task<IReadOnlyList<string>> GetTableNamesAsync()
        {
            await using var connection = await Factory.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync();
            var tableNames = new List<string>();
            while (await reader.ReadAsync())
            {
                tableNames.Add(reader.GetString(0));
            }

            return tableNames;
        }

        public async Task<IReadOnlyList<string>> GetAllColumnNamesAsync()
        {
            var columnNames = new List<string>();
            await using var connection = await Factory.OpenConnectionAsync();
            foreach (var tableName in await GetTableNamesAsync())
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"PRAGMA table_info({tableName});";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    columnNames.Add(reader.GetString(1));
                }
            }

            return columnNames;
        }

        public async Task<IReadOnlyList<string>> GetAccountProfileColumnNamesAsync()
        {
            await using var connection = await Factory.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(account_profiles);";
            await using var reader = await command.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1));
            }

            return columns;
        }

        public async Task<(DateTimeOffset? LastSuccessfulSyncAtUtc, long? Outcome, long? ErrorCategory, DateTimeOffset? HistoryStartUtc, DateTimeOffset? HistoryEndUtc)> GetAccountSyncStateAsync()
        {
            await using var connection = await Factory.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT last_successful_sync_at_utc, last_sync_outcome, last_sync_error_category,
                       history_coverage_start_utc, history_coverage_end_utc
                FROM account_profiles WHERE account_scope_id = 'opaque-account-a';
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (
                reader.IsDBNull(0) ? null : DateTimeOffset.Parse(reader.GetString(0), System.Globalization.CultureInfo.InvariantCulture),
                reader.IsDBNull(1) ? null : reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2),
                reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3), System.Globalization.CultureInfo.InvariantCulture),
                reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture));
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
