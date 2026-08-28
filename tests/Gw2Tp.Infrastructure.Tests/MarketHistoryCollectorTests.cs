using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.Time;
using Gw2Tp.Domain.MarketData;
using Gw2Tp.Infrastructure.MarketHistory;
using Gw2Tp.Testing;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class MarketHistoryCollectorTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Due_items_are_collected_in_one_prices_batch_and_one_listings_batch()
    {
        var watchlistStore = new StubWatchlistStore(
        [
            new MarketTrackedItem(20, MarketSamplingClass.Background),
            new MarketTrackedItem(10, MarketSamplingClass.Watchlist),
        ]);
        var snapshotStore = new StubSnapshotStore();
        var client = new StubGw2ApiClient(
            priceResults:
            [
                Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success(
                    [CreatePrice(10), CreatePrice(20)],
                    freshness: CreateFreshness()),
            ],
            orderBookResults:
            [
                Gw2ApiResult<IReadOnlyList<MarketListing>>.Success(
                    [CreateOrderBook(10), CreateOrderBook(20)],
                    freshness: CreateFreshness()),
            ]);
        var collector = CreateCollector(watchlistStore, snapshotStore, client);

        var outcome = await collector.CollectDueAsync(CancellationToken.None);

        Assert.False(outcome.IsRateLimited);
        Assert.Equal(2, outcome.RequestCount);
        Assert.Equal(2, outcome.PriceSnapshotCount);
        Assert.Equal(2, outcome.OrderBookSnapshotCount);
        Assert.Equal([10, 20], Assert.Single(client.PriceRequests));
        Assert.Equal([10, 20], Assert.Single(client.OrderBookRequests));
        Assert.Equal([10, 20], snapshotStore.PriceSnapshots.Select(snapshot => snapshot.Price.ItemId));
        Assert.Equal([10, 20], snapshotStore.OrderBookSnapshots.Select(snapshot => snapshot.ItemId));
    }

    [Fact]
    public async Task Request_budget_prioritizes_prices_and_defers_order_books()
    {
        var watchlistStore = new StubWatchlistStore([new MarketTrackedItem(10, MarketSamplingClass.Watchlist)]);
        var snapshotStore = new StubSnapshotStore();
        var client = new StubGw2ApiClient(
            priceResults:
            [
                Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success(
                    [CreatePrice(10)],
                    freshness: CreateFreshness()),
            ],
            orderBookResults: []);
        var collector = CreateCollector(
            watchlistStore,
            snapshotStore,
            client,
            maximumRequestsPerCycle: 1);

        var outcome = await collector.CollectDueAsync(CancellationToken.None);

        Assert.Equal(1, outcome.RequestCount);
        Assert.Single(client.PriceRequests);
        Assert.Empty(client.OrderBookRequests);
        Assert.Single(snapshotStore.PriceSnapshots);
        Assert.Empty(snapshotStore.OrderBookSnapshots);
    }

    [Fact]
    public async Task Terminal_rate_limit_stops_the_remaining_cycle_work()
    {
        var watchlistStore = new StubWatchlistStore([new MarketTrackedItem(10, MarketSamplingClass.Watchlist)]);
        var snapshotStore = new StubSnapshotStore();
        var client = new StubGw2ApiClient(
            priceResults:
            [Gw2ApiResult<IReadOnlyList<MarketPrice>>.Failure(Gw2ApiErrorCategory.RateLimited)],
            orderBookResults: []);
        var collector = CreateCollector(watchlistStore, snapshotStore, client);

        var outcome = await collector.CollectDueAsync(CancellationToken.None);

        Assert.True(outcome.IsRateLimited);
        Assert.Equal(1, outcome.RequestCount);
        Assert.Single(client.PriceRequests);
        Assert.Empty(client.OrderBookRequests);
        Assert.Empty(snapshotStore.PriceSnapshots);
        Assert.Empty(snapshotStore.OrderBookSnapshots);
    }

    [Fact]
    public async Task Rate_limited_collection_pauses_then_resumes_and_stops_cleanly_on_shutdown()
    {
        var watchlistStore = new StubWatchlistStore([new MarketTrackedItem(10, MarketSamplingClass.Watchlist)]);
        var snapshotStore = new StubSnapshotStore();
        var client = new StubGw2ApiClient(
            priceResults:
            [
                Gw2ApiResult<IReadOnlyList<MarketPrice>>.Failure(Gw2ApiErrorCategory.RateLimited),
                Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success(
                    [CreatePrice(10)],
                    freshness: CreateFreshness()),
            ],
            orderBookResults:
            [
                Gw2ApiResult<IReadOnlyList<MarketListing>>.Success(
                    [CreateOrderBook(10)],
                    freshness: CreateFreshness()),
            ]);
        var options = CreateOptions();
        var collector = CreateCollector(watchlistStore, snapshotStore, client, options: options);
        var delay = new TwoStepDelay();
        var hostedService = new MarketHistoryCollectionHostedService(collector, options, delay);
        using var cancellationSource = new CancellationTokenSource();

        var runTask = hostedService.RunCollectionLoopAsync(cancellationSource.Token);
        var secondDelay = await delay.SecondDelay.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromMinutes(5), await delay.FirstDelay.Task);
        Assert.Equal(TimeSpan.FromMinutes(1), secondDelay);
        Assert.Equal(2, client.PriceRequests.Count);
        Assert.Single(client.OrderBookRequests);

        cancellationSource.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, client.PriceRequests.Count);
        Assert.Single(client.OrderBookRequests);
    }

    private static MarketHistoryCollector CreateCollector(
        IMarketWatchlistStore watchlistStore,
        IMarketSnapshotStore snapshotStore,
        IGw2ApiClient client,
        int maximumRequestsPerCycle = 2,
        MarketHistoryCollectionOptions? options = null) =>
        new(
            watchlistStore,
            snapshotStore,
            client,
            new FrozenClock(NowUtc),
            options ?? CreateOptions(maximumRequestsPerCycle));

    private static MarketHistoryCollectionOptions CreateOptions(int maximumRequestsPerCycle = 2) => new()
    {
        MaximumRequestsPerCycle = maximumRequestsPerCycle,
        IdlePollSeconds = 60,
        RateLimitCooldownSeconds = 300,
    };

    private static DataFreshness CreateFreshness() => new(NowUtc, NowUtc.AddMinutes(2));

    private static MarketPrice CreatePrice(int itemId) => new(
        itemId,
        IsWhitelisted: true,
        new MarketOrderSummary(20, 100),
        new MarketOrderSummary(15, 110));

    private static MarketListing CreateOrderBook(int itemId) => new(
        itemId,
        [new MarketOrderLevel(1, 20, 100)],
        [new MarketOrderLevel(1, 15, 110)]);

    private sealed class StubWatchlistStore(IReadOnlyList<MarketTrackedItem> items) : IMarketWatchlistStore
    {
        public Task<IReadOnlyList<MarketTrackedItem>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(items);
        }

        public Task AddAsync(MarketTrackedItem item, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateSamplingClassAsync(
            int itemId,
            MarketSamplingClass samplingClass,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveAsync(int itemId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubSnapshotStore : IMarketSnapshotStore
    {
        public List<MarketPriceSnapshot> PriceSnapshots { get; } = [];

        public List<MarketOrderBookSnapshot> OrderBookSnapshots { get; } = [];

        public Task AppendAsync(MarketPriceSnapshot snapshot, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PriceSnapshots.Add(snapshot);
            return Task.CompletedTask;
        }

        public Task AppendAsync(MarketOrderBookSnapshot snapshot, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OrderBookSnapshots.Add(snapshot);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MarketPriceSnapshot>> ListPriceSnapshotsAsync(
            int itemId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MarketPriceSnapshot>>(
                PriceSnapshots.Where(snapshot => snapshot.Price.ItemId == itemId).ToArray());

        public Task<IReadOnlyList<MarketOrderBookSnapshot>> ListOrderBookSnapshotsAsync(
            int itemId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MarketOrderBookSnapshot>>(
                OrderBookSnapshots.Where(snapshot => snapshot.ItemId == itemId).ToArray());

        public Task<IReadOnlyDictionary<int, MarketSnapshotCollectionState>> GetCollectionStatesAsync(
            IReadOnlyCollection<int> itemIds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyDictionary<int, MarketSnapshotCollectionState> states = itemIds
                .Distinct()
                .ToDictionary(
                    itemId => itemId,
                    itemId => new MarketSnapshotCollectionState(
                        PriceSnapshots
                            .Where(snapshot => snapshot.Price.ItemId == itemId)
                            .Select(snapshot => (DateTimeOffset?)snapshot.Freshness.CapturedAtUtc)
                            .Max(),
                        OrderBookSnapshots
                            .Where(snapshot => snapshot.ItemId == itemId)
                            .Select(snapshot => (DateTimeOffset?)snapshot.Freshness.CapturedAtUtc)
                            .Max()));
            return Task.FromResult(states);
        }
    }

    private sealed class StubGw2ApiClient(
        IEnumerable<Gw2ApiResult<IReadOnlyList<MarketPrice>>> priceResults,
        IEnumerable<Gw2ApiResult<IReadOnlyList<MarketListing>>> orderBookResults) : IGw2ApiClient
    {
        private readonly Queue<Gw2ApiResult<IReadOnlyList<MarketPrice>>> priceResults = new(priceResults);
        private readonly Queue<Gw2ApiResult<IReadOnlyList<MarketListing>>> orderBookResults = new(orderBookResults);

        public List<IReadOnlyList<int>> PriceRequests { get; } = [];

        public List<IReadOnlyList<int>> OrderBookRequests { get; } = [];

        public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
            IReadOnlyCollection<int> itemIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PriceRequests.Add(itemIds.OrderBy(itemId => itemId).ToArray());
            return Task.FromResult(priceResults.Dequeue());
        }

        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
            IReadOnlyCollection<int> itemIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OrderBookRequests.Add(itemIds.OrderBy(itemId => itemId).ToArray());
            return Task.FromResult(orderBookResults.Dequeue());
        }
    }

    private sealed class TwoStepDelay : IMarketHistoryCollectionDelay
    {
        private int callCount;

        public TaskCompletionSource<TimeSpan> FirstDelay { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<TimeSpan> SecondDelay { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Interlocked.Increment(ref callCount) switch
            {
                1 => CompleteFirstDelayAsync(delay),
                2 => WaitForShutdownAsync(delay, cancellationToken),
                _ => throw new InvalidOperationException("The test should stop the collection service after its second delay."),
            };
        }

        private Task CompleteFirstDelayAsync(TimeSpan delay)
        {
            FirstDelay.TrySetResult(delay);
            return Task.CompletedTask;
        }

        private async Task WaitForShutdownAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            SecondDelay.TrySetResult(delay);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
