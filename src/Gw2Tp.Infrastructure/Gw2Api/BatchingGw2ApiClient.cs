using Gw2Tp.Application.MarketData;
using Gw2Tp.Infrastructure.Diagnostics;
using System.Diagnostics;

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
    private readonly SafeTransportDiagnosticBuffer? _diagnostics;

    public BatchingGw2ApiClient(
        IGw2ApiTransport transport,
        SafeTransportDiagnosticBuffer? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        _diagnostics = diagnostics;
    }

    public Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(
        CancellationToken cancellationToken = default) =>
        _transport.GetPriceItemIdsAsync(cancellationToken);

    public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default) =>
        GetBatchedAsync("commerce/prices", itemIds, _transport.GetPricesAsync, static price => price.ItemId, cancellationToken);

    public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default) =>
        GetBatchedAsync("commerce/listings", itemIds, _transport.GetListingsAsync, static listing => listing.ItemId, cancellationToken);

    public Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default) =>
        GetBatchedAsync("items", itemIds, _transport.GetItemMetadataAsync, static item => item.ItemId, cancellationToken);

    private async Task<Gw2ApiResult<IReadOnlyList<T>>> GetBatchedAsync<T>(
        string operation,
        IReadOnlyCollection<int> itemIds,
        Func<IReadOnlyCollection<int>, CancellationToken, Task<Gw2ApiResult<IReadOnlyList<T>>>> getBatchAsync,
        Func<T, int> getItemId,
        CancellationToken cancellationToken)
    {
        var requestedItemIds = ValidateAndOrderItemIds(itemIds);
        var values = new List<T>(requestedItemIds.Length);
        var batchCount = (requestedItemIds.Length + MaximumBatchSize - 1) / MaximumBatchSize;
        var batchIndex = 0;

        foreach (var itemIdBatch in requestedItemIds.Chunk(MaximumBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            batchIndex++;
            var timer = Stopwatch.StartNew();
            var batchResult = await getBatchAsync(itemIdBatch, cancellationToken).ConfigureAwait(false);
            timer.Stop();

            if (!batchResult.IsSuccess)
            {
                RecordBatch(operation, batchIndex, batchCount, itemIdBatch.Length, null, null, null, null,
                    batchResult.IsPartialData, batchResult.ErrorCategory, timer.Elapsed);
                return Gw2ApiResult<IReadOnlyList<T>>.Failure(batchResult.ErrorCategory!.Value);
            }

            if (batchResult.IsPartialData || batchResult.Value is null)
            {
                RecordBatch(operation, batchIndex, batchCount, itemIdBatch.Length, batchResult.Value?.Count, null, null, null,
                    batchResult.IsPartialData, Gw2ApiErrorCategory.IncompleteData, timer.Elapsed);
                return Gw2ApiResult<IReadOnlyList<T>>.Failure(Gw2ApiErrorCategory.IncompleteData);
            }

            var discrepancy = DescribeDiscrepancy(batchResult.Value, itemIdBatch, getItemId);
            if (discrepancy.MissingCount > 0 || discrepancy.UnexpectedCount > 0 || discrepancy.DuplicateCount > 0)
            {
                RecordBatch(operation, batchIndex, batchCount, itemIdBatch.Length, batchResult.Value.Count,
                    discrepancy.MissingCount, discrepancy.UnexpectedCount, discrepancy.DuplicateCount, false,
                    Gw2ApiErrorCategory.IncompleteData, timer.Elapsed);
                return Gw2ApiResult<IReadOnlyList<T>>.Failure(Gw2ApiErrorCategory.IncompleteData);
            }

            RecordBatch(operation, batchIndex, batchCount, itemIdBatch.Length, batchResult.Value.Count,
                0, 0, 0, false, null, timer.Elapsed);

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

    private static BatchDiscrepancy DescribeDiscrepancy<T>(
        IReadOnlyList<T> values,
        IReadOnlyCollection<int> requestedItemIds,
        Func<T, int> getItemId)
    {
        var requested = requestedItemIds.ToHashSet();
        var received = new HashSet<int>();
        var unexpected = 0;
        var duplicates = 0;
        foreach (var value in values)
        {
            var itemId = getItemId(value);
            if (!requested.Contains(itemId)) unexpected++;
            else if (!received.Add(itemId)) duplicates++;
        }

        var missing = requested.Where(itemId => !received.Contains(itemId)).OrderBy(itemId => itemId).ToArray();
        return new(missing, unexpected, duplicates);
    }

    private void RecordBatch(
        string operation,
        int batchIndex,
        int batchCount,
        int requestedItemIdCount,
        int? responseItemIdCount,
        int? missingItemIdCount,
        int? unexpectedItemIdCount,
        int? duplicateItemIdCount,
        bool isPartialResponse,
        Gw2ApiErrorCategory? errorCategory,
        TimeSpan elapsed) =>
        _diagnostics?.RecordMarketBatch(
            operation,
            batchIndex,
            batchCount,
            requestedItemIdCount,
            responseItemIdCount,
            missingItemIdCount,
            unexpectedItemIdCount,
            duplicateItemIdCount,
            isPartialResponse,
            errorCategory,
            elapsed);

    private sealed record BatchDiscrepancy(
        IReadOnlyList<int> MissingItemIds,
        int UnexpectedCount,
        int DuplicateCount)
    {
        public int MissingCount => MissingItemIds.Count;
    }
}
