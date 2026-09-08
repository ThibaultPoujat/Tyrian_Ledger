using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Gw2Tp.Infrastructure.Persistence;

internal sealed class SqliteSchemaMigrator(ISqliteConnectionFactory connectionFactory)
{
    private const int LatestVersion = 6;

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> LatestSchemaColumns =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["account_profiles"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "id", "account_scope_id", "created_at_utc", "last_successful_sync_at_utc",
                "last_sync_attempted_at_utc", "last_sync_outcome", "last_sync_error_category",
                "history_coverage_start_utc", "history_coverage_end_utc",
            },
            ["completed_tp_transactions"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "id", "account_profile_id", "external_transaction_id", "side", "item_id",
                "unit_price_in_copper", "quantity", "created_at_utc", "completed_at_utc",
                "first_imported_at_utc", "last_seen_at_utc",
            },
            ["current_order_sync_batches"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "id", "account_profile_id", "observed_at_utc",
            },
            ["current_tp_orders"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "id", "account_profile_id", "sync_batch_id", "external_order_id", "side",
                "item_id", "unit_price_in_copper", "quantity", "created_at_utc", "observed_at_utc",
            },
            ["current_tp_order_observations"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "id", "sync_batch_id", "account_profile_id", "external_order_id", "side",
                "item_id", "unit_price_in_copper", "quantity", "created_at_utc", "observed_at_utc",
            },
            ["item_metadata"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "item_id", "name", "observed_at_utc",
            },
            ["schema_migrations"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "version", "name", "applied_at_utc",
            },
            ["user_settings"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "singleton_id", "settings_version", "minimum_profit_in_copper",
                "minimum_roi_basis_points", "cash_reserve_basis_points", "updated_at_utc",
            },
            ["watchlist_entries"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "item_id", "added_at_utc",
            },
            ["market_price_observations"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "id", "observed_at_utc", "item_id", "highest_buy_price_in_copper", "lowest_sell_price_in_copper",
                "aggregate_buy_quantity", "aggregate_sell_quantity", "source_status", "sampling_tier", "sampling_policy_version",
            },
            ["market_order_book_snapshots"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "id", "observed_at_utc", "item_id", "source_status", "sampling_tier", "sampling_policy_version",
            },
            ["market_order_book_levels"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "id", "snapshot_id", "side", "level_ordinal", "unit_price_in_copper", "quantity", "listings",
            },
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, SqliteColumnDefinition>> LatestColumnDefinitions =
        new Dictionary<string, IReadOnlyDictionary<string, SqliteColumnDefinition>>(StringComparer.Ordinal)
        {
            ["account_profiles"] = Columns(("id", "INTEGER", false, 1), ("account_scope_id", "TEXT", true, 0), ("created_at_utc", "TEXT", true, 0), ("last_successful_sync_at_utc", "TEXT", false, 0), ("last_sync_attempted_at_utc", "TEXT", false, 0), ("last_sync_outcome", "INTEGER", false, 0), ("last_sync_error_category", "INTEGER", false, 0), ("history_coverage_start_utc", "TEXT", false, 0), ("history_coverage_end_utc", "TEXT", false, 0)),
            ["completed_tp_transactions"] = Columns(("id", "INTEGER", false, 1), ("account_profile_id", "INTEGER", true, 0), ("external_transaction_id", "INTEGER", true, 0), ("side", "INTEGER", true, 0), ("item_id", "INTEGER", true, 0), ("unit_price_in_copper", "INTEGER", true, 0), ("quantity", "INTEGER", true, 0), ("created_at_utc", "TEXT", true, 0), ("completed_at_utc", "TEXT", true, 0), ("first_imported_at_utc", "TEXT", true, 0), ("last_seen_at_utc", "TEXT", true, 0)),
            ["current_order_sync_batches"] = Columns(("id", "INTEGER", false, 1), ("account_profile_id", "INTEGER", true, 0), ("observed_at_utc", "TEXT", true, 0)),
            ["current_tp_orders"] = Columns(("id", "INTEGER", false, 1), ("account_profile_id", "INTEGER", true, 0), ("sync_batch_id", "INTEGER", true, 0), ("external_order_id", "INTEGER", true, 0), ("side", "INTEGER", true, 0), ("item_id", "INTEGER", true, 0), ("unit_price_in_copper", "INTEGER", true, 0), ("quantity", "INTEGER", true, 0), ("created_at_utc", "TEXT", true, 0), ("observed_at_utc", "TEXT", true, 0)),
            ["current_tp_order_observations"] = Columns(("id", "INTEGER", false, 1), ("sync_batch_id", "INTEGER", true, 0), ("account_profile_id", "INTEGER", true, 0), ("external_order_id", "INTEGER", true, 0), ("side", "INTEGER", true, 0), ("item_id", "INTEGER", true, 0), ("unit_price_in_copper", "INTEGER", true, 0), ("quantity", "INTEGER", true, 0), ("created_at_utc", "TEXT", true, 0), ("observed_at_utc", "TEXT", true, 0)),
            ["item_metadata"] = Columns(("item_id", "INTEGER", false, 1), ("name", "TEXT", true, 0), ("observed_at_utc", "TEXT", true, 0)),
            ["schema_migrations"] = Columns(("version", "INTEGER", false, 1), ("name", "TEXT", true, 0), ("applied_at_utc", "TEXT", true, 0)),
            ["user_settings"] = Columns(("singleton_id", "INTEGER", false, 1), ("settings_version", "INTEGER", true, 0), ("minimum_profit_in_copper", "INTEGER", false, 0), ("minimum_roi_basis_points", "INTEGER", false, 0), ("cash_reserve_basis_points", "INTEGER", false, 0), ("updated_at_utc", "TEXT", true, 0)),
            ["watchlist_entries"] = Columns(("item_id", "INTEGER", false, 1), ("added_at_utc", "TEXT", true, 0)),
            ["market_price_observations"] = Columns(("id", "INTEGER", false, 1), ("observed_at_utc", "TEXT", true, 0), ("item_id", "INTEGER", true, 0), ("highest_buy_price_in_copper", "INTEGER", true, 0), ("lowest_sell_price_in_copper", "INTEGER", true, 0), ("aggregate_buy_quantity", "INTEGER", true, 0), ("aggregate_sell_quantity", "INTEGER", true, 0), ("source_status", "INTEGER", true, 0), ("sampling_tier", "INTEGER", true, 0), ("sampling_policy_version", "INTEGER", true, 0)),
            ["market_order_book_snapshots"] = Columns(("id", "INTEGER", false, 1), ("observed_at_utc", "TEXT", true, 0), ("item_id", "INTEGER", true, 0), ("source_status", "INTEGER", true, 0), ("sampling_tier", "INTEGER", true, 0), ("sampling_policy_version", "INTEGER", true, 0)),
            ["market_order_book_levels"] = Columns(("id", "INTEGER", false, 1), ("snapshot_id", "INTEGER", true, 0), ("side", "INTEGER", true, 0), ("level_ordinal", "INTEGER", true, 0), ("unit_price_in_copper", "INTEGER", true, 0), ("quantity", "INTEGER", true, 0), ("listings", "INTEGER", true, 0)),
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<SqliteIndexDefinition>> RequiredIndexes =
        new Dictionary<string, IReadOnlyList<SqliteIndexDefinition>>(StringComparer.Ordinal)
        {
            ["account_profiles"] = [new(null, true, ["account_scope_id"], "BINARY")],
            ["completed_tp_transactions"] = [new(null, true, ["account_profile_id", "external_transaction_id"]), new("ix_completed_transactions_account_completed_at", false, ["account_profile_id", "completed_at_utc"])],
            ["current_tp_orders"] = [new(null, true, ["account_profile_id", "external_order_id"])],
            ["current_tp_order_observations"] = [new(null, true, ["sync_batch_id", "external_order_id"]), new("ix_current_order_observations_account_observed_at", false, ["account_profile_id", "observed_at_utc"])],
            ["market_price_observations"] = [new(null, true, ["item_id", "observed_at_utc"])],
            ["market_order_book_snapshots"] = [new(null, true, ["item_id", "observed_at_utc"])],
            ["market_order_book_levels"] = [new(null, true, ["snapshot_id", "side", "level_ordinal"])],
        };

    private static readonly IReadOnlyList<SqliteForeignKeyDefinition> RequiredForeignKeys =
    [
        new("completed_tp_transactions", "account_profile_id", "account_profiles", "id"),
        new("current_order_sync_batches", "account_profile_id", "account_profiles", "id"),
        new("current_tp_orders", "account_profile_id", "account_profiles", "id"),
        new("current_tp_orders", "sync_batch_id", "current_order_sync_batches", "id"),
        new("current_tp_order_observations", "account_profile_id", "account_profiles", "id"),
        new("current_tp_order_observations", "sync_batch_id", "current_order_sync_batches", "id"),
        new("market_order_book_levels", "snapshot_id", "market_order_book_snapshots", "id"),
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> RequiredCheckConstraints =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["account_profiles"] = Checks("last_sync_outcomein(1,2)", "last_sync_error_categorybetween0and11"),
            ["completed_tp_transactions"] = Checks("sidein(1,2)", "item_id>0", "unit_price_in_copper>=0", "quantity>0"),
            ["current_tp_orders"] = Checks("sidein(1,2)", "item_id>0", "unit_price_in_copper>=0", "quantity>0"),
            ["current_tp_order_observations"] = Checks("sidein(1,2)", "item_id>0", "unit_price_in_copper>=0", "quantity>0"),
            ["item_metadata"] = Checks("item_id>0", "length(name)>0"),
            ["user_settings"] = Checks("singleton_id=1", "settings_version>0", "minimum_profit_in_copper>=0", "minimum_roi_basis_pointsbetween0and10000", "cash_reserve_basis_pointsbetween0and10000"),
            ["watchlist_entries"] = Checks("item_id>0"),
            ["market_price_observations"] = Checks("item_id>0", "highest_buy_price_in_copper>=0", "lowest_sell_price_in_copper>=0", "aggregate_buy_quantity>=0", "aggregate_sell_quantity>=0", "source_status=1", "sampling_tierbetween1and3", "sampling_policy_version>0"),
            ["market_order_book_snapshots"] = Checks("item_id>0", "source_status=1", "sampling_tierbetween1and3", "sampling_policy_version>0"),
            ["market_order_book_levels"] = Checks("snapshot_id>0", "sidein(1,2)", "level_ordinal>=0", "unit_price_in_copper>=0", "quantity>0", "listings>0"),
        };

    // Version 6 was briefly published with this stricter equivalent constraint.
    // Accepting it keeps those local databases usable while current migration 6
    // preserves schema-valid version-5 zero-price evidence without rewriting it.
    private static readonly IReadOnlySet<string> StrictOrderBookLevelPriceChecks =
        Checks("snapshot_id>0", "sidein(1,2)", "level_ordinal>=0", "unit_price_in_copper>0", "quantity>0", "listings>0");

    private static readonly IReadOnlyList<SqliteSchemaMigration> Migrations =
    [
        new(
            1,
            "initial_personal_transaction_schema",
            """
            CREATE TABLE account_profiles (
                id INTEGER PRIMARY KEY,
                account_scope_id TEXT NOT NULL COLLATE BINARY,
                created_at_utc TEXT NOT NULL,
                last_successful_sync_at_utc TEXT NULL,
                CONSTRAINT uq_account_profiles_scope UNIQUE (account_scope_id)
            );

            CREATE TABLE completed_tp_transactions (
                id INTEGER PRIMARY KEY,
                account_profile_id INTEGER NOT NULL,
                external_transaction_id INTEGER NOT NULL,
                side INTEGER NOT NULL CHECK (side IN (1, 2)),
                item_id INTEGER NOT NULL CHECK (item_id > 0),
                unit_price_in_copper INTEGER NOT NULL CHECK (unit_price_in_copper >= 0),
                quantity INTEGER NOT NULL CHECK (quantity > 0),
                created_at_utc TEXT NOT NULL,
                completed_at_utc TEXT NOT NULL,
                first_imported_at_utc TEXT NOT NULL,
                last_seen_at_utc TEXT NOT NULL,
                CONSTRAINT fk_completed_transactions_account FOREIGN KEY (account_profile_id)
                    REFERENCES account_profiles(id) ON DELETE RESTRICT,
                CONSTRAINT uq_completed_transactions_account_external_id
                    UNIQUE (account_profile_id, external_transaction_id)
            );

            CREATE INDEX ix_completed_transactions_account_completed_at
                ON completed_tp_transactions (account_profile_id, completed_at_utc);
            """),
        new(
            2,
            "current_order_metadata_and_settings_schema",
            """
            CREATE TABLE current_order_sync_batches (
                id INTEGER PRIMARY KEY,
                account_profile_id INTEGER NOT NULL,
                observed_at_utc TEXT NOT NULL,
                CONSTRAINT fk_current_order_sync_batches_account FOREIGN KEY (account_profile_id)
                    REFERENCES account_profiles(id) ON DELETE RESTRICT
            );

            CREATE TABLE current_tp_orders (
                id INTEGER PRIMARY KEY,
                account_profile_id INTEGER NOT NULL,
                sync_batch_id INTEGER NOT NULL,
                external_order_id INTEGER NOT NULL,
                side INTEGER NOT NULL CHECK (side IN (1, 2)),
                item_id INTEGER NOT NULL CHECK (item_id > 0),
                unit_price_in_copper INTEGER NOT NULL CHECK (unit_price_in_copper >= 0),
                quantity INTEGER NOT NULL CHECK (quantity > 0),
                created_at_utc TEXT NOT NULL,
                observed_at_utc TEXT NOT NULL,
                CONSTRAINT fk_current_tp_orders_account FOREIGN KEY (account_profile_id)
                    REFERENCES account_profiles(id) ON DELETE RESTRICT,
                CONSTRAINT fk_current_tp_orders_sync_batch FOREIGN KEY (sync_batch_id)
                    REFERENCES current_order_sync_batches(id) ON DELETE RESTRICT,
                CONSTRAINT uq_current_tp_orders_account_external_id
                    UNIQUE (account_profile_id, external_order_id)
            );

            CREATE TABLE current_tp_order_observations (
                id INTEGER PRIMARY KEY,
                sync_batch_id INTEGER NOT NULL,
                account_profile_id INTEGER NOT NULL,
                external_order_id INTEGER NOT NULL,
                side INTEGER NOT NULL CHECK (side IN (1, 2)),
                item_id INTEGER NOT NULL CHECK (item_id > 0),
                unit_price_in_copper INTEGER NOT NULL CHECK (unit_price_in_copper >= 0),
                quantity INTEGER NOT NULL CHECK (quantity > 0),
                created_at_utc TEXT NOT NULL,
                observed_at_utc TEXT NOT NULL,
                CONSTRAINT fk_current_order_observations_batch FOREIGN KEY (sync_batch_id)
                    REFERENCES current_order_sync_batches(id) ON DELETE RESTRICT,
                CONSTRAINT fk_current_order_observations_account FOREIGN KEY (account_profile_id)
                    REFERENCES account_profiles(id) ON DELETE RESTRICT,
                CONSTRAINT uq_current_order_observations_batch_external_id
                    UNIQUE (sync_batch_id, external_order_id)
            );

            CREATE INDEX ix_current_order_observations_account_observed_at
                ON current_tp_order_observations (account_profile_id, observed_at_utc);

            CREATE TABLE item_metadata (
                item_id INTEGER PRIMARY KEY CHECK (item_id > 0),
                name TEXT NOT NULL CHECK (length(name) > 0),
                observed_at_utc TEXT NOT NULL
            );

            CREATE TABLE user_settings (
                singleton_id INTEGER PRIMARY KEY CHECK (singleton_id = 1),
                settings_version INTEGER NOT NULL CHECK (settings_version > 0),
                minimum_profit_in_copper INTEGER NULL CHECK (minimum_profit_in_copper >= 0),
                minimum_roi_basis_points INTEGER NULL CHECK (minimum_roi_basis_points BETWEEN 0 AND 10000),
                cash_reserve_basis_points INTEGER NULL CHECK (cash_reserve_basis_points BETWEEN 0 AND 10000),
                updated_at_utc TEXT NOT NULL
            );
            """),
        new(
            3,
            "personal_sync_state_schema",
            """
            ALTER TABLE account_profiles ADD COLUMN last_sync_attempted_at_utc TEXT NULL;
            ALTER TABLE account_profiles ADD COLUMN last_sync_outcome INTEGER NULL CHECK (last_sync_outcome IN (1, 2));
            ALTER TABLE account_profiles ADD COLUMN last_sync_error_category INTEGER NULL CHECK (last_sync_error_category BETWEEN 0 AND 11);
            ALTER TABLE account_profiles ADD COLUMN history_coverage_start_utc TEXT NULL;
            ALTER TABLE account_profiles ADD COLUMN history_coverage_end_utc TEXT NULL;
            """),
        new(
            4,
            "local_watchlist_schema",
            """
            CREATE TABLE watchlist_entries (
                item_id INTEGER PRIMARY KEY CHECK (item_id > 0),
                added_at_utc TEXT NOT NULL
            );
            """),
        new(
            5,
            "market_history_schema",
            """
            CREATE TABLE market_price_observations (
                id INTEGER PRIMARY KEY,
                observed_at_utc TEXT NOT NULL,
                item_id INTEGER NOT NULL CHECK (item_id > 0),
                highest_buy_price_in_copper INTEGER NOT NULL CHECK (highest_buy_price_in_copper >= 0),
                lowest_sell_price_in_copper INTEGER NOT NULL CHECK (lowest_sell_price_in_copper >= 0),
                aggregate_buy_quantity INTEGER NOT NULL CHECK (aggregate_buy_quantity >= 0),
                aggregate_sell_quantity INTEGER NOT NULL CHECK (aggregate_sell_quantity >= 0),
                source_status INTEGER NOT NULL CHECK (source_status = 1),
                sampling_tier INTEGER NOT NULL CHECK (sampling_tier BETWEEN 1 AND 3),
                sampling_policy_version INTEGER NOT NULL CHECK (sampling_policy_version > 0),
                CONSTRAINT uq_market_price_observations_item_observed_at UNIQUE (item_id, observed_at_utc)
            );

            CREATE INDEX ix_market_price_observations_item_observed_at
                ON market_price_observations (item_id, observed_at_utc);

            CREATE TABLE market_order_book_snapshots (
                id INTEGER PRIMARY KEY,
                observed_at_utc TEXT NOT NULL,
                item_id INTEGER NOT NULL CHECK (item_id > 0),
                source_status INTEGER NOT NULL CHECK (source_status = 1),
                sampling_tier INTEGER NOT NULL CHECK (sampling_tier BETWEEN 1 AND 3),
                sampling_policy_version INTEGER NOT NULL CHECK (sampling_policy_version > 0),
                CONSTRAINT uq_market_order_book_snapshots_item_observed_at UNIQUE (item_id, observed_at_utc)
            );

            CREATE INDEX ix_market_order_book_snapshots_item_observed_at
                ON market_order_book_snapshots (item_id, observed_at_utc);

            CREATE TABLE market_order_book_levels (
                id INTEGER PRIMARY KEY,
                snapshot_id INTEGER NOT NULL CHECK (snapshot_id > 0),
                side INTEGER NOT NULL CHECK (side IN (1, 2)),
                level_ordinal INTEGER NOT NULL CHECK (level_ordinal >= 0),
                unit_price_in_copper INTEGER NOT NULL CHECK (unit_price_in_copper >= 0),
                quantity INTEGER NOT NULL CHECK (quantity > 0),
                listings INTEGER NOT NULL CHECK (listings > 0),
                CONSTRAINT fk_market_order_book_levels_snapshot FOREIGN KEY (snapshot_id)
                    REFERENCES market_order_book_snapshots(id) ON DELETE RESTRICT,
                CONSTRAINT uq_market_order_book_levels_snapshot_side_ordinal
                    UNIQUE (snapshot_id, side, level_ordinal)
            );

            CREATE INDEX ix_market_order_book_levels_snapshot
                ON market_order_book_levels (snapshot_id);
            """),
        new(
            6,
            "market_history_validation_hardening",
            """
            DROP INDEX IF EXISTS ix_market_price_observations_item_observed_at;
            DROP INDEX IF EXISTS ix_market_order_book_snapshots_item_observed_at;
            DROP INDEX IF EXISTS ix_market_order_book_levels_snapshot;
            """),
    ];

    public Task MigrateAsync(CancellationToken cancellationToken = default) =>
        MigrateToAsync(LatestVersion, validatePersistedData: false, cancellationToken);

    internal Task MigrateToAsync(int targetVersion, CancellationToken cancellationToken = default) =>
        MigrateToAsync(targetVersion, validatePersistedData: false, cancellationToken);

    internal Task MigrateAndValidatePersistedDataAsync(CancellationToken cancellationToken = default) =>
        MigrateToAsync(LatestVersion, validatePersistedData: true, cancellationToken);

    /// <summary>
    /// Performs the same full schema, SQLite, value, duplicate/index, and
    /// foreign-key validation used for a restore candidate against the live
    /// initialized database. This is intentionally on-demand because raw
    /// market-history validation scales with retained evidence.
    /// </summary>
    internal async Task ValidatePersistedDataAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await ValidateIntegrityAsync(connection, cancellationToken).ConfigureAwait(false);
        var appliedMigrations = await GetAppliedMigrationsAsync(connection, cancellationToken).ConfigureAwait(false);
        try
        {
            ValidateAppliedMigrations(appliedMigrations);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException("The SQLite database has inconsistent migration history.", exception);
        }

        if (appliedMigrations.Count != LatestVersion)
        {
            throw new InvalidDataException("The SQLite database does not have the current migration history.");
        }

        await ValidateLatestSchemaAsync(connection, validatePersistedData: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task MigrateToAsync(
        int targetVersion,
        bool validatePersistedData,
        CancellationToken cancellationToken)
    {
        if (targetVersion is < 0 or > LatestVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(targetVersion));
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureMigrationHistoryAsync(connection, cancellationToken).ConfigureAwait(false);
        var appliedMigrations = await GetAppliedMigrationsAsync(connection, cancellationToken).ConfigureAwait(false);
        ValidateAppliedMigrations(appliedMigrations);

        foreach (var migration in Migrations.Where(candidate => candidate.Version <= targetVersion && !appliedMigrations.ContainsKey(candidate.Version)))
        {
            await ApplyMigrationAsync(connection, migration, cancellationToken).ConfigureAwait(false);
        }

        if (targetVersion == LatestVersion)
        {
            await ValidateLatestSchemaAsync(connection, validatePersistedData, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static async Task ValidateBackupCandidateAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(databasePath) || !File.Exists(databasePath))
        {
            throw new InvalidDataException("The selected backup file is unavailable.");
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true,
            Pooling = false,
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ValidateIntegrityAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var migrationTableCheck = connection.CreateCommand();
        migrationTableCheck.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_migrations';";
        if (Convert.ToInt64(await migrationTableCheck.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 1)
        {
            throw new InvalidDataException("The selected file is not a Tyrian Ledger database backup.");
        }

        var appliedMigrations = await GetAppliedMigrationsAsync(connection, cancellationToken).ConfigureAwait(false);
        if (appliedMigrations.Count == 0)
        {
            throw new InvalidDataException("The selected file has no Tyrian Ledger schema history.");
        }

        ValidateAppliedMigrations(appliedMigrations);
        if (appliedMigrations.Count == LatestVersion)
        {
            await ValidateLatestSchemaAsync(connection, validatePersistedData: true, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EnsureMigrationHistoryAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                applied_at_utc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Dictionary<int, string>> GetAppliedMigrationsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version, name FROM schema_migrations ORDER BY version;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var migrations = new Dictionary<int, string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            migrations.Add(reader.GetInt32(0), reader.GetString(1));
        }

        return migrations;
    }

    internal static void ValidateAppliedMigrations(IReadOnlyDictionary<int, string> appliedMigrations)
    {
        var expectedMigrations = Migrations.ToDictionary(migration => migration.Version);
        if (appliedMigrations.Any(applied => !expectedMigrations.TryGetValue(applied.Key, out var expected) || expected.Name != applied.Value))
        {
            throw new InvalidOperationException("The SQLite database was created by a newer or unknown schema migration.");
        }

        if (appliedMigrations.Count > 0)
        {
            var highestAppliedVersion = appliedMigrations.Keys.Max();
            if (Enumerable.Range(1, highestAppliedVersion).Any(version => !appliedMigrations.ContainsKey(version)))
            {
                throw new InvalidOperationException("The SQLite migration history is incomplete.");
            }
        }
    }

    private static async Task ValidateLatestSchemaAsync(
        SqliteConnection connection,
        bool validatePersistedData,
        CancellationToken cancellationToken)
    {
        await ValidateTableSetAsync(connection, cancellationToken).ConfigureAwait(false);
        foreach (var (tableName, expectedColumns) in LatestSchemaColumns)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_xinfo({QuoteIdentifier(tableName)});";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var actualColumns = new Dictionary<string, SqliteColumnDefinition>(StringComparer.Ordinal);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.GetInt32(6) != 0 || !reader.IsDBNull(4))
                {
                    throw new InvalidDataException($"The SQLite database schema for '{tableName}' contains unsupported hidden/generated columns or defaults.");
                }

                actualColumns.Add(
                    reader.GetString(1),
                    new SqliteColumnDefinition(
                        reader.GetString(2),
                        reader.GetInt32(3) != 0,
                        reader.GetInt32(5)));
            }

            if (!actualColumns.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expectedColumns)
                || !actualColumns.OrderBy(column => column.Key).SequenceEqual(
                    LatestColumnDefinitions[tableName].OrderBy(column => column.Key)))
            {
                throw new InvalidDataException($"The SQLite database schema for '{tableName}' is incompatible with this application version.");
            }

            var requiredChecks = RequiredCheckConstraints.TryGetValue(tableName, out var checks)
                ? checks
                : new HashSet<string>(StringComparer.Ordinal);
            await ValidateCheckConstraintsAsync(connection, tableName, requiredChecks, cancellationToken).ConfigureAwait(false);

            var requiredIndexes = RequiredIndexes.TryGetValue(tableName, out var indexes)
                ? indexes
                : [];
            await ValidateIndexesAsync(connection, tableName, requiredIndexes, cancellationToken).ConfigureAwait(false);
        }

        await ValidateForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);
        await ValidateNoExecutableSchemaObjectsAsync(connection, cancellationToken).ConfigureAwait(false);

        if (!validatePersistedData)
        {
            return;
        }

        await ValidatePersistedValuesAsync(connection, cancellationToken).ConfigureAwait(false);
        await ValidatePersistedDomainInvariantsAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var foreignKeyCheck = connection.CreateCommand();
        foreignKeyCheck.CommandText = "PRAGMA foreign_key_check;";
        await using var foreignKeyReader = await foreignKeyCheck.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await foreignKeyReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("The SQLite database has failed its foreign-key integrity check.");
        }
    }

    private static async Task ValidateTableSetAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var actualTables = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            actualTables.Add(reader.GetString(0));
        }

        if (!actualTables.SetEquals(LatestSchemaColumns.Keys))
        {
            throw new InvalidDataException("The SQLite database contains incompatible tables.");
        }
    }

    private static async Task ValidateCheckConstraintsAsync(
        SqliteConnection connection,
        string tableName,
        IReadOnlySet<string> requiredChecks,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $tableName;";
        command.Parameters.AddWithValue("$tableName", tableName);
        var schemaSql = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        var actualChecks = ExtractCheckConstraints(schemaSql);
        var acceptsStrictOrderBookLevelPrices = tableName == "market_order_book_levels"
            && actualChecks.SetEquals(StrictOrderBookLevelPriceChecks);
        if (!actualChecks.SetEquals(requiredChecks) && !acceptsStrictOrderBookLevelPrices)
        {
            throw new InvalidDataException($"The SQLite database schema for '{tableName}' has incompatible check constraints.");
        }
    }

    private static async Task ValidateIndexesAsync(
        SqliteConnection connection,
        string tableName,
        IReadOnlyList<SqliteIndexDefinition> requiredIndexes,
        CancellationToken cancellationToken)
    {
        var actualIndexes = new List<(string Name, bool IsUnique, bool IsPartial)>();
        await using (var indexList = connection.CreateCommand())
        {
            indexList.CommandText = $"PRAGMA index_list({QuoteIdentifier(tableName)});";
            await using var reader = await indexList.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                actualIndexes.Add((reader.GetString(1), reader.GetInt32(2) != 0, reader.FieldCount > 4 && reader.GetInt32(4) != 0));
            }
        }

        if (actualIndexes.Count != requiredIndexes.Count)
        {
            throw new InvalidDataException($"The SQLite database schema for '{tableName}' has incompatible indexes.");
        }

        foreach (var requiredIndex in requiredIndexes)
        {
            var hasMatch = false;
            foreach (var actualIndex in actualIndexes)
            {
                if (actualIndex.IsPartial || actualIndex.IsUnique != requiredIndex.IsUnique
                    || (requiredIndex.Name is not null && !string.Equals(requiredIndex.Name, actualIndex.Name, StringComparison.Ordinal)))
                {
                    continue;
                }

                var columns = new List<(string Name, string Collation)>();
                await using var indexInfo = connection.CreateCommand();
                indexInfo.CommandText = $"PRAGMA index_xinfo({QuoteIdentifier(actualIndex.Name)});";
                await using var reader = await indexInfo.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (reader.GetInt32(5) != 0)
                    {
                        columns.Add((reader.GetString(2), reader.GetString(4)));
                    }
                }

                if (columns.Select(column => column.Name).SequenceEqual(requiredIndex.Columns, StringComparer.Ordinal)
                    && (requiredIndex.Collation is null || columns.All(column => string.Equals(column.Collation, requiredIndex.Collation, StringComparison.Ordinal))))
                {
                    hasMatch = true;
                    break;
                }
            }

            if (!hasMatch)
            {
                throw new InvalidDataException($"The SQLite database schema for '{tableName}' is missing a required index.");
            }
        }
    }

    private static async Task ValidateForeignKeysAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var actualForeignKeys = new List<SqliteForeignKeyDefinition>();
        foreach (var tableName in LatestSchemaColumns.Keys.Where(table => table is not "schema_migrations"))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA foreign_key_list({QuoteIdentifier(tableName)});";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!string.Equals(reader.GetString(5), "NO ACTION", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(reader.GetString(6), "RESTRICT", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"The SQLite database schema for '{tableName}' has an incompatible foreign-key action.");
                }

                actualForeignKeys.Add(new SqliteForeignKeyDefinition(
                    tableName,
                    reader.GetString(3),
                    reader.GetString(2),
                    reader.GetString(4)));
            }
        }

        if (!actualForeignKeys
                .OrderBy(foreignKey => foreignKey.TableName, StringComparer.Ordinal)
                .ThenBy(foreignKey => foreignKey.FromColumn, StringComparer.Ordinal)
                .SequenceEqual(
                    RequiredForeignKeys
                        .OrderBy(foreignKey => foreignKey.TableName, StringComparer.Ordinal)
                        .ThenBy(foreignKey => foreignKey.FromColumn, StringComparer.Ordinal)))
        {
            throw new InvalidDataException("The SQLite database schema has incompatible foreign-key constraints.");
        }
    }

    private static async Task ValidatePersistedValuesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        foreach (var (tableName, columns) in LatestColumnDefinitions)
        {
            var columnDefinitions = columns.ToArray();
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {string.Join(", ", columnDefinitions.Select(column => $"typeof({QuoteIdentifier(column.Key)}), {QuoteIdentifier(column.Key)}"))} FROM {QuoteIdentifier(tableName)};";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                for (var columnIndex = 0; columnIndex < columnDefinitions.Length; columnIndex++)
                {
                    var (columnName, definition) = columnDefinitions[columnIndex];
                    var storageTypeIndex = columnIndex * 2;
                    var valueIndex = storageTypeIndex + 1;
                    if (reader.IsDBNull(valueIndex))
                    {
                        if (definition.IsNotNull)
                        {
                            throw new InvalidDataException($"The SQLite database contains a null value for required column '{tableName}.{columnName}'.");
                        }

                        continue;
                    }

                    var expectedStorageType = string.Equals(definition.Type, "INTEGER", StringComparison.OrdinalIgnoreCase)
                        ? "integer"
                        : "text";
                    if (!string.Equals(reader.GetString(storageTypeIndex), expectedStorageType, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException($"The SQLite database contains an incompatible storage type for '{tableName}.{columnName}'.");
                    }

                    if (columnName.EndsWith("_utc", StringComparison.Ordinal))
                    {
                        _ = SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(valueIndex), $"{tableName}.{columnName}");
                    }

                    if ((tableName, columnName) is ("account_profiles", "account_scope_id") or ("item_metadata", "name")
                        && string.IsNullOrWhiteSpace(reader.GetString(valueIndex)))
                    {
                        throw new InvalidDataException($"The SQLite database contains a whitespace-only value for '{tableName}.{columnName}'.");
                    }
                }
            }
        }
    }

    private static async Task ValidatePersistedDomainInvariantsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        const long Int32Maximum = int.MaxValue;
        var invalidRowPredicates = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["account_profiles"] = "id <= 0 OR (history_coverage_start_utc IS NULL) <> (history_coverage_end_utc IS NULL) OR history_coverage_start_utc > history_coverage_end_utc",
            ["completed_tp_transactions"] = $"id <= 0 OR account_profile_id <= 0 OR external_transaction_id <= 0 OR side NOT IN (1, 2) OR item_id <= 0 OR item_id > {Int32Maximum} OR unit_price_in_copper < 0 OR unit_price_in_copper > {Int32Maximum} OR quantity <= 0 OR quantity > {Int32Maximum}",
            ["current_order_sync_batches"] = "id <= 0 OR account_profile_id <= 0",
            ["current_tp_orders"] = $"id <= 0 OR account_profile_id <= 0 OR sync_batch_id <= 0 OR external_order_id <= 0 OR side NOT IN (1, 2) OR item_id <= 0 OR item_id > {Int32Maximum} OR unit_price_in_copper < 0 OR unit_price_in_copper > {Int32Maximum} OR quantity <= 0 OR quantity > {Int32Maximum}",
            ["current_tp_order_observations"] = $"id <= 0 OR sync_batch_id <= 0 OR account_profile_id <= 0 OR external_order_id <= 0 OR side NOT IN (1, 2) OR item_id <= 0 OR item_id > {Int32Maximum} OR unit_price_in_copper < 0 OR unit_price_in_copper > {Int32Maximum} OR quantity <= 0 OR quantity > {Int32Maximum}",
            ["item_metadata"] = $"item_id <= 0 OR item_id > {Int32Maximum}",
            ["watchlist_entries"] = $"item_id <= 0 OR item_id > {Int32Maximum}",
            ["market_price_observations"] = $"id <= 0 OR item_id <= 0 OR item_id > {Int32Maximum} OR highest_buy_price_in_copper < 0 OR highest_buy_price_in_copper > {Int32Maximum} OR lowest_sell_price_in_copper < 0 OR lowest_sell_price_in_copper > {Int32Maximum} OR aggregate_buy_quantity < 0 OR aggregate_buy_quantity > {Int32Maximum} OR aggregate_sell_quantity < 0 OR aggregate_sell_quantity > {Int32Maximum} OR source_status <> 1 OR sampling_tier NOT BETWEEN 1 AND 3 OR sampling_policy_version <= 0 OR sampling_policy_version > {Int32Maximum}",
            ["market_order_book_snapshots"] = $"id <= 0 OR item_id <= 0 OR item_id > {Int32Maximum} OR source_status <> 1 OR sampling_tier NOT BETWEEN 1 AND 3 OR sampling_policy_version <= 0 OR sampling_policy_version > {Int32Maximum}",
            ["market_order_book_levels"] = $"id <= 0 OR snapshot_id <= 0 OR side NOT IN (1, 2) OR level_ordinal < 0 OR level_ordinal > {Int32Maximum} OR unit_price_in_copper < 0 OR unit_price_in_copper > {Int32Maximum} OR quantity <= 0 OR quantity > {Int32Maximum} OR listings <= 0 OR listings > {Int32Maximum}",
            ["schema_migrations"] = "version <= 0 OR trim(name) = ''",
            ["user_settings"] = $"singleton_id <> 1 OR settings_version <= 0 OR settings_version > {Int32Maximum} OR (minimum_profit_in_copper IS NOT NULL AND (minimum_profit_in_copper < 0 OR minimum_profit_in_copper > {Int32Maximum})) OR (minimum_roi_basis_points IS NOT NULL AND (minimum_roi_basis_points < 0 OR minimum_roi_basis_points > 10000)) OR (cash_reserve_basis_points IS NOT NULL AND (cash_reserve_basis_points < 0 OR cash_reserve_basis_points > 10000))",
        };

        foreach (var (tableName, invalidRowPredicate) in invalidRowPredicates)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT 1 FROM {QuoteIdentifier(tableName)} WHERE {invalidRowPredicate} LIMIT 1;";
            if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                throw new InvalidDataException($"The SQLite database contains domain-invalid values in '{tableName}'.");
            }
        }
    }

    private static async Task ValidateNoExecutableSchemaObjectsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('trigger', 'view') LIMIT 1;";
        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new InvalidDataException("The SQLite database schema contains unsupported executable objects.");
        }
    }

    private static IReadOnlySet<string> ExtractCheckConstraints(string? schemaSql)
    {
        if (string.IsNullOrWhiteSpace(schemaSql))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var checks = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < schemaSql.Length; index++)
        {
            if (TrySkipSqlLiteralOrComment(schemaSql, ref index))
            {
                continue;
            }

            if (!schemaSql.AsSpan(index).StartsWith("CHECK", StringComparison.OrdinalIgnoreCase)
                || (index > 0 && IsSqlIdentifierCharacter(schemaSql[index - 1])))
            {
                continue;
            }

            var expressionStart = index + "CHECK".Length;
            while (expressionStart < schemaSql.Length && char.IsWhiteSpace(schemaSql[expressionStart]))
            {
                expressionStart++;
            }

            if (expressionStart >= schemaSql.Length || schemaSql[expressionStart] != '(')
            {
                continue;
            }

            var expressionEnd = FindMatchingParenthesis(schemaSql, expressionStart);
            if (expressionEnd < 0)
            {
                throw new InvalidDataException("The SQLite database schema contains an unterminated check constraint.");
            }

            checks.Add(new string(schemaSql[(expressionStart + 1)..expressionEnd]
                .Where(character => !char.IsWhiteSpace(character))
                .ToArray())
                .ToLowerInvariant());
            index = expressionEnd;
        }

        return checks;
    }

    private static int FindMatchingParenthesis(string value, int openingIndex)
    {
        var depth = 0;
        for (var index = openingIndex; index < value.Length; index++)
        {
            if (TrySkipSqlLiteralOrComment(value, ref index))
            {
                continue;
            }

            if (value[index] == '(')
            {
                depth++;
            }
            else if (value[index] == ')' && --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TrySkipSqlLiteralOrComment(string value, ref int index)
    {
        if (value[index] is '\'' or '\"' or '`')
        {
            var delimiter = value[index];
            index++;
            while (index < value.Length)
            {
                if (value[index] == delimiter)
                {
                    if (index + 1 < value.Length && value[index + 1] == delimiter)
                    {
                        index += 2;
                        continue;
                    }

                    return true;
                }

                index++;
            }

            return true;
        }

        if (value[index] == '[')
        {
            while (index < value.Length && value[index] != ']')
            {
                index++;
            }

            return true;
        }

        if (value[index] == '-' && index + 1 < value.Length && value[index + 1] == '-')
        {
            while (index < value.Length && value[index] is not '\r' and not '\n')
            {
                index++;
            }

            return true;
        }

        if (value[index] == '/' && index + 1 < value.Length && value[index + 1] == '*')
        {
            index += 2;
            while (index + 1 < value.Length && (value[index] != '*' || value[index + 1] != '/'))
            {
                index++;
            }

            index++;
            return true;
        }

        return false;
    }

    private static bool IsSqlIdentifierCharacter(char character) =>
        char.IsLetterOrDigit(character) || character == '_';

    private static string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static async Task ValidateIntegrityAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The SQLite database has failed its integrity check.");
        }
    }

    private static async Task ApplyMigrationAsync(
        SqliteConnection connection,
        SqliteSchemaMigration migration,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var migrationCommand = connection.CreateCommand();
        migrationCommand.Transaction = transaction;
        migrationCommand.CommandText = migration.Sql;
        await migrationCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var historyCommand = connection.CreateCommand();
        historyCommand.Transaction = transaction;
        historyCommand.CommandText = """
            INSERT INTO schema_migrations (version, name, applied_at_utc)
            VALUES ($version, $name, $appliedAtUtc);
            """;
        historyCommand.Parameters.AddWithValue("$version", migration.Version);
        historyCommand.Parameters.AddWithValue("$name", migration.Name);
        historyCommand.Parameters.AddWithValue("$appliedAtUtc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await historyCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, SqliteColumnDefinition> Columns(
        params (string Name, string Type, bool IsNotNull, int PrimaryKeyPosition)[] values) =>
        values.ToDictionary(
            value => value.Name,
            value => new SqliteColumnDefinition(value.Type, value.IsNotNull, value.PrimaryKeyPosition),
            StringComparer.Ordinal);

    private static IReadOnlySet<string> Checks(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);

    private sealed record SqliteColumnDefinition(string Type, bool IsNotNull, int PrimaryKeyPosition);

    private sealed record SqliteIndexDefinition(string? Name, bool IsUnique, IReadOnlyList<string> Columns, string? Collation = null);

    private sealed record SqliteForeignKeyDefinition(string TableName, string FromColumn, string ReferencedTable, string ReferencedColumn);

    private sealed record SqliteSchemaMigration(int Version, string Name, string Sql);
}
