using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
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
        endpoints.MapGet("/api/market-history", async (
            HttpRequest request,
            IMarketHistoryStatusService statusService,
            CancellationToken cancellationToken) =>
        {
            if (!TryReadCoverageQuery(request.Query, out var query))
            {
                return NoStoreJson(StatusCodes.Status400BadRequest, new { error = "invalid_market_history_coverage_query" });
            }

            var status = await statusService.GetStatusAsync(query, cancellationToken).ConfigureAwait(false);
            return NoStoreJson(StatusCodes.Status200OK, status);
        });

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

    private static bool TryReadCoverageQuery(IQueryCollection values, out MarketHistoryCoverageQuery query)
    {
        query = new MarketHistoryCoverageQuery(null, null, null);
        if (!TryReadOptionalInt(values, "itemId", out var itemId) ||
            !TryReadOptionalUtc(values, "fromInclusiveUtc", out var fromInclusiveUtc) ||
            !TryReadOptionalUtc(values, "toInclusiveUtc", out var toInclusiveUtc))
        {
            return false;
        }

        try
        {
            query = new MarketHistoryCoverageQuery(itemId, fromInclusiveUtc, toInclusiveUtc);
            query.Validate();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryReadOptionalInt(IQueryCollection values, string key, out int? value)
    {
        value = null;
        if (!values.TryGetValue(key, out var rawValue))
        {
            return true;
        }

        if (rawValue.Count != 1 || !int.TryParse(rawValue[0], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryReadOptionalUtc(IQueryCollection values, string key, out DateTimeOffset? value)
    {
        value = null;
        if (!values.TryGetValue(key, out var rawValue))
        {
            return true;
        }

        if (rawValue.Count != 1 || !DateTimeOffset.TryParseExact(
                rawValue[0], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
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
