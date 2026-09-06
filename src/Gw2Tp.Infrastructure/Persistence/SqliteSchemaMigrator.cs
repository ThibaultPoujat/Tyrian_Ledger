using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Gw2Tp.Infrastructure.Persistence;

internal sealed class SqliteSchemaMigrator(ISqliteConnectionFactory connectionFactory)
{
    private const int LatestVersion = 3;

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
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<SqliteIndexDefinition>> RequiredIndexes =
        new Dictionary<string, IReadOnlyList<SqliteIndexDefinition>>(StringComparer.Ordinal)
        {
            ["account_profiles"] = [new(null, true, ["account_scope_id"])],
            ["completed_tp_transactions"] = [new(null, true, ["account_profile_id", "external_transaction_id"]), new("ix_completed_transactions_account_completed_at", false, ["account_profile_id", "completed_at_utc"])],
            ["current_tp_orders"] = [new(null, true, ["account_profile_id", "external_order_id"])],
            ["current_tp_order_observations"] = [new(null, true, ["sync_batch_id", "external_order_id"]), new("ix_current_order_observations_account_observed_at", false, ["account_profile_id", "observed_at_utc"])],
        };

    private static readonly IReadOnlyList<SqliteForeignKeyDefinition> RequiredForeignKeys =
    [
        new("completed_tp_transactions", "account_profile_id", "account_profiles", "id"),
        new("current_order_sync_batches", "account_profile_id", "account_profiles", "id"),
        new("current_tp_orders", "account_profile_id", "account_profiles", "id"),
        new("current_tp_orders", "sync_batch_id", "current_order_sync_batches", "id"),
        new("current_tp_order_observations", "account_profile_id", "account_profiles", "id"),
        new("current_tp_order_observations", "sync_batch_id", "current_order_sync_batches", "id"),
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RequiredSchemaFragments =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["account_profiles"] = ["account_scope_idtextnotnullcollatebinary", "unique(account_scope_id)", "check(last_sync_outcomein(1,2))", "check(last_sync_error_categorybetween0and11)"],
            ["completed_tp_transactions"] = ["check(sidein(1,2))", "check(item_id>0)", "check(unit_price_in_copper>=0)", "check(quantity>0)"],
            ["current_tp_orders"] = ["check(sidein(1,2))", "check(item_id>0)", "check(unit_price_in_copper>=0)", "check(quantity>0)"],
            ["current_tp_order_observations"] = ["check(sidein(1,2))", "check(item_id>0)", "check(unit_price_in_copper>=0)", "check(quantity>0)"],
            ["item_metadata"] = ["check(item_id>0)", "check(length(name)>0)"],
            ["user_settings"] = ["check(singleton_id=1)", "check(settings_version>0)", "check(minimum_profit_in_copper>=0)", "check(minimum_roi_basis_pointsbetween0and10000)", "check(cash_reserve_basis_pointsbetween0and10000)"],
        };

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
    ];

    public Task MigrateAsync(CancellationToken cancellationToken = default) =>
        MigrateToAsync(LatestVersion, cancellationToken);

    internal async Task MigrateToAsync(int targetVersion, CancellationToken cancellationToken = default)
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
            await ValidateLatestSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
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

    private static async Task ValidateLatestSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ValidateIntegrityAsync(connection, cancellationToken).ConfigureAwait(false);
        foreach (var (tableName, expectedColumns) in LatestSchemaColumns)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)});";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var actualColumns = new Dictionary<string, SqliteColumnDefinition>(StringComparer.Ordinal);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
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

            if (RequiredSchemaFragments.TryGetValue(tableName, out var requiredFragments))
            {
                await ValidateTableSqlAsync(connection, tableName, requiredFragments, cancellationToken).ConfigureAwait(false);
            }

            var requiredIndexes = RequiredIndexes.TryGetValue(tableName, out var indexes)
                ? indexes
                : [];
            await ValidateIndexesAsync(connection, tableName, requiredIndexes, cancellationToken).ConfigureAwait(false);
        }

        await ValidateForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);
        await ValidateNoExecutableSchemaObjectsAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var foreignKeyCheck = connection.CreateCommand();
        foreignKeyCheck.CommandText = "PRAGMA foreign_key_check;";
        await using var foreignKeyReader = await foreignKeyCheck.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await foreignKeyReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("The SQLite database has failed its foreign-key integrity check.");
        }
    }

    private static async Task ValidateTableSqlAsync(
        SqliteConnection connection,
        string tableName,
        IReadOnlyList<string> requiredFragments,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $tableName;";
        command.Parameters.AddWithValue("$tableName", tableName);
        var schemaSql = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        var normalizedSchemaSql = NormalizeSchemaSql(schemaSql);
        if (string.IsNullOrEmpty(normalizedSchemaSql) || requiredFragments.Any(fragment => !normalizedSchemaSql.Contains(fragment, StringComparison.Ordinal)))
        {
            throw new InvalidDataException($"The SQLite database schema for '{tableName}' is missing required constraints.");
        }
    }

    private static async Task ValidateIndexesAsync(
        SqliteConnection connection,
        string tableName,
        IReadOnlyList<SqliteIndexDefinition> requiredIndexes,
        CancellationToken cancellationToken)
    {
        if (requiredIndexes.Count == 0)
        {
            return;
        }

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

                var columns = new List<string>();
                await using var indexInfo = connection.CreateCommand();
                indexInfo.CommandText = $"PRAGMA index_info({QuoteIdentifier(actualIndex.Name)});";
                await using var reader = await indexInfo.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    columns.Add(reader.GetString(2));
                }

                if (columns.SequenceEqual(requiredIndex.Columns, StringComparer.Ordinal))
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
                if (!string.Equals(reader.GetString(6), "RESTRICT", StringComparison.OrdinalIgnoreCase))
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

    private static async Task ValidateNoExecutableSchemaObjectsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('trigger', 'view') LIMIT 1;";
        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new InvalidDataException("The SQLite database schema contains unsupported executable objects.");
        }
    }

    private static string NormalizeSchemaSql(string? schemaSql) =>
        schemaSql is null
            ? string.Empty
            : new string(schemaSql.Where(character => !char.IsWhiteSpace(character)).ToArray()).ToLowerInvariant();

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

    private sealed record SqliteColumnDefinition(string Type, bool IsNotNull, int PrimaryKeyPosition);

    private sealed record SqliteIndexDefinition(string? Name, bool IsUnique, IReadOnlyList<string> Columns);

    private sealed record SqliteForeignKeyDefinition(string TableName, string FromColumn, string ReferencedTable, string ReferencedColumn);

    private sealed record SqliteSchemaMigration(int Version, string Name, string Sql);
}
