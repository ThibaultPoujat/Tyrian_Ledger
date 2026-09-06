namespace Gw2Tp.Infrastructure.Persistence;

/// <summary>
/// Serializes application-owned SQLite work while a recovery operation can
/// replace the database file. SQLite still protects against external writers;
/// this gate prevents this process from writing the old file during a restore.
/// </summary>
internal interface ISqliteDatabaseGate
{
    ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default);
}

internal sealed class SqliteDatabaseGate : ISqliteDatabaseGate
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    public async ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}
