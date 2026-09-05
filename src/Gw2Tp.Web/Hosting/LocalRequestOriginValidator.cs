namespace Gw2Tp.Web.Hosting;

internal sealed class LocalRequestOriginValidator(
    LocalHostOptions options,
    IWebHostEnvironment environment)
{
    private readonly HashSet<string> trustedDevelopmentOrigins = options.TrustedDevelopmentOrigins
        .Select(origin =>
        {
            OriginNormalizer.TryNormalize(origin, out var normalizedOrigin);
            return normalizedOrigin;
        })
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal bool IsAllowed(HttpRequest request)
    {
        if (!OriginNormalizer.TryNormalize(request.Headers.Origin, out var requestOrigin))
        {
            return false;
        }

        var sameOrigin = $"{request.Scheme}://{request.Host.Value}";
        if (string.Equals(requestOrigin, sameOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return environment.IsDevelopment() && trustedDevelopmentOrigins.Contains(requestOrigin);
    }
}
