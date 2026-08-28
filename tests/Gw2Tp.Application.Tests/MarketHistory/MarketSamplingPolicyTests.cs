using Gw2Tp.Application.MarketHistory;
using Xunit;

namespace Gw2Tp.Application.Tests.MarketHistory;

public sealed class MarketSamplingPolicyTests
{
    [Fact]
    public void Returns_conservative_cadences_by_explicit_item_class()
    {
        var watchlist = MarketSamplingPolicy.GetSchedule(MarketSamplingClass.Watchlist);
        var background = MarketSamplingPolicy.GetSchedule(MarketSamplingClass.Background);
        var untracked = MarketSamplingPolicy.GetSchedule(MarketSamplingClass.Untracked);

        Assert.Equal(TimeSpan.FromHours(1), watchlist.PriceSnapshotInterval);
        Assert.Equal(TimeSpan.FromDays(1), watchlist.OrderBookSnapshotInterval);
        Assert.Equal(TimeSpan.FromDays(1), background.PriceSnapshotInterval);
        Assert.Equal(TimeSpan.FromDays(7), background.OrderBookSnapshotInterval);
        Assert.False(untracked.IsTracked);
        Assert.Null(untracked.PriceSnapshotInterval);
        Assert.Null(untracked.OrderBookSnapshotInterval);
    }

    [Fact]
    public void Estimates_the_owner_selected_small_watchlist_storage_budget()
    {
        var estimate = MarketSamplingPolicy.EstimateAnnualStorage(
            watchlistItemCount: 25,
            backgroundItemCount: 0);

        Assert.Equal(219_000, estimate.AnnualPriceSnapshotCount);
        Assert.Equal(9_125, estimate.AnnualOrderBookSnapshotCount);
        Assert.Equal(365_000, estimate.AnnualOrderBookLevelCount);
        Assert.Equal(47_450_000, estimate.EstimatedBytes);
        Assert.Equal(52_195_000, estimate.PlanningBudgetBytes);
    }

    [Fact]
    public void Rejects_a_tracked_item_count_above_the_local_cap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MarketSamplingPolicy.EstimateAnnualStorage(
            watchlistItemCount: MarketSamplingPolicy.MaximumTrackedItemCount,
            backgroundItemCount: 1));
    }

    [Fact]
    public void Rejects_unknown_sampling_classes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MarketSamplingPolicy.GetSchedule((MarketSamplingClass)99));
    }
}
