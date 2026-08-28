using Gw2Tp.Application.MarketHistory;
using Xunit;

namespace Gw2Tp.Application.Tests.MarketHistory;

public sealed class MarketCollectionPlannerTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Missing_snapshots_are_due_immediately_for_each_explicitly_tracked_item()
    {
        var plan = MarketCollectionPlanner.CreatePlan(
            [
                new MarketTrackedItem(20, MarketSamplingClass.Background),
                new MarketTrackedItem(10, MarketSamplingClass.Watchlist),
            ],
            new Dictionary<int, MarketSnapshotCollectionState>(),
            NowUtc);

        Assert.Equal([10, 20], plan.PriceItemIds);
        Assert.Equal([10, 20], plan.OrderBookItemIds);
    }

    [Fact]
    public void Watchlist_price_becomes_due_before_background_price_while_each_order_book_cadence_is_preserved()
    {
        var plan = MarketCollectionPlanner.CreatePlan(
            [
                new MarketTrackedItem(10, MarketSamplingClass.Watchlist),
                new MarketTrackedItem(20, MarketSamplingClass.Background),
            ],
            new Dictionary<int, MarketSnapshotCollectionState>
            {
                [10] = new(
                    latestPriceCapturedAtUtc: NowUtc.AddHours(-1),
                    latestOrderBookCapturedAtUtc: NowUtc.AddHours(-23)),
                [20] = new(
                    latestPriceCapturedAtUtc: NowUtc.AddHours(-23),
                    latestOrderBookCapturedAtUtc: NowUtc.AddDays(-6)),
            },
            NowUtc);

        Assert.Equal([10], plan.PriceItemIds);
        Assert.Empty(plan.OrderBookItemIds);
    }

    [Fact]
    public void Exact_schedule_boundaries_are_due()
    {
        var plan = MarketCollectionPlanner.CreatePlan(
            [
                new MarketTrackedItem(10, MarketSamplingClass.Watchlist),
                new MarketTrackedItem(20, MarketSamplingClass.Background),
            ],
            new Dictionary<int, MarketSnapshotCollectionState>
            {
                [10] = new(
                    latestPriceCapturedAtUtc: NowUtc.AddHours(-1),
                    latestOrderBookCapturedAtUtc: NowUtc.AddDays(-1)),
                [20] = new(
                    latestPriceCapturedAtUtc: NowUtc.AddDays(-1),
                    latestOrderBookCapturedAtUtc: NowUtc.AddDays(-7)),
            },
            NowUtc);

        Assert.Equal([10, 20], plan.PriceItemIds);
        Assert.Equal([10, 20], plan.OrderBookItemIds);
    }
}
