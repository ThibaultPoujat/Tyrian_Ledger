using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Gw2Tp.Infrastructure.Persistence;

internal sealed class SqliteSchemaMigrator(ISqliteConnectionFactory connectionFactory)
{
    private const int LatestVersion = 3;

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

    private static void ValidateAppliedMigrations(IReadOnlyDictionary<int, string> appliedMigrations)
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

    private sealed record SqliteSchemaMigration(int Version, string Name, string Sql);
}
