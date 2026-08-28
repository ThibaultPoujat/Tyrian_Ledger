namespace Gw2Tp.Application.MarketHistory;

/// <summary>
/// The most recent durable capture instants for one item. A missing instant
/// means that snapshot kind has not yet been collected locally.
/// </summary>
public sealed record MarketSnapshotCollectionState
{
    public MarketSnapshotCollectionState(
        DateTimeOffset? latestPriceCapturedAtUtc,
        DateTimeOffset? latestOrderBookCapturedAtUtc)
    {
        LatestPriceCapturedAtUtc = latestPriceCapturedAtUtc?.ToUniversalTime();
        LatestOrderBookCapturedAtUtc = latestOrderBookCapturedAtUtc?.ToUniversalTime();
    }

    public DateTimeOffset? LatestPriceCapturedAtUtc { get; }

    public DateTimeOffset? LatestOrderBookCapturedAtUtc { get; }
}

/// <summary>
/// Deterministic work that is due at one collection instant. Item IDs are
/// sorted so callers can construct stable, batched gateway requests.
/// </summary>
public sealed record MarketCollectionPlan(
    IReadOnlyList<int> PriceItemIds,
    IReadOnlyList<int> OrderBookItemIds);

/// <summary>
/// Selects due work from the persisted collection state without performing
/// I/O or making assumptions about the upstream API quota.
/// </summary>
public static class MarketCollectionPlanner
{
    public static MarketCollectionPlan CreatePlan(
        IReadOnlyCollection<MarketTrackedItem> trackedItems,
        IReadOnlyDictionary<int, MarketSnapshotCollectionState> collectionStates,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(trackedItems);
        ArgumentNullException.ThrowIfNull(collectionStates);

        var normalizedNowUtc = nowUtc.ToUniversalTime();
        var duePrices = new List<int>();
        var dueOrderBooks = new List<int>();
        var seenItemIds = new HashSet<int>();

        foreach (var item in trackedItems.OrderBy(candidate => candidate.ItemId))
        {
            if (!seenItemIds.Add(item.ItemId))
            {
                throw new ArgumentException(
                    "A collection plan cannot contain the same item more than once.",
                    nameof(trackedItems));
            }

            var schedule = MarketSamplingPolicy.GetSchedule(item.SamplingClass);
            collectionStates.TryGetValue(item.ItemId, out var state);

            if (IsDue(schedule.PriceSnapshotInterval, state?.LatestPriceCapturedAtUtc, normalizedNowUtc))
            {
                duePrices.Add(item.ItemId);
            }

            if (IsDue(schedule.OrderBookSnapshotInterval, state?.LatestOrderBookCapturedAtUtc, normalizedNowUtc))
            {
                dueOrderBooks.Add(item.ItemId);
            }
        }

        return new MarketCollectionPlan(duePrices, dueOrderBooks);
    }

    private static bool IsDue(
        TimeSpan? interval,
        DateTimeOffset? latestCapturedAtUtc,
        DateTimeOffset nowUtc) =>
        interval is not null &&
        (latestCapturedAtUtc is null || latestCapturedAtUtc.Value + interval.Value <= nowUtc);
}
