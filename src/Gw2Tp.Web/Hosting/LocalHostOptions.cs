using System.Net;

namespace Gw2Tp.Web.Hosting;

internal sealed class LocalHostOptions
{
    internal const string ConfigurationSection = "TyrianLedger:Host";
    internal const string DevelopmentCorsPolicy = "TrustedDevelopmentOrigins";

    public int Port { get; init; } = 5080;

    public string[] ListenAddresses { get; init; } = ["127.0.0.1", "::1"];

    public string[] AllowedHosts { get; init; } = ["localhost", "127.0.0.1", "[::1]"];

    public string[] TrustedDevelopmentOrigins { get; init; } = [];

    internal static LocalHostOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(ConfigurationSection);
        return new LocalHostOptions
        {
            Port = section.GetValue<int?>(nameof(Port)) ?? 5080,
            ListenAddresses = section.GetSection(nameof(ListenAddresses)).Get<string[]>() ?? ["127.0.0.1", "::1"],
            AllowedHosts = section.GetSection(nameof(AllowedHosts)).Get<string[]>() ?? ["localhost", "127.0.0.1", "[::1]"],
            TrustedDevelopmentOrigins = section.GetSection(nameof(TrustedDevelopmentOrigins)).Get<string[]>() ?? [],
        };
    }

    internal IReadOnlyList<IPAddress> GetListenAddresses()
    {
        return ListenAddresses
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(IPAddress.Parse)
            .ToArray();
    }
}
