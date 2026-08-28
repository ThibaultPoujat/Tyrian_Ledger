namespace Gw2Tp.Application.MarketHistory;

/// <summary>
/// Local, user-controlled item selection for future historical collection.
/// </summary>
public interface IMarketWatchlistStore
{
    Task<IReadOnlyList<MarketTrackedItem>> ListAsync(CancellationToken cancellationToken);

    Task AddAsync(MarketTrackedItem item, CancellationToken cancellationToken);

    Task UpdateSamplingClassAsync(
        int itemId,
        MarketSamplingClass samplingClass,
        CancellationToken cancellationToken);

    Task RemoveAsync(int itemId, CancellationToken cancellationToken);
}

/// <summary>
/// One explicitly selected public-market item. Untracked items are deliberately
/// absent from local persistence rather than stored as a third watchlist value.
/// </summary>
public sealed record MarketTrackedItem
{
    public MarketTrackedItem(int itemId, MarketSamplingClass samplingClass)
    {
        if (itemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), "A market watchlist item ID must be positive.");
        }

        if (samplingClass is not (MarketSamplingClass.Watchlist or MarketSamplingClass.Background))
        {
            throw new ArgumentOutOfRangeException(
                nameof(samplingClass),
                samplingClass,
                "Only watchlist and background items may be persisted for historical collection.");
        }

        ItemId = itemId;
        SamplingClass = samplingClass;
    }

    public int ItemId { get; }

    public MarketSamplingClass SamplingClass { get; }
}
