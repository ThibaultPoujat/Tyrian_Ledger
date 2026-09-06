using Gw2Tp.Application.Persistence;

namespace Gw2Tp.Infrastructure.Persistence;

internal sealed class SqliteUserSettingsRepository(ISqliteConnectionFactory connectionFactory) : IUserSettingsRepository
{
    public async Task<UserSettings?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT settings_version, minimum_profit_in_copper, minimum_roi_basis_points,
                   cash_reserve_basis_points, updated_at_utc
            FROM user_settings
            WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new UserSettings(
            reader.GetInt32(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3),
            SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(4), "user_settings.updated_at_utc"));
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO user_settings (
                singleton_id, settings_version, minimum_profit_in_copper,
                minimum_roi_basis_points, cash_reserve_basis_points, updated_at_utc)
            VALUES (
                1, $settingsVersion, $minimumProfitInCopper,
                $minimumRoiBasisPoints, $cashReserveBasisPoints, $updatedAtUtc)
            ON CONFLICT(singleton_id) DO UPDATE SET
                settings_version = excluded.settings_version,
                minimum_profit_in_copper = excluded.minimum_profit_in_copper,
                minimum_roi_basis_points = excluded.minimum_roi_basis_points,
                cash_reserve_basis_points = excluded.cash_reserve_basis_points,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$settingsVersion", settings.SettingsVersion);
        command.Parameters.AddWithValue("$minimumProfitInCopper", (object?)settings.MinimumProfitInCopper ?? DBNull.Value);
        command.Parameters.AddWithValue("$minimumRoiBasisPoints", (object?)settings.MinimumRoiBasisPoints ?? DBNull.Value);
        command.Parameters.AddWithValue("$cashReserveBasisPoints", (object?)settings.CashReserveBasisPoints ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAtUtc", SqlitePersistenceValues.ToUtcTimestamp(settings.UpdatedAtUtc, nameof(settings.UpdatedAtUtc)));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateSettings(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SettingsVersion <= 0 || settings.MinimumProfitInCopper is < 0 ||
            settings.MinimumRoiBasisPoints is < 0 or > 10000 ||
            settings.CashReserveBasisPoints is < 0 or > 10000)
        {
            throw new ArgumentException("The typed user settings contain an invalid version, copper value, or basis-point value.", nameof(settings));
        }

        _ = SqlitePersistenceValues.ToUtcTimestamp(settings.UpdatedAtUtc, nameof(settings.UpdatedAtUtc));
    }
}
