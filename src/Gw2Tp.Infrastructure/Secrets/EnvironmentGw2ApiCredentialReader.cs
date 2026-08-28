namespace Gw2Tp.Infrastructure.Secrets;

internal sealed class EnvironmentGw2ApiCredentialReader : IGw2ApiCredentialReader
{
    private readonly Func<string, string?> _readEnvironmentVariable;

    public EnvironmentGw2ApiCredentialReader()
        : this(Environment.GetEnvironmentVariable)
    {
    }

    internal EnvironmentGw2ApiCredentialReader(Func<string, string?> readEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(readEnvironmentVariable);
        _readEnvironmentVariable = readEnvironmentVariable;
    }

    public ValueTask<string?> ReadGw2ApiCredentialAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var credential = _readEnvironmentVariable(EnvironmentSecretStore.Gw2ApiCredentialEnvironmentVariable);
        return ValueTask.FromResult(string.IsNullOrWhiteSpace(credential) ? null : credential);
    }
}
