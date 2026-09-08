using Gw2Tp.Application.MarketHistory;
using Microsoft.Data.Sqlite;

namespace Gw2Tp.Infrastructure.Persistence;

internal sealed class SqliteMarketHistoryRepository(
    ISqliteConnectionFactory connectionFactory,
    ISqliteDatabaseGate? databaseGate = null) : IMarketHistoryRepository
{
    private readonly ISqliteDatabaseGate gate = databaseGate ?? new SqliteDatabaseGate();

    public async Task AppendPriceObservationAsync(
        MarketPriceObservation observation,
        CancellationToken cancellationToken = default)
    {
        Validate(observation);
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO market_price_observations (
                observed_at_utc, item_id, highest_buy_price_in_copper, lowest_sell_price_in_copper,
                aggregate_buy_quantity, aggregate_sell_quantity, source_status, sampling_tier, sampling_policy_version)
            VALUES (
                $observedAtUtc, $itemId, $highestBuyPriceInCopper, $lowestSellPriceInCopper,
                $aggregateBuyQuantity, $aggregateSellQuantity, $sourceStatus, $samplingTier, $samplingPolicyVersion);
            """;
        Bind(command, observation);
        await ExecuteAppendAsync(command, "market price observation", cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendOrderBookSnapshotAsync(
        MarketOrderBookSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        Validate(snapshot);
        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var snapshotCommand = connection.CreateCommand();
        snapshotCommand.Transaction = transaction;
        snapshotCommand.CommandText = """
            INSERT INTO market_order_book_snapshots (
                observed_at_utc, item_id, source_status, sampling_tier, sampling_policy_version)
            VALUES ($observedAtUtc, $itemId, $sourceStatus, $samplingTier, $samplingPolicyVersion);
            SELECT last_insert_rowid();
            """;
        snapshotCommand.Parameters.AddWithValue("$observedAtUtc", SqlitePersistenceValues.ToUtcTimestamp(snapshot.ObservedAtUtc, nameof(snapshot.ObservedAtUtc)));
        snapshotCommand.Parameters.AddWithValue("$itemId", snapshot.ItemId);
        snapshotCommand.Parameters.AddWithValue("$sourceStatus", (int)snapshot.SourceStatus);
        snapshotCommand.Parameters.AddWithValue("$samplingTier", (int)snapshot.SamplingTier);
        snapshotCommand.Parameters.AddWithValue("$samplingPolicyVersion", snapshot.SamplingPolicyVersion);

        long snapshotId;
        try
        {
            snapshotId = Convert.ToInt64(await snapshotCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("A market order-book snapshot already exists for this item and observation time.", exception);
        }

        foreach (var level in snapshot.Levels)
        {
            await using var levelCommand = connection.CreateCommand();
            levelCommand.Transaction = transaction;
            levelCommand.CommandText = """
                INSERT INTO market_order_book_levels (
                    snapshot_id, side, level_ordinal, unit_price_in_copper, quantity, listings)
                VALUES ($snapshotId, $side, $levelOrdinal, $unitPriceInCopper, $quantity, $listings);
                """;
            levelCommand.Parameters.AddWithValue("$snapshotId", snapshotId);
            levelCommand.Parameters.AddWithValue("$side", (int)level.Side);
            levelCommand.Parameters.AddWithValue("$levelOrdinal", level.LevelOrdinal);
            levelCommand.Parameters.AddWithValue("$unitPriceInCopper", level.UnitPriceInCopper);
            levelCommand.Parameters.AddWithValue("$quantity", level.Quantity);
            levelCommand.Parameters.AddWithValue("$listings", level.Listings);
            await levelCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MarketPriceObservation>> GetPriceObservationsAsync(
        int itemId,
        DateTimeOffset fromInclusiveUtc,
        DateTimeOffset toInclusiveUtc,
        CancellationToken cancellationToken = default)
    {
        if (itemId <= 0) throw new ArgumentOutOfRangeException(nameof(itemId));
        var from = SqlitePersistenceValues.ToUtcTimestamp(fromInclusiveUtc, nameof(fromInclusiveUtc));
        var to = SqlitePersistenceValues.ToUtcTimestamp(toInclusiveUtc, nameof(toInclusiveUtc));
        if (fromInclusiveUtc > toInclusiveUtc) throw new ArgumentOutOfRangeException(nameof(toInclusiveUtc));

        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT observed_at_utc, item_id, highest_buy_price_in_copper, lowest_sell_price_in_copper,
                   aggregate_buy_quantity, aggregate_sell_quantity, source_status, sampling_tier, sampling_policy_version
            FROM market_price_observations
            WHERE item_id = $itemId AND observed_at_utc >= $fromInclusiveUtc AND observed_at_utc <= $toInclusiveUtc
            ORDER BY observed_at_utc, item_id;
            """;
        command.Parameters.AddWithValue("$itemId", itemId);
        command.Parameters.AddWithValue("$fromInclusiveUtc", from);
        command.Parameters.AddWithValue("$toInclusiveUtc", to);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var observations = new List<MarketPriceObservation>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            observations.Add(new MarketPriceObservation(
                SqlitePersistenceValues.FromUtcTimestamp(reader.GetString(0), "market_price_observations.observed_at_utc"),
                reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5),
                (MarketObservationSourceStatus)reader.GetInt32(6), (MarketSamplingTier)reader.GetInt32(7), reader.GetInt32(8)));
        }

        return observations;
    }

    private static void Bind(SqliteCommand command, MarketPriceObservation observation)
    {
        command.Parameters.AddWithValue("$observedAtUtc", SqlitePersistenceValues.ToUtcTimestamp(observation.ObservedAtUtc, nameof(observation.ObservedAtUtc)));
        command.Parameters.AddWithValue("$itemId", observation.ItemId);
        command.Parameters.AddWithValue("$highestBuyPriceInCopper", observation.HighestBuyPriceInCopper);
        command.Parameters.AddWithValue("$lowestSellPriceInCopper", observation.LowestSellPriceInCopper);
        command.Parameters.AddWithValue("$aggregateBuyQuantity", observation.AggregateBuyQuantity);
        command.Parameters.AddWithValue("$aggregateSellQuantity", observation.AggregateSellQuantity);
        command.Parameters.AddWithValue("$sourceStatus", (int)observation.SourceStatus);
        command.Parameters.AddWithValue("$samplingTier", (int)observation.SamplingTier);
        command.Parameters.AddWithValue("$samplingPolicyVersion", observation.SamplingPolicyVersion);
    }

    private static async Task ExecuteAppendAsync(SqliteCommand command, string entityName, CancellationToken cancellationToken)
    {
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException($"A {entityName} already exists for this item and observation time.", exception);
        }
    }

    private static void Validate(MarketPriceObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ValidateCommon(observation.ObservedAtUtc, observation.ItemId, observation.SourceStatus, observation.SamplingTier, observation.SamplingPolicyVersion);
        if (observation.HighestBuyPriceInCopper < 0 || observation.LowestSellPriceInCopper < 0 ||
            observation.AggregateBuyQuantity < 0 || observation.AggregateSellQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(observation));
        }
    }

    private static void Validate(MarketOrderBookSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Levels);
        ValidateCommon(snapshot.ObservedAtUtc, snapshot.ItemId, snapshot.SourceStatus, snapshot.SamplingTier, snapshot.SamplingPolicyVersion);
        var seenLevels = new HashSet<(MarketOrderBookSide Side, int Ordinal)>();
        foreach (var level in snapshot.Levels)
        {
            ArgumentNullException.ThrowIfNull(level);
            if (!Enum.IsDefined(level.Side) || level.LevelOrdinal < 0 || level.UnitPriceInCopper < 0 || level.Quantity <= 0 || level.Listings <= 0 || !seenLevels.Add((level.Side, level.LevelOrdinal)))
            {
                throw new ArgumentOutOfRangeException(nameof(snapshot));
            }
        }
    }

    private static void ValidateCommon(DateTimeOffset observedAtUtc, int itemId, MarketObservationSourceStatus sourceStatus, MarketSamplingTier tier, int policyVersion)
    {
        _ = SqlitePersistenceValues.ToUtcTimestamp(observedAtUtc, nameof(observedAtUtc));
        if (itemId <= 0 || policyVersion <= 0 || sourceStatus != MarketObservationSourceStatus.Complete || !Enum.IsDefined(tier))
        {
            throw new ArgumentOutOfRangeException(nameof(itemId));
        }
    }
}
