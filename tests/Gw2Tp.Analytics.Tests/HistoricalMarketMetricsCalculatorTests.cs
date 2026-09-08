using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.MarketHistory;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Analytics.Tests;

public sealed class HistoricalMarketMetricsCalculatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Regular_constant_series_reproduces_known_roi_depth_and_zero_volatility_metrics()
    {
        var calculator = CreateCalculator();
        var observations = Enumerable.Range(0, 20)
            .Select(index => Observation(Start.AddHours(index * 8), 100, 150, 10, 20))
            .ToArray();

        var result = calculator.Calculate(observations, HistoricalMarketMetricsSettings.Default);

        Assert.Equal(20, result.RawObservationCount);
        Assert.Equal(20, result.EligibleObservationCount);
        Assert.Equal(0, result.ExcludedObservationCount);
        Assert.Equal(TimeSpan.FromHours(8), result.LargestEligibleObservationGap);
        Assert.Equal(new Money(25), result.LatestEligibleNetRoi?.NetProfit);
        Assert.Equal(new Money(109), result.LatestEligibleNetRoi?.TotalCost);
        Assert.Equal(250_000m / 109m, result.LatestEligibleNetRoi?.BasisPoints);

        var summary = Assert.IsType<HistoricalMarketMetricSummary>(result.Summary);
        Assert.Equal(250_000m / 109m, summary.MedianNetRoiBasisPoints);
        Assert.Equal([new HistoricalRoiThresholdRate(1_500, 100m), new HistoricalRoiThresholdRate(2_000, 100m)], summary.RoiThresholdRates);
        Assert.Equal(100m, summary.PositiveNetRoiPercent);
        Assert.Equal(0d, summary.BuyPricePopulationCoefficientOfVariation);
        Assert.Equal(0d, summary.SellPricePopulationCoefficientOfVariation);
        Assert.Equal(0d, summary.SpreadRatioPopulationCoefficientOfVariation);
        Assert.Equal(10m, summary.MedianAggregateBuyQuantity);
        Assert.Equal(20m, summary.MedianAggregateSellQuantity);
        Assert.Equal(0d, summary.MinimumSideDepthPopulationCoefficientOfVariation);
        Assert.Equal(new HistoricalPriceRange(100, 100), summary.BuyPriceRange);
        Assert.Equal(new HistoricalPriceRange(150, 150), summary.SellPriceRange);
        Assert.Equal(0d, summary.MaximumSellPriceDrawdownPercent);
    }

    [Fact]
    public void Irregular_series_uses_actual_observations_for_normalized_volatility_and_drawdown()
    {
        var calculator = CreateCalculator();
        var result = calculator.Calculate(
        [
            Observation(Start, 100, 200, 10, 40),
            Observation(Start.AddDays(3), 200, 100, 30, 20),
        ], HistoricalMarketMetricsSettings.Default);

        var summary = Assert.IsType<HistoricalMarketMetricSummary>(result.Summary);
        Assert.Equal(TimeSpan.FromDays(3), result.LargestEligibleObservationGap);
        Assert.Equal(1d / 3d, summary.BuyPricePopulationCoefficientOfVariation!.Value, 12);
        Assert.Equal(1d / 3d, summary.SellPricePopulationCoefficientOfVariation!.Value, 12);
        Assert.Equal(0.6d, summary.SpreadRatioPopulationCoefficientOfVariation!.Value, 12);
        Assert.Equal(1d / 3d, summary.MinimumSideDepthPopulationCoefficientOfVariation!.Value, 12);
        Assert.Equal(new HistoricalPriceRange(100, 200), summary.BuyPriceRange);
        Assert.Equal(new HistoricalPriceRange(100, 200), summary.SellPriceRange);
        Assert.Equal(50d, summary.MaximumSellPriceDrawdownPercent, 12);
    }

    [Fact]
    public void Exact_threshold_and_invalid_legacy_rows_are_handled_without_fabricating_a_sample()
    {
        var calculator = CreateCalculator();
        var result = calculator.Calculate(
        [
            Observation(Start, 17, 27, 0, 10),
            Observation(Start.AddHours(1), 0, 27, 10, 10),
        ], HistoricalMarketMetricsSettings.Default);

        Assert.Equal(2, result.RawObservationCount);
        Assert.Equal(1, result.EligibleObservationCount);
        Assert.Equal(1, result.ExcludedObservationCount);
        Assert.Null(result.LargestEligibleObservationGap);
        Assert.Equal(1_500m, result.LatestEligibleNetRoi?.BasisPoints);
        var summary = Assert.IsType<HistoricalMarketMetricSummary>(result.Summary);
        Assert.Equal(100m, Assert.Single(summary.RoiThresholdRates, rate => rate.ThresholdBasisPoints == 1_500).Percent);
        Assert.Equal(0m, Assert.Single(summary.RoiThresholdRates, rate => rate.ThresholdBasisPoints == 2_000).Percent);
        Assert.Null(summary.MinimumSideDepthPopulationCoefficientOfVariation);
    }

    [Fact]
    public void Repeated_calculation_is_deterministic_regardless_of_input_order()
    {
        HistoricalMarketObservation[] observations =
        [
            Observation(Start.AddHours(2), 120, 180, 20, 10),
            Observation(Start, 100, 150, 10, 20),
            Observation(Start.AddHours(1), 110, 160, 15, 15),
        ];

        var calculator = CreateCalculator();
        var first = calculator.Calculate(observations, HistoricalMarketMetricsSettings.Default);
        var second = calculator.Calculate(observations.Reverse().ToArray(), HistoricalMarketMetricsSettings.Default);

        Assert.Equal(first.RawObservationCount, second.RawObservationCount);
        Assert.Equal(first.EligibleObservationCount, second.EligibleObservationCount);
        Assert.Equal(first.ExcludedObservationCount, second.ExcludedObservationCount);
        Assert.Equal(first.FirstEligibleObservedAtUtc, second.FirstEligibleObservedAtUtc);
        Assert.Equal(first.LastEligibleObservedAtUtc, second.LastEligibleObservedAtUtc);
        Assert.Equal(first.LargestEligibleObservationGap, second.LargestEligibleObservationGap);
        Assert.Equal(first.LatestEligibleNetRoi, second.LatestEligibleNetRoi);
        Assert.Equal(first.Summary!.MedianNetRoiBasisPoints, second.Summary!.MedianNetRoiBasisPoints);
        Assert.Equal(first.Summary.RoiThresholdRates, second.Summary.RoiThresholdRates);
        Assert.Equal(first.Summary.PositiveNetRoiPercent, second.Summary.PositiveNetRoiPercent);
        Assert.Equal(first.Summary.BuyPricePopulationCoefficientOfVariation, second.Summary.BuyPricePopulationCoefficientOfVariation);
        Assert.Equal(first.Summary.SellPricePopulationCoefficientOfVariation, second.Summary.SellPricePopulationCoefficientOfVariation);
        Assert.Equal(first.Summary.SpreadRatioPopulationCoefficientOfVariation, second.Summary.SpreadRatioPopulationCoefficientOfVariation);
        Assert.Equal(first.Summary.MedianAggregateBuyQuantity, second.Summary.MedianAggregateBuyQuantity);
        Assert.Equal(first.Summary.MedianAggregateSellQuantity, second.Summary.MedianAggregateSellQuantity);
        Assert.Equal(first.Summary.MinimumSideDepthPopulationCoefficientOfVariation, second.Summary.MinimumSideDepthPopulationCoefficientOfVariation);
        Assert.Equal(first.Summary.BuyPriceRange, second.Summary.BuyPriceRange);
        Assert.Equal(first.Summary.SellPriceRange, second.Summary.SellPriceRange);
        Assert.Equal(first.Summary.MaximumSellPriceDrawdownPercent, second.Summary.MaximumSellPriceDrawdownPercent);
    }

    private static HistoricalMarketMetricsCalculator CreateCalculator()
    {
        var fees = new TransactionFeePolicy(
            new FeeRule(500, FeeRounding.Up, new Money(1)),
            new FeeRule(1_000, FeeRounding.Up, new Money(1)));
        return new HistoricalMarketMetricsCalculator(
            new FlipProfitCalculator(fees),
            static (profit, acquisition, listingFee) => new ExactRoi(profit, acquisition + listingFee));
    }

    private static HistoricalMarketObservation Observation(
        DateTimeOffset observedAtUtc,
        int highestBuyPrice,
        int lowestSellPrice,
        int aggregateBuyQuantity,
        int aggregateSellQuantity) => new(
        observedAtUtc,
        highestBuyPrice,
        lowestSellPrice,
        aggregateBuyQuantity,
        aggregateSellQuantity);
}
