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
/// Explicit, versioned retention intent for one class of owned market
/// evidence. Version one is deliberately non-destructive: measured storage
/// informs a later owner-approved migration rather than silently replacing or
/// deleting raw observations.
/// </summary>
public sealed record MarketHistoryRetentionPolicy(
    int Version,
    MarketHistoryEvidenceKind EvidenceKind,
    MarketHistoryRetentionMode Mode);

public enum MarketHistoryEvidenceKind
{
    AggregatePrices = 1,
    DetailedOrderBooks = 2,
}

public enum MarketHistoryRetentionMode
{
    PreserveAllRawEvidence = 1,
}

public static class MarketHistoryRetentionPolicies
{
    public const int CurrentVersion = 1;

    public static readonly IReadOnlyList<MarketHistoryRetentionPolicy> Current =
    [
        new(CurrentVersion, MarketHistoryEvidenceKind.AggregatePrices, MarketHistoryRetentionMode.PreserveAllRawEvidence),
        new(CurrentVersion, MarketHistoryEvidenceKind.DetailedOrderBooks, MarketHistoryRetentionMode.PreserveAllRawEvidence),
    ];
}

/// <summary>
/// An optional item and UTC time-window filter for read-only market-history
/// coverage. A null value for every member represents all retained history.
/// </summary>
public sealed record MarketHistoryCoverageQuery(
    int? ItemId,
    DateTimeOffset? FromInclusiveUtc,
    DateTimeOffset? ToInclusiveUtc)
{
    public void Validate()
    {
        if (ItemId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ItemId));
        }

        if ((FromInclusiveUtc is null) != (ToInclusiveUtc is null))
        {
            throw new ArgumentException("Market-history coverage timestamps must be supplied together.");
        }

        if (FromInclusiveUtc is { } from && ToInclusiveUtc is { } to)
        {
            if (from.Offset != TimeSpan.Zero || to.Offset != TimeSpan.Zero || from > to)
            {
                throw new ArgumentOutOfRangeException(nameof(ToInclusiveUtc));
            }
        }
    }
}

public sealed record MarketHistoryCoverage(
    long AggregateObservationCount,
    DateTimeOffset? AggregateFirstObservedAtUtc,
    DateTimeOffset? AggregateLastObservedAtUtc,
    long DetailedBookSnapshotCount,
    long DetailedBookLevelCount,
    DateTimeOffset? DetailedBookFirstObservedAtUtc,
    DateTimeOffset? DetailedBookLastObservedAtUtc);

public enum MarketHistoryIntegrityState
{
    Passed = 1,
    Failed = 2,
}

/// <summary>
/// Safe local history-governance data. It contains neither account data nor
/// upstream payloads and never changes retained market evidence.
/// </summary>
public sealed record MarketHistoryStatus(
    IReadOnlyList<MarketHistoryRetentionPolicy> RetentionPolicies,
    long DatabaseFileBytes,
    MarketHistoryCoverage Coverage,
    MarketHistoryIntegrityState IntegrityState);

public interface IMarketHistoryStatusService
{
    Task<MarketHistoryStatus> GetStatusAsync(
        MarketHistoryCoverageQuery query,
        CancellationToken cancellationToken = default);
}

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

    /// <summary>
    /// Appends a validated collection of immutable aggregate observations in a
    /// single durable operation. A duplicate never authorizes an overwrite or
    /// a partial batch commit.
    /// </summary>
    Task AppendPriceObservationsAsync(
        IReadOnlyCollection<MarketPriceObservation> observations,
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
    /// Gets the newest raw aggregate observation for each requested item.
    /// Collection uses this one batched lookup to resume the configured
    /// sampling cadence after a restart; it never changes or interprets the
    /// retained raw evidence.
    /// </summary>
    Task<IReadOnlyDictionary<int, MarketPriceObservation>> GetLatestPriceObservationsAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default);
}
