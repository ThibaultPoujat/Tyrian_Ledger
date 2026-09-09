using Gw2Tp.Analytics.MarketHistory;
using Gw2Tp.Analytics.Finance;
using Gw2Tp.Application.Finance;
using Gw2Tp.Application.Time;

namespace Gw2Tp.Application.MarketHistory;

public enum HistoricalMarketWindowState
{
    Available = 1,
    InsufficientData = 2,
}

public sealed record HistoricalMarketWindowDefinition(TimeSpan Duration, int MinimumEligibleObservationCount)
{
    public void Validate()
    {
        if (Duration <= TimeSpan.Zero || MinimumEligibleObservationCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(HistoricalMarketWindowDefinition));
        }
    }
}

/// <summary>
/// Version-one historical-research policy. A window must contain its stated
/// number of calculable observations and span at least 80% of its actual UTC
/// duration; a reported gap is evidence, not fabricated coverage.
/// </summary>
public sealed record HistoricalMarketAnalyticsSettings(
    HistoricalMarketMetricsSettings Metrics,
    IReadOnlyList<HistoricalMarketWindowDefinition> Windows,
    decimal MinimumObservedSpanPercent)
{
    public static HistoricalMarketAnalyticsSettings Default { get; } = new(
        HistoricalMarketMetricsSettings.Default,
        [new HistoricalMarketWindowDefinition(TimeSpan.FromDays(7), 20), new HistoricalMarketWindowDefinition(TimeSpan.FromDays(30), 60)],
        80m);

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Metrics);
        ArgumentNullException.ThrowIfNull(Windows);
        Metrics.Validate();
        if (Windows.Count == 0 || Windows.Any(window => window is null) ||
            Windows.Select(window => window.Duration).Distinct().Count() != Windows.Count ||
            MinimumObservedSpanPercent is <= 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(HistoricalMarketAnalyticsSettings));
        }

        foreach (var window in Windows)
        {
            window.Validate();
        }
    }
}

public sealed record HistoricalMarketWindowCoverage(
    DateTimeOffset FromInclusiveUtc,
    DateTimeOffset ToInclusiveUtc,
    int RawObservationCount,
    int EligibleObservationCount,
    int ExcludedObservationCount,
    DateTimeOffset? FirstEligibleObservedAtUtc,
    DateTimeOffset? LastEligibleObservedAtUtc,
    decimal ObservedSpanPercent,
    TimeSpan? LargestEligibleObservationGap);

public sealed record HistoricalMarketWindowAnalytics(
    HistoricalMarketWindowState State,
    HistoricalMarketWindowCoverage Coverage,
    HistoricalMarketMetricSummary? Metrics);

public sealed record HistoricalMarketAnalytics(
    int ItemId,
    DateTimeOffset AsOfUtc,
    bool IsFeeRoundingExternallyVerified,
    HistoricalMarketAnalyticsSettings Settings,
    HistoricalNetRoi? LatestObservedNetRoi,
    IReadOnlyList<HistoricalMarketWindowAnalytics> Windows);

public interface IHistoricalMarketAnalyticsService
{
    Task<HistoricalMarketAnalytics> GetAsync(int itemId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Orchestrates read-only analysis of retained aggregate observations. It does
/// not call ArenaNet, write derived history, or claim that retained evidence is
/// a prediction of a future trade.
/// </summary>
public sealed class HistoricalMarketAnalyticsService(
    IMarketHistoryRepository repository,
    IClock clock,
    HistoricalMarketAnalyticsSettings? settings = null) : IHistoricalMarketAnalyticsService
{
    private readonly HistoricalMarketAnalyticsSettings settings = settings ?? HistoricalMarketAnalyticsSettings.Default;
    private readonly HistoricalMarketMetricsCalculator calculator = new(
        new FlipProfitCalculator(Gw2TradingPostFeePolicy.Create()),
        Gw2TradingPostFeePolicy.CalculateExactRoi);

    public async Task<HistoricalMarketAnalytics> GetAsync(int itemId, CancellationToken cancellationToken = default)
    {
        if (itemId <= 0) throw new ArgumentOutOfRangeException(nameof(itemId));
        settings.Validate();
        var asOfUtc = clock.UtcNow;
        if (asOfUtc.Offset != TimeSpan.Zero) throw new InvalidOperationException("The historical analytics clock must return UTC.");
        var longestWindow = settings.Windows.MaxBy(window => window.Duration)!;
        var allObservationsTask = repository.GetPriceObservationsAsync(
            itemId,
            asOfUtc - longestWindow.Duration,
            asOfUtc,
            cancellationToken);
        var latestObservationTask = GetLatestEligibleObservationAsync(itemId, cancellationToken);
        await Task.WhenAll(allObservationsTask, latestObservationTask).ConfigureAwait(false);
        var allObservations = await allObservationsTask.ConfigureAwait(false);
        var latestObservation = await latestObservationTask.ConfigureAwait(false);

        var latestCalculation = calculator.Calculate(
            latestObservation is null ? [] : ToAnalyticsObservations([latestObservation]),
            settings.Metrics);
        var windows = settings.Windows
            .OrderBy(window => window.Duration)
            .Select(window => BuildWindow(window, asOfUtc, allObservations))
            .ToArray();

        return new HistoricalMarketAnalytics(
            itemId,
            asOfUtc,
            Gw2TradingPostFeePolicy.IsFractionalCopperRoundingExternallyVerified,
            settings,
            latestCalculation.LatestEligibleNetRoi,
            windows);
    }

    private Task<MarketPriceObservation?> GetLatestEligibleObservationAsync(int itemId, CancellationToken cancellationToken)
    {
        var minimumSellPrice = (long)settings.Metrics.ListUndercutCopper + 1;
        if (minimumSellPrice > int.MaxValue)
        {
            return Task.FromResult<MarketPriceObservation?>(null);
        }

        return repository.GetLatestPriceObservationAsync(
            itemId,
            new LatestMarketPriceObservationQuery(1, (int)minimumSellPrice, 1, 1),
            cancellationToken);
    }

    private HistoricalMarketWindowAnalytics BuildWindow(
        HistoricalMarketWindowDefinition definition,
        DateTimeOffset asOfUtc,
        IReadOnlyList<MarketPriceObservation> allObservations)
    {
        var fromInclusiveUtc = asOfUtc - definition.Duration;
        var calculation = calculator.Calculate(
            ToAnalyticsObservations(allObservations.Where(observation => observation.ObservedAtUtc >= fromInclusiveUtc)),
            settings.Metrics);
        var observedSpanPercent = CalculateObservedSpanPercent(
            calculation.FirstEligibleObservedAtUtc,
            calculation.LastEligibleObservedAtUtc,
            definition.Duration);
        var coverage = new HistoricalMarketWindowCoverage(
            fromInclusiveUtc,
            asOfUtc,
            calculation.RawObservationCount,
            calculation.EligibleObservationCount,
            calculation.ExcludedObservationCount,
            calculation.FirstEligibleObservedAtUtc,
            calculation.LastEligibleObservedAtUtc,
            observedSpanPercent,
            calculation.LargestEligibleObservationGap);
        var isSufficient = calculation.EligibleObservationCount >= definition.MinimumEligibleObservationCount &&
            observedSpanPercent >= settings.MinimumObservedSpanPercent;
        return new HistoricalMarketWindowAnalytics(
            isSufficient ? HistoricalMarketWindowState.Available : HistoricalMarketWindowState.InsufficientData,
            coverage,
            isSufficient ? calculation.Summary : null);
    }

    private static IReadOnlyList<HistoricalMarketObservation> ToAnalyticsObservations(IEnumerable<MarketPriceObservation> observations) =>
        observations.Select(observation => new HistoricalMarketObservation(
            observation.ObservedAtUtc,
            observation.HighestBuyPriceInCopper,
            observation.LowestSellPriceInCopper,
            observation.AggregateBuyQuantity,
            observation.AggregateSellQuantity)).ToArray();

    private static decimal CalculateObservedSpanPercent(DateTimeOffset? first, DateTimeOffset? last, TimeSpan duration)
    {
        if (first is null || last is null)
        {
            return 0m;
        }

        var span = last.Value - first.Value;
        return Math.Min(100m, span.Ticks * 100m / duration.Ticks);
    }
}
