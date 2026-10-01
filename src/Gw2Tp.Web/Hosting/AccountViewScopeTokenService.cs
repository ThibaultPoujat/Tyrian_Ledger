using System.Collections.Concurrent;
using Gw2Tp.Application.LocalData;

namespace Gw2Tp.Web.Hosting;

/// <summary>Browser view identities are opaque and rotate with every host work generation.</summary>
internal sealed class AccountViewScopeTokenService
{
    private readonly ConcurrentDictionary<string, string> tokens = new(StringComparer.Ordinal);
    private readonly IAccountWorkFence? fence;
    public AccountViewScopeTokenService(IAccountWorkFence? fence = null)
    {
        this.fence = fence;
        if (fence is not null) fence.Invalidated += tokens.Clear;
    }
    public string GetToken(string accountScopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountScopeId);
        return tokens.GetOrAdd((fence?.Generation ?? "isolated") + ":" + accountScopeId, _ => Guid.NewGuid().ToString("N"));
    }
}
