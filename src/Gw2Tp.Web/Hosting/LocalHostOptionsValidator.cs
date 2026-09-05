using System.Net;

namespace Gw2Tp.Web.Hosting;

internal static class LocalHostOptionsValidator
{
    private static readonly string[] UnsupportedServerConfigurationKeys =
    [
        "urls",
        "http_ports",
        "https_ports",
    ];

    internal static void ValidateAndThrow(LocalHostOptions options, IConfiguration configuration)
    {
        if (options.Port is < 0 or > 65535)
        {
            throw new InvalidOperationException("TyrianLedger:Host:Port must be between 0 and 65535.");
        }

        var listenAddresses = options.ListenAddresses
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .ToArray();
        if (listenAddresses.Length == 0)
        {
            throw new InvalidOperationException("At least one explicit loopback listen address is required.");
        }

        foreach (var addressText in listenAddresses)
        {
            if (!IPAddress.TryParse(addressText, out var address) || !IPAddress.IsLoopback(address))
            {
                throw new InvalidOperationException(
                    $"Listen address '{addressText}' is not an explicit IPv4 or IPv6 loopback address.");
            }
        }

        if (options.AllowedHosts.Length == 0 || options.AllowedHosts.Any(IsWildcardHost))
        {
            throw new InvalidOperationException("TyrianLedger:Host:AllowedHosts must be a non-wildcard allowlist.");
        }

        foreach (var origin in options.TrustedDevelopmentOrigins)
        {
            if (!OriginNormalizer.TryNormalize(origin, out _))
            {
                throw new InvalidOperationException(
                    $"Trusted development origin '{origin}' must be an exact HTTP(S) origin without credentials or a path.");
            }
        }

        foreach (var key in UnsupportedServerConfigurationKeys)
        {
            if (!string.IsNullOrWhiteSpace(configuration[key]))
            {
                throw new InvalidOperationException(
                    $"The standard '{key}' server override is disabled. Configure TyrianLedger:Host with explicit loopback addresses instead.");
            }
        }

        if (configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
        {
            throw new InvalidOperationException(
                "Kestrel endpoint overrides are disabled. Configure TyrianLedger:Host with explicit loopback addresses instead.");
        }
    }

    private static bool IsWildcardHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Contains('*', StringComparison.Ordinal))
        {
            return true;
        }

        var normalizedHost = new HostString(host).ToUriComponent();
        return normalizedHost is "0.0.0.0" or "[::]";
    }
}
