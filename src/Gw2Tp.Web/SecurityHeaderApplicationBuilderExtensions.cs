using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Gw2Tp.Web;

internal static class SecurityHeaderApplicationBuilderExtensions
{
    internal const string ContentSecurityPolicy = "default-src 'none'; base-uri 'none'; frame-ancestors 'none'";
    internal const string XContentTypeOptions = "nosniff";
    internal const string XFrameOptions = "DENY";
    internal const string ReferrerPolicy = "no-referrer";

    internal static IApplicationBuilder UseTyrianLedgerSecurityHeaders(this IApplicationBuilder application)
    {
        return application.Use(static async (context, next) =>
        {
            context.Response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;
            context.Response.Headers["X-Content-Type-Options"] = XContentTypeOptions;
            context.Response.Headers["X-Frame-Options"] = XFrameOptions;
            context.Response.Headers["Referrer-Policy"] = ReferrerPolicy;

            await next(context);
        });
    }
}
