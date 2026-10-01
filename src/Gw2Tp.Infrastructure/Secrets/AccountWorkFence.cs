using Gw2Tp.Application.LocalData;
using Gw2Tp.Application.PersonalTradingPost;

namespace Gw2Tp.Infrastructure.Secrets;

internal sealed class HostCredentialSource(IGw2ApiKeySource source)
{
    internal IGw2ApiKeySource Source { get; } = source;
}

internal interface IStoreIncarnationStore
{
    Task<string> ReadOrCreateAsync(CancellationToken cancellationToken);
    Task<string> RotateAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Serializes observed transitions with private commits. AsyncLocal belongs to
/// this host service; raw credentials remain exclusively in Infrastructure memory.
/// </summary>
internal sealed class AccountWorkFence(HostCredentialSource credentialSource,
    IStoreIncarnationStore incarnationStore) : IAccountWorkFence
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim observations = new(1, 1);
    private readonly AsyncLocal<WorkSession?> ambient = new();
    private readonly IStoreIncarnationStore store = incarnationStore;
    private string generation = NewId();
    private string credentialSession = NewId();
    private string? incarnation;
    private Gw2ApiKeyReadResult? observedCredential;
    private AccountScope? account;
    private bool ready;

    public AccountWorkContext? Current => ambient.Value?.Context;
    public string Generation => Volatile.Read(ref generation);
    public event Action? Invalidated;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            incarnation = await store.ReadOrCreateAsync(cancellationToken).ConfigureAwait(false);
            generation = NewId();
            ready = true;
        }
        finally { gate.Release(); }
    }

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (ambient.Value is not null) return await work(cancellationToken).ConfigureAwait(false);
        await ObserveCredentialAsync(cancellationToken).ConfigureAwait(false);
        WorkSession session;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!ready || incarnation is null) throw new AccountWorkRejectedException();
            session = new(new(account, credentialSession, incarnation, generation), observedCredential!);
        }
        finally { gate.Release(); }
        ambient.Value = session;
        try
        {
            return await work(cancellationToken).ConfigureAwait(false);
        }
        finally { ambient.Value = null; }
    }

    internal ValueTask<Gw2ApiKeyReadResult> ReadCapturedCredentialAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ambient.Value is { } session
            ? ValueTask.FromResult(session.Credential)
            : credentialSource.Source.ReadAsync(cancellationToken);
    }

    private async Task ObserveCredentialAsync(CancellationToken cancellationToken)
    {
        // Order native-store observations; no generation/database lease during this IO.
        await observations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Gw2ApiKeyReadResult read;
            try { read = await credentialSource.Source.ReadAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { read = Gw2ApiKeyReadResult.Unavailable; }
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (observedCredential?.State != read.State ||
                    !string.Equals(observedCredential?.Value, read.Value, StringComparison.Ordinal))
                {
                    observedCredential = read;
                    credentialSession = NewId();
                    account = null;
                    Invalidate();
                }
            }
            finally { gate.Release(); }
        }
        finally { observations.Release(); }
    }

    public async Task BindAccountAsync(AccountScope value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value.AccountId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = RequireCurrent();
            if (session.Context.AccountScope is { } captured && captured != value)
            {
                account = value;
                Invalidate();
                throw new AccountWorkRejectedException();
            }
            if (account is not null && account != value)
            {
                account = value;
                Invalidate();
                throw new AccountWorkRejectedException();
            }
            account = value;
            session.Context = session.Context with { AccountScope = value };
        }
        finally { gate.Release(); }
    }

    public async ValueTask<IAsyncDisposable> AcquireCommitAsync(CancellationToken cancellationToken = default)
    {
        await ObserveCredentialAsync(cancellationToken).ConfigureAwait(false);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _ = RequireCurrent(); return new Lease(gate); }
        catch { gate.Release(); throw; }
    }

    private WorkSession RequireCurrent()
    {
        var session = ambient.Value;
        if (!ready || session is null || session.Context.Generation != generation ||
            session.Context.CredentialSessionId != credentialSession || session.Context.StoreIncarnation != incarnation)
            throw new AccountWorkRejectedException();
        return session;
    }

    public async ValueTask<IAccountWorkTransition> QuiesceAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ready = false;
            Invalidate();
            return new Transition(this);
        }
        catch { gate.Release(); throw; }
    }

    private void Invalidate()
    {
        generation = NewId();
        try { Invalidated?.Invoke(); }
        catch
        {
            ready = false;
            throw new AccountWorkRejectedException();
        }
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
    private sealed class WorkSession(AccountWorkContext context, Gw2ApiKeyReadResult credential)
    {
        internal AccountWorkContext Context { get; set; } = context;
        internal Gw2ApiKeyReadResult Credential { get; } = credential;
    }
    private sealed class Lease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { semaphore.Release(); return ValueTask.CompletedTask; }
    }
    private sealed class Transition(AccountWorkFence owner) : IAccountWorkTransition
    {
        public async Task PublishAsync(CancellationToken cancellationToken = default)
        {
            owner.incarnation = await owner.store.RotateAsync(cancellationToken).ConfigureAwait(false);
            owner.generation = NewId();
            owner.ready = true;
        }
        public ValueTask DisposeAsync() { owner.gate.Release(); return ValueTask.CompletedTask; }
    }
}

internal sealed class CapturedGw2ApiKeySource(AccountWorkFence fence) : IGw2ApiKeySource
{
    public ValueTask<Gw2ApiKeyReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
        fence.ReadCapturedCredentialAsync(cancellationToken);
}
