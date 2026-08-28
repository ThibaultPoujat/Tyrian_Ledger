namespace Gw2Tp.Infrastructure.Secrets;

internal sealed class PreferredGw2ApiCredentialReader : IGw2ApiCredentialReader
{
    private readonly IGw2ApiCredentialReader _preferred;
    private readonly IGw2ApiCredentialReader _fallback;

    public PreferredGw2ApiCredentialReader(
        IGw2ApiCredentialReader preferred,
        IGw2ApiCredentialReader fallback)
    {
        ArgumentNullException.ThrowIfNull(preferred);
        ArgumentNullException.ThrowIfNull(fallback);
        _preferred = preferred;
        _fallback = fallback;
    }

    public async ValueTask<string?> ReadGw2ApiCredentialAsync(CancellationToken cancellationToken = default)
    {
        var preferredCredential = await _preferred.ReadGw2ApiCredentialAsync(cancellationToken);
        return preferredCredential ?? await _fallback.ReadGw2ApiCredentialAsync(cancellationToken);
    }
}
