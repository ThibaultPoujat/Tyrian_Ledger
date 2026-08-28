using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Domain.MarketData;
using Xunit;

namespace Gw2Tp.Application.Tests.MarketHistory;

public sealed class HistoricalMarketAnalyticsCalculatorTests
{
    private static readonly DateTimeOffset StartedAtUtc = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Calculates_known_descriptive_metrics_from_local_observations()
    {
        var snapshots = Enumerable.Range(0, HistoricalMarketAnalyticsCalculator.MinimumObservationCount)
            .Select(index => CreateSnapshot(
                index,
                index < 10 ? 100 : index < 20 ? 200 : 100,
                buyQuantity: index % 2 == 0 ? 10 : 20,
                sellQuantity: 30))
            .ToArray();

        var analytics = new HistoricalMarketAnalyticsCalculator().Calculate(snapshots);

        Assert.Equal(10, analytics.ItemId);
        Assert.Equal(30, analytics.Coverage.ObservationCount);
        Assert.Equal(StartedAtUtc, analytics.Coverage.FirstCapturedAtUtc);
        Assert.Equal(StartedAtUtc.AddHours(29), analytics.Coverage.LastCapturedAtUtc);
        Assert.Equal(30, analytics.BuyPrices.ObservationCount);
        Assert.Equal(29, analytics.BuyPrices.ReturnObservationCount);
        Assert.Equal(100, analytics.BuyPrices.TenthPercentileCopper);
        Assert.Equal(100, analytics.BuyPrices.MedianCopper);
        Assert.Equal(200, analytics.BuyPrices.NinetiethPercentileCopper);
        Assert.Equal(20.689655m, analytics.BuyPrices.VolatilityPercent);
        Assert.Equal(50m, analytics.BuyPrices.MaximumDrawdownPercent);
        Assert.Equal(30, analytics.SpreadPersistence.ObservationCount);
        Assert.Equal(30, analytics.SpreadPersistence.PositiveSpreadObservationCount);
        Assert.Equal(100m, analytics.SpreadPersistence.PositiveSpreadPercent);
        Assert.Equal(33.333333m, analytics.BuyLiquidityStability.CoefficientOfVariationPercent);
        Assert.Equal(0m, analytics.SellLiquidityStability.CoefficientOfVariationPercent);
    }

    [Fact]
    public void Withholds_metrics_below_the_minimum_observation_count()
    {
        var snapshots = Enumerable.Range(0, HistoricalMarketAnalyticsCalculator.MinimumObservationCount - 1)
            .Select(index => CreateSnapshot(index, 100, 10, 20))
            .ToArray();

        var analytics = new HistoricalMarketAnalyticsCalculator().Calculate(snapshots);

        Assert.Equal(29, analytics.Coverage.ObservationCount);
        Assert.Equal(29, analytics.BuyPrices.ObservationCount);
        Assert.Equal(28, analytics.BuyPrices.ReturnObservationCount);
        Assert.Null(analytics.BuyPrices.MedianCopper);
        Assert.Null(analytics.BuyPrices.VolatilityPercent);
        Assert.Null(analytics.BuyPrices.MaximumDrawdownPercent);
        Assert.Null(analytics.SpreadPersistence.PositiveSpreadPercent);
        Assert.Null(analytics.BuyLiquidityStability.CoefficientOfVariationPercent);
    }

    [Fact]
    public void Excludes_missing_price_sides_without_inventing_observations()
    {
        var snapshots = Enumerable.Range(0, HistoricalMarketAnalyticsCalculator.MinimumObservationCount)
            .Select(index => CreateSnapshot(
                index,
                buyPrice: index == 12 ? 0 : 100,
                buyQuantity: 0,
                sellQuantity: 20))
            .ToArray();

        var analytics = new HistoricalMarketAnalyticsCalculator().Calculate(snapshots);

        Assert.Equal(30, analytics.Coverage.ObservationCount);
        Assert.Equal(29, analytics.BuyPrices.ObservationCount);
        Assert.Null(analytics.BuyPrices.MedianCopper);
        Assert.Equal(30, analytics.SellPrices.ObservationCount);
        Assert.Equal(110, analytics.SellPrices.MedianCopper);
        Assert.Equal(29, analytics.SpreadPersistence.ObservationCount);
        Assert.Null(analytics.SpreadPersistence.PositiveSpreadPercent);
        Assert.Equal(30, analytics.BuyLiquidityStability.ObservationCount);
        Assert.Null(analytics.BuyLiquidityStability.CoefficientOfVariationPercent);
    }

    [Fact]
    public void Rejects_snapshots_for_multiple_items()
    {
        var snapshots = new[]
        {
            CreateSnapshot(0, 100, 10, 20),
            CreateSnapshot(1, 100, 10, 20, itemId: 11),
        };

        Assert.Throws<ArgumentException>(() => new HistoricalMarketAnalyticsCalculator().Calculate(snapshots));
    }

    private static MarketPriceSnapshot CreateSnapshot(
        int index,
        int buyPrice,
        int buyQuantity,
        int sellQuantity,
        int itemId = 10) => new(
        Guid.Parse($"00000000-0000-0000-0000-{index + 1:000000000000}"),
        new MarketPrice(
            itemId,
            IsWhitelisted: false,
            new MarketOrderSummary(buyQuantity, buyPrice),
            new MarketOrderSummary(sellQuantity, buyPrice + 10)),
        new DataFreshness(StartedAtUtc.AddHours(index), StartedAtUtc.AddHours(index + 1)));
}
