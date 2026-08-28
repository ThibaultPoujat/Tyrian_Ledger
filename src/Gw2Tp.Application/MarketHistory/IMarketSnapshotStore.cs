namespace Gw2Tp.Application.MarketHistory;

/// <summary>
/// Durable, append-only local storage for public market observations.
/// </summary>
public interface IMarketSnapshotStore
{
    Task AppendAsync(MarketPriceSnapshot snapshot, CancellationToken cancellationToken);

    Task AppendAsync(MarketOrderBookSnapshot snapshot, CancellationToken cancellationToken);

    Task<IReadOnlyList<MarketPriceSnapshot>> ListPriceSnapshotsAsync(
        int itemId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MarketOrderBookSnapshot>> ListOrderBookSnapshotsAsync(
        int itemId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns only the latest capture timestamps needed to determine which
    /// explicitly tracked items are due for their next collection cycle.
    /// </summary>
    Task<IReadOnlyDictionary<int, MarketSnapshotCollectionState>> GetCollectionStatesAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken);
}
