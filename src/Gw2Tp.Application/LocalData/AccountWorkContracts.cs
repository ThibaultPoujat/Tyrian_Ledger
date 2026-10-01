using Gw2Tp.Application.PersonalTradingPost;

namespace Gw2Tp.Application.LocalData;

/// <summary>Trusted internal context; none of these identities is a credential or a browser token.</summary>
public sealed record AccountWorkContext(AccountScope? AccountScope, string CredentialSessionId,
    string StoreIncarnation, string Generation);

/// <summary>Host-owned admission, short commit leases and recovery transitions.</summary>
public interface IAccountWorkFence
{
    AccountWorkContext? Current { get; }
    string Generation { get; }
    event Action? Invalidated;
    Task InitializeAsync(CancellationToken cancellationToken = default);
    /// <summary>Captures a bundle before IO. Its writes/publications must explicitly acquire a commit lease.</summary>
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);
    Task BindAccountAsync(AccountScope account, CancellationToken cancellationToken = default);
    ValueTask<IAsyncDisposable> AcquireCommitAsync(CancellationToken cancellationToken = default);
    ValueTask<IAccountWorkTransition> QuiesceAsync(CancellationToken cancellationToken = default);
}

public interface IAccountWorkTransition : IAsyncDisposable
{
    Task PublishAsync(CancellationToken cancellationToken = default);
}

/// <summary>Safe rejection; never includes a key, account identity or persistence diagnostic.</summary>
public sealed class AccountWorkRejectedException() : Exception("Account work is no longer admissible.");
