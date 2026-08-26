using Gw2Tp.Application.Secrets;
using Microsoft.Extensions.Logging;

namespace Gw2Tp.Infrastructure.Secrets;

/// <summary>
/// Development and test-only credential provider. Registration limits its use
/// to Development and Testing environments.
/// </summary>
public sealed class EnvironmentSecretStore : ISecretStore
{
    public const string Gw2ApiCredentialEnvironmentVariable = "TYRIAN_LEDGER_GW2_API_KEY";

    private readonly Func<string, string?> _readEnvironmentVariable;
    private readonly ILogger<EnvironmentSecretStore> _logger;

    public EnvironmentSecretStore(ILogger<EnvironmentSecretStore> logger)
        : this(Environment.GetEnvironmentVariable, logger)
    {
    }

    internal EnvironmentSecretStore(
        Func<string, string?> readEnvironmentVariable,
        ILogger<EnvironmentSecretStore> logger)
    {
        ArgumentNullException.ThrowIfNull(readEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(logger);

        _readEnvironmentVariable = readEnvironmentVariable;
        _logger = logger;
    }

    public ValueTask<SecretAvailability> GetGw2ApiCredentialAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(GetCredentialOrNull() is null
            ? SecretAvailability.Unavailable
            : SecretAvailability.Available);
    }

    public ValueTask EnsureGw2ApiCredentialAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var credential = GetCredentialOrNull();
        if (credential is null)
        {
            _logger.LogWarning(
                "GW2 API credential is unavailable from the local development environment variable.");
            throw new LocalConfigurationException();
        }

        _logger.LogInformation(
            "GW2 API credential resolved from the local development environment variable.");
        return ValueTask.CompletedTask;
    }

    private string? GetCredentialOrNull()
    {
        var credential = _readEnvironmentVariable(Gw2ApiCredentialEnvironmentVariable);
        return string.IsNullOrWhiteSpace(credential) ? null : credential;
    }
}
