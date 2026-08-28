using Gw2Tp.Application.MarketData;

namespace Gw2Tp.Application.MarketHistory;

/// <summary>
/// Calculates descriptive, evidence-scoped market metrics from locally retained
/// top-of-book observations. The result is intentionally not a forecast.
/// </summary>
public sealed class HistoricalMarketAnalyticsCalculator
{
    public const int MinimumObservationCount = 30;
    private const int PercentageDecimalPlaces = 6;

    public HistoricalMarketAnalytics Calculate(IReadOnlyList<MarketPriceSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        if (snapshots.Any(snapshot => snapshot is null))
        {
            throw new ArgumentException("Historical observations cannot contain null snapshots.", nameof(snapshots));
        }

        if (snapshots.Count == 0)
        {
            return HistoricalMarketAnalytics.Empty;
        }

        var orderedSnapshots = snapshots
            .OrderBy(snapshot => snapshot.Price.ItemId)
            .ThenBy(snapshot => snapshot.Freshness.CapturedAtUtc)
            .ThenBy(snapshot => snapshot.Id)
            .ToArray();
        var itemId = orderedSnapshots[0].Price.ItemId;
        if (orderedSnapshots.Any(snapshot => snapshot.Price.ItemId != itemId))
        {
            throw new ArgumentException("Historical observations must belong to one item.", nameof(snapshots));
        }

        var coverage = new HistoricalObservationCoverage(
            orderedSnapshots.Length,
            orderedSnapshots[0].Freshness.CapturedAtUtc,
            orderedSnapshots[^1].Freshness.CapturedAtUtc);
        var buyPrices = orderedSnapshots
            .Where(snapshot => snapshot.Price.Buys.UnitPriceInCopper > 0)
            .Select(snapshot => snapshot.Price.Buys.UnitPriceInCopper)
            .ToArray();
        var sellPrices = orderedSnapshots
            .Where(snapshot => snapshot.Price.Sells.UnitPriceInCopper > 0)
            .Select(snapshot => snapshot.Price.Sells.UnitPriceInCopper)
            .ToArray();
        var spreads = orderedSnapshots
            .Where(snapshot =>
                snapshot.Price.Buys.UnitPriceInCopper > 0 &&
                snapshot.Price.Sells.UnitPriceInCopper > 0)
            .Select(snapshot => snapshot.Price.Sells.UnitPriceInCopper - snapshot.Price.Buys.UnitPriceInCopper)
            .ToArray();

        return new HistoricalMarketAnalytics(
            itemId,
            coverage,
            CalculatePriceStatistics(buyPrices),
            CalculatePriceStatistics(sellPrices),
            CalculateSpreadPersistence(spreads),
            CalculateLiquidityStability(orderedSnapshots.Select(snapshot => snapshot.Price.Buys.Quantity)),
            CalculateLiquidityStability(orderedSnapshots.Select(snapshot => snapshot.Price.Sells.Quantity)));
    }

    private static HistoricalPriceStatistics CalculatePriceStatistics(IReadOnlyList<int> prices)
    {
        if (prices.Count < MinimumObservationCount)
        {
            return new HistoricalPriceStatistics(prices.Count, Math.Max(prices.Count - 1, 0), null, null, null, null, null);
        }

        var orderedPrices = prices.Order().ToArray();
        return new HistoricalPriceStatistics(
            prices.Count,
            prices.Count - 1,
            GetNearestRank(orderedPrices, 10),
            GetNearestRank(orderedPrices, 50),
            GetNearestRank(orderedPrices, 90),
            CalculateVolatilityPercent(prices),
            CalculateMaximumDrawdownPercent(prices));
    }

    private static HistoricalSpreadPersistence CalculateSpreadPersistence(IReadOnlyList<int> spreads)
    {
        var positiveSpreadCount = spreads.Count(spread => spread > 0);
        return new HistoricalSpreadPersistence(
            spreads.Count,
            positiveSpreadCount,
            spreads.Count < MinimumObservationCount
                ? null
                : RoundPercentage((decimal)positiveSpreadCount * 100m / spreads.Count));
    }

    private static HistoricalLiquidityStability CalculateLiquidityStability(IEnumerable<int> quantities)
    {
        var observedQuantities = quantities.ToArray();
        if (observedQuantities.Length < MinimumObservationCount)
        {
            return new HistoricalLiquidityStability(observedQuantities.Length, null);
        }

        var mean = observedQuantities.Average(quantity => (decimal)quantity);
        if (mean == 0m)
        {
            return new HistoricalLiquidityStability(observedQuantities.Length, null);
        }

        return new HistoricalLiquidityStability(
            observedQuantities.Length,
            RoundPercentage(CalculatePopulationStandardDeviation(observedQuantities.Select(quantity => (decimal)quantity)) * 100m / mean));
    }

    private static int GetNearestRank(IReadOnlyList<int> orderedValues, int percentile)
    {
        var rank = checked((orderedValues.Count * percentile + 99) / 100);
        return orderedValues[rank - 1];
    }

    private static decimal CalculateVolatilityPercent(IReadOnlyList<int> prices)
    {
        var percentageReturns = new decimal[prices.Count - 1];
        for (var index = 1; index < prices.Count; index++)
        {
            percentageReturns[index - 1] = ((decimal)prices[index] - prices[index - 1]) * 100m / prices[index - 1];
        }

        return RoundPercentage(CalculatePopulationStandardDeviation(percentageReturns));
    }

    private static decimal CalculateMaximumDrawdownPercent(IReadOnlyList<int> prices)
    {
        var peak = prices[0];
        var maximumDrawdown = 0m;
        foreach (var price in prices)
        {
            if (price > peak)
            {
                peak = price;
                continue;
            }

            var drawdown = ((decimal)peak - price) * 100m / peak;
            if (drawdown > maximumDrawdown)
            {
                maximumDrawdown = drawdown;
            }
        }

        return RoundPercentage(maximumDrawdown);
    }

    private static decimal CalculatePopulationStandardDeviation(IEnumerable<decimal> values)
    {
        var observations = values.ToArray();
        var mean = observations.Average();
        var variance = observations.Sum(value => (value - mean) * (value - mean)) / observations.Length;
        return CalculateSquareRoot(variance);
    }

    private static decimal RoundPercentage(decimal value) => decimal.Round(
        value,
        PercentageDecimalPlaces,
        MidpointRounding.ToEven);

    private static decimal CalculateSquareRoot(decimal value)
    {
        if (value == 0m)
        {
            return 0m;
        }

        var estimate = value < 1m ? 1m : value / 2m;
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var nextEstimate = (estimate + value / estimate) / 2m;
            if (nextEstimate == estimate)
            {
                return nextEstimate;
            }

            estimate = nextEstimate;
        }

        return estimate;
    }
}

/// <summary>
/// Descriptive metrics for one item, calculated solely from locally retained observations.
/// Null values mean the local sample is not sufficient for that metric.
/// </summary>
public sealed record HistoricalMarketAnalytics(
    int? ItemId,
    HistoricalObservationCoverage Coverage,
    HistoricalPriceStatistics BuyPrices,
    HistoricalPriceStatistics SellPrices,
    HistoricalSpreadPersistence SpreadPersistence,
    HistoricalLiquidityStability BuyLiquidityStability,
    HistoricalLiquidityStability SellLiquidityStability)
{
    public static readonly HistoricalMarketAnalytics Empty = new(
        null,
        HistoricalObservationCoverage.Empty,
        HistoricalPriceStatistics.Empty,
        HistoricalPriceStatistics.Empty,
        HistoricalSpreadPersistence.Empty,
        HistoricalLiquidityStability.Empty,
        HistoricalLiquidityStability.Empty);
}

/// <summary>
/// The full local capture window supplied to the calculation, before side-specific exclusions.
/// </summary>
public sealed record HistoricalObservationCoverage(
    int ObservationCount,
    DateTimeOffset? FirstCapturedAtUtc,
    DateTimeOffset? LastCapturedAtUtc)
{
    public static readonly HistoricalObservationCoverage Empty = new(0, null, null);
}

/// <summary>
/// Observed integer-copper percentiles and non-monetary price-change metrics for one book side.
/// </summary>
public sealed record HistoricalPriceStatistics(
    int ObservationCount,
    int ReturnObservationCount,
    int? TenthPercentileCopper,
    int? MedianCopper,
    int? NinetiethPercentileCopper,
    decimal? VolatilityPercent,
    decimal? MaximumDrawdownPercent)
{
    public static readonly HistoricalPriceStatistics Empty = new(0, 0, null, null, null, null, null);
}

/// <summary>
/// The proportion of locally observed valid price pairs with a positive top-of-book spread.
/// </summary>
public sealed record HistoricalSpreadPersistence(
    int ObservationCount,
    int PositiveSpreadObservationCount,
    decimal? PositiveSpreadPercent)
{
    public static readonly HistoricalSpreadPersistence Empty = new(0, 0, null);
}

/// <summary>
/// Variability of observed best-book quantities, expressed as a percentage of their mean.
/// </summary>
public sealed record HistoricalLiquidityStability(int ObservationCount, decimal? CoefficientOfVariationPercent)
{
    public static readonly HistoricalLiquidityStability Empty = new(0, null);
}
