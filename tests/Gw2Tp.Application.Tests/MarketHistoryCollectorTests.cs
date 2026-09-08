using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.Time;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class MarketHistoryCollectorTests
{
    private static readonly DateTimeOffset FirstObservedAtUtc = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Due_collection_appends_complete_prices_and_explicit_books_then_honors_cadence()
    {
        var clock = new MutableClock(FirstObservedAtUtc);
        var repository = new InMemoryHistoryRepository();
        var client = new StubMarketDataClient
        {
            Prices = Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success([Price(42), Price(84)]),
            Listings = Gw2ApiResult<IReadOnlyList<MarketListing>>.Success([Listing(42)]),
        };
        var collector = CreateCollector(clock, repository, client,
        [
            new MarketSamplingTarget(42, MarketSamplingTier.CurrentPersonalOrder, TimeSpan.FromMinutes(15), true, ["orders"]),
            new MarketSamplingTarget(84, MarketSamplingTier.Watchlist, TimeSpan.FromMinutes(30), false, ["watchlist"]),
        ]);

        var first = await collector.CollectDueAsync();
        var notDue = await collector.CollectDueAsync();
        clock.UtcNow = FirstObservedAtUtc.AddMinutes(16);
        var later = await collector.CollectDueAsync();

        Assert.Equal(MarketHistoryCollectionOutcome.Succeeded, first.Outcome);
        Assert.Equal(2, first.AppendedPriceObservationCount);
        Assert.Equal(1, first.AppendedOrderBookSnapshotCount);
        Assert.Equal(FirstObservedAtUtc.AddMinutes(15), first.NextDueAtUtc);
        Assert.Equal(0, notDue.DueItemCount);
        Assert.Equal(FirstObservedAtUtc.AddMinutes(15), notDue.NextDueAtUtc);
        Assert.Equal(1, later.DueItemCount);
        Assert.Equal(FirstObservedAtUtc.AddMinutes(30), later.NextDueAtUtc);
        Assert.Equal([42, 84, 42], repository.Prices.Select(observation => observation.ItemId));
        var book = repository.Books[0];
        Assert.Equal(2, repository.Books.Count);
        Assert.Equal([MarketOrderBookSide.Buy, MarketOrderBookSide.Sell], book.Levels.Select(level => level.Side));
        Assert.Equal(FirstObservedAtUtc.AddMinutes(16), collector.GetHealth().LastSuccessfulCaptureAtUtc);
        Assert.Equal(2, collector.GetHealth().TrackedItemCount);
        Assert.Equal(2, client.PriceCallCount);
        Assert.Equal(2, client.ListingCallCount);
    }

    [Fact]
    public async Task Manual_collection_ignores_cadence_and_records_its_own_valid_observation()
    {
        var clock = new MutableClock(FirstObservedAtUtc);
        var repository = new InMemoryHistoryRepository();
        var client = new StubMarketDataClient { Prices = Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success([Price(42)]) };
        var collector = CreateCollector(clock, repository, client,
            [new MarketSamplingTarget(42, MarketSamplingTier.CurrentPersonalOrder, TimeSpan.FromMinutes(15), false, ["orders"])]);

        await collector.CollectDueAsync();
        clock.UtcNow = FirstObservedAtUtc.AddMinutes(1);
        var manual = await collector.CollectNowAsync();

        Assert.Equal(MarketHistoryCollectionOutcome.Succeeded, manual.Outcome);
        Assert.Equal(1, manual.DueItemCount);
        Assert.Equal(2, repository.Prices.Count);
        Assert.Equal(FirstObservedAtUtc.AddMinutes(1), repository.Prices[1].ObservedAtUtc);
    }

    [Fact]
    public async Task Serialized_manual_runs_allocate_distinct_sampling_timestamps_when_the_clock_has_not_advanced()
    {
        var clock = new MutableClock(FirstObservedAtUtc);
        var repository = new InMemoryHistoryRepository();
        var client = new StubMarketDataClient { Prices = Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success([Price(42)]) };
        var collector = CreateCollector(clock, repository, client,
            [new MarketSamplingTarget(42, MarketSamplingTier.CurrentPersonalOrder, TimeSpan.FromMinutes(15), false, ["orders"])]);

        await collector.CollectNowAsync();
        await collector.CollectNowAsync();

        Assert.Equal(2, repository.Prices.Count);
        Assert.Equal(FirstObservedAtUtc.AddTicks(1), repository.Prices[1].ObservedAtUtc);
        Assert.Equal(2, repository.Prices.Select(observation => observation.ObservedAtUtc).Distinct().Count());
    }

    [Fact]
    public async Task Partial_or_rate_limited_prices_append_nothing_and_preserve_existing_history()
    {
        var repository = new InMemoryHistoryRepository();
        repository.Prices.Add(Observation(84, FirstObservedAtUtc.AddDays(-1)));
        var client = new StubMarketDataClient { Prices = Gw2ApiResult<IReadOnlyList<MarketPrice>>.Failure(Gw2ApiErrorCategory.RateLimited) };
        var collector = CreateCollector(new MutableClock(FirstObservedAtUtc), repository, client,
            [new MarketSamplingTarget(42, MarketSamplingTier.CurrentPersonalOrder, TimeSpan.FromMinutes(15), false, ["orders"])]);

        var result = await collector.CollectDueAsync();

        Assert.Equal(MarketHistoryCollectionOutcome.Failed, result.Outcome);
        Assert.Equal(Gw2ApiErrorCategory.RateLimited, result.ErrorCategory);
        Assert.Single(repository.Prices);
        Assert.Equal(Gw2ApiErrorCategory.RateLimited, collector.GetHealth().LastFailure);
        Assert.Equal(1, collector.GetHealth().FailureCount);
    }

    [Fact]
    public async Task Listing_failure_keeps_complete_aggregate_evidence_but_never_appends_a_book()
    {
        var repository = new InMemoryHistoryRepository();
        var client = new StubMarketDataClient
        {
            Prices = Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success([Price(42)]),
            Listings = Gw2ApiResult<IReadOnlyList<MarketListing>>.Failure(Gw2ApiErrorCategory.UpstreamUnavailable),
        };
        var collector = CreateCollector(new MutableClock(FirstObservedAtUtc), repository, client,
            [new MarketSamplingTarget(42, MarketSamplingTier.Watchlist, TimeSpan.FromMinutes(30), true, ["shortlist"])]);

        var result = await collector.CollectDueAsync();

        Assert.Equal(MarketHistoryCollectionOutcome.Failed, result.Outcome);
        Assert.Equal(1, result.AppendedPriceObservationCount);
        Assert.Equal(0, result.AppendedOrderBookSnapshotCount);
        Assert.Single(repository.Prices);
        Assert.Empty(repository.Books);
        Assert.Equal(FirstObservedAtUtc, collector.GetHealth().LastSuccessfulCaptureAtUtc);
    }

    [Fact]
    public async Task Empty_policy_never_invents_markets_or_calls_the_gateway()
    {
        var repository = new InMemoryHistoryRepository();
        var client = new StubMarketDataClient();
        var collector = CreateCollector(new MutableClock(FirstObservedAtUtc), repository, client, []);

        var result = await collector.CollectDueAsync();

        Assert.Equal(MarketHistoryCollectionOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, result.TrackedItemCount);
        Assert.Equal(0, client.PriceCallCount);
        Assert.Empty(repository.Prices);
    }

    [Fact]
    public async Task Collection_loads_latest_observations_once_for_the_entire_policy_plan()
    {
        var targets = Enumerable.Range(1, 500)
            .Select(itemId => new MarketSamplingTarget(itemId, MarketSamplingTier.BroadMarket, TimeSpan.FromHours(6), false, ["broad"]))
            .ToArray();
        var repository = new InMemoryHistoryRepository();
        var client = new StubMarketDataClient
        {
            Prices = Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success(targets.Select(target => Price(target.ItemId)).ToArray()),
        };
        var collector = CreateCollector(new MutableClock(FirstObservedAtUtc), repository, client, targets);

        var result = await collector.CollectDueAsync();

        Assert.Equal(MarketHistoryCollectionOutcome.Succeeded, result.Outcome);
        Assert.Single(repository.LatestObservationQueries);
        Assert.Equal(Enumerable.Range(1, 500), repository.LatestObservationQueries[0]);
        Assert.Equal(500, repository.Prices.Count);
        Assert.Equal(1, repository.PriceObservationBatchAppendCount);
    }

    [Fact]
    public async Task Cancellation_propagates_without_recording_a_failed_capture()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new StubMarketDataClient
        {
            PricesHandler = async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success([Price(42)]);
            },
        };
        var collector = CreateCollector(new MutableClock(FirstObservedAtUtc), new InMemoryHistoryRepository(), client,
            [new MarketSamplingTarget(42, MarketSamplingTier.CurrentPersonalOrder, TimeSpan.FromMinutes(15), false, ["orders"])]);

        var capture = collector.CollectDueAsync(cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture);
        Assert.Null(collector.GetHealth().LastFailure);
        Assert.Equal(MarketHistoryCollectorState.Idle, collector.GetHealth().State);
    }

    [Fact]
    public async Task Local_persistence_failure_propagates_instead_of_being_misclassified_as_an_upstream_response()
    {
        var repository = new InMemoryHistoryRepository { AppendPriceException = new IOException("synthetic local write failure") };
        var client = new StubMarketDataClient { Prices = Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success([Price(42)]) };
        var collector = CreateCollector(new MutableClock(FirstObservedAtUtc), repository, client,
            [new MarketSamplingTarget(42, MarketSamplingTier.CurrentPersonalOrder, TimeSpan.FromMinutes(15), false, ["orders"])]);

        await Assert.ThrowsAsync<IOException>(() => collector.CollectDueAsync());

        Assert.Null(collector.GetHealth().LastFailure);
        Assert.Equal(MarketHistoryCollectorState.Idle, collector.GetHealth().State);
    }

    private static MarketHistoryCollector CreateCollector(
        IClock clock,
        InMemoryHistoryRepository repository,
        StubMarketDataClient client,
        IReadOnlyList<MarketSamplingTarget> targets) => new(
        new StaticPolicy(targets), repository, client, clock);

    private static MarketPrice Price(int itemId) => new(itemId, true, new MarketOrderSummary(10, 100), new MarketOrderSummary(12, 120));

    private static MarketListing Listing(int itemId) => new(itemId,
        [new MarketOrderLevel(2, 10, 100)], [new MarketOrderLevel(3, 12, 120)]);

    private static MarketPriceObservation Observation(int itemId, DateTimeOffset observedAtUtc) => new(
        observedAtUtc, itemId, 100, 120, 10, 12, MarketObservationSourceStatus.Complete, MarketSamplingTier.Watchlist, 1);

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class StaticPolicy(IReadOnlyList<MarketSamplingTarget> targets) : IAdaptiveMarketSamplingPolicy
    {
        public Task<MarketSamplingPlan> BuildPlanAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new MarketSamplingPlan(1, targets));
    }

    private sealed class InMemoryHistoryRepository : IMarketHistoryRepository
    {
        public List<MarketPriceObservation> Prices { get; } = [];
        public List<MarketOrderBookSnapshot> Books { get; } = [];
        public List<int[]> LatestObservationQueries { get; } = [];
        public Exception? AppendPriceException { get; init; }
        public int PriceObservationBatchAppendCount { get; private set; }

        public Task AppendPriceObservationAsync(MarketPriceObservation observation, CancellationToken cancellationToken = default)
        {
            if (AppendPriceException is { } exception)
            {
                throw exception;
            }

            Prices.Add(observation);
            return Task.CompletedTask;
        }

        public async Task AppendPriceObservationsAsync(IReadOnlyCollection<MarketPriceObservation> observations, CancellationToken cancellationToken = default)
        {
            PriceObservationBatchAppendCount++;
            foreach (var observation in observations)
            {
                await AppendPriceObservationAsync(observation, cancellationToken);
            }
        }

        public Task AppendOrderBookSnapshotAsync(MarketOrderBookSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Books.Add(snapshot);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MarketPriceObservation>> GetPriceObservationsAsync(int itemId, DateTimeOffset fromInclusiveUtc, DateTimeOffset toInclusiveUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MarketPriceObservation>>(Prices.Where(observation => observation.ItemId == itemId && observation.ObservedAtUtc >= fromInclusiveUtc && observation.ObservedAtUtc <= toInclusiveUtc).ToArray());

        public Task<MarketPriceObservation?> GetLatestPriceObservationAsync(int itemId, LatestMarketPriceObservationQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<MarketPriceObservation?>(null);

        public Task<IReadOnlyDictionary<int, MarketPriceObservation>> GetLatestPriceObservationsAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default)
        {
            var orderedItemIds = itemIds.OrderBy(itemId => itemId).ToArray();
            LatestObservationQueries.Add(orderedItemIds);
            return Task.FromResult<IReadOnlyDictionary<int, MarketPriceObservation>>(Prices
                .Where(observation => orderedItemIds.Contains(observation.ItemId))
                .GroupBy(observation => observation.ItemId)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(observation => observation.ObservedAtUtc).First()));
        }
    }

    private sealed class StubMarketDataClient : IGw2ApiClient
    {
        public Gw2ApiResult<IReadOnlyList<MarketPrice>> Prices { get; init; } = Gw2ApiResult<IReadOnlyList<MarketPrice>>.Failure(Gw2ApiErrorCategory.UnexpectedResponse);
        public Gw2ApiResult<IReadOnlyList<MarketListing>> Listings { get; init; } = Gw2ApiResult<IReadOnlyList<MarketListing>>.Failure(Gw2ApiErrorCategory.UnexpectedResponse);
        public Func<CancellationToken, Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>>>? PricesHandler { get; init; }
        public int PriceCallCount { get; private set; }
        public int ListingCallCount { get; private set; }

        public Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default)
        {
            PriceCallCount++;
            if (PricesHandler is not null)
            {
                return PricesHandler(cancellationToken);
            }

            return Task.FromResult(Prices.IsSuccess && Prices.Value is not null
                ? Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success(Prices.Value.Where(price => itemIds.Contains(price.ItemId)).ToArray(), Prices.IsPartialData)
                : Prices);
        }

        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default)
        {
            ListingCallCount++;
            return Task.FromResult(Listings.IsSuccess && Listings.Value is not null
                ? Gw2ApiResult<IReadOnlyList<MarketListing>>.Success(Listings.Value.Where(listing => itemIds.Contains(listing.ItemId)).ToArray(), Listings.IsPartialData)
                : Listings);
        }

        public Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
