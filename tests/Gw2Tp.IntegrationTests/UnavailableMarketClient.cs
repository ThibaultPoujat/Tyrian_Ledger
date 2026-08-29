using Gw2Tp.Application.MarketData;

namespace Gw2Tp.IntegrationTests;

/// <summary>
/// Keeps endpoint test hosts independent from the live public-market gateway.
/// Individual tests that exercise market behaviour replace this with a recording client.
/// </summary>
internal sealed class UnavailableMarketClient : IGw2ApiClient
{
    public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Gw2ApiResult<IReadOnlyList<MarketPrice>>.Failure(Gw2ApiErrorCategory.UpstreamUnavailable));

    public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Gw2ApiResult<IReadOnlyList<MarketListing>>.Failure(Gw2ApiErrorCategory.UpstreamUnavailable));
}
