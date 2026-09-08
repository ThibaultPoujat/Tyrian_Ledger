namespace Gw2Tp.Application.MarketHistory;

/// <summary>
/// Collection priority for locally owned market evidence. Lower numeric values
/// are more frequent and win when an item is supplied by multiple sources.
/// </summary>
public enum MarketSamplingTier
{
    CurrentPersonalOrder = 1,
    Watchlist = 2,
    BroadMarket = 3,
}

/// <summary>
/// The completeness/freshness classification supplied by a collector. M18 only
/// permits complete, directly observed public-market responses to be persisted.
/// </summary>
public enum MarketObservationSourceStatus
{
    Complete = 1,
}

public enum MarketOrderBookSide
{
    Buy = 1,
    Sell = 2,
}

/// <summary>
/// One immutable aggregate top-of-book observation. All money is integer copper
/// and the observation timestamp must be UTC.
/// </summary>
public sealed record MarketPriceObservation(
    DateTimeOffset ObservedAtUtc,
    int ItemId,
    int HighestBuyPriceInCopper,
    int LowestSellPriceInCopper,
    int AggregateBuyQuantity,
    int AggregateSellQuantity,
    MarketObservationSourceStatus SourceStatus,
    MarketSamplingTier SamplingTier,
    int SamplingPolicyVersion);

/// <summary>
/// One explicitly requested full order-book capture. It is deliberately a
/// separate record from cheap aggregate observations because its storage cost is
/// materially higher.
/// </summary>
public sealed record MarketOrderBookSnapshot(
    DateTimeOffset ObservedAtUtc,
    int ItemId,
    MarketObservationSourceStatus SourceStatus,
    MarketSamplingTier SamplingTier,
    int SamplingPolicyVersion,
    IReadOnlyList<MarketOrderBookLevel> Levels);

public sealed record MarketOrderBookLevel(
    MarketOrderBookSide Side,
    int LevelOrdinal,
    int UnitPriceInCopper,
    int Quantity,
    int Listings);

/// <summary>
/// Persistence boundary for raw public-market evidence. Implementations append
/// immutable observations and never interpret a duplicate as permission to
/// overwrite prior history.
/// </summary>
public interface IMarketHistoryRepository
{
    Task AppendPriceObservationAsync(
        MarketPriceObservation observation,
        CancellationToken cancellationToken = default);

    Task AppendOrderBookSnapshotAsync(
        MarketOrderBookSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MarketPriceObservation>> GetPriceObservationsAsync(
        int itemId,
        DateTimeOffset fromInclusiveUtc,
        DateTimeOffset toInclusiveUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the newest raw aggregate observation for one item. Collection uses
    /// this only to resume the configured sampling cadence after a restart; it
    /// never changes or interprets the retained raw evidence.
    /// </summary>
    Task<MarketPriceObservation?> GetLatestPriceObservationAsync(
        int itemId,
        CancellationToken cancellationToken = default);
}
