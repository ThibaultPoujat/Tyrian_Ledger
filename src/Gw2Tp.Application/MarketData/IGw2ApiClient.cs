namespace Gw2Tp.Application.MarketData;

/// <summary>
/// Read-only access to public Guild Wars 2 market data.
/// </summary>
public interface IGw2ApiClient
{
    Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default);

    Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default);
}
