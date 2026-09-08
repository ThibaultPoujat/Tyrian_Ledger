namespace Gw2Tp.Web.Hosting;

internal sealed class LocalRequestOriginProtectionMiddleware(RequestDelegate next)
{
    internal const string RequestHeader = "X-Tyrian-Ledger-Request";
    internal const string RequestHeaderValue = "1";

    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    private static readonly PathString AccountConnectionPath = "/api/account-connection";
    private static readonly PathString LiveMarketScannerPath = "/api/live-market-scanner";

    public async Task InvokeAsync(HttpContext context, LocalRequestOriginValidator originValidator)
    {
        var isUnsafeRequest = !SafeMethods.Contains(context.Request.Method);
        var isProtectedRead = (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            && (IsProtectedPath(context.Request.Path, AccountConnectionPath) ||
                IsProtectedPath(context.Request.Path, LiveMarketScannerPath));
        var hasOrigin = context.Request.Headers.Origin.Count > 0;
        var unsafeRequestDenied = isUnsafeRequest
            && (!originValidator.IsAllowed(context.Request) || !HasRequestHeader(context.Request));
        var protectedReadDenied = isProtectedRead
            && (!HasRequestHeader(context.Request)
                || (hasOrigin && !originValidator.IsAllowed(context.Request)));
        if (unsafeRequestDenied || protectedReadDenied)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "trusted_origin_required" });
            return;
        }

        await next(context);
    }

    // A custom request header forces a cross-origin browser request through
    // CORS preflight. When Origin is present, validate it here as defence in
    // depth; same-origin browsers do not consistently emit Origin for GET.
    internal static bool HasRequestHeader(HttpRequest request)
    {
        var values = request.Headers[RequestHeader];
        return values.Count == 1
            && string.Equals(values[0], RequestHeaderValue, StringComparison.Ordinal);
    }

    private static bool IsProtectedPath(PathString requestPath, PathString protectedPath) =>
        string.Equals(
            requestPath.Value?.TrimEnd('/'),
            protectedPath.Value,
            StringComparison.OrdinalIgnoreCase);
}
