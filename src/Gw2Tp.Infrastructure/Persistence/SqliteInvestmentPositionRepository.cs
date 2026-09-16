using Gw2Tp.Application.Persistence;
using Microsoft.Data.Sqlite;

namespace Gw2Tp.Infrastructure.Persistence;

internal sealed class SqliteInvestmentPositionRepository(
    ISqliteConnectionFactory connectionFactory,
    ISqliteDatabaseGate? databaseGate = null) : IInvestmentPositionRepository
{
    private readonly ISqliteDatabaseGate gate = databaseGate ?? new SqliteDatabaseGate();

    public async Task<IReadOnlyList<InvestmentPosition>> GetAllAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default)
    {
        SqlitePersistenceValues.ValidateAccountProfile(accountProfile);
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadPositionsAsync(connection, null, accountProfile.Id, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<InvestmentPosition?> GetAsync(AccountProfile accountProfile, long positionId, CancellationToken cancellationToken = default)
    {
        SqlitePersistenceValues.ValidateAccountProfile(accountProfile);
        if (positionId <= 0) throw new ArgumentOutOfRangeException(nameof(positionId));
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return (await ReadPositionsAsync(connection, null, accountProfile.Id, positionId, cancellationToken).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task<InvestmentPosition> CreateAsync(AccountProfile accountProfile, CreateInvestmentPosition position, DateTimeOffset createdAtUtc, CancellationToken cancellationToken = default)
    {
        SqlitePersistenceValues.ValidateAccountProfile(accountProfile);
        ValidateCreate(position);
        var createdAt = SqlitePersistenceValues.ToUtcTimestamp(createdAtUtc, nameof(createdAtUtc));
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        long id;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO investment_positions (account_profile_id, item_id, original_quantity, acquisition_basis_in_copper, strategy, category, opened_at_utc, thesis, notes, is_closed, created_at_utc, updated_at_utc, closed_at_utc)
                VALUES ($accountProfileId, $itemId, $quantity, $basis, $strategy, $category, $openedAtUtc, $thesis, $notes, 0, $createdAtUtc, $createdAtUtc, NULL);
                SELECT last_insert_rowid();
                """;
            AddPositionParameters(command, accountProfile.Id, position.ItemId, position.Quantity, position.AcquisitionBasisInCopper, position.Strategy, position.Category, position.OpenedAtUtc, position.Thesis, position.Notes, createdAt);
            id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        }
        await ReplaceTargetsAsync(connection, transaction, id, position.Targets, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (await ReadPositionsAsync(connection, null, accountProfile.Id, id, cancellationToken).ConfigureAwait(false)).Single();
    }

    public async Task<InvestmentPosition?> UpdateAsync(AccountProfile accountProfile, long positionId, UpdateInvestmentPosition position, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken = default)
    {
        SqlitePersistenceValues.ValidateAccountProfile(accountProfile);
        if (positionId <= 0) throw new ArgumentOutOfRangeException(nameof(positionId));
        ValidateUpdate(position);
        var updatedAt = SqlitePersistenceValues.ToUtcTimestamp(updatedAtUtc, nameof(updatedAtUtc));
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var existing = (await ReadPositionsAsync(connection, transaction, accountProfile.Id, positionId, cancellationToken).ConfigureAwait(false)).SingleOrDefault();
        if (existing is null || existing.IsClosed) return null;
        var exitedQuantity = existing.OriginalQuantity - existing.RemainingQuantity;
        var allocatedBasis = existing.Exits.Where(exit => exit.AllocatedBasisInCopper is not null).Sum(exit => (long)exit.AllocatedBasisInCopper!.Value);
        if (position.Quantity < exitedQuantity || position.Targets.Sum(target => (long)target.Quantity) > position.Quantity - exitedQuantity ||
            position.AcquisitionBasisInCopper is { } basis && basis < allocatedBasis)
        {
            throw new ArgumentException("The update conflicts with retained exit history.", nameof(position));
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE investment_positions SET original_quantity = $quantity, acquisition_basis_in_copper = $basis,
                    strategy = $strategy, category = $category, opened_at_utc = $openedAtUtc, thesis = $thesis,
                    notes = $notes, updated_at_utc = $updatedAtUtc
                WHERE id = $id AND account_profile_id = $accountProfileId AND is_closed = 0;
                """;
            AddPositionParameters(command, accountProfile.Id, existing.ItemId, position.Quantity, position.AcquisitionBasisInCopper, position.Strategy, position.Category, position.OpenedAtUtc, position.Thesis, position.Notes, updatedAt);
            command.Parameters.AddWithValue("$id", positionId);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) return null;
        }
        await ReplaceTargetsAsync(connection, transaction, positionId, position.Targets, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (await ReadPositionsAsync(connection, null, accountProfile.Id, positionId, cancellationToken).ConfigureAwait(false)).Single();
    }

    public async Task<InvestmentPosition?> RecordExitAsync(AccountProfile accountProfile, long positionId, int quantity, DateTimeOffset exitedAtUtc, string? notes, CancellationToken cancellationToken = default)
    {
        SqlitePersistenceValues.ValidateAccountProfile(accountProfile);
        if (positionId <= 0 || quantity <= 0) throw new ArgumentOutOfRangeException(nameof(positionId));
        if (notes is { Length: > 4000 }) throw new ArgumentOutOfRangeException(nameof(notes));
        var exitedAt = SqlitePersistenceValues.ToUtcTimestamp(exitedAtUtc, nameof(exitedAtUtc));
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var existing = (await ReadPositionsAsync(connection, transaction, accountProfile.Id, positionId, cancellationToken).ConfigureAwait(false)).SingleOrDefault();
        if (existing is null || existing.IsClosed) return null;
        if (quantity > existing.RemainingQuantity) throw new ArgumentOutOfRangeException(nameof(quantity));
        int? allocatedBasis = null;
        if (existing.AcquisitionBasisInCopper is { } totalBasis)
        {
            var alreadyAllocated = existing.Exits.Sum(exit => (long)(exit.AllocatedBasisInCopper ?? 0));
            allocatedBasis = quantity == existing.RemainingQuantity
                ? checked((int)(totalBasis - alreadyAllocated))
                : checked((int)((long)totalBasis * quantity / existing.OriginalQuantity));
        }
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO investment_position_exits (position_id, quantity, allocated_basis_in_copper, exited_at_utc, notes) VALUES ($positionId, $quantity, $basis, $exitedAtUtc, $notes);";
            insert.Parameters.AddWithValue("$positionId", positionId); insert.Parameters.AddWithValue("$quantity", quantity);
            insert.Parameters.AddWithValue("$basis", (object?)allocatedBasis ?? DBNull.Value); insert.Parameters.AddWithValue("$exitedAtUtc", exitedAt); insert.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await ConsumeTargetsAsync(connection, transaction, positionId, quantity, cancellationToken).ConfigureAwait(false);
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE investment_positions SET is_closed = CASE WHEN $quantity = $remaining THEN 1 ELSE 0 END, closed_at_utc = CASE WHEN $quantity = $remaining THEN $exitedAtUtc ELSE NULL END, updated_at_utc = $exitedAtUtc WHERE id = $positionId;";
            update.Parameters.AddWithValue("$quantity", quantity); update.Parameters.AddWithValue("$remaining", existing.RemainingQuantity); update.Parameters.AddWithValue("$exitedAtUtc", exitedAt); update.Parameters.AddWithValue("$positionId", positionId);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (await ReadPositionsAsync(connection, null, accountProfile.Id, positionId, cancellationToken).ConfigureAwait(false)).Single();
    }

    private static async Task ReplaceTargetsAsync(SqliteConnection connection, SqliteTransaction transaction, long positionId, IReadOnlyList<InvestmentTarget> targets, CancellationToken cancellationToken)
    {
        await using (var delete = connection.CreateCommand()) { delete.Transaction = transaction; delete.CommandText = "DELETE FROM investment_position_targets WHERE position_id = $positionId;"; delete.Parameters.AddWithValue("$positionId", positionId); await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
        foreach (var target in targets.OrderBy(target => target.Ordinal))
        {
            await using var insert = connection.CreateCommand(); insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO investment_position_targets (position_id, ordinal, unit_price_in_copper, quantity) VALUES ($positionId, $ordinal, $price, $quantity);";
            insert.Parameters.AddWithValue("$positionId", positionId); insert.Parameters.AddWithValue("$ordinal", target.Ordinal); insert.Parameters.AddWithValue("$price", target.UnitPriceInCopper); insert.Parameters.AddWithValue("$quantity", target.Quantity);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A manual exit consumes the outstanding staged plan in its configured
    /// order. The changed plan is durable, while the exit row remains immutable,
    /// so an already-completed first stage cannot be suggested again after a
    /// restart.
    /// </summary>
    private static async Task ConsumeTargetsAsync(SqliteConnection connection, SqliteTransaction transaction, long positionId, int exitQuantity, CancellationToken cancellationToken)
    {
        var remaining = exitQuantity;
        var targets = await ReadTargetsAsync(connection, transaction, positionId, cancellationToken).ConfigureAwait(false);
        foreach (var target in targets)
        {
            if (remaining == 0) break;
            var consumed = Math.Min(remaining, target.Quantity);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = consumed == target.Quantity
                ? "DELETE FROM investment_position_targets WHERE position_id = $positionId AND ordinal = $ordinal;"
                : "UPDATE investment_position_targets SET quantity = quantity - $consumed WHERE position_id = $positionId AND ordinal = $ordinal;";
            command.Parameters.AddWithValue("$positionId", positionId);
            command.Parameters.AddWithValue("$ordinal", target.Ordinal);
            if (consumed != target.Quantity) command.Parameters.AddWithValue("$consumed", consumed);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            remaining -= consumed;
        }
    }

    private static async Task<IReadOnlyList<InvestmentPosition>> ReadPositionsAsync(SqliteConnection connection, SqliteTransaction? transaction, long profileId, long? positionId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT id, item_id, original_quantity, acquisition_basis_in_copper, strategy, category, opened_at_utc, thesis, notes, is_closed, created_at_utc, updated_at_utc, closed_at_utc FROM investment_positions WHERE account_profile_id = $profileId AND ($positionId IS NULL OR id = $positionId) ORDER BY is_closed, opened_at_utc, id;";
        command.Parameters.AddWithValue("$profileId", profileId); command.Parameters.AddWithValue("$positionId", (object?)positionId ?? DBNull.Value);
        var raw = new List<(long Id, int ItemId, int Quantity, int? Basis, string Strategy, string Category, DateTimeOffset Opened, string Thesis, string? Notes, bool Closed, DateTimeOffset Created, DateTimeOffset Updated, DateTimeOffset? ClosedAt)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) raw.Add((reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.GetString(4), reader.GetString(5), SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(6), "investment_positions.opened_at_utc"), reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetInt32(9) != 0, SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(10), "investment_positions.created_at_utc"), SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(11), "investment_positions.updated_at_utc"), reader.IsDBNull(12) ? null : SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(12), "investment_positions.closed_at_utc")));
        var result = new List<InvestmentPosition>();
        foreach (var row in raw)
        {
            var exits = await ReadExitsAsync(connection, transaction, row.Id, cancellationToken).ConfigureAwait(false);
            var targets = await ReadTargetsAsync(connection, transaction, row.Id, cancellationToken).ConfigureAwait(false);
            result.Add(new InvestmentPosition(row.Id, profileId, row.ItemId, row.Quantity, checked(row.Quantity - exits.Sum(exit => exit.Quantity)), row.Basis, row.Strategy, row.Category, row.Opened, row.Thesis, row.Notes, row.Closed, row.Created, row.Updated, row.ClosedAt, exits, targets));
        }
        return result;
    }

    private static async Task<IReadOnlyList<InvestmentExit>> ReadExitsAsync(SqliteConnection c, SqliteTransaction? t, long id, CancellationToken ct) { await using var x = c.CreateCommand(); x.Transaction = t; x.CommandText = "SELECT id, quantity, allocated_basis_in_copper, exited_at_utc, notes FROM investment_position_exits WHERE position_id = $id ORDER BY exited_at_utc, id;"; x.Parameters.AddWithValue("$id", id); await using var r = await x.ExecuteReaderAsync(ct).ConfigureAwait(false); var v = new List<InvestmentExit>(); while (await r.ReadAsync(ct).ConfigureAwait(false)) v.Add(new(r.GetInt64(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetInt32(2), SqlitePersistenceValues.FromUtcTimestamp(r.GetString(3), "investment_position_exits.exited_at_utc"), r.IsDBNull(4) ? null : r.GetString(4))); return v; }
    private static async Task<IReadOnlyList<InvestmentTarget>> ReadTargetsAsync(SqliteConnection c, SqliteTransaction? t, long id, CancellationToken ct) { await using var x = c.CreateCommand(); x.Transaction = t; x.CommandText = "SELECT ordinal, unit_price_in_copper, quantity FROM investment_position_targets WHERE position_id = $id ORDER BY ordinal;"; x.Parameters.AddWithValue("$id", id); await using var r = await x.ExecuteReaderAsync(ct).ConfigureAwait(false); var v = new List<InvestmentTarget>(); while (await r.ReadAsync(ct).ConfigureAwait(false)) v.Add(new(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2))); return v; }

    private static void AddPositionParameters(SqliteCommand command, long profileId, int itemId, int quantity, int? basis, string strategy, string category, DateTimeOffset opened, string thesis, string? notes, string timestamp) { command.Parameters.AddWithValue("$accountProfileId", profileId); command.Parameters.AddWithValue("$itemId", itemId); command.Parameters.AddWithValue("$quantity", quantity); command.Parameters.AddWithValue("$basis", (object?)basis ?? DBNull.Value); command.Parameters.AddWithValue("$strategy", strategy); command.Parameters.AddWithValue("$category", category); command.Parameters.AddWithValue("$openedAtUtc", SqlitePersistenceValues.ToUtcTimestamp(opened, nameof(opened))); command.Parameters.AddWithValue("$thesis", thesis); command.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value); command.Parameters.AddWithValue("$createdAtUtc", timestamp); command.Parameters.AddWithValue("$updatedAtUtc", timestamp); }
    private static void ValidateCreate(CreateInvestmentPosition p) => Validate(p.ItemId, p.Quantity, p.AcquisitionBasisInCopper, p.Strategy, p.Category, p.OpenedAtUtc, p.Thesis, p.Notes, p.Targets);
    private static void ValidateUpdate(UpdateInvestmentPosition p) => Validate(1, p.Quantity, p.AcquisitionBasisInCopper, p.Strategy, p.Category, p.OpenedAtUtc, p.Thesis, p.Notes, p.Targets);
    private static void Validate(int itemId, int quantity, int? basis, string strategy, string category, DateTimeOffset opened, string thesis, string? notes, IReadOnlyList<InvestmentTarget> targets) { if (itemId <= 0 || quantity <= 0 || basis < 0 || string.IsNullOrWhiteSpace(strategy) || string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(thesis) || strategy.Length > 120 || category.Length > 120 || thesis.Length > 4000 || notes is { Length: > 4000 } || opened.Offset != TimeSpan.Zero || targets is null || targets.Count > 20 || targets.Any(target => target is null || target.Ordinal < 0 || target.UnitPriceInCopper <= 0 || target.Quantity <= 0) || targets.Select(target => target.Ordinal).Distinct().Count() != targets.Count || targets.Sum(target => (long)target.Quantity) > quantity) throw new ArgumentOutOfRangeException(nameof(quantity)); }
}
