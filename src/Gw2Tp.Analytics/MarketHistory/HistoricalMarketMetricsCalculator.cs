using Gw2Tp.Analytics.Finance;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.MarketHistory;

/// <summary>
/// Deterministic, observation-only inputs for historical market metrics.
/// Prices and quantities retain their source units; no missing observation is
/// synthesized by this calculator.
/// </summary>
public sealed record HistoricalMarketObservation(
    DateTimeOffset ObservedAtUtc,
    int HighestBuyPriceInCopper,
    int LowestSellPriceInCopper,
    int AggregateBuyQuantity,
    int AggregateSellQuantity);

/// <summary>
/// Price policy and ROI thresholds used for a historical calculation. The
/// price adjustments intentionally match the current scanner's proposed bid
/// and list convention.
/// </summary>
public sealed record HistoricalMarketMetricsSettings(
    int BidIncrementCopper,
    int ListUndercutCopper,
    IReadOnlyList<int> RoiThresholdBasisPoints)
{
    public static HistoricalMarketMetricsSettings Default { get; } = new(1, 1, [1_500, 2_000]);

    public void Validate()
    {
        if (BidIncrementCopper <= 0 || ListUndercutCopper <= 0 || RoiThresholdBasisPoints is null ||
            RoiThresholdBasisPoints.Count == 0 || RoiThresholdBasisPoints.Any(value => value < 0) ||
            RoiThresholdBasisPoints.Distinct().Count() != RoiThresholdBasisPoints.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(HistoricalMarketMetricsSettings));
        }
    }
}

/// <summary>
/// A net-ROI observation. Monetary members are exact integer copper; the
/// basis-point value is a statistical ratio, represented as decimal.
/// </summary>
public sealed record HistoricalNetRoi(
    DateTimeOffset ObservedAtUtc,
    Money NetProfit,
    Money TotalCost,
    decimal BasisPoints);

public sealed record HistoricalPriceRange(int MinimumCopper, int MaximumCopper);

/// <summary>
/// Statistics calculated only from eligible retained observations. Volatility
/// values are population coefficients of variation (standard deviation / mean)
/// and are therefore dimensionless IEEE-754 values. Spread is the raw
/// lowest-sell/highest-buy ratio; it remains positive even when a market's net
/// flip ROI is negative.
/// </summary>
public sealed record HistoricalMarketMetricSummary(
    decimal MedianNetRoiBasisPoints,
    IReadOnlyList<HistoricalRoiThresholdRate> RoiThresholdRates,
    decimal PositiveNetRoiPercent,
    double? BuyPricePopulationCoefficientOfVariation,
    double? SellPricePopulationCoefficientOfVariation,
    double? SpreadRatioPopulationCoefficientOfVariation,
    decimal MedianAggregateBuyQuantity,
    decimal MedianAggregateSellQuantity,
    double? MinimumSideDepthPopulationCoefficientOfVariation,
    HistoricalPriceRange BuyPriceRange,
    HistoricalPriceRange SellPriceRange,
    double MaximumSellPriceDrawdownPercent);

public sealed record HistoricalRoiThresholdRate(int ThresholdBasisPoints, decimal Percent);

/// <summary>
/// The complete evidence result for one requested window. A null summary means
/// no eligible price observations existed; callers decide their own sufficiency
/// policy rather than treating a shorter or sparse window as equivalent.
/// </summary>
public sealed record HistoricalMarketMetricCalculation(
    int RawObservationCount,
    int EligibleObservationCount,
    int ExcludedObservationCount,
    DateTimeOffset? FirstEligibleObservedAtUtc,
    DateTimeOffset? LastEligibleObservedAtUtc,
    TimeSpan? LargestEligibleObservationGap,
    HistoricalNetRoi? LatestEligibleNetRoi,
    HistoricalMarketMetricSummary? Summary);

/// <summary>
/// Calculates historical descriptive metrics from raw observations. It never
/// predicts prices, interpolates missing time, persists data, or fetches a
/// current market price.
/// </summary>
public sealed class HistoricalMarketMetricsCalculator
{
    private readonly FlipProfitCalculator profitCalculator;
    private readonly Func<Money, Money, Money, ExactRoi> roiCalculator;

    /// <summary>
    /// The caller supplies its canonical completed-sale and ROI policy. This
    /// keeps descriptive analytics independent of application policy ownership.
    /// </summary>
    public HistoricalMarketMetricsCalculator(
        FlipProfitCalculator profitCalculator,
        Func<Money, Money, Money, ExactRoi> roiCalculator)
    {
        this.profitCalculator = profitCalculator ?? throw new ArgumentNullException(nameof(profitCalculator));
        this.roiCalculator = roiCalculator ?? throw new ArgumentNullException(nameof(roiCalculator));
    }

    public HistoricalMarketMetricCalculation Calculate(
        IReadOnlyCollection<HistoricalMarketObservation> observations,
        HistoricalMarketMetricsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        if (observations.Any(observation => observation is null || observation.ObservedAtUtc.Offset != TimeSpan.Zero))
        {
            throw new ArgumentException("Historical observations must be non-null and UTC.", nameof(observations));
        }

        var ordered = observations
            .OrderBy(observation => observation.ObservedAtUtc)
            .ThenBy(observation => observation.HighestBuyPriceInCopper)
            .ThenBy(observation => observation.LowestSellPriceInCopper)
            .ThenBy(observation => observation.AggregateBuyQuantity)
            .ThenBy(observation => observation.AggregateSellQuantity)
            .ToArray();
        var eligible = new List<EligibleObservation>(ordered.Length);
        foreach (var observation in ordered)
        {
            if (TryCreateEligibleObservation(observation, settings, out var calculated))
            {
                eligible.Add(calculated);
            }
        }

        var first = eligible.FirstOrDefault()?.ObservedAtUtc;
        var last = eligible.LastOrDefault()?.ObservedAtUtc;
        var largestGap = CalculateLargestGap(eligible);
        var summary = eligible.Count == 0 ? null : CalculateSummary(eligible, settings.RoiThresholdBasisPoints);
        return new HistoricalMarketMetricCalculation(
            ordered.Length,
            eligible.Count,
            ordered.Length - eligible.Count,
            first,
            last,
            largestGap,
            eligible.LastOrDefault()?.Roi,
            summary);
    }

    private bool TryCreateEligibleObservation(
        HistoricalMarketObservation observation,
        HistoricalMarketMetricsSettings settings,
        out EligibleObservation calculated)
    {
        calculated = default!;
        if (observation.HighestBuyPriceInCopper <= 0 || observation.LowestSellPriceInCopper <= 0 ||
            observation.AggregateBuyQuantity <= 0 || observation.AggregateSellQuantity <= 0)
        {
            return false;
        }

        try
        {
            var bid = new Money(checked((long)observation.HighestBuyPriceInCopper + settings.BidIncrementCopper));
            var list = new Money(checked((long)observation.LowestSellPriceInCopper - settings.ListUndercutCopper));
            if (list.Copper <= 0)
            {
                return false;
            }

            var scenario = profitCalculator.Calculate(bid, list);
            var exactRoi = roiCalculator(scenario.NetProfit, bid, scenario.ListingFee);
            var basisPoints = ToBasisPoints(exactRoi);
            calculated = new EligibleObservation(
                observation.ObservedAtUtc,
                observation.HighestBuyPriceInCopper,
                observation.LowestSellPriceInCopper,
                observation.AggregateBuyQuantity,
                observation.AggregateSellQuantity,
                new HistoricalNetRoi(observation.ObservedAtUtc, exactRoi.Profit, exactRoi.TotalCost, basisPoints));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static HistoricalMarketMetricSummary CalculateSummary(
        IReadOnlyList<EligibleObservation> observations,
        IReadOnlyList<int> thresholds)
    {
        var roiBasisPoints = observations.Select(observation => observation.Roi.BasisPoints).ToArray();
        return new HistoricalMarketMetricSummary(
            Median(roiBasisPoints),
            thresholds.Select(threshold => new HistoricalRoiThresholdRate(
                threshold,
                Percent(observations.Count(observation => MeetsThreshold(observation.Roi, threshold)), observations.Count))).ToArray(),
            Percent(observations.Count(observation => observation.Roi.NetProfit.Copper > 0), observations.Count),
            PopulationCoefficientOfVariation(observations.Select(observation => (double)observation.HighestBuyPriceInCopper)),
            PopulationCoefficientOfVariation(observations.Select(observation => (double)observation.LowestSellPriceInCopper)),
            PopulationCoefficientOfVariation(observations.Select(observation =>
                (double)observation.LowestSellPriceInCopper / observation.HighestBuyPriceInCopper)),
            Median(observations.Select(observation => (decimal)observation.AggregateBuyQuantity).ToArray()),
            Median(observations.Select(observation => (decimal)observation.AggregateSellQuantity).ToArray()),
            PopulationCoefficientOfVariation(observations.Select(observation =>
                (double)Math.Min(observation.AggregateBuyQuantity, observation.AggregateSellQuantity))),
            new HistoricalPriceRange(
                observations.Min(observation => observation.HighestBuyPriceInCopper),
                observations.Max(observation => observation.HighestBuyPriceInCopper)),
            new HistoricalPriceRange(
                observations.Min(observation => observation.LowestSellPriceInCopper),
                observations.Max(observation => observation.LowestSellPriceInCopper)),
            MaximumDrawdownPercent(observations.Select(observation => observation.LowestSellPriceInCopper)));
    }

    private static decimal ToBasisPoints(ExactRoi roi) => decimal.Divide(roi.Profit.Copper * 10_000m, roi.TotalCost.Copper);

    private static bool MeetsThreshold(HistoricalNetRoi roi, int thresholdBasisPoints) =>
        roi.NetProfit.Copper * 10_000m >= roi.TotalCost.Copper * thresholdBasisPoints;

    private static decimal Percent(int matchingCount, int totalCount) => totalCount == 0 ? 0 : matchingCount * 100m / totalCount;

    private static decimal Median(decimal[] values)
    {
        Array.Sort(values);
        var middle = values.Length / 2;
        return values.Length % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2m;
    }

    private static double? PopulationCoefficientOfVariation(IEnumerable<double> values)
    {
        var collected = values.ToArray();
        var mean = collected.Average();
        if (mean == 0d)
        {
            return null;
        }

        var variance = collected.Select(value => Math.Pow(value - mean, 2d)).Average();
        return Math.Sqrt(variance) / mean;
    }

    private static double MaximumDrawdownPercent(IEnumerable<int> values)
    {
        var peak = 0;
        var maximumDrawdown = 0d;
        foreach (var value in values)
        {
            peak = Math.Max(peak, value);
            if (peak > 0)
            {
                maximumDrawdown = Math.Max(maximumDrawdown, (1d - ((double)value / peak)) * 100d);
            }
        }

        return maximumDrawdown;
    }

    private static TimeSpan? CalculateLargestGap(IReadOnlyList<EligibleObservation> observations)
    {
        if (observations.Count < 2)
        {
            return null;
        }

        var largest = TimeSpan.Zero;
        for (var index = 1; index < observations.Count; index++)
        {
            largest = TimeSpan.FromTicks(Math.Max(largest.Ticks, (observations[index].ObservedAtUtc - observations[index - 1].ObservedAtUtc).Ticks));
        }

        return largest;
    }

    private sealed record EligibleObservation(
        DateTimeOffset ObservedAtUtc,
        int HighestBuyPriceInCopper,
        int LowestSellPriceInCopper,
        int AggregateBuyQuantity,
        int AggregateSellQuantity,
        HistoricalNetRoi Roi);
}
