using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gw2Tp.Analytics.MarketHistory;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Web.Hosting;

internal static class HistoricalMarketAnalyticsEndpoints
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    internal static IEndpointRouteBuilder MapHistoricalMarketAnalyticsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/market-history/{itemId:int}/analytics", async (
            int itemId,
            IHistoricalMarketAnalyticsService analyticsService,
            CancellationToken cancellationToken) =>
        {
            if (itemId <= 0)
            {
                return NoStoreJson(StatusCodes.Status400BadRequest, new { error = "invalid_market_history_item_id" });
            }

            var analytics = await analyticsService.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
            return NoStoreJson(StatusCodes.Status200OK, ToResponse(analytics));
        });

        return endpoints;
    }

    private static HistoricalMarketAnalyticsResponse ToResponse(HistoricalMarketAnalytics analytics) => new(
        analytics.ItemId,
        analytics.AsOfUtc,
        analytics.IsFeeRoundingExternallyVerified,
        new HistoricalMarketAnalyticsSettingsResponse(
            analytics.Settings.Metrics.BidIncrementCopper,
            analytics.Settings.Metrics.ListUndercutCopper,
            analytics.Settings.Metrics.RoiThresholdBasisPoints.ToArray(),
            analytics.Settings.MinimumObservedSpanPercent,
            analytics.Settings.Windows.OrderBy(window => window.Duration).Select(window =>
                new HistoricalMarketWindowPolicyResponse((int)window.Duration.TotalDays, window.MinimumEligibleObservationCount)).ToArray()),
        analytics.LatestObservedNetRoi is { } latest ? ToRoiResponse(latest) : null,
        analytics.Windows.Select(window => new HistoricalMarketWindowResponse(
            (int)(window.Coverage.ToInclusiveUtc - window.Coverage.FromInclusiveUtc).TotalDays,
            window.State,
            new HistoricalMarketWindowCoverageResponse(
                window.Coverage.FromInclusiveUtc,
                window.Coverage.ToInclusiveUtc,
                window.Coverage.RawObservationCount,
                window.Coverage.EligibleObservationCount,
                window.Coverage.ExcludedObservationCount,
                window.Coverage.FirstEligibleObservedAtUtc,
                window.Coverage.LastEligibleObservedAtUtc,
                window.Coverage.ObservedSpanPercent,
                window.Coverage.LargestEligibleObservationGap?.TotalSeconds),
            window.Metrics is { } metrics ? ToMetricsResponse(metrics) : null)).ToArray());

    private static HistoricalNetRoiResponse ToRoiResponse(HistoricalNetRoi roi) => new(
        roi.ObservedAtUtc,
        MoneyResponse.From(roi.NetProfit),
        MoneyResponse.From(roi.TotalCost),
        roi.BasisPoints);

    private static HistoricalMarketMetricsResponse ToMetricsResponse(HistoricalMarketMetricSummary metrics) => new(
        metrics.MedianNetRoiBasisPoints,
        metrics.RoiThresholdRates.Select(rate => new HistoricalRoiThresholdRateResponse(rate.ThresholdBasisPoints, rate.Percent)).ToArray(),
        metrics.PositiveNetRoiPercent,
        metrics.BuyPricePopulationCoefficientOfVariation,
        metrics.SellPricePopulationCoefficientOfVariation,
        metrics.SpreadRatioPopulationCoefficientOfVariation,
        metrics.MedianAggregateBuyQuantity,
        metrics.MedianAggregateSellQuantity,
        metrics.MinimumSideDepthPopulationCoefficientOfVariation,
        new HistoricalPriceRangeResponse(
            MoneyResponse.From(new Money(metrics.BuyPriceRange.MinimumCopper)),
            MoneyResponse.From(new Money(metrics.BuyPriceRange.MaximumCopper))),
        new HistoricalPriceRangeResponse(
            MoneyResponse.From(new Money(metrics.SellPriceRange.MinimumCopper)),
            MoneyResponse.From(new Money(metrics.SellPriceRange.MaximumCopper))),
        metrics.MaximumSellPriceDrawdownPercent);

    private static IResult NoStoreJson(int statusCode, object payload) => new NoStoreJsonResult(statusCode, payload);

    private sealed class NoStoreJsonResult(int statusCode, object payload) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            httpContext.Response.Headers.CacheControl = "no-store";
            await JsonSerializer.SerializeAsync(httpContext.Response.Body, payload, SerializerOptions, httpContext.RequestAborted).ConfigureAwait(false);
        }
    }

    private sealed record MoneyResponse(string Copper)
    {
        public static MoneyResponse From(Money value) => new(value.Copper.ToString(CultureInfo.InvariantCulture));
    }

    private sealed record HistoricalMarketAnalyticsSettingsResponse(
        int BidIncrementCopper,
        int ListUndercutCopper,
        IReadOnlyList<int> RoiThresholdBasisPoints,
        decimal MinimumObservedSpanPercent,
        IReadOnlyList<HistoricalMarketWindowPolicyResponse> Windows);

    private sealed record HistoricalMarketWindowPolicyResponse(int DurationDays, int MinimumEligibleObservationCount);

    private sealed record HistoricalNetRoiResponse(
        DateTimeOffset ObservedAtUtc,
        MoneyResponse NetProfit,
        MoneyResponse TotalCost,
        decimal BasisPoints);

    private sealed record HistoricalMarketWindowCoverageResponse(
        DateTimeOffset FromInclusiveUtc,
        DateTimeOffset ToInclusiveUtc,
        int RawObservationCount,
        int EligibleObservationCount,
        int ExcludedObservationCount,
        DateTimeOffset? FirstEligibleObservedAtUtc,
        DateTimeOffset? LastEligibleObservedAtUtc,
        decimal ObservedSpanPercent,
        double? LargestEligibleObservationGapSeconds);

    private sealed record HistoricalMarketWindowResponse(
        int DurationDays,
        HistoricalMarketWindowState State,
        HistoricalMarketWindowCoverageResponse Coverage,
        HistoricalMarketMetricsResponse? Metrics);

    private sealed record HistoricalMarketMetricsResponse(
        decimal MedianNetRoiBasisPoints,
        IReadOnlyList<HistoricalRoiThresholdRateResponse> RoiThresholdRates,
        decimal PositiveNetRoiPercent,
        double? BuyPricePopulationCoefficientOfVariation,
        double? SellPricePopulationCoefficientOfVariation,
        double? SpreadRatioPopulationCoefficientOfVariation,
        decimal MedianAggregateBuyQuantity,
        decimal MedianAggregateSellQuantity,
        double? MinimumSideDepthPopulationCoefficientOfVariation,
        HistoricalPriceRangeResponse BuyPriceRange,
        HistoricalPriceRangeResponse SellPriceRange,
        double MaximumSellPriceDrawdownPercent);

    private sealed record HistoricalRoiThresholdRateResponse(int ThresholdBasisPoints, decimal Percent);

    private sealed record HistoricalPriceRangeResponse(MoneyResponse MinimumCopper, MoneyResponse MaximumCopper);

    private sealed record HistoricalMarketAnalyticsResponse(
        int ItemId,
        DateTimeOffset AsOfUtc,
        bool IsFeeRoundingExternallyVerified,
        HistoricalMarketAnalyticsSettingsResponse Settings,
        HistoricalNetRoiResponse? LatestObservedNetRoi,
        IReadOnlyList<HistoricalMarketWindowResponse> Windows);
}
