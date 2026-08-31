namespace Gw2Tp.Application.MarketData;

/// <summary>
/// Read-only access to public Guild Wars 2 market data.
/// </summary>
public interface IGw2ApiClient
{
    /// <summary>
    /// Gets the public item IDs that currently have aggregate commerce prices.
    /// </summary>
    Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(
        CancellationToken cancellationToken = default);

    Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default);

    Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets display metadata for public-market finalists.
    /// </summary>
    Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default);
}
