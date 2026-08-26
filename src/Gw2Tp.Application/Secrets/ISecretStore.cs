namespace Gw2Tp.Application.Secrets;

/// <summary>
/// Retrieves and validates application credentials without making their values
/// available to application features or web responses.
/// </summary>
public interface ISecretStore
{
    ValueTask<SecretAvailability> GetGw2ApiCredentialAvailabilityAsync(
        CancellationToken cancellationToken = default);

    ValueTask EnsureGw2ApiCredentialAvailableAsync(
        CancellationToken cancellationToken = default);
}
