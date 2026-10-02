using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.RateLimiting;
using Gw2Tp.Application.LocalData;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Infrastructure.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gw2Tp.Infrastructure.Gw2Api;

internal interface IGw2RequestScheduler
{
    Task<T> ScheduleAsync<T>(Gw2RequestKey requestKey,
        Func<CancellationToken, Task<Gw2ScheduledResult<T>>> sendAsync,
        CancellationToken cancellationToken);
}

internal sealed record Gw2SchedulerDiagnostics(int Pending, int Active, int PeakPending, int PeakActive,
    long Dispatches, long Rejections, long Promotions);

/// <summary>One outbound authority. Pending includes retry cooldown; only HTTP attempts own concurrency.</summary>
internal sealed class Gw2RequestScheduler : IGw2RequestScheduler, IDisposable
{
    private static readonly Meter Meter = new("TyrianLedger.Gw2Api");
    private static readonly Counter<long> RateLimitedCounter = Meter.CreateCounter<long>("gw2.api.rate_limited");
    private static readonly Counter<long> DispatchCounter = Meter.CreateCounter<long>("gw2.api.dispatches");
    private static readonly Histogram<double> WaitHistogram = Meter.CreateHistogram<double>("gw2.api.queue_wait_ms");
    private static readonly Histogram<int> DepthHistogram = Meter.CreateHistogram<int>("gw2.api.queue_depth");
    private readonly object gate = new();
    private readonly Dictionary<Gw2RequestKey, InFlightRequest> inFlight = [];
    private readonly List<InFlightRequest> ready = [];
    private readonly CancellationTokenSource shutdown = new();
    private readonly TokenBucketRateLimiter rateLimiter;
    private readonly Gw2ApiSchedulerOptions options;
    private readonly IGw2RequestDelay delay;
    private readonly ILogger logger;
    private readonly SafeTransportDiagnosticBuffer? diagnostics;
    private readonly IAccountWorkFence? fence;
    private readonly CancellationTokenRegistration stopping;
    private bool stopped;
    private bool rateWaiting;
    private int active;
    private int peakPending;
    private int peakActive;
    private int backgroundBypasses;
    private int refreshBypasses;
    private long sequence;
    private long dispatches;
    private long rejections;
    private long promotions;

    public Gw2RequestScheduler(IOptions<Gw2ApiSchedulerOptions> options, ILogger<Gw2RequestScheduler> logger,
        SafeTransportDiagnosticBuffer? diagnostics = null, IAccountWorkFence? fence = null,
        IHostApplicationLifetime? lifetime = null)
        : this(options?.Value ?? throw new ArgumentNullException(nameof(options)), SystemGw2RequestDelay.Instance,
            logger, diagnostics, fence, lifetime?.ApplicationStopping ?? default) { }

    internal Gw2RequestScheduler(Gw2ApiSchedulerOptions options, IGw2RequestDelay delay,
        ILogger? logger = null, SafeTransportDiagnosticBuffer? diagnostics = null,
        IAccountWorkFence? fence = null, CancellationToken applicationStopping = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.delay = delay ?? throw new ArgumentNullException(nameof(delay));
        if (!options.TryValidate(out var error))
            throw new OptionsValidationException(Gw2ApiSchedulerOptions.ConfigurationSectionName,
                typeof(Gw2ApiSchedulerOptions), [error]);
        this.options = options;
        this.logger = logger ?? NullLogger.Instance;
        this.diagnostics = diagnostics;
        this.fence = fence;
        rateLimiter = new(new TokenBucketRateLimiterOptions
        {
            TokenLimit = options.RateLimit.BurstSize,
            TokensPerPeriod = options.RateLimit.RefillTokensPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1), AutoReplenishment = true,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            // Only the dispatcher waits here. Logical work stays in the fair queue
            // until a token exists, so rate waits cannot reserve low-priority work.
            QueueLimit = 1,
        });
        if (fence is not null) fence.Invalidated += CancelObsoletePrivateWork;
        stopping = applicationStopping.Register(Stop);
    }

    public async Task<T> ScheduleAsync<T>(Gw2RequestKey requestKey,
        Func<CancellationToken, Task<Gw2ScheduledResult<T>>> sendAsync, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestKey);
        ArgumentNullException.ThrowIfNull(sendAsync);
        cancellationToken.ThrowIfCancellationRequested();
        var purpose = Gw2RequestPurposeScope.Current;
        var generation = requestKey.IsPrivate ? fence?.Current?.Generation : null;
        requestKey = requestKey with { Generation = generation };
        InFlightRequest work;
        var created = false;
        lock (gate)
        {
            if (stopped) throw new OperationCanceledException(shutdown.Token);
            if (requestKey.IsPrivate && generation is not null && generation != fence!.Generation)
                throw new AccountWorkRejectedException();
            if (!inFlight.TryGetValue(requestKey, out work!) || work.Token.IsCancellationRequested)
            {
                work = new(requestKey, purpose);
                inFlight[requestKey] = work;
                created = true;
                QueueAttempt(work);
                if (!WithinCapacity(work))
                {
                    Remove(work);
                    work.Dispose();
                    rejections++;
                    throw new Gw2RequestSchedulerCapacityExceededException();
                }
            }
            else if (purpose > work.Purpose)
            {
                if (work.State != RequestState.Running && Pending(purpose) >= ClassLimit(purpose))
                {
                    rejections++;
                    throw new Gw2RequestSchedulerCapacityExceededException();
                }
                // Promotion is monotonic for this shared logical request. Losing
                // the promoting waiter cannot cancel or duplicate surviving work.
                work.Purpose = purpose;
                promotions++;
            }
            work.Waiters++;
            ObserveDepth();
        }
        if (created) _ = CompleteAsync(work, sendAsync);
        try { return (T)await work.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
        finally
        {
            lock (gate)
            {
                work.Waiters--;
                if (work.Waiters == 0)
                {
                    if (!work.Completion.Task.IsCompleted) Cancel(work);
                    else if (work.Finished) work.Dispose();
                }
            }
        }
    }

    public Gw2SchedulerDiagnostics GetDiagnostics()
    {
        lock (gate) return new(Pending(), active, peakPending, peakActive, dispatches, rejections, promotions);
    }

    private async Task CompleteAsync<T>(InFlightRequest work, Func<CancellationToken, Task<Gw2ScheduledResult<T>>> send)
    {
        try
        {
            var attempt = 0;
            while (true)
            {
                // The permit itself is cancelled while queued. Once dispatched,
                // always enter the release finally, even if cancellation races
                // with its continuation; WaitAsync(token) could lose that lease.
                await work.Permit.Task.ConfigureAwait(false);
                Gw2ScheduledResult<T> result;
                try
                {
                    work.Token.ThrowIfCancellationRequested();
                    if (IsObsolete(work)) throw new AccountWorkRejectedException();
                    result = await send(work.Token).ConfigureAwait(false);
                    work.Token.ThrowIfCancellationRequested();
                }
                finally
                {
                    lock (gate) { active--; work.State = RequestState.Completed; Pump(); }
                }
                attempt++;
                if (result.RetryKind == Gw2RetryKind.RateLimited)
                {
                    RateLimitedCounter.Add(1);
                    logger.LogWarning("GW2 API request received HTTP 429; applying configured retry handling.");
                }
                var retry = GetRetryOptions(result.RetryKind);
                if (retry is null || attempt >= retry.MaxAttempts ||
                    (options.Retry.HonorServerRetryAfter && result.RetryKind == Gw2RetryKind.RateLimited &&
                     result.RetryAfter > TimeSpan.FromMilliseconds(options.Retry.MaxServerRetryAfterMs)))
                {
                    work.Completion.TrySetResult(result.Result!);
                    return;
                }
                lock (gate)
                {
                    work.State = RequestState.Cooling;
                    if (!WithinCapacity(work))
                    {
                        rejections++;
                        throw new Gw2RequestSchedulerCapacityExceededException();
                    }
                    ObserveDepth();
                }
                var backoff = GetRetryDelay(result, retry, attempt);
                diagnostics?.RecordMarketRetry(work.Key.Operation ?? "unknown", attempt, result.RetryKind, backoff);
                await delay.DelayAsync(backoff, work.Token).ConfigureAwait(false);
                lock (gate)
                {
                    work.Token.ThrowIfCancellationRequested();
                    QueueAttempt(work);
                    ObserveDepth();
                }
            }
        }
        catch (OperationCanceledException) when (work.Token.IsCancellationRequested)
        { work.Completion.TrySetCanceled(work.Token); }
        catch (Exception exception) { work.Completion.TrySetException(exception); }
        finally
        {
            lock (gate)
            {
                Remove(work);
                work.Finished = true;
                if (work.Waiters == 0) work.Dispose();
                Pump();
            }
        }
    }

    // All queue/state mutations are serialized by gate. Continuations run outside it.
    private void QueueAttempt(InFlightRequest work)
    {
        work.State = RequestState.Queued;
        work.Sequence = ++sequence;
        work.QueuedAt = Stopwatch.GetTimestamp();
        work.Permit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ready.Add(work);
        Pump();
    }

    private void Pump()
    {
        foreach (var obsolete in ready.Where(IsObsolete).ToArray()) Cancel(obsolete, obsolete: true, pump: false);
        while (!stopped && !rateWaiting && active < options.RateLimit.MaxConcurrentRequests && ready.Count > 0)
        {
            using var lease = rateLimiter.AttemptAcquire();
            if (!lease.IsAcquired)
            {
                rateWaiting = true;
                _ = WaitForRateAsync();
                return;
            }
            Dispatch();
        }
    }

    private async Task WaitForRateAsync()
    {
        // Yield out of the lock even if replenishment happens between try/acquire.
        await Task.Yield();
        try
        {
            using var lease = await rateLimiter.AcquireAsync(1, shutdown.Token).ConfigureAwait(false);
            lock (gate)
            {
                rateWaiting = false;
                if (lease.IsAcquired && !stopped && ready.Count > 0) Dispatch();
                Pump();
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (stopped) { }
    }

    private void Dispatch()
    {
        foreach (var obsolete in ready.Where(IsObsolete).ToArray()) Cancel(obsolete, obsolete: true, pump: false);
        if (ready.Count == 0) return;
        var background = ready.Where(value => value.Purpose == Gw2RequestPurpose.BackgroundResearch).MinBy(value => value.Sequence);
        var refresh = ready.Where(value => value.Purpose == Gw2RequestPurpose.AccountRefresh).MinBy(value => value.Sequence);
        var work = background is not null && backgroundBypasses >= 8 ? background
            : refresh is not null && refreshBypasses >= 8 ? refresh
            : ready.OrderByDescending(value => value.Purpose).ThenBy(value => value.Sequence).First();
        backgroundBypasses = background is null || work.Purpose == Gw2RequestPurpose.BackgroundResearch ? 0 : backgroundBypasses + 1;
        refreshBypasses = refresh is null || work.Purpose != Gw2RequestPurpose.ActionValidation ? 0 : refreshBypasses + 1;
        ready.Remove(work);
        work.State = RequestState.Running;
        active++;
        peakActive = Math.Max(peakActive, active);
        dispatches++;
        DispatchCounter.Add(1);
        WaitHistogram.Record(Stopwatch.GetElapsedTime(work.QueuedAt).TotalMilliseconds);
        work.Permit.TrySetResult();
    }

    private int Pending(Gw2RequestPurpose? purpose = null) => inFlight.Values.Count(value =>
        value.State is not (RequestState.Running or RequestState.Completed) &&
        (purpose is null || value.Purpose == purpose));
    private int ClassLimit(Gw2RequestPurpose purpose) => purpose switch
    {
        Gw2RequestPurpose.ActionValidation => options.Queue.ActionValidationLimit,
        Gw2RequestPurpose.BackgroundResearch => options.Queue.BackgroundResearchLimit,
        _ => options.Queue.AccountRefreshLimit,
    };
    private bool WithinCapacity(InFlightRequest work) => work.State == RequestState.Running ||
        (Pending() <= options.RateLimit.MaxQueuedRequests && Pending(work.Purpose) <= ClassLimit(work.Purpose));
    private void ObserveDepth()
    {
        var count = Pending();
        peakPending = Math.Max(peakPending, count);
        DepthHistogram.Record(count);
    }
    private void Remove(InFlightRequest work)
    {
        ready.Remove(work);
        work.State = RequestState.Completed;
        if (inFlight.TryGetValue(work.Key, out var current) && ReferenceEquals(current, work)) inFlight.Remove(work.Key);
    }
    private bool IsObsolete(InFlightRequest work) => work.Key.IsPrivate && work.Key.Generation is not null &&
        work.Key.Generation != fence?.Generation;
    private void Cancel(InFlightRequest work, bool obsolete = false, bool pump = true)
    {
        // Removing the exact entry frees its queue slot immediately, even if a
        // noncooperative transport has not acknowledged cancellation yet.
        Remove(work);
        work.Cancellation.Cancel();
        work.Permit.TrySetCanceled(work.Token);
        if (obsolete) work.Completion.TrySetException(new AccountWorkRejectedException());
        else work.Completion.TrySetCanceled(work.Token);
        if (pump) Pump();
    }
    private void CancelObsoletePrivateWork()
    {
        lock (gate)
        {
            foreach (var work in inFlight.Values.Where(IsObsolete).ToArray()) Cancel(work, obsolete: true, pump: false);
            Pump();
        }
    }
    private void Stop()
    {
        lock (gate)
        {
            if (stopped) return;
            stopped = true;
            shutdown.Cancel();
            foreach (var work in inFlight.Values.ToArray()) Cancel(work);
        }
    }
    public void Dispose()
    {
        Stop();
        stopping.Dispose();
        if (fence is not null) fence.Invalidated -= CancelObsoletePrivateWork;
        rateLimiter.Dispose();
    }
    private Gw2BackoffOptions? GetRetryOptions(Gw2RetryKind kind) => kind switch
    {
        Gw2RetryKind.RateLimited => options.Retry.On429,
        Gw2RetryKind.UpstreamUnavailable => options.Retry.On5xx,
        _ => null,
    };
    private TimeSpan GetRetryDelay<T>(Gw2ScheduledResult<T> result, Gw2BackoffOptions retry, int attempt)
    {
        if (result.RetryKind == Gw2RetryKind.RateLimited && options.Retry.HonorServerRetryAfter && result.RetryAfter is { } server)
            return server;
        return TimeSpan.FromMilliseconds(Math.Min((long)retry.InitialBackoffMs * (1L << Math.Min(attempt - 1, 30)), retry.MaxBackoffMs));
    }
    private enum RequestState { Idle, Queued, Running, Cooling, Completed }
    private sealed class InFlightRequest : IDisposable
    {
        internal InFlightRequest(Gw2RequestKey key, Gw2RequestPurpose purpose)
        {
            Key = key;
            Purpose = purpose;
            Token = Cancellation.Token;
        }
        internal Gw2RequestKey Key { get; }
        internal Gw2RequestPurpose Purpose { get; set; }
        internal CancellationTokenSource Cancellation { get; } = new();
        internal CancellationToken Token { get; }
        internal TaskCompletionSource<object> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Permit { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal RequestState State { get; set; }
        internal int Waiters { get; set; }
        internal bool Finished { get; set; }
        internal long Sequence { get; set; }
        internal long QueuedAt { get; set; }
        public void Dispose() => Cancellation.Dispose();
    }
}

internal sealed record Gw2RequestKey(string Value, string? Operation = null, bool IsPrivate = false, string? Generation = null);
internal sealed record Gw2ScheduledResult<T>(T Result, Gw2RetryKind RetryKind = Gw2RetryKind.None, TimeSpan? RetryAfter = null);
internal enum Gw2RetryKind { None, RateLimited, UpstreamUnavailable }
internal sealed class Gw2RequestSchedulerCapacityExceededException : Exception { }
internal interface IGw2RequestDelay { Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken); }
internal sealed class SystemGw2RequestDelay : IGw2RequestDelay
{
    public static readonly SystemGw2RequestDelay Instance = new();
    private SystemGw2RequestDelay() { }
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}
