using Gw2Tp.Application.AccountConnection;
using Gw2Tp.Application.LocalData;

namespace Gw2Tp.Infrastructure.AccountConnection;

/// <summary>
/// Keeps validation results briefly in host memory. Browser responses remain
/// no-store; this only bounds repeated vault and authenticated upstream work.
/// </summary>
internal sealed class CachedAccountConnectionStatusService : IAccountConnectionStatusService
{
    internal static readonly TimeSpan StatusCacheDuration = TimeSpan.FromSeconds(30);

    private readonly IAccountConnectionStatusService _inner;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AccountConnectionStatus? _cachedStatus;
    private readonly IAccountWorkFence? fence;
    private string? cachedGeneration;
    private DateTimeOffset _expiresAt;

    public CachedAccountConnectionStatusService(
        IAccountConnectionStatusService inner,
        TimeProvider? timeProvider = null,
        IAccountWorkFence? fence = null)
    {
        this.fence = fence;
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<AccountConnectionStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        fence is null ? GetStatusCoreAsync(cancellationToken) : fence.RunAsync(GetStatusCoreAsync, cancellationToken);

    private async Task<AccountConnectionStatus> GetStatusCoreAsync(CancellationToken cancellationToken)
    {
        if (TryGetCachedStatus(out var status))
        {
            await using var read = fence is null ? null : await fence.AcquireCommitAsync(cancellationToken).ConfigureAwait(false);
            return status;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A queued caller must still own its captured context before reading
            // the cache or starting an authenticated validation bundle.
            await using (var read = fence is null ? null : await fence.AcquireCommitAsync(cancellationToken).ConfigureAwait(false))
            {
                if (TryGetCachedStatus(out status)) return status;
            }

            var generation = fence?.Current?.Generation;
            status = await _inner.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            await using var publication = fence is null ? null : await fence.AcquireCommitAsync(cancellationToken).ConfigureAwait(false);
            cachedGeneration = generation;
            _cachedStatus = status;
            _expiresAt = _timeProvider.GetUtcNow() + StatusCacheDuration;
            return status;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryGetCachedStatus(out AccountConnectionStatus status)
    {
        if (cachedGeneration == fence?.Current?.Generation && cachedGeneration == fence?.Generation &&
            _cachedStatus is not null && _timeProvider.GetUtcNow() < _expiresAt)
        {
            status = _cachedStatus;
            return true;
        }

        status = null!;
        return false;
    }
}
