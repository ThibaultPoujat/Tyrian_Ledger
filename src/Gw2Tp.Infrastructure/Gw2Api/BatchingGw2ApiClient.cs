using Gw2Tp.Application.MarketData;

namespace Gw2Tp.Infrastructure.Gw2Api;

/// <summary>
/// Transport-only public GW2 API contract. All callers use the batching
/// application gateway below rather than constructing requests themselves.
/// </summary>
internal interface IGw2ApiTransport
{
    Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(
        CancellationToken cancellationToken = default);

    Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default);

    Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default);

    Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Application-facing public-market gateway. It batches large reads while
/// retaining no completed response; request de-duplication remains in the
/// transport scheduler for the lifetime of an active outbound request only.
/// </summary>
internal sealed class BatchingGw2ApiClient : IGw2ApiClient
{
    internal const int MaximumBatchSize = 200;

    private readonly IGw2ApiTransport _transport;

    public BatchingGw2ApiClient(IGw2ApiTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
    }

    public Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(
        CancellationToken cancellationToken = default) =>
        _transport.GetPriceItemIdsAsync(cancellationToken);

    public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default) =>
        GetBatchedAsync(itemIds, _transport.GetPricesAsync, static price => price.ItemId, cancellationToken);

    public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default) =>
        GetBatchedAsync(itemIds, _transport.GetListingsAsync, static listing => listing.ItemId, cancellationToken);

    public Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default) =>
        GetBatchedAsync(itemIds, _transport.GetItemMetadataAsync, static item => item.ItemId, cancellationToken);

    private static async Task<Gw2ApiResult<IReadOnlyList<T>>> GetBatchedAsync<T>(
        IReadOnlyCollection<int> itemIds,
        Func<IReadOnlyCollection<int>, CancellationToken, Task<Gw2ApiResult<IReadOnlyList<T>>>> getBatchAsync,
        Func<T, int> getItemId,
        CancellationToken cancellationToken)
    {
        var requestedItemIds = ValidateAndOrderItemIds(itemIds);
        var values = new List<T>(requestedItemIds.Length);

        foreach (var itemIdBatch in requestedItemIds.Chunk(MaximumBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchResult = await getBatchAsync(itemIdBatch, cancellationToken).ConfigureAwait(false);

            if (!batchResult.IsSuccess)
            {
                return Gw2ApiResult<IReadOnlyList<T>>.Failure(batchResult.ErrorCategory!.Value);
            }

            if (batchResult.IsPartialData || batchResult.Value is null ||
                !ContainsExactlyRequestedItemIds(batchResult.Value, itemIdBatch, getItemId))
            {
                return Gw2ApiResult<IReadOnlyList<T>>.Failure(Gw2ApiErrorCategory.IncompleteData);
            }

            values.AddRange(batchResult.Value);
        }

        return Gw2ApiResult<IReadOnlyList<T>>.Success(
            values.OrderBy(getItemId).ToArray());
    }

    private static int[] ValidateAndOrderItemIds(IReadOnlyCollection<int> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        if (itemIds.Count == 0)
        {
            throw new ArgumentException("At least one item ID is required.", nameof(itemIds));
        }

        var orderedItemIds = itemIds.OrderBy(itemId => itemId).ToArray();
        if (orderedItemIds.Any(itemId => itemId <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(itemIds), "Item IDs must be positive.");
        }

        if (orderedItemIds.Distinct().Count() != orderedItemIds.Length)
        {
            throw new ArgumentException("Item IDs must be unique.", nameof(itemIds));
        }

        return orderedItemIds;
    }

    private static bool ContainsExactlyRequestedItemIds<T>(
        IReadOnlyList<T> values,
        IReadOnlyCollection<int> requestedItemIds,
        Func<T, int> getItemId)
    {
        if (values.Count != requestedItemIds.Count)
        {
            return false;
        }

        var requested = requestedItemIds.ToHashSet();
        var received = new HashSet<int>();
        foreach (var value in values)
        {
            var itemId = getItemId(value);
            if (!received.Add(itemId) || !requested.Contains(itemId))
            {
                return false;
            }
        }

        return received.SetEquals(requested);
    }
}
