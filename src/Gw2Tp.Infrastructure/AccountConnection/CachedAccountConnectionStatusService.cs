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

    public async Task<AccountConnectionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (TryGetCachedStatus(out var status))
        {
            return status;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetCachedStatus(out status))
            {
                return status;
            }

            var generation = fence?.Generation;
            status = await _inner.GetStatusAsync(cancellationToken).ConfigureAwait(false);
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
        if (cachedGeneration == fence?.Generation && _cachedStatus is not null && _timeProvider.GetUtcNow() < _expiresAt)
        {
            status = _cachedStatus;
            return true;
        }

        status = null!;
        return false;
    }
}
