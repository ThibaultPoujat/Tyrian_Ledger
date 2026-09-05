using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Gw2Tp.Web.Hosting;

internal static class HealthResponseWriter
{
    internal static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";

        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status == HealthStatus.Healthy ? "healthy" : "unhealthy",
        }));
    }
}
