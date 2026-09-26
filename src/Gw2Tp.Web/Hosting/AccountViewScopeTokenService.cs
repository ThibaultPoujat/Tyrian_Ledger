using System.Collections.Concurrent;

namespace Gw2Tp.Web.Hosting;

/// <summary>
/// Produces process-local opaque cache scopes. The browser can separate in-memory
/// views without receiving an ArenaNet account identifier or any credential.
/// </summary>
internal sealed class AccountViewScopeTokenService
{
    private readonly ConcurrentDictionary<string, string> tokens = new(StringComparer.Ordinal);

    public string GetToken(string accountScopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountScopeId);
        return tokens.GetOrAdd(accountScopeId, _ => Guid.NewGuid().ToString("N"));
    }
}
