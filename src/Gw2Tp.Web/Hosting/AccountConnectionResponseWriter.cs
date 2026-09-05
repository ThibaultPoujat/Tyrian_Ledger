using System.Text.Json;
using Gw2Tp.Application.AccountConnection;

namespace Gw2Tp.Web.Hosting;

internal static class AccountConnectionResponseWriter
{
    internal static Task WriteAsync(HttpContext context, AccountConnectionStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";

        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            state = ToWireValue(status.State),
            grantedPermissions = status.GrantedPermissions,
            missingRequiredPermissions = status.MissingRequiredPermissions,
        }));
    }

    private static string ToWireValue(AccountConnectionState state) => state switch
    {
        AccountConnectionState.NotConfigured => "not_configured",
        AccountConnectionState.Valid => "valid",
        AccountConnectionState.Invalid => "invalid",
        AccountConnectionState.InsufficientPermissions => "insufficient_permissions",
        AccountConnectionState.Unavailable => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown account connection state."),
    };
}
