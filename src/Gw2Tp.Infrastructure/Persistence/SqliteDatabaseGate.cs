namespace Gw2Tp.Infrastructure.Persistence;

using Gw2Tp.Application.LocalData;
using Microsoft.Data.Sqlite;

/// <summary>
/// Serializes application-owned SQLite work while a recovery operation can
/// replace the database file. SQLite still protects against external writers;
/// this gate prevents this process from writing the old file during a restore.
/// </summary>
internal interface ISqliteDatabaseGate
{
    ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default);
    ValueTask<IAsyncDisposable> AcquirePrivateAsync(CancellationToken cancellationToken = default) => AcquireAsync(cancellationToken);
    void ValidateAccountScope(string accountScopeId) { }
    Task ValidateAccountProfileAsync(SqliteConnection connection, long profileId, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class SqliteDatabaseGate(IAccountWorkFence? fence = null) : ISqliteDatabaseGate
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    public void ValidateAccountScope(string accountScopeId)
    {
        if (fence is not null && !string.Equals(fence.Current?.AccountScope?.AccountId, accountScopeId, StringComparison.Ordinal))
            throw new AccountWorkRejectedException();
    }

    public async Task ValidateAccountProfileAsync(SqliteConnection connection, long profileId, CancellationToken cancellationToken)
    {
        if (fence is null) return;
        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT account_scope_id FROM account_profiles WHERE id = $id";
        query.Parameters.AddWithValue("$id", profileId);
        var scope = await query.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (scope is null) throw new AccountWorkRejectedException();
        ValidateAccountScope(scope);
    }

    public async ValueTask<IAsyncDisposable> AcquirePrivateAsync(CancellationToken cancellationToken = default)
    {
        var generationLease = fence is null ? null : await fence.AcquireCommitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var databaseLease = await AcquireAsync(cancellationToken).ConfigureAwait(false);
            return new PrivateLease(databaseLease, generationLease);
        }
        catch
        {
            if (generationLease is not null) await generationLease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class PrivateLease(IAsyncDisposable database, IAsyncDisposable? generation) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await database.DisposeAsync().ConfigureAwait(false);
            if (generation is not null) await generation.DisposeAsync().ConfigureAwait(false);
        }
    }

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

internal sealed class PersonalDataOperationGate : IPersonalDataOperationGate
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
