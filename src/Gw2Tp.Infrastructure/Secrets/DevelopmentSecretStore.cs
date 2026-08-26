using Gw2Tp.Application.Secrets;

namespace Gw2Tp.Infrastructure.Secrets;

/// <summary>
/// Prefers a transient development environment variable and otherwise uses the
/// persistent OS-backed provider.
/// </summary>
public sealed class DevelopmentSecretStore : ISecretStore
{
    private readonly ISecretStore _environmentSecretStore;
    private readonly ISecretStore _persistentSecretStore;

    public DevelopmentSecretStore(
        ISecretStore environmentSecretStore,
        ISecretStore persistentSecretStore)
    {
        ArgumentNullException.ThrowIfNull(environmentSecretStore);
        ArgumentNullException.ThrowIfNull(persistentSecretStore);

        _environmentSecretStore = environmentSecretStore;
        _persistentSecretStore = persistentSecretStore;
    }

    public async ValueTask<SecretAvailability> GetGw2ApiCredentialAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        var environmentAvailability = await _environmentSecretStore
            .GetGw2ApiCredentialAvailabilityAsync(cancellationToken);

        return environmentAvailability == SecretAvailability.Available
            ? SecretAvailability.Available
            : await _persistentSecretStore.GetGw2ApiCredentialAvailabilityAsync(cancellationToken);
    }

    public async ValueTask EnsureGw2ApiCredentialAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        var environmentAvailability = await _environmentSecretStore
            .GetGw2ApiCredentialAvailabilityAsync(cancellationToken);

        if (environmentAvailability == SecretAvailability.Available)
        {
            await _environmentSecretStore.EnsureGw2ApiCredentialAvailableAsync(cancellationToken);
            return;
        }

        await _persistentSecretStore.EnsureGw2ApiCredentialAvailableAsync(cancellationToken);
    }
}
