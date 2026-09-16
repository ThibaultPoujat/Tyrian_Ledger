using System.Text.Json;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;

namespace Gw2Tp.Web.Hosting;

/// <summary>
/// Deliberately small browser contract for a private account refresh. Detailed
/// owned-item and character facts stay in application/persistence boundaries
/// until the crafting UX ticket defines their display contract.
/// </summary>
internal static class AccountCraftingResponseWriter
{
    internal static Task WriteAsync(HttpContext context, Gw2ApiResult<AccountCraftingSnapshot> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsync(JsonSerializer.Serialize<object>(result.IsSuccess && result.Value is { } snapshot
            ? new
            {
                outcome = "succeeded",
                capturedAtUtc = snapshot.CapturedAtUtc,
                bank = Feature(snapshot.BankInventory),
                materials = Feature(snapshot.MaterialStorage),
                recipes = Feature(snapshot.RecipeUnlocks),
                crafting = Feature(snapshot.CharacterCrafting),
                error = (string?)null,
            }
            : new
            {
                outcome = "failed",
                capturedAtUtc = (DateTimeOffset?)null,
                bank = (object?)null,
                materials = (object?)null,
                recipes = (object?)null,
                crafting = (object?)null,
                error = ToWireError(result.ErrorCategory),
            }));
    }

    private static object Feature<T>(CraftingFeatureResult<IReadOnlyList<T>> feature) => new
    {
        availability = feature.Availability.ToString(),
        count = feature.Availability == CraftingFeatureAvailability.Available ? feature.Value?.Count : null,
        error = feature.ErrorCategory is null ? null : ToWireError(feature.ErrorCategory),
    };

    private static string ToWireError(Gw2ApiErrorCategory? error) => error switch
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
        _ => "unexpected_response",
    };
}
