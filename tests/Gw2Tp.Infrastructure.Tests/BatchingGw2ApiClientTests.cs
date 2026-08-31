using Gw2Tp.Application.MarketData;
using Gw2Tp.Infrastructure.Gw2Api;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class BatchingGw2ApiClientTests
{
    [Fact]
    public async Task Price_reads_are_sorted_and_split_at_the_conservative_two_hundred_id_boundary()
    {
        var transport = new StubMarketTransport(
            getPricesAsync: (itemIds, _) => Task.FromResult(PricesFor(itemIds)));
        var client = new BatchingGw2ApiClient(transport);

        var result = await client.GetPricesAsync(Enumerable.Range(1, 201).Reverse().ToArray());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, transport.PriceRequestCount);
        Assert.Equal(Enumerable.Range(1, 200), transport.PriceRequests[0]);
        Assert.Equal([201], transport.PriceRequests[1]);
        Assert.Equal(Enumerable.Range(1, 201), result.Value!.Select(price => price.ItemId));
    }

    [Theory]
    [InlineData(BatchShape.Partial)]
    [InlineData(BatchShape.Missing)]
    [InlineData(BatchShape.Duplicate)]
    public async Task Incomplete_finalist_batches_return_no_usable_data(BatchShape batchShape)
    {
        var transport = new StubMarketTransport(
            getPricesAsync: (itemIds, _) => Task.FromResult(batchShape switch
            {
                BatchShape.Partial => Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success(
                    PricesFor(itemIds).Value!,
                    isPartialData: true),
                BatchShape.Missing => Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success(
                    PricesFor(itemIds.Take(itemIds.Count - 1)).Value!),
                BatchShape.Duplicate => Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success(
                    [CreatePrice(itemIds.First()), CreatePrice(itemIds.First())]),
                _ => throw new ArgumentOutOfRangeException(nameof(batchShape)),
            }));
        var client = new BatchingGw2ApiClient(transport);

        var result = await client.GetPricesAsync([900001, 900002]);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(Gw2ApiErrorCategory.IncompleteData, result.ErrorCategory);
    }

    [Fact]
    public async Task Transport_failures_stop_remaining_batches_and_preserve_the_stable_category()
    {
        var transport = new StubMarketTransport(
            getPricesAsync: (_, _) => Task.FromResult(
                Gw2ApiResult<IReadOnlyList<MarketPrice>>.Failure(Gw2ApiErrorCategory.RateLimited)));
        var client = new BatchingGw2ApiClient(transport);

        var result = await client.GetPricesAsync(Enumerable.Range(1, 201).ToArray());

        Assert.False(result.IsSuccess);
        Assert.Equal(Gw2ApiErrorCategory.RateLimited, result.ErrorCategory);
        Assert.Equal(1, transport.PriceRequestCount);
    }

    [Fact]
    public async Task Caller_cancellation_stops_a_multi_batch_read_without_a_failure_result()
    {
        var secondBatchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new StubMarketTransport(
            getPricesAsync: async (itemIds, cancellationToken) =>
            {
                if (itemIds.First() == 1)
                {
                    return PricesFor(itemIds);
                }

                secondBatchStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return PricesFor(itemIds);
            });
        var client = new BatchingGw2ApiClient(transport);
        using var cancellationSource = new CancellationTokenSource();

        var operation = client.GetPricesAsync(Enumerable.Range(1, 201).ToArray(), cancellationSource.Token);
        await secondBatchStarted.Task;
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    [Fact]
    public async Task Completed_market_responses_are_not_retained_between_calls()
    {
        var transport = new StubMarketTransport(
            getPricesAsync: (itemIds, _) => Task.FromResult(PricesFor(itemIds)));
        var client = new BatchingGw2ApiClient(transport);

        await client.GetPricesAsync([900001]);
        await client.GetPricesAsync([900001]);

        Assert.Equal(2, transport.PriceRequestCount);
    }

    [Fact]
    public async Task Item_metadata_preserves_the_owner_selected_normal_stack_policy()
    {
        var transport = new StubMarketTransport(
            getItemMetadataAsync: (itemIds, _) => Task.FromResult(
                Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>.Success(
                [
                    .. itemIds.Select(itemId => new MarketItemMetadata(
                        itemId,
                        $"Synthetic item {itemId}",
                        MarketItemStackPolicy.NormalStackLimit)),
                ])));
        var client = new BatchingGw2ApiClient(transport);

        var result = await client.GetItemMetadataAsync([900002, 900001]);

        var metadata = Assert.IsAssignableFrom<IReadOnlyList<MarketItemMetadata>>(result.Value);
        Assert.Equal([900001, 900002], metadata.Select(item => item.ItemId));
        Assert.All(metadata, item => Assert.Equal(250, item.NormalStackLimit));
    }

    [Fact]
    public async Task Invalid_or_duplicate_requested_ids_are_rejected_before_transport_access()
    {
        var transport = new StubMarketTransport(
            getPricesAsync: (itemIds, _) => Task.FromResult(PricesFor(itemIds)));
        var client = new BatchingGw2ApiClient(transport);

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetPricesAsync([900001, 900001]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetPricesAsync([0]));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetPricesAsync([]));

        Assert.Equal(0, transport.PriceRequestCount);
    }

    private static Gw2ApiResult<IReadOnlyList<MarketPrice>> PricesFor(
        IEnumerable<int> itemIds) =>
        Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success(
        [
            .. itemIds.Select(CreatePrice),
        ]);

    private static MarketPrice CreatePrice(int itemId) =>
        new(
            itemId,
            IsWhitelisted: false,
            new MarketOrderSummary(100, 10),
            new MarketOrderSummary(100, 20));

    public enum BatchShape
    {
        Partial,
        Missing,
        Duplicate,
    }

    private sealed class StubMarketTransport : IGw2ApiTransport
    {
        private readonly Func<IReadOnlyCollection<int>, CancellationToken, Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>>>
            _getPricesAsync;
        private readonly Func<IReadOnlyCollection<int>, CancellationToken, Task<Gw2ApiResult<IReadOnlyList<MarketListing>>>>
            _getListingsAsync;
        private readonly Func<IReadOnlyCollection<int>, CancellationToken, Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>>>
            _getItemMetadataAsync;

        public StubMarketTransport(
            Func<IReadOnlyCollection<int>, CancellationToken, Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>>>? getPricesAsync = null,
            Func<IReadOnlyCollection<int>, CancellationToken, Task<Gw2ApiResult<IReadOnlyList<MarketListing>>>>? getListingsAsync = null,
            Func<IReadOnlyCollection<int>, CancellationToken, Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>>>? getItemMetadataAsync = null)
        {
            _getPricesAsync = getPricesAsync ?? ((itemIds, _) => Task.FromResult(PricesFor(itemIds)));
            _getListingsAsync = getListingsAsync ?? ((_, _) => Task.FromResult(
                Gw2ApiResult<IReadOnlyList<MarketListing>>.Success([])));
            _getItemMetadataAsync = getItemMetadataAsync ?? ((_, _) => Task.FromResult(
                Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>.Success([])));
        }

        public int PriceRequestCount => PriceRequests.Count;

        public List<IReadOnlyCollection<int>> PriceRequests { get; } = [];

        public Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<IReadOnlyList<int>>.Success([900001]));

        public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
            IReadOnlyCollection<int> itemIds,
            CancellationToken cancellationToken = default)
        {
            PriceRequests.Add(itemIds.ToArray());
            return _getPricesAsync(itemIds, cancellationToken);
        }

        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
            IReadOnlyCollection<int> itemIds,
            CancellationToken cancellationToken = default) =>
            _getListingsAsync(itemIds, cancellationToken);

        public Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(
            IReadOnlyCollection<int> itemIds,
            CancellationToken cancellationToken = default) =>
            _getItemMetadataAsync(itemIds, cancellationToken);
    }
}
