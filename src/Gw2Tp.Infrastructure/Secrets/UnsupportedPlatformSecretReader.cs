namespace Gw2Tp.Infrastructure.Secrets;

internal sealed class UnsupportedPlatformSecretReader : IPlatformSecretReader
{
    public string StoreName => "unsupported operating-system secret store";

    public ValueTask<string?> ReadGw2ApiCredentialAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<string?>(null);
    }
}
