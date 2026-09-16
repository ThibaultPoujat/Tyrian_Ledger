using System.Globalization;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.PersonalTradingPost;
using Microsoft.Data.Sqlite;

namespace Gw2Tp.Infrastructure.Persistence;

/// <summary>
/// Stores one normalized current crafting snapshot per account profile. Raw
/// API bodies and character names are never persisted; replacing a snapshot
/// deletes superseded feature rows in the same transaction.
/// </summary>
internal sealed class SqliteAccountCraftingSnapshotRepository(
    ISqliteConnectionFactory connectionFactory,
    ISqliteDatabaseGate? databaseGate = null) : IAccountCraftingSnapshotRepository
{
    private readonly ISqliteDatabaseGate gate = databaseGate ?? new SqliteDatabaseGate();

    public async Task ReplaceAsync(AccountCraftingSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ValidateSnapshot(snapshot);
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var profileId = await GetOrCreateProfileIdAsync(connection, transaction, snapshot.AccountScope.AccountId, snapshot.CapturedAtUtc, cancellationToken).ConfigureAwait(false);

        foreach (var table in new[] { "account_crafting_bank_entries", "account_crafting_material_entries", "account_crafting_recipe_unlocks", "account_crafting_disciplines" })
        {
            await using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = $"DELETE FROM {table} WHERE account_profile_id = $profileId;";
            delete.Parameters.AddWithValue("$profileId", profileId);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO account_crafting_snapshots (
                    account_profile_id, captured_at_utc,
                    bank_availability, bank_error_category,
                    materials_availability, materials_error_category,
                    recipes_availability, recipes_error_category,
                    crafting_availability, crafting_error_category)
                VALUES ($profileId, $capturedAtUtc, $bankAvailability, $bankError, $materialsAvailability, $materialsError, $recipesAvailability, $recipesError, $craftingAvailability, $craftingError)
                ON CONFLICT(account_profile_id) DO UPDATE SET
                    captured_at_utc = excluded.captured_at_utc,
                    bank_availability = excluded.bank_availability,
                    bank_error_category = excluded.bank_error_category,
                    materials_availability = excluded.materials_availability,
                    materials_error_category = excluded.materials_error_category,
                    recipes_availability = excluded.recipes_availability,
                    recipes_error_category = excluded.recipes_error_category,
                    crafting_availability = excluded.crafting_availability,
                    crafting_error_category = excluded.crafting_error_category;
                """;
            upsert.Parameters.AddWithValue("$profileId", profileId);
            upsert.Parameters.AddWithValue("$capturedAtUtc", ToUtc(snapshot.CapturedAtUtc));
            AddFeatureParameters(upsert, "bank", snapshot.BankInventory);
            AddFeatureParameters(upsert, "materials", snapshot.MaterialStorage);
            AddFeatureParameters(upsert, "recipes", snapshot.RecipeUnlocks);
            AddFeatureParameters(upsert, "crafting", snapshot.CharacterCrafting);
            await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (snapshot.BankInventory.Value is { } bank)
        {
            foreach (var entry in bank.GroupBy(entry => (entry.ItemId, entry.Binding)).Select(group => new AccountInventoryEntry(group.Key.ItemId, checked(group.Sum(entry => entry.Quantity)), group.Key.Binding)))
            {
                await InsertAsync(connection, transaction, "INSERT INTO account_crafting_bank_entries (account_profile_id, item_id, binding, quantity) VALUES ($profileId, $itemId, $binding, $quantity);", profileId, entry.ItemId, (int)entry.Binding, entry.Quantity, cancellationToken).ConfigureAwait(false);
            }
        }
        if (snapshot.MaterialStorage.Value is { } materials)
        {
            foreach (var entry in materials)
            {
                await InsertAsync(connection, transaction, "INSERT INTO account_crafting_material_entries (account_profile_id, item_id, category_id, binding, quantity) VALUES ($profileId, $itemId, $categoryId, $binding, $quantity);", profileId, entry.ItemId, entry.CategoryId, entry.Quantity, cancellationToken, entry.Binding).ConfigureAwait(false);
            }
        }
        if (snapshot.RecipeUnlocks.Value is { } recipes)
        {
            foreach (var recipeId in recipes)
            {
                await InsertAsync(connection, transaction, "INSERT INTO account_crafting_recipe_unlocks (account_profile_id, recipe_id) VALUES ($profileId, $itemId);", profileId, recipeId, null, null, cancellationToken).ConfigureAwait(false);
            }
        }
        if (snapshot.CharacterCrafting.Value is { } disciplines)
        {
            foreach (var discipline in disciplines)
            {
                await InsertAsync(connection, transaction, "INSERT INTO account_crafting_disciplines (account_profile_id, discipline, rating, is_active) VALUES ($profileId, $discipline, $rating, $active);", profileId, discipline.Discipline, discipline.Rating, discipline.IsActive ? 1 : 0, cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AccountCraftingSnapshot?> GetLatestAsync(AccountScope accountScope, CancellationToken cancellationToken = default)
    {
        if (accountScope is null || string.IsNullOrWhiteSpace(accountScope.AccountId)) throw new ArgumentException("An opaque account scope is required.", nameof(accountScope));
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.captured_at_utc, s.bank_availability, s.bank_error_category,
                   s.materials_availability, s.materials_error_category,
                   s.recipes_availability, s.recipes_error_category,
                   s.crafting_availability, s.crafting_error_category, p.id
            FROM account_crafting_snapshots s
            JOIN account_profiles p ON p.id = s.account_profile_id
            WHERE p.account_scope_id = $accountScopeId;
            """;
        command.Parameters.AddWithValue("$accountScopeId", accountScope.AccountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var capturedAt = SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(0), "account_crafting_snapshots.captured_at_utc");
        var bank = ReadFeature<IReadOnlyList<AccountInventoryEntry>>(reader, 1, 2);
        var materials = ReadFeature<IReadOnlyList<AccountMaterialEntry>>(reader, 3, 4);
        var recipes = ReadFeature<IReadOnlyList<int>>(reader, 5, 6);
        var crafting = ReadFeature<IReadOnlyList<CraftingDisciplineCapability>>(reader, 7, 8);
        var profileId = reader.GetInt64(9);
        if (profileId <= 0) throw new InvalidDataException("The stored crafting snapshot has an invalid account profile.");
        await reader.DisposeAsync().ConfigureAwait(false);

        return new AccountCraftingSnapshot(
            accountScope,
            capturedAt,
            bank with { Value = bank.Availability == CraftingFeatureAvailability.Available ? await ReadBankAsync(connection, profileId, cancellationToken).ConfigureAwait(false) : null },
            materials with { Value = materials.Availability == CraftingFeatureAvailability.Available ? await ReadMaterialsAsync(connection, profileId, cancellationToken).ConfigureAwait(false) : null },
            recipes with { Value = recipes.Availability == CraftingFeatureAvailability.Available ? await ReadRecipesAsync(connection, profileId, cancellationToken).ConfigureAwait(false) : null },
            crafting with { Value = crafting.Availability == CraftingFeatureAvailability.Available ? await ReadDisciplinesAsync(connection, profileId, cancellationToken).ConfigureAwait(false) : null });
    }

    private static async Task<long> GetOrCreateProfileIdAsync(SqliteConnection connection, SqliteTransaction transaction, string accountScopeId, DateTimeOffset createdAtUtc, CancellationToken cancellationToken)
    {
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO account_profiles (account_scope_id, created_at_utc) VALUES ($scope, $createdAt) ON CONFLICT(account_scope_id) DO NOTHING;";
            insert.Parameters.AddWithValue("$scope", accountScopeId);
            insert.Parameters.AddWithValue("$createdAt", ToUtc(createdAtUtc));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = "SELECT id FROM account_profiles WHERE account_scope_id = $scope;";
        select.Parameters.AddWithValue("$scope", accountScopeId);
        var value = await select.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is long profileId && profileId > 0 ? profileId : throw new InvalidDataException("The account profile could not be created.");
    }

    private static async Task InsertAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, long profileId, object valueOne, object? valueTwo, object? valueThree, CancellationToken cancellationToken, AccountItemBinding? binding = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$profileId", profileId);
        if (sql.Contains("$itemId", StringComparison.Ordinal)) command.Parameters.AddWithValue("$itemId", valueOne);
        if (sql.Contains("$binding", StringComparison.Ordinal)) command.Parameters.AddWithValue("$binding", binding is null ? valueTwo! : (int)binding.Value);
        if (sql.Contains("$categoryId", StringComparison.Ordinal)) command.Parameters.AddWithValue("$categoryId", valueTwo!);
        if (sql.Contains("$discipline", StringComparison.Ordinal)) command.Parameters.AddWithValue("$discipline", valueOne);
        if (sql.Contains("$quantity", StringComparison.Ordinal)) command.Parameters.AddWithValue("$quantity", valueThree!);
        if (sql.Contains("$rating", StringComparison.Ordinal)) command.Parameters.AddWithValue("$rating", valueTwo!);
        if (sql.Contains("$active", StringComparison.Ordinal)) command.Parameters.AddWithValue("$active", valueThree!);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddFeatureParameters<T>(SqliteCommand command, string prefix, CraftingFeatureResult<T> feature)
    {
        command.Parameters.AddWithValue($"${prefix}Availability", (int)feature.Availability);
        command.Parameters.AddWithValue($"${prefix}Error", feature.ErrorCategory is null ? DBNull.Value : (int)feature.ErrorCategory.Value);
    }

    private static CraftingFeatureResult<T> ReadFeature<T>(SqliteDataReader reader, int availabilityIndex, int errorIndex)
    {
        var availability = (CraftingFeatureAvailability)reader.GetInt32(availabilityIndex);
        var error = reader.IsDBNull(errorIndex) ? (Gw2ApiErrorCategory?)null : (Gw2ApiErrorCategory)reader.GetInt32(errorIndex);
        if (!Enum.IsDefined(availability) || (error is not null && !Enum.IsDefined(error.Value)) || (availability == CraftingFeatureAvailability.Available) != (error is null)) throw new InvalidDataException("The stored crafting feature status is invalid.");
        return new CraftingFeatureResult<T>(availability, default, error);
    }

    private static async Task<IReadOnlyList<AccountInventoryEntry>> ReadBankAsync(SqliteConnection connection, long profileId, CancellationToken cancellationToken) =>
        await ReadRowsAsync(connection, "SELECT item_id, binding, quantity FROM account_crafting_bank_entries WHERE account_profile_id = $profileId ORDER BY item_id, binding;", profileId, reader => new AccountInventoryEntry(reader.GetInt32(0), reader.GetInt32(2), (AccountItemBinding)reader.GetInt32(1)), cancellationToken).ConfigureAwait(false);

    private static async Task<IReadOnlyList<AccountMaterialEntry>> ReadMaterialsAsync(SqliteConnection connection, long profileId, CancellationToken cancellationToken) =>
        await ReadRowsAsync(connection, "SELECT item_id, category_id, quantity, binding FROM account_crafting_material_entries WHERE account_profile_id = $profileId ORDER BY item_id;", profileId, reader => new AccountMaterialEntry(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), (AccountItemBinding)reader.GetInt32(3)), cancellationToken).ConfigureAwait(false);

    private static async Task<IReadOnlyList<int>> ReadRecipesAsync(SqliteConnection connection, long profileId, CancellationToken cancellationToken) =>
        await ReadRowsAsync(connection, "SELECT recipe_id FROM account_crafting_recipe_unlocks WHERE account_profile_id = $profileId ORDER BY recipe_id;", profileId, reader => reader.GetInt32(0), cancellationToken).ConfigureAwait(false);

    private static async Task<IReadOnlyList<CraftingDisciplineCapability>> ReadDisciplinesAsync(SqliteConnection connection, long profileId, CancellationToken cancellationToken) =>
        await ReadRowsAsync(connection, "SELECT discipline, rating, is_active FROM account_crafting_disciplines WHERE account_profile_id = $profileId ORDER BY discipline;", profileId, reader => new CraftingDisciplineCapability(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2) == 1), cancellationToken).ConfigureAwait(false);

    private static async Task<IReadOnlyList<T>> ReadRowsAsync<T>(SqliteConnection connection, string sql, long profileId, Func<SqliteDataReader, T> map, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$profileId", profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) rows.Add(map(reader));
        return rows;
    }

    private static string ToUtc(DateTimeOffset value) => SqlitePersistenceValues.ToUtcTimestamp(value, nameof(value));

    private static void ValidateSnapshot(AccountCraftingSnapshot snapshot)
    {
        if (snapshot is null || snapshot.AccountScope is null || string.IsNullOrWhiteSpace(snapshot.AccountScope.AccountId)) throw new ArgumentException("A complete account crafting snapshot is required.", nameof(snapshot));
        _ = ToUtc(snapshot.CapturedAtUtc);
        ValidateFeature(snapshot.BankInventory, value => value.All(entry => entry.ItemId > 0 && entry.Quantity > 0 && Enum.IsDefined(entry.Binding)));
        ValidateFeature(snapshot.MaterialStorage, value => value.All(entry => entry.ItemId > 0 && entry.CategoryId > 0 && entry.Quantity >= 0 && Enum.IsDefined(entry.Binding)) && value.Select(entry => entry.ItemId).Distinct().Count() == value.Count);
        ValidateFeature(snapshot.RecipeUnlocks, value => value.All(id => id > 0) && value.Distinct().Count() == value.Count);
        ValidateFeature(snapshot.CharacterCrafting, value => value.All(entry => !string.IsNullOrWhiteSpace(entry.Discipline) && entry.Rating >= 0) && value.Select(entry => entry.Discipline).Distinct(StringComparer.Ordinal).Count() == value.Count);
    }

    private static void ValidateFeature<T>(CraftingFeatureResult<IReadOnlyList<T>> feature, Func<IReadOnlyList<T>, bool> validate)
    {
        if (feature is null || !Enum.IsDefined(feature.Availability) || (feature.Availability == CraftingFeatureAvailability.Available) != (feature.Value is not null) || (feature.Availability == CraftingFeatureAvailability.Available) != (feature.ErrorCategory is null) || (feature.ErrorCategory is not null && !Enum.IsDefined(feature.ErrorCategory.Value)) || (feature.Value is not null && !validate(feature.Value))) throw new ArgumentException("The crafting feature snapshot is invalid.");
    }
}
