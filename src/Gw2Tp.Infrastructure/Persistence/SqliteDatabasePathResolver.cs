using Microsoft.Extensions.Configuration;

namespace Gw2Tp.Infrastructure.Persistence;

internal sealed class SqliteDatabasePathResolver(IConfiguration configuration)
{
    internal const string ConfigurationKey = "TyrianLedger:Database:Path";

    public string Resolve()
    {
        var configuredPath = configuration[ConfigurationKey];
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (!Path.IsPathFullyQualified(configuredPath))
            {
                throw new InvalidOperationException($"{ConfigurationKey} must be an absolute file path.");
            }

            return Path.GetFullPath(configuredPath);
        }

        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException("The operating system did not provide a per-user application-data directory.");
        }

        return Path.Combine(localApplicationData, "Tyrian Ledger", "tyrian-ledger.db");
    }
}
