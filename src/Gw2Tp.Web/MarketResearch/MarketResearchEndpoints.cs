using Gw2Tp.Application.MarketHistory;

namespace Gw2Tp.Web.MarketResearch;

internal static class MarketResearchEndpoints
{
    public static IEndpointRouteBuilder MapMarketResearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/market-research/watchlist", async (
            IMarketWatchlistStore watchlistStore,
            IMarketSnapshotStore snapshotStore,
            HistoricalMarketAnalyticsCalculator calculator,
            CancellationToken cancellationToken) =>
        {
            var watchlist = await watchlistStore.ListAsync(cancellationToken);
            var items = await Task.WhenAll(watchlist
                .Where(item => item.SamplingClass == MarketSamplingClass.Watchlist)
                .Select(item => ToResponseAsync(item.ItemId, snapshotStore, calculator, cancellationToken)));

            return Results.Ok(new MarketResearchWatchlistResponse(
                MarketSamplingPolicy.MaximumTrackedItemCount,
                watchlist.Count,
                items));
        });
        endpoints.MapPost("/api/market-research/watchlist", async (
            UpdateMarketResearchWatchlistRequest request,
            IMarketWatchlistStore watchlistStore,
            CancellationToken cancellationToken) =>
        {
            if (request.ItemId <= 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["itemId"] = ["Item ID must be a positive whole number."],
                });
            }

            var existingItem = (await watchlistStore.ListAsync(cancellationToken))
                .SingleOrDefault(item => item.ItemId == request.ItemId);
            if (existingItem is null)
            {
                try
                {
                    await watchlistStore.AddAsync(
                        new MarketTrackedItem(request.ItemId, MarketSamplingClass.Watchlist),
                        cancellationToken);
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Conflict(new { message = exception.Message });
                }
            }
            else if (existingItem.SamplingClass == MarketSamplingClass.Background)
            {
                await watchlistStore.UpdateSamplingClassAsync(
                    request.ItemId,
                    MarketSamplingClass.Watchlist,
                    cancellationToken);
            }
            else
            {
                return Results.Conflict(new { message = "The market item is already on the research watchlist." });
            }

            return Results.NoContent();
        });
        endpoints.MapDelete("/api/market-research/watchlist/{itemId:int}", async (
            int itemId,
            IMarketWatchlistStore watchlistStore,
            CancellationToken cancellationToken) =>
        {
            var existingItem = (await watchlistStore.ListAsync(cancellationToken))
                .SingleOrDefault(item => item.ItemId == itemId);
            if (existingItem?.SamplingClass != MarketSamplingClass.Watchlist)
            {
                return Results.NotFound();
            }

            await watchlistStore.RemoveAsync(itemId, cancellationToken);
            return Results.NoContent();
        });

        return endpoints;
    }

    private static async Task<MarketResearchWatchlistItemResponse> ToResponseAsync(
        int itemId,
        IMarketSnapshotStore snapshotStore,
        HistoricalMarketAnalyticsCalculator calculator,
        CancellationToken cancellationToken)
    {
        var analytics = calculator.Calculate(await snapshotStore.ListPriceSnapshotsAsync(itemId, cancellationToken));
        return new MarketResearchWatchlistItemResponse(
            itemId,
            MarketResearchCoverageResponse.From(analytics.Coverage),
            MarketResearchPriceStatisticsResponse.From(analytics.BuyPrices),
            MarketResearchPriceStatisticsResponse.From(analytics.SellPrices),
            MarketResearchLiquidityResponse.From(analytics.BuyLiquidityStability),
            MarketResearchLiquidityResponse.From(analytics.SellLiquidityStability));
    }

    private sealed record UpdateMarketResearchWatchlistRequest(int ItemId);

    private sealed record MarketResearchWatchlistResponse(
        int MaximumTrackedItemCount,
        int TrackedItemCount,
        IReadOnlyList<MarketResearchWatchlistItemResponse> Items);

    private sealed record MarketResearchWatchlistItemResponse(
        int ItemId,
        MarketResearchCoverageResponse Coverage,
        MarketResearchPriceStatisticsResponse BuyPrices,
        MarketResearchPriceStatisticsResponse SellPrices,
        MarketResearchLiquidityResponse BuyLiquidity,
        MarketResearchLiquidityResponse SellLiquidity);

    private sealed record MarketResearchCoverageResponse(
        int ObservationCount,
        DateTimeOffset? FirstCapturedAtUtc,
        DateTimeOffset? LastCapturedAtUtc)
    {
        public static MarketResearchCoverageResponse From(HistoricalObservationCoverage coverage) => new(
            coverage.ObservationCount,
            coverage.FirstCapturedAtUtc,
            coverage.LastCapturedAtUtc);
    }

    private sealed record MarketResearchPriceStatisticsResponse(
        int ObservationCount,
        int? TenthPercentileCopper,
        int? MedianCopper,
        int? NinetiethPercentileCopper)
    {
        public static MarketResearchPriceStatisticsResponse From(HistoricalPriceStatistics statistics) => new(
            statistics.ObservationCount,
            statistics.TenthPercentileCopper,
            statistics.MedianCopper,
            statistics.NinetiethPercentileCopper);
    }

    private sealed record MarketResearchLiquidityResponse(int ObservationCount, decimal? CoefficientOfVariationPercent)
    {
        public static MarketResearchLiquidityResponse From(HistoricalLiquidityStability stability) => new(
            stability.ObservationCount,
            stability.CoefficientOfVariationPercent);
    }
}
