using System.Text.Json;
using System.Text.Json.Serialization;
using Gw2Tp.Application.MarketHistory;

namespace Gw2Tp.Web.Hosting;

internal static class MarketHistoryCollectorEndpoints
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    internal static IEndpointRouteBuilder MapMarketHistoryCollectorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/market-history/collector", (IMarketHistoryCollector collector) =>
            NoStoreJson(StatusCodes.Status200OK, ToHealthResponse(collector.GetHealth())));

        endpoints.MapPost("/api/market-history/collector/run", async (IMarketHistoryCollector collector, CancellationToken cancellationToken) =>
        {
            var run = await collector.CollectNowAsync(cancellationToken).ConfigureAwait(false);
            var statusCode = run.Outcome == MarketHistoryCollectionOutcome.Succeeded
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable;
            return NoStoreJson(statusCode, new
            {
                outcome = run.Outcome,
                trackedItemCount = run.TrackedItemCount,
                dueItemCount = run.DueItemCount,
                appendedPriceObservationCount = run.AppendedPriceObservationCount,
                appendedOrderBookSnapshotCount = run.AppendedOrderBookSnapshotCount,
                error = run.ErrorCategory,
                health = ToHealthResponse(collector.GetHealth()),
            });
        });

        return endpoints;
    }

    private static object ToHealthResponse(MarketHistoryCollectorHealth health) => new
    {
        state = health.State,
        lastSuccessfulCaptureAtUtc = health.LastSuccessfulCaptureAtUtc,
        lastFailure = health.LastFailure,
        failureCount = health.FailureCount,
        trackedItemCount = health.TrackedItemCount,
        nextRunAtUtc = health.NextRunAtUtc,
    };

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
}
