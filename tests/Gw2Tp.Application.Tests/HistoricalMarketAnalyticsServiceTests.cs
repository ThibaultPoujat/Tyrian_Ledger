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

        public Task<IReadOnlyDictionary<int, MarketPriceObservation>> GetLatestPriceObservationsAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
