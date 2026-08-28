namespace Gw2Tp.Infrastructure.Secrets;

/// <summary>
/// Infrastructure-only credential access. The credential must only be used to
/// construct authenticated requests inside a typed GW2 gateway.
/// </summary>
internal interface IGw2ApiCredentialReader
{
    ValueTask<string?> ReadGw2ApiCredentialAsync(CancellationToken cancellationToken = default);
}
