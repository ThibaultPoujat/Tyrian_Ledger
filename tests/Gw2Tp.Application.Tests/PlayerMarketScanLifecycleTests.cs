using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Preferences;
using Gw2Tp.Application.Recommendations;
using Gw2Tp.Application.Scans;
using Gw2Tp.Domain.Finance;
using Gw2Tp.Testing;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class PlayerMarketScanLifecycleTests
{
    private static readonly DateTimeOffset ScanTime = new(2026, 8, 31, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Complete_scan_follows_the_bounded_sequence_and_publishes_grouped_results_only_at_completion()
    {
        var client = new StubMarketDataClient();
        var lifecycle = CreateLifecycle(client);

        Assert.True(lifecycle.TryStart(Request(), out var started));
        Assert.Equal(PlayerMarketScanState.Running, started.State);
        Assert.Null(started.Result);

        var completed = await WaitForTerminalAsync(lifecycle);

        Assert.Equal(PlayerMarketScanState.Complete, completed.State);
        var result = Assert.IsType<BeginnerRecommendationResult>(completed.Result);
        Assert.Equal(ScanTime, result.ScanCompletedAtUtc);
        Assert.Single(result.PlaceOrderAndWait);
        Assert.Empty(result.CanActNow);
        Assert.Equal(["price-item-ids", "prices", "listings", "metadata"], client.Calls);
        Assert.Single(client.ListingRequests);
        Assert.Single(client.MetadataRequests);
    }

    [Fact]
    public async Task Empty_eligible_set_completes_without_detailed_market_reads()
    {
        var client = new StubMarketDataClient(
            prices: itemIds => Success(itemIds.Select(itemId => new MarketPrice(
                itemId,
                IsWhitelisted: false,
                new MarketOrderSummary(0, 0),
                new MarketOrderSummary(0, 0)))));
        var lifecycle = CreateLifecycle(client);

        Assert.True(lifecycle.TryStart(Request(), out _));
        var completed = await WaitForTerminalAsync(lifecycle);

        Assert.Equal(PlayerMarketScanState.Complete, completed.State);
        Assert.Empty(Assert.IsType<BeginnerRecommendationResult>(completed.Result).Recommendations);
        Assert.Equal(["price-item-ids", "prices"], client.Calls);
        Assert.Empty(client.ListingRequests);
        Assert.Empty(client.MetadataRequests);
    }

    [Fact]
    public async Task Screening_selects_at_most_two_hundred_finalists_by_gap_then_item_id_when_depth_is_equal()
    {
        var itemIds = Enumerable.Range(1, 205).Reverse().ToArray();
        var client = new StubMarketDataClient(
            itemIds: itemIds,
            prices: ids => Success(ids.Select(itemId => new MarketPrice(
                itemId,
                IsWhitelisted: false,
                new MarketOrderSummary(10, 2_000),
                new MarketOrderSummary(10, 3_000 + itemId)))));
        var lifecycle = CreateLifecycle(client);

        Assert.True(lifecycle.TryStart(Request(), out _));
        var completed = await WaitForTerminalAsync(lifecycle);

        Assert.Equal(PlayerMarketScanState.Complete, completed.State);
        var finalists = Assert.Single(client.ListingRequests);
        Assert.Equal(PlayerMarketScanLifecycle.MaximumFinalistCount, finalists.Count);
        Assert.Equal(205, finalists[0]);
        Assert.Equal(6, finalists[^1]);
        Assert.Equal(finalists, Assert.Single(client.MetadataRequests));
    }

    [Fact]
    public async Task Screening_requires_balanced_aggregate_depth_and_planned_price_spread()
    {
        var client = new StubMarketDataClient(
            itemIds: [1, 2, 3],
            prices: _ => Success(
            [
                new MarketPrice(1, false, new MarketOrderSummary(10, 999), new MarketOrderSummary(10, 2_001)),
                new MarketPrice(2, false, new MarketOrderSummary(9, 999), new MarketOrderSummary(10, 2_001)),
                new MarketPrice(3, false, new MarketOrderSummary(10, 999), new MarketOrderSummary(10, 2_002)),
            ]));
        var lifecycle = CreateLifecycle(client);

        Assert.True(lifecycle.TryStart(Request(), out _));
        var completed = await WaitForTerminalAsync(lifecycle);

        Assert.Equal(PlayerMarketScanState.Complete, completed.State);
        Assert.Equal([1], Assert.Single(client.ListingRequests));
        Assert.Equal([1], Assert.Single(client.MetadataRequests));
    }

    [Fact]
    public async Task Screening_prioritizes_two_sided_depth_before_raw_price_gap()
    {
        var client = new StubMarketDataClient(
            itemIds: [1, 2, 3],
            prices: _ => Success(
            [
                new MarketPrice(1, false, new MarketOrderSummary(15, 1_000), new MarketOrderSummary(15, 1_800)),
                new MarketPrice(2, false, new MarketOrderSummary(30, 1_000), new MarketOrderSummary(30, 1_200)),
                new MarketPrice(3, false, new MarketOrderSummary(50, 1_000), new MarketOrderSummary(30, 1_300)),
            ]));
        var lifecycle = CreateLifecycle(client);

        Assert.True(lifecycle.TryStart(Request(), out _));
        var completed = await WaitForTerminalAsync(lifecycle);

        Assert.Equal(PlayerMarketScanState.Complete, completed.State);
        Assert.Equal([3, 2, 1], Assert.Single(client.ListingRequests));
    }

    [Theory]
    [InlineData(Gw2ApiErrorCategory.TransportFailure)]
    [InlineData(Gw2ApiErrorCategory.Forbidden)]
    [InlineData(Gw2ApiErrorCategory.InvalidPayload)]
    [InlineData(Gw2ApiErrorCategory.IncompleteData)]
    public async Task Non_rate_limit_gateway_failures_publish_no_result_and_are_retryable(
        Gw2ApiErrorCategory errorCategory)
    {
        var client = new StubMarketDataClient(
            itemIdsResult: Gw2ApiResult<IReadOnlyList<int>>.Failure(errorCategory));
        var lifecycle = CreateLifecycle(client);

        Assert.True(lifecycle.TryStart(Request(), out _));
        var terminal = await WaitForTerminalAsync(lifecycle);

        Assert.Equal(PlayerMarketScanState.Failed, terminal.State);
        Assert.True(terminal.IsRetryable);
        Assert.Null(terminal.Result);
    }

    [Fact]
    public async Task Rate_limit_is_distinct_and_publishes_no_result()
    {
        var client = new StubMarketDataClient(
            itemIdsResult: Gw2ApiResult<IReadOnlyList<int>>.Failure(Gw2ApiErrorCategory.RateLimited));
        var lifecycle = CreateLifecycle(client);

        Assert.True(lifecycle.TryStart(Request(), out _));
        var terminal = await WaitForTerminalAsync(lifecycle);

        Assert.Equal(PlayerMarketScanState.RateLimited, terminal.State);
        Assert.True(terminal.IsRetryable);
        Assert.Null(terminal.Result);
    }

    [Fact]
    public async Task Incomplete_finalist_data_fails_without_publishing_the_partial_recommendations()
    {
        var client = new StubMarketDataClient(
            metadata: _ => Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>.Success([]));
        var lifecycle = CreateLifecycle(client);

        Assert.True(lifecycle.TryStart(Request(), out _));
        var terminal = await WaitForTerminalAsync(lifecycle);

        Assert.Equal(PlayerMarketScanState.Failed, terminal.State);
        Assert.Null(terminal.Result);
        Assert.Single(client.ListingRequests);
        Assert.Single(client.MetadataRequests);
    }

    [Fact]
    public async Task Structurally_malformed_aggregate_data_fails_before_detailed_reads()
    {
        var client = new StubMarketDataClient(
            prices: _ => Success(
            [
                new MarketPrice(
                    1,
                    IsWhitelisted: false,
                    new MarketOrderSummary(100, -1),
                    new MarketOrderSummary(100, 2_001)),
            ]));
        var lifecycle = CreateLifecycle(client);

        Assert.True(lifecycle.TryStart(Request(), out _));
        var terminal = await WaitForTerminalAsync(lifecycle);

        Assert.Equal(PlayerMarketScanState.Failed, terminal.State);
        Assert.Null(terminal.Result);
        Assert.Empty(client.ListingRequests);
        Assert.Empty(client.MetadataRequests);
    }

    [Fact]
    public async Task Cancellation_reaches_the_gateway_and_waits_for_a_cancelled_terminal_state()
    {
        var pricesStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken receivedCancellationToken = default;
        var client = new StubMarketDataClient(
            pricesAsync: async (_, cancellationToken) =>
            {
                receivedCancellationToken = cancellationToken;
                pricesStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success([]);
            });
        var lifecycle = CreateLifecycle(client);

        Assert.True(lifecycle.TryStart(Request(), out _));
        await pricesStarted.Task;
        var cancellation = await lifecycle.CancelAsync();

        Assert.True(cancellation.HadActiveScan);
        Assert.Equal(PlayerMarketScanState.Cancelled, cancellation.Snapshot.State);
        Assert.Null(cancellation.Snapshot.Result);
        Assert.True(receivedCancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Concurrent_start_is_rejected_and_a_retry_after_failure_starts_a_fresh_scan()
    {
        var firstPricesStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var responseCount = 0;
        var client = new StubMarketDataClient(
            itemIdsAsync: async cancellationToken =>
            {
                if (Interlocked.Increment(ref responseCount) == 1)
                {
                    firstPricesStarted.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                return Gw2ApiResult<IReadOnlyList<int>>.Failure(Gw2ApiErrorCategory.TransportFailure);
            });
        var lifecycle = CreateLifecycle(client);

        Assert.True(lifecycle.TryStart(Request(), out _));
        await firstPricesStarted.Task;
        Assert.False(lifecycle.TryStart(Request(), out var alreadyRunning));
        Assert.Equal(PlayerMarketScanState.Running, alreadyRunning.State);

        await lifecycle.CancelAsync();
        Assert.True(lifecycle.TryStart(Request(), out _));
        var retried = await WaitForTerminalAsync(lifecycle);

        Assert.Equal(PlayerMarketScanState.Failed, retried.State);
        Assert.Equal(2, responseCount);
    }

    [Fact]
    public void Lifecycle_has_no_persistence_or_completed_market_data_dependency()
    {
        var constructor = Assert.Single(typeof(PlayerMarketScanLifecycle).GetConstructors());
        var parameterTypes = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        var snapshotPropertyTypes = typeof(PlayerMarketScanSnapshot).GetProperties()
            .Select(property => property.PropertyType)
            .ToArray();

        Assert.Equal(
        [
            typeof(IGw2ApiClient),
            typeof(BeginnerRecommendationEngine),
            typeof(Gw2Tp.Application.Time.IClock),
        ],
        parameterTypes);
        Assert.DoesNotContain(typeof(IUserSessionPreferencesStore), parameterTypes);
        Assert.DoesNotContain(typeof(MarketPrice), snapshotPropertyTypes);
        Assert.DoesNotContain(typeof(MarketListing), snapshotPropertyTypes);
        Assert.DoesNotContain(typeof(MarketItemMetadata), snapshotPropertyTypes);
    }

    private static PlayerMarketScanLifecycle CreateLifecycle(StubMarketDataClient client) => new(
        client,
        new BeginnerRecommendationEngine(),
        new FrozenClock(ScanTime));

    private static PlayerMarketScanRequest Request() => new(
        new Money(22_000),
        BeginnerRiskProfile.Cautious);

    private static async Task<PlayerMarketScanSnapshot> WaitForTerminalAsync(IPlayerMarketScanLifecycle lifecycle)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var snapshot = lifecycle.GetSnapshot();
            if (snapshot.State != PlayerMarketScanState.Running)
            {
                return snapshot;
            }

            await Task.Delay(10, timeout.Token);
        }
    }

    private static Gw2ApiResult<IReadOnlyList<T>> Success<T>(IEnumerable<T> values) =>
        Gw2ApiResult<IReadOnlyList<T>>.Success(values.ToArray());

    private sealed class StubMarketDataClient : IGw2ApiClient
    {
        private readonly IReadOnlyList<int> itemIds;
        private readonly Func<IReadOnlyCollection<int>, Gw2ApiResult<IReadOnlyList<MarketPrice>>> prices;
        private readonly Func<IReadOnlyCollection<int>, CancellationToken, Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>>> pricesAsync;
        private readonly Func<IReadOnlyCollection<int>, Gw2ApiResult<IReadOnlyList<MarketListing>>> listings;
        private readonly Func<IReadOnlyCollection<int>, Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> metadata;
        private readonly Func<CancellationToken, Task<Gw2ApiResult<IReadOnlyList<int>>>> itemIdsAsync;
        private readonly Gw2ApiResult<IReadOnlyList<int>>? itemIdsResult;

        public StubMarketDataClient(
            IReadOnlyList<int>? itemIds = null,
            Gw2ApiResult<IReadOnlyList<int>>? itemIdsResult = null,
            Func<CancellationToken, Task<Gw2ApiResult<IReadOnlyList<int>>>>? itemIdsAsync = null,
            Func<IReadOnlyCollection<int>, Gw2ApiResult<IReadOnlyList<MarketPrice>>>? prices = null,
            Func<IReadOnlyCollection<int>, CancellationToken, Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>>>? pricesAsync = null,
            Func<IReadOnlyCollection<int>, Gw2ApiResult<IReadOnlyList<MarketListing>>>? listings = null,
            Func<IReadOnlyCollection<int>, Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>>? metadata = null)
        {
            this.itemIds = itemIds ?? [1];
            this.itemIdsResult = itemIdsResult;
            this.itemIdsAsync = itemIdsAsync ?? (cancellationToken =>
                Task.FromResult(this.itemIdsResult ?? Success(this.itemIds)));
            this.prices = prices ?? (ids => Success(ids.Select(StandardPrice)));
            this.pricesAsync = pricesAsync ?? ((ids, _) => Task.FromResult(this.prices(ids)));
            this.listings = listings ?? (ids => Success(ids.Select(StandardListing)));
            this.metadata = metadata ?? (ids => Success(ids.Select(StandardMetadata)));
        }

        public List<string> Calls { get; } = [];

        public List<IReadOnlyList<int>> ListingRequests { get; } = [];

        public List<IReadOnlyList<int>> MetadataRequests { get; } = [];

        public async Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(
            CancellationToken cancellationToken = default)
        {
            Calls.Add("price-item-ids");
            return await itemIdsAsync(cancellationToken);
        }

        public async Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
            IReadOnlyCollection<int> requestedItemIds,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("prices");
            return await pricesAsync(requestedItemIds, cancellationToken);
        }

        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
            IReadOnlyCollection<int> requestedItemIds,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("listings");
            ListingRequests.Add(requestedItemIds.ToArray());
            return Task.FromResult(listings(requestedItemIds));
        }

        public Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(
            IReadOnlyCollection<int> requestedItemIds,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("metadata");
            MetadataRequests.Add(requestedItemIds.ToArray());
            return Task.FromResult(metadata(requestedItemIds));
        }

        private static MarketPrice StandardPrice(int itemId) => new(
            itemId,
            IsWhitelisted: false,
            new MarketOrderSummary(100, 999),
            new MarketOrderSummary(100, 2_001));

        private static MarketListing StandardListing(int itemId) => new(
            itemId,
            [new MarketOrderLevel(3, 100, 999)],
            [new MarketOrderLevel(3, 100, 2_001)]);

        private static MarketItemMetadata StandardMetadata(int itemId) => new(
            itemId,
            $"Item {itemId}",
            MarketItemStackPolicy.NormalStackLimit);
    }
}
