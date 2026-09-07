using System.Text.Json;
using System.Text.Json.Serialization;
using Gw2Tp.Application.Dashboard;

namespace Gw2Tp.Web.Hosting;

internal static class PersonalDashboardResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    internal static Task WriteAsync(HttpContext context, PersonalDashboard dashboard)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsync(JsonSerializer.Serialize(dashboard, SerializerOptions));
    }
}
