using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.Time;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class HistoricalMarketAnalyticsServiceTests
{
    private static readonly DateTimeOffset AsOfUtc = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Default_windows_are_inclusive_coverage_aware_and_return_latest_observed_economics()
    {
        var repository = new InMemoryHistoryRepository();
        for (var index = 0; index < 120; index++)
        {
            repository.Prices.Add(Observation(42, AsOfUtc.AddHours(-6 * (119 - index))));
        }
        repository.Prices.Add(Observation(42, AsOfUtc.AddDays(-1)) with { HighestBuyPriceInCopper = 0 });

        var service = new HistoricalMarketAnalyticsService(repository, new FixedClock(AsOfUtc));
        var analytics = await service.GetAsync(42);

        Assert.Equal(42, analytics.ItemId);
        Assert.Equal(AsOfUtc, analytics.AsOfUtc);
        Assert.Equal(AsOfUtc, analytics.LatestObservedNetRoi?.ObservedAtUtc);
        Assert.Equal([TimeSpan.FromDays(7), TimeSpan.FromDays(30)], analytics.Windows.Select(window => window.Coverage.ToInclusiveUtc - window.Coverage.FromInclusiveUtc));
        Assert.All(analytics.Windows, window => Assert.Equal(HistoricalMarketWindowState.Available, window.State));
        var sevenDay = analytics.Windows[0];
        Assert.Equal(30, sevenDay.Coverage.RawObservationCount);
        Assert.Equal(29, sevenDay.Coverage.EligibleObservationCount);
        Assert.Equal(1, sevenDay.Coverage.ExcludedObservationCount);
        Assert.Equal(100m, sevenDay.Coverage.ObservedSpanPercent);
        Assert.Equal(TimeSpan.FromHours(6), sevenDay.Coverage.LargestEligibleObservationGap);
        Assert.NotNull(sevenDay.Metrics);
        Assert.Single(repository.Queries);
        Assert.Equal((42, AsOfUtc.AddDays(-30), AsOfUtc), repository.Queries[0]);
    }

    [Fact]
    public async Task Sufficient_sample_count_without_required_span_is_explicitly_insufficient()
    {
        var repository = new InMemoryHistoryRepository();
        for (var index = 0; index < 70; index++)
        {
            repository.Prices.Add(Observation(42, AsOfUtc.AddMinutes(-20 * index)));
        }

        var analytics = await new HistoricalMarketAnalyticsService(repository, new FixedClock(AsOfUtc)).GetAsync(42);

        Assert.All(analytics.Windows, window =>
        {
            Assert.Equal(HistoricalMarketWindowState.InsufficientData, window.State);
            Assert.Null(window.Metrics);
            Assert.True(window.Coverage.EligibleObservationCount >= (window.Coverage.ToInclusiveUtc - window.Coverage.FromInclusiveUtc == TimeSpan.FromDays(7) ? 20 : 60));
            Assert.True(window.Coverage.ObservedSpanPercent < 80m);
        });
    }

    [Fact]
    public async Task Seven_day_window_includes_both_UTC_bounds_and_enforces_the_exact_twenty_sample_cutoff()
    {
        var sufficient = new InMemoryHistoryRepository();
        AddEvenlySpacedObservations(sufficient, 20, TimeSpan.FromDays(7));

        var available = await new HistoricalMarketAnalyticsService(sufficient, new FixedClock(AsOfUtc)).GetAsync(42);
        var sevenDayAvailable = Assert.Single(available.Windows, window => window.Coverage.ToInclusiveUtc - window.Coverage.FromInclusiveUtc == TimeSpan.FromDays(7));

        Assert.Equal(HistoricalMarketWindowState.Available, sevenDayAvailable.State);
        Assert.Equal(20, sevenDayAvailable.Coverage.RawObservationCount);
        Assert.Equal(20, sevenDayAvailable.Coverage.EligibleObservationCount);
        Assert.Equal(AsOfUtc.AddDays(-7), sevenDayAvailable.Coverage.FirstEligibleObservedAtUtc);
        Assert.Equal(AsOfUtc, sevenDayAvailable.Coverage.LastEligibleObservedAtUtc);
        Assert.Equal(100m, sevenDayAvailable.Coverage.ObservedSpanPercent);

        var oneShort = new InMemoryHistoryRepository();
        AddEvenlySpacedObservations(oneShort, 19, TimeSpan.FromDays(7));
        var insufficient = await new HistoricalMarketAnalyticsService(oneShort, new FixedClock(AsOfUtc)).GetAsync(42);
        var sevenDayInsufficient = Assert.Single(insufficient.Windows, window => window.Coverage.ToInclusiveUtc - window.Coverage.FromInclusiveUtc == TimeSpan.FromDays(7));

        Assert.Equal(HistoricalMarketWindowState.InsufficientData, sevenDayInsufficient.State);
        Assert.Equal(19, sevenDayInsufficient.Coverage.EligibleObservationCount);
        Assert.Equal(100m, sevenDayInsufficient.Coverage.ObservedSpanPercent);
        Assert.Null(sevenDayInsufficient.Metrics);
    }

    [Fact]
    public async Task Thirty_day_window_includes_both_UTC_bounds_and_enforces_the_exact_sixty_sample_cutoff()
    {
        var sufficient = new InMemoryHistoryRepository();
        AddEvenlySpacedObservations(sufficient, 60, TimeSpan.FromDays(30));

        var available = await new HistoricalMarketAnalyticsService(sufficient, new FixedClock(AsOfUtc)).GetAsync(42);
        var thirtyDayAvailable = Assert.Single(available.Windows, window => window.Coverage.ToInclusiveUtc - window.Coverage.FromInclusiveUtc == TimeSpan.FromDays(30));

        Assert.Equal(HistoricalMarketWindowState.Available, thirtyDayAvailable.State);
        Assert.Equal(60, thirtyDayAvailable.Coverage.RawObservationCount);
        Assert.Equal(60, thirtyDayAvailable.Coverage.EligibleObservationCount);
        Assert.Equal(AsOfUtc.AddDays(-30), thirtyDayAvailable.Coverage.FirstEligibleObservedAtUtc);
        Assert.Equal(AsOfUtc, thirtyDayAvailable.Coverage.LastEligibleObservedAtUtc);
        Assert.Equal(100m, thirtyDayAvailable.Coverage.ObservedSpanPercent);

        var oneShort = new InMemoryHistoryRepository();
        AddEvenlySpacedObservations(oneShort, 59, TimeSpan.FromDays(30));
        var insufficient = await new HistoricalMarketAnalyticsService(oneShort, new FixedClock(AsOfUtc)).GetAsync(42);
        var thirtyDayInsufficient = Assert.Single(insufficient.Windows, window => window.Coverage.ToInclusiveUtc - window.Coverage.FromInclusiveUtc == TimeSpan.FromDays(30));

        Assert.Equal(HistoricalMarketWindowState.InsufficientData, thirtyDayInsufficient.State);
        Assert.Equal(59, thirtyDayInsufficient.Coverage.EligibleObservationCount);
        Assert.Equal(100m, thirtyDayInsufficient.Coverage.ObservedSpanPercent);
        Assert.Null(thirtyDayInsufficient.Metrics);
    }

    [Fact]
    public async Task No_retained_observations_and_invalid_item_are_explicit()
    {
        var service = new HistoricalMarketAnalyticsService(new InMemoryHistoryRepository(), new FixedClock(AsOfUtc));

        var analytics = await service.GetAsync(42);

        Assert.Null(analytics.LatestObservedNetRoi);
        Assert.All(analytics.Windows, window =>
        {
            Assert.Equal(HistoricalMarketWindowState.InsufficientData, window.State);
            Assert.Equal(0, window.Coverage.RawObservationCount);
            Assert.Equal(0m, window.Coverage.ObservedSpanPercent);
        });
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetAsync(0));
    }

    [Fact]
    public async Task Latest_observed_roi_uses_the_latest_eligible_retained_observation_outside_the_analytics_windows()
    {
        var repository = new InMemoryHistoryRepository();
        var olderEligible = Observation(42, AsOfUtc.AddDays(-31));
        repository.Prices.Add(olderEligible);
        repository.Prices.Add(Observation(42, AsOfUtc.AddDays(-1)) with { AggregateBuyQuantity = 0 });
        repository.Prices.Add(Observation(42, AsOfUtc.AddHours(-1)) with { HighestBuyPriceInCopper = 0 });

        var analytics = await new HistoricalMarketAnalyticsService(repository, new FixedClock(AsOfUtc)).GetAsync(42);

        Assert.Equal(olderEligible.ObservedAtUtc, analytics.LatestObservedNetRoi?.ObservedAtUtc);
        Assert.All(analytics.Windows, window =>
        {
            Assert.Equal(HistoricalMarketWindowState.InsufficientData, window.State);
            Assert.Equal(2, window.Coverage.RawObservationCount);
            Assert.Equal(0, window.Coverage.EligibleObservationCount);
        });
    }

    [Fact]
    public async Task Latest_observed_roi_does_not_include_observations_after_the_analytics_as_of_time()
    {
        var repository = new InMemoryHistoryRepository();
        var asOfEligible = Observation(42, AsOfUtc);
        repository.Prices.Add(asOfEligible);
        repository.Prices.Add(Observation(42, AsOfUtc.AddTicks(1)) with { HighestBuyPriceInCopper = 101 });

        var analytics = await new HistoricalMarketAnalyticsService(repository, new FixedClock(AsOfUtc)).GetAsync(42);

        Assert.Equal(asOfEligible.ObservedAtUtc, analytics.LatestObservedNetRoi?.ObservedAtUtc);
    }

    private static MarketPriceObservation Observation(int itemId, DateTimeOffset observedAtUtc) => new(
        observedAtUtc,
        itemId,
        100,
        150,
        10,
        20,
        MarketObservationSourceStatus.Complete,
        MarketSamplingTier.Watchlist,
        1);

    private static void AddEvenlySpacedObservations(InMemoryHistoryRepository repository, int count, TimeSpan duration)
    {
        for (var index = 0; index < count; index++)
        {
            repository.Prices.Add(Observation(
                42,
                AsOfUtc - duration + TimeSpan.FromTicks(duration.Ticks * index / (count - 1))));
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class InMemoryHistoryRepository : IMarketHistoryRepository
    {
        public List<MarketPriceObservation> Prices { get; } = [];
        public List<(int ItemId, DateTimeOffset FromInclusiveUtc, DateTimeOffset ToInclusiveUtc)> Queries { get; } = [];

        public Task AppendPriceObservationAsync(MarketPriceObservation observation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AppendPriceObservationsAsync(IReadOnlyCollection<MarketPriceObservation> observations, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AppendOrderBookSnapshotAsync(MarketOrderBookSnapshot snapshot, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<MarketPriceObservation>> GetPriceObservationsAsync(
            int itemId,
            DateTimeOffset fromInclusiveUtc,
            DateTimeOffset toInclusiveUtc,
            CancellationToken cancellationToken = default)
        {
            Queries.Add((itemId, fromInclusiveUtc, toInclusiveUtc));
            return Task.FromResult<IReadOnlyList<MarketPriceObservation>>(Prices
                .Where(observation => observation.ItemId == itemId && observation.ObservedAtUtc >= fromInclusiveUtc && observation.ObservedAtUtc <= toInclusiveUtc)
                .ToArray());
        }

        public Task<MarketPriceObservation?> GetLatestPriceObservationAsync(
            int itemId,
            LatestMarketPriceObservationQuery query,
            CancellationToken cancellationToken = default)
        {
            query.Validate();
            return Task.FromResult(Prices
                .Where(observation => observation.ItemId == itemId &&
                    observation.ObservedAtUtc <= query.MaximumObservedAtUtc &&
                    observation.HighestBuyPriceInCopper >= query.MinimumHighestBuyPriceInCopper &&
                    observation.LowestSellPriceInCopper >= query.MinimumLowestSellPriceInCopper &&
                    observation.AggregateBuyQuantity >= query.MinimumAggregateBuyQuantity &&
                    observation.AggregateSellQuantity >= query.MinimumAggregateSellQuantity)
                .OrderByDescending(observation => observation.ObservedAtUtc)
                .FirstOrDefault());
        }

        public Task<IReadOnlyDictionary<int, MarketPriceObservation>> GetLatestPriceObservationsAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
