namespace Gw2Tp.Web.Hosting;

internal static class OriginNormalizer
{
    internal static bool TryNormalize(string? value, out string normalizedOrigin)
    {
        normalizedOrigin = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        normalizedOrigin = uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
        return true;
    }
}
