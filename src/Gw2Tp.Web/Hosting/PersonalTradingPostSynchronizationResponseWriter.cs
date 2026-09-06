using System.Text.Json;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.PersonalTradingPost;

namespace Gw2Tp.Web.Hosting;

internal static class PersonalTradingPostSynchronizationResponseWriter
{
    internal static Task WriteAsync(HttpContext context, PersonalTradingPostSynchronizationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            outcome = result.IsSuccess ? "succeeded" : "failed",
            attemptedAtUtc = result.AttemptedAtUtc,
            completedTransactionCount = result.IsSuccess ? result.CompletedTransactionCount : (int?)null,
            currentOrderCount = result.IsSuccess ? result.CurrentOrderCount : (int?)null,
            historyCoverageStartUtc = result.IsSuccess ? result.HistoryCoverageStartUtc : null,
            historyCoverageEndUtc = result.IsSuccess ? result.HistoryCoverageEndUtc : null,
            error = ToWireError(result),
        }));
    }

    private static string? ToWireError(PersonalTradingPostSynchronizationResult result)
    {
        if (result.IsSuccess)
        {
            return null;
        }

        if (result.IsPersistenceFailure)
        {
            return "persistence_failure";
        }

        return result.ErrorCategory switch
        {
            Gw2ApiErrorCategory.CredentialNotConfigured => "credential_not_configured",
            Gw2ApiErrorCategory.CredentialUnavailable => "credential_unavailable",
            Gw2ApiErrorCategory.InvalidRequest => "invalid_request",
            Gw2ApiErrorCategory.Unauthorized => "unauthorized",
            Gw2ApiErrorCategory.Forbidden => "forbidden",
            Gw2ApiErrorCategory.NotFound => "not_found",
            Gw2ApiErrorCategory.RateLimited => "rate_limited",
            Gw2ApiErrorCategory.UpstreamUnavailable => "upstream_unavailable",
            Gw2ApiErrorCategory.TransportFailure => "transport_failure",
            Gw2ApiErrorCategory.InvalidPayload => "invalid_payload",
            Gw2ApiErrorCategory.IncompleteData => "incomplete_data",
            Gw2ApiErrorCategory.UnexpectedResponse => "unexpected_response",
            _ => "unexpected_response",
        };
    }
}
