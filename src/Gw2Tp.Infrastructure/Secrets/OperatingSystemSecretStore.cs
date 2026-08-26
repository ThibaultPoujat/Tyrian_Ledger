using Gw2Tp.Application.Secrets;
using Microsoft.Extensions.Logging;

namespace Gw2Tp.Infrastructure.Secrets;

/// <summary>
/// Retrieves the local credential through the current operating system's
/// secret service without exposing its value outside Infrastructure.
/// </summary>
public sealed class OperatingSystemSecretStore : ISecretStore
{
    private readonly IPlatformSecretReader _credentialReader;
    private readonly ILogger<OperatingSystemSecretStore> _logger;

    internal OperatingSystemSecretStore(
        IPlatformSecretReader credentialReader,
        ILogger<OperatingSystemSecretStore> logger)
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
            _logger.LogWarning(
                "GW2 API credential is unavailable from {SecretStore}.",
                _credentialReader.StoreName);
            throw new LocalConfigurationException();
        }

        _logger.LogInformation(
            "GW2 API credential resolved from {SecretStore}.",
            _credentialReader.StoreName);
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
            // OS secret-service diagnostics can contain sensitive data. Return
            // only the stable application-level configuration state instead.
            return null;
        }
    }
}
