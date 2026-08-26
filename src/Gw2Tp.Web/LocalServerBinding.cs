using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Gw2Tp.Web;

internal static class LocalServerBinding
{
    internal const string DefaultUrl = "http://127.0.0.1:5000";

    internal static string ResolveUrls(IConfiguration configuration)
    {
        var configuredUrls = configuration[WebHostDefaults.ServerUrlsKey];

        return string.IsNullOrWhiteSpace(configuredUrls)
            ? DefaultUrl
            : configuredUrls;
    }
}
