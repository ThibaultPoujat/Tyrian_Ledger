using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gw2Tp.Infrastructure.Gw2Api;

internal interface IGw2RequestScheduler
{
    Task<T> ScheduleAsync<T>(
        Gw2RequestKey requestKey,
        Func<CancellationToken, Task<Gw2ScheduledResult<T>>> sendAsync,
        CancellationToken cancellationToken);
}

/// <summary>
/// Coordinates all outbound gateway requests: rate limiting, concurrent-work
/// bounds, deduplication, and bounded retries remain behind this boundary.
/// </summary>
internal sealed class Gw2RequestScheduler : IGw2RequestScheduler, IDisposable
{
    private static readonly Meter Meter = new("TyrianLedger.Gw2Api");
    private static readonly Counter<long> RateLimitedCounter = Meter.CreateCounter<long>("gw2.api.rate_limited");

    private readonly Gw2ApiSchedulerOptions _options;
    private readonly TokenBucketRateLimiter _rateLimiter;
    private readonly ConcurrencyLimiter _concurrencyLimiter;
    private readonly IGw2RequestDelay _delay;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<Gw2RequestKey, Task<object>> _inFlight = new();

    public Gw2RequestScheduler(
        IOptions<Gw2ApiSchedulerOptions> options,
        ILogger<Gw2RequestScheduler> logger)
        : this(
            options?.Value ?? throw new ArgumentNullException(nameof(options)),
            SystemGw2RequestDelay.Instance,
            logger ?? throw new ArgumentNullException(nameof(logger)))
    {
    }

    internal Gw2RequestScheduler(
        Gw2ApiSchedulerOptions options,
        IGw2RequestDelay delay,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(delay);

        if (!options.TryValidate(out var validationError))
        {
            throw new OptionsValidationException(
                Gw2ApiSchedulerOptions.ConfigurationSectionName,
                typeof(Gw2ApiSchedulerOptions),
                [validationError]);
        }

        _options = options;
        _delay = delay;
        _logger = logger ?? NullLogger.Instance;
        _rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = options.RateLimit.BurstSize,
            TokensPerPeriod = options.RateLimit.RefillTokensPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            AutoReplenishment = true,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = options.RateLimit.MaxConcurrentRequests,
        });
        _concurrencyLimiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = options.RateLimit.MaxConcurrentRequests,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = options.RateLimit.MaxQueuedRequests,
        });
    }

    public async Task<T> ScheduleAsync<T>(
        Gw2RequestKey requestKey,
        Func<CancellationToken, Task<Gw2ScheduledResult<T>>> sendAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestKey);
        ArgumentNullException.ThrowIfNull(sendAsync);

        // Callers may independently cancel their wait without cancelling the
        // shared network operation needed by other deduplicated callers.
        while (true)
        {
            if (!_inFlight.TryGetValue(requestKey, out var inFlight))
            {
                var completion = new TaskCompletionSource<object>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                if (_inFlight.TryAdd(requestKey, completion.Task))
                {
                    _ = CompleteInFlightAsync(requestKey, completion, sendAsync);
                    inFlight = completion.Task;
                }
                else
                {
                    continue;
                }
            }

            return (T)await inFlight.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _rateLimiter.Dispose();
        _concurrencyLimiter.Dispose();
    }

    private async Task CompleteInFlightAsync<T>(
        Gw2RequestKey requestKey,
        TaskCompletionSource<object> completion,
        Func<CancellationToken, Task<Gw2ScheduledResult<T>>> sendAsync)
    {
        try
        {
            var result = await ExecuteBoxedAsync(sendAsync).ConfigureAwait(false);
            completion.TrySetResult(result);
            RemoveInFlight(requestKey, completion.Task);
        }
        catch (OperationCanceledException)
        {
            completion.TrySetCanceled();
            RemoveInFlight(requestKey, completion.Task);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
            RemoveInFlight(requestKey, completion.Task);
        }
    }

    private async Task<object> ExecuteBoxedAsync<T>(
        Func<CancellationToken, Task<Gw2ScheduledResult<T>>> sendAsync)
    {
        var attempt = 0;

        while (true)
        {
            attempt++;
            var result = await ExecuteOnceAsync(sendAsync).ConfigureAwait(false);

            if (result.RetryKind == Gw2RetryKind.RateLimited)
            {
                RateLimitedCounter.Add(1);
                _logger.LogWarning("GW2 API request received HTTP 429; applying configured retry handling.");
            }

            var retryOptions = GetRetryOptions(result.RetryKind);
            if (retryOptions is null || attempt >= retryOptions.MaxAttempts)
            {
                return result.Result!;
            }

            var delay = GetRetryDelay(result, retryOptions, attempt);
            await _delay.DelayAsync(delay, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<Gw2ScheduledResult<T>> ExecuteOnceAsync<T>(
        Func<CancellationToken, Task<Gw2ScheduledResult<T>>> sendAsync)
    {
        using var concurrencyLease = await _concurrencyLimiter
            .AcquireAsync(1, CancellationToken.None)
            .ConfigureAwait(false);
        if (!concurrencyLease.IsAcquired)
        {
            throw new Gw2RequestSchedulerCapacityExceededException();
        }

        using var rateLimitLease = await _rateLimiter
            .AcquireAsync(1, CancellationToken.None)
            .ConfigureAwait(false);
        if (!rateLimitLease.IsAcquired)
        {
            throw new Gw2RequestSchedulerCapacityExceededException();
        }

        return await sendAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private void RemoveInFlight(Gw2RequestKey requestKey, Task<object> expectedTask)
    {
        if (_inFlight.TryGetValue(requestKey, out var currentTask) &&
            ReferenceEquals(currentTask, expectedTask))
        {
            _inFlight.TryRemove(requestKey, out _);
        }
    }

    private Gw2BackoffOptions? GetRetryOptions(Gw2RetryKind retryKind) => retryKind switch
    {
        Gw2RetryKind.RateLimited => _options.Retry.On429,
        Gw2RetryKind.UpstreamUnavailable => _options.Retry.On5xx,
        _ => null,
    };

    private TimeSpan GetRetryDelay<T>(
        Gw2ScheduledResult<T> result,
        Gw2BackoffOptions retryOptions,
        int attempt)
    {
        if (result.RetryKind == Gw2RetryKind.RateLimited &&
            _options.Retry.HonorServerRetryAfter &&
            result.RetryAfter is { } retryAfter)
        {
            return retryAfter;
        }

        var multiplier = 1L << Math.Min(attempt - 1, 30);
        var calculatedMilliseconds = Math.Min(
            (long)retryOptions.InitialBackoffMs * multiplier,
            retryOptions.MaxBackoffMs);
        return TimeSpan.FromMilliseconds(calculatedMilliseconds);
    }
}

internal sealed record Gw2RequestKey(string Value);

internal sealed record Gw2ScheduledResult<T>(
    T Result,
    Gw2RetryKind RetryKind = Gw2RetryKind.None,
    TimeSpan? RetryAfter = null);

internal enum Gw2RetryKind
{
    None,
    RateLimited,
    UpstreamUnavailable,
}

internal sealed class Gw2RequestSchedulerCapacityExceededException : Exception
{
}

internal interface IGw2RequestDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class SystemGw2RequestDelay : IGw2RequestDelay
{
    public static readonly SystemGw2RequestDelay Instance = new();

    private SystemGw2RequestDelay()
    {
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
