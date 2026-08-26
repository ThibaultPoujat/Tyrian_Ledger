using Gw2Tp.Application.Secrets;
using Microsoft.Extensions.Logging;

namespace Gw2Tp.Infrastructure.Secrets;

/// <summary>
/// Persistent local credential provider backed by the macOS Keychain.
/// </summary>
public sealed class MacOsKeychainSecretStore : ISecretStore
{
    private readonly IKeychainCredentialReader _credentialReader;
    private readonly ILogger<MacOsKeychainSecretStore> _logger;

    public MacOsKeychainSecretStore(ILogger<MacOsKeychainSecretStore> logger)
        : this(new MacOsKeychainCredentialReader(), logger)
    {
    }

    internal MacOsKeychainSecretStore(
        IKeychainCredentialReader credentialReader,
        ILogger<MacOsKeychainSecretStore> logger)
    {
        ArgumentNullException.ThrowIfNull(credentialReader);
        ArgumentNullException.ThrowIfNull(logger);

        _credentialReader = credentialReader;
        _logger = logger;
    }

    public async ValueTask<SecretAvailability> GetGw2ApiCredentialAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        return await GetCredentialOrNullAsync(cancellationToken) is null
            ? SecretAvailability.Unavailable
            : SecretAvailability.Available;
    }

    public async ValueTask EnsureGw2ApiCredentialAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        var credential = await GetCredentialOrNullAsync(cancellationToken);
        if (credential is null)
        {
            _logger.LogWarning("GW2 API credential is unavailable from the local OS secret store.");
            throw new LocalConfigurationException();
        }

        _logger.LogInformation("GW2 API credential resolved from the local OS secret store.");
    }

    private async ValueTask<string?> GetCredentialOrNullAsync(CancellationToken cancellationToken)
    {
        try
        {
            var credential = await _credentialReader.ReadGw2ApiCredentialAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(credential) ? null : credential;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Keychain/provider diagnostics can contain sensitive data. Return
            // only the stable application-level configuration state instead.
            return null;
        }
    }
}
