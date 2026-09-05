namespace Gw2Tp.Web.Hosting;

internal sealed class LocalRequestOriginProtectionMiddleware(RequestDelegate next)
{
    internal const string RequestHeader = "X-Tyrian-Ledger-Request";
    internal const string RequestHeaderValue = "1";

    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    public async Task InvokeAsync(HttpContext context, LocalRequestOriginValidator originValidator)
    {
        if (!SafeMethods.Contains(context.Request.Method)
            && (!originValidator.IsAllowed(context.Request) || !HasRequestHeader(context.Request)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "trusted_origin_required" });
            return;
        }

        await next(context);
    }

    private static bool HasRequestHeader(HttpRequest request)
    {
        var values = request.Headers[RequestHeader];
        return values.Count == 1
            && string.Equals(values[0], RequestHeaderValue, StringComparison.Ordinal);
    }
}
