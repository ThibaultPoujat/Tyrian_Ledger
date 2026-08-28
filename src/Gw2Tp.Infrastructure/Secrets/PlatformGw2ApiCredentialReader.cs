namespace Gw2Tp.Infrastructure.Secrets;

internal sealed class PlatformGw2ApiCredentialReader : IGw2ApiCredentialReader
{
    private readonly IPlatformSecretReader _platformReader;

    public PlatformGw2ApiCredentialReader(IPlatformSecretReader platformReader)
    {
        ArgumentNullException.ThrowIfNull(platformReader);
        _platformReader = platformReader;
    }

    public async ValueTask<string?> ReadGw2ApiCredentialAsync(CancellationToken cancellationToken = default)
    {
        var credential = await _platformReader.ReadGw2ApiCredentialAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(credential) ? null : credential;
    }
}
