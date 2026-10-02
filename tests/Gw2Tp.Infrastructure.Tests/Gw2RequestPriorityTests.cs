using System.Net;
using System.Text;
using System.Threading.Channels;
using Gw2Tp.Application.LocalData;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.Secrets;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class Gw2RequestPriorityTests
{
    [Fact]
    public async Task Foreground_advances_background_and_coalesced_promotion_keeps_one_slot_after_cancellation()
    {
        using var scheduler = Create();
        var sends = new Sends();
        var running = Start(scheduler, sends, "running");
        var first = await sends.Next();
        var research = Start(scheduler, sends, "shared", Gw2RequestPurpose.BackgroundResearch);
        var other = Start(scheduler, sends, "other", Gw2RequestPurpose.BackgroundResearch);
        using var foreground = new CancellationTokenSource();
        var promoted = Start(scheduler, sends, "shared", Gw2RequestPurpose.ActionValidation, foreground.Token);
        Assert.Equal(2, scheduler.GetDiagnostics().Pending);
        Assert.Equal(1, scheduler.GetDiagnostics().Promotions);
        foreground.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => promoted);
        first.Finish();
        var next = await sends.Next();
        Assert.Equal("shared", next.Name);
        next.Finish();
        (await sends.Next()).Finish();
        await Task.WhenAll(running, research, other);
        Assert.Equal(3, scheduler.GetDiagnostics().Dispatches);
        Assert.Equal(0, scheduler.GetDiagnostics().Pending);
    }

    [Fact]
    public async Task Background_gets_a_turn_after_eight_higher_priority_dispatches()
    {
        using var scheduler = Create();
        var sends = new Sends();
        var tasks = new List<Task<int>> { Start(scheduler, sends, "running") };
        var attempt = await sends.Next();
        tasks.Add(Start(scheduler, sends, "background", Gw2RequestPurpose.BackgroundResearch));
        for (var i = 0; i < 12; i++) tasks.Add(Start(scheduler, sends, $"foreground-{i}", Gw2RequestPurpose.ActionValidation));
        for (var i = 0; i < 8; i++)
        {
            attempt.Finish(); attempt = await sends.Next();
            Assert.Equal($"foreground-{i}", attempt.Name);
        }
        attempt.Finish(); attempt = await sends.Next();
        Assert.Equal("background", attempt.Name);
        for (var i = 8; i < 12; i++) { attempt.Finish(); attempt = await sends.Next(); Assert.Equal($"foreground-{i}", attempt.Name); }
        attempt.Finish(); await Task.WhenAll(tasks);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Class_and_total_capacity_reject_without_leaking_or_cancelling_survivors(bool classLimit)
    {
        var options = Options();
        options.RateLimit.MaxQueuedRequests = classLimit ? 3 : 1;
        options.Queue.BackgroundResearchLimit = 1;
        using var scheduler = Create(options);
        var sends = new Sends();
        var active = Start(scheduler, sends, "running");
        var first = await sends.Next();
        var background = Start(scheduler, sends, "background", Gw2RequestPurpose.BackgroundResearch);
        await Assert.ThrowsAsync<Gw2RequestSchedulerCapacityExceededException>(() => Start(scheduler, sends, "overflow", Gw2RequestPurpose.BackgroundResearch));
        Task<int>? action = classLimit ? Start(scheduler, sends, "action", Gw2RequestPurpose.ActionValidation) : null;
        if (!classLimit) await Assert.ThrowsAsync<Gw2RequestSchedulerCapacityExceededException>(() => Start(scheduler, sends, "action", Gw2RequestPurpose.ActionValidation));
        Assert.Equal(classLimit ? 2 : 1, scheduler.GetDiagnostics().Pending);
        first.Finish();
        var next = await sends.Next();
        Assert.Equal(classLimit ? "action" : "background", next.Name);
        next.Finish();
        if (action is not null) { (await sends.Next()).Finish(); await action; }
        await Task.WhenAll(active, background);
        Assert.Equal(0, scheduler.GetDiagnostics().Active);
    }

    [Fact]
    public async Task Promotion_respects_class_capacity_and_keeps_existing_waiter()
    {
        var options = Options(); options.Queue.ActionValidationLimit = 1;
        using var scheduler = Create(options);
        var sends = new Sends();
        var active = Start(scheduler, sends, "running"); var first = await sends.Next();
        var shared = Start(scheduler, sends, "shared", Gw2RequestPurpose.BackgroundResearch);
        var action = Start(scheduler, sends, "action", Gw2RequestPurpose.ActionValidation);
        await Assert.ThrowsAsync<Gw2RequestSchedulerCapacityExceededException>(() => Start(scheduler, sends, "shared", Gw2RequestPurpose.ActionValidation));
        Assert.Equal(2, scheduler.GetDiagnostics().Pending);
        first.Finish(); (await sends.Next()).Finish(); (await sends.Next()).Finish();
        await Task.WhenAll(active, shared, action);
    }

    [Fact]
    public async Task Last_queued_waiter_and_shutdown_remove_work_before_send()
    {
        using var shutdown = new CancellationTokenSource();
        using var scheduler = new Gw2RequestScheduler(Options(), new RealDelay(), applicationStopping: shutdown.Token);
        var sends = new Sends();
        var active = Start(scheduler, sends, "running"); var first = await sends.Next();
        using var cancellation = new CancellationTokenSource();
        var queued = Start(scheduler, sends, "cancelled", token: cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(0, scheduler.GetDiagnostics().Pending);
        var stopped = Start(scheduler, sends, "stopped");
        shutdown.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopped);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        first.Finish();
        Assert.False(sends.HasAttempt);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Retry_cooldown_owns_bounded_slot_and_no_HTTP_permit_then_reenters_fair_queue(int retryKind)
    {
        var kind = (Gw2RetryKind)retryKind;
        var delay = new HeldDelay();
        using var scheduler = Create(delay: delay);
        var sends = new Sends();
        var retry = Start(scheduler, sends, "retry", Gw2RequestPurpose.BackgroundResearch);
        (await sends.Next()).Finish(kind, TimeSpan.FromSeconds(2));
        await delay.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(kind == Gw2RetryKind.RateLimited ? 2 : 1), delay.Duration);
        Assert.Equal(0, scheduler.GetDiagnostics().Active);
        Assert.Equal(1, scheduler.GetDiagnostics().Pending);
        var unrelated = Start(scheduler, sends, "unrelated"); var held = await sends.Next();
        var foreground = Start(scheduler, sends, "foreground", Gw2RequestPurpose.ActionValidation);
        delay.Release.TrySetResult();
        await WaitUntil(() => scheduler.GetDiagnostics().Pending == 2);
        held.Finish(); var next = await sends.Next(); Assert.Equal("foreground", next.Name); next.Finish();
        next = await sends.Next(); Assert.Equal("retry", next.Name); next.Finish();
        await Task.WhenAll(retry, unrelated, foreground);
    }

    [Fact]
    public async Task Retry_wait_cancellation_frees_capacity_and_never_sends_again()
    {
        var delay = new HeldDelay();
        using var scheduler = Create(delay: delay);
        var sends = new Sends(); using var cancel = new CancellationTokenSource();
        var retry = Start(scheduler, sends, "retry", token: cancel.Token);
        (await sends.Next()).Finish(Gw2RetryKind.RateLimited, TimeSpan.FromSeconds(2));
        await delay.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retry);
        delay.Release.TrySetResult();
        var recovery = Start(scheduler, sends, "recovery");
        var next = await sends.Next(); Assert.Equal("recovery", next.Name); next.Finish(); await recovery;
        Assert.Equal(2, scheduler.GetDiagnostics().Dispatches);
    }

    [Fact]
    public async Task Real_account_clear_invalidates_queued_private_work_and_new_generation_cannot_join_it()
    {
        var fence = new AccountWorkFence(new HostCredentialSource(new EmptyKey()), new MemoryIncarnation());
        await fence.InitializeAsync();
        using var scheduler = new Gw2RequestScheduler(Options(), new RealDelay(), fence: fence);
        var sends = new Sends();
        var active = Start(scheduler, sends, "running"); var first = await sends.Next();
        var old = fence.RunAsync(token => Start(scheduler, sends, "private", token: token, isPrivate: true));
        Assert.Equal(1, scheduler.GetDiagnostics().Pending);
        await using (var transition = await fence.QuiesceAsync()) { await transition.PublishAsync(); }
        await Assert.ThrowsAsync<AccountWorkRejectedException>(() => old);
        Assert.Equal(0, scheduler.GetDiagnostics().Pending);
        var fresh = fence.RunAsync(token => Start(scheduler, sends, "private", token: token, isPrivate: true));
        first.Finish(); var next = await sends.Next(); Assert.Equal("private", next.Name); next.Finish();
        await Task.WhenAll(active, fresh);
        Assert.Equal(2, scheduler.GetDiagnostics().Dispatches);
    }

    [Fact]
    public async Task Completed_public_cache_hit_bypasses_saturated_scheduler()
    {
        var options = Options(); options.RateLimit.MaxQueuedRequests = 0;
        using var scheduler = Create(options);
        var sends = new Sends();
        using var http = new HttpClient(new JsonHandler()) { BaseAddress = new("https://api.guildwars2.com/v2/") };
        var gateway = new BatchingGw2ApiClient(new Gw2ApiClient(http, scheduler));
        Assert.True((await gateway.GetItemMetadataAsync([1])).IsSuccess);
        var active = Start(scheduler, sends, "running"); var first = await sends.Next();
        Assert.True((await gateway.GetItemMetadataAsync([1])).IsSuccess);
        Assert.Equal(2, scheduler.GetDiagnostics().Dispatches);
        Assert.Equal(Gw2ApiErrorCategory.UpstreamUnavailable, (await gateway.GetItemMetadataAsync([2])).ErrorCategory);
        first.Finish(); await active;
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Negative_class_limits_fail_startup_validation(int field)
    {
        var options = Options();
        if (field == 0) options.Queue.ActionValidationLimit = -1;
        else if (field == 1) options.Queue.AccountRefreshLimit = -1;
        else options.Queue.BackgroundResearchLimit = -1;
        Assert.False(options.TryValidate(out var error)); Assert.Contains("Gw2Api:Queue", error);
    }

    [Fact]
    public async Task Actual_bounded_local_HTTP_load_trace_is_separate_from_barrier_policy_vectors()
    {
        var options = Options();
        options.RateLimit.MaxConcurrentRequests = 3;
        options.RateLimit.BurstSize = 5;
        options.RateLimit.MaxQueuedRequests = 60;
        options.Queue.BackgroundResearchLimit = 50;
        using var scheduler = Create(options);
        using var handler = new LoadHandler();
        using var http = new HttpClient(handler) { BaseAddress = new("https://api.guildwars2.com/v2/") };
        var gateway = new Gw2ApiClient(http, scheduler);
        var requests = new List<Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>>>();
        // Two overlapping 30-item synthetic scans share each active request.
        for (var pass = 0; pass < 2; pass++)
        for (var i = 1; i <= 30; i++)
        {
            using var purpose = Gw2RequestPurposeScope.Begin(Gw2RequestPurpose.BackgroundResearch);
            requests.Add(gateway.GetPricesAsync([i]));
        }
        for (var i = 31; i <= 38; i++)
        {
            using var purpose = Gw2RequestPurposeScope.Begin(Gw2RequestPurpose.ActionValidation);
            requests.Add(gateway.GetPricesAsync([i]));
        }
        Assert.All(await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(15)), result => Assert.True(result.IsSuccess));
        var stats = scheduler.GetDiagnostics();
        Assert.Equal(38, stats.Dispatches);
        Assert.InRange(stats.PeakActive, 1, 3);
        Assert.InRange(stats.PeakPending, 1, 60);
        Assert.InRange(handler.PeakActive, 1, 3);
        Assert.Equal(0, stats.Pending);
        var export = Environment.GetEnvironmentVariable("TYRIAN_LEDGER_P03B2_LOAD_TRACE");
        if (!string.IsNullOrWhiteSpace(export))
            await File.WriteAllTextAsync(export, System.Text.Json.JsonSerializer.Serialize(new
            {
                Evidence = "actual local HTTP transport with synthetic public responses; no ArenaNet or quota/latency claim",
                Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                Bounds = new { TotalPending = 60, BackgroundPending = 50, ConcurrentHTTP = 3, Burst = 5, RefillPerSecond = 100 },
                RequestedReads = 68, OutboundAttempts = handler.Starts.Count, Scheduler = stats,
                PeakObservedHTTP = handler.PeakActive, Starts = handler.Starts,
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class LoadHandler : HttpMessageHandler
    {
        private readonly object gate = new();
        private readonly System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
        private int active;
        public int PeakActive { get; private set; }
        public List<LoadStart> Starts { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            lock (gate)
            {
                active++;
                PeakActive = Math.Max(PeakActive, active);
                Starts.Add(new(Gw2RequestPurposeScope.Current.ToString(), elapsed.ElapsedMilliseconds));
            }
            try
            {
                await Task.Delay(5, token);
                return new(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") };
            }
            finally { lock (gate) active--; }
        }
    }
    private sealed record LoadStart(string Purpose, long ElapsedMilliseconds);

    [Fact]
    public async Task Refresh_fairness_cannot_push_background_beyond_eight_higher_dispatches()
    {
        using var scheduler = Create();
        var sends = new Sends();
        var tasks = new List<Task<int>> { Start(scheduler, sends, "running") };
        var attempt = await sends.Next();
        tasks.Add(Start(scheduler, sends, "refresh"));
        tasks.Add(Start(scheduler, sends, "background", Gw2RequestPurpose.BackgroundResearch));
        for (var i = 0; i < 10; i++) tasks.Add(Start(scheduler, sends, $"action-{i}", Gw2RequestPurpose.ActionValidation));
        for (var i = 0; i < 8; i++) { attempt.Finish(); attempt = await sends.Next(); Assert.Equal($"action-{i}", attempt.Name); }
        attempt.Finish(); attempt = await sends.Next(); Assert.Equal("background", attempt.Name);
        for (var i = 0; i < 3; i++) { attempt.Finish(); attempt = await sends.Next(); }
        attempt.Finish(); await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task Retry_after_above_local_wait_bound_returns_degraded_without_premature_retry()
    {
        var delay = new HeldDelay();
        using var scheduler = Create(delay: delay);
        var result = await scheduler.ScheduleAsync(new Gw2RequestKey("cooldown-exhausted"),
            _ => Task.FromResult(new Gw2ScheduledResult<Gw2ApiResult<int>>(
                Gw2ApiResult<int>.Failure(Gw2ApiErrorCategory.RateLimited), Gw2RetryKind.RateLimited, TimeSpan.FromDays(1))),
            CancellationToken.None);
        Assert.Equal(Gw2ApiErrorCategory.RateLimited, result.ErrorCategory);
        Assert.False(delay.Started.Task.IsCompleted);
        Assert.Equal(1, scheduler.GetDiagnostics().Dispatches);
    }

    [Fact]
    public async Task Rate_wait_selects_priority_at_token_availability_without_reserving_concurrency()
    {
        var options = Options(); options.RateLimit.BurstSize = 1; options.RateLimit.RefillTokensPerSecond = 1;
        using var scheduler = Create(options);
        var sends = new Sends();
        var first = Start(scheduler, sends, "first"); (await sends.Next()).Finish(); await first;
        var background = Start(scheduler, sends, "background", Gw2RequestPurpose.BackgroundResearch);
        var action = Start(scheduler, sends, "action", Gw2RequestPurpose.ActionValidation);
        Assert.Equal(0, scheduler.GetDiagnostics().Active);
        Assert.Equal(2, scheduler.GetDiagnostics().Pending);
        var attempt = await sends.Next(); Assert.Equal("action", attempt.Name); attempt.Finish();
        attempt = await sends.Next(); Assert.Equal("background", attempt.Name); attempt.Finish();
        await Task.WhenAll(action, background);
    }

    private static Task<int> Start(Gw2RequestScheduler scheduler, Sends sends, string name,
        Gw2RequestPurpose purpose = Gw2RequestPurpose.AccountRefresh, CancellationToken token = default, bool isPrivate = false)
    {
        using var scope = Gw2RequestPurposeScope.Begin(purpose);
        return scheduler.ScheduleAsync(new Gw2RequestKey(name, IsPrivate: isPrivate), cancellation => sends.Send(name, cancellation), token);
    }
    private static Gw2ApiSchedulerOptions Options() => new()
    {
        RateLimit = new() { BurstSize = 100, RefillTokensPerSecond = 100, MaxConcurrentRequests = 1, MaxQueuedRequests = 40 },
        Queue = new() { BackgroundResearchLimit = 20, ActionValidationLimit = 40, AccountRefreshLimit = 40 },
    };
    private static Gw2RequestScheduler Create(Gw2ApiSchedulerOptions? options = null, IGw2RequestDelay? delay = null) => new(options ?? Options(), delay ?? new RealDelay());
    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(1, timeout.Token);
    }
    private sealed class Sends
    {
        private readonly Channel<Attempt> attempts = Channel.CreateUnbounded<Attempt>();
        public bool HasAttempt => attempts.Reader.TryPeek(out _);
        public async Task<Gw2ScheduledResult<int>> Send(string name, CancellationToken token)
        {
            var attempt = new Attempt(name); await attempts.Writer.WriteAsync(attempt, token);
            return await attempt.Response.Task.WaitAsync(token);
        }
        public async Task<Attempt> Next() => await attempts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    }
    private sealed class Attempt(string name)
    {
        public string Name => name;
        public TaskCompletionSource<Gw2ScheduledResult<int>> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Finish(Gw2RetryKind kind = Gw2RetryKind.None, TimeSpan? backoff = null) => Response.TrySetResult(new(1, kind, backoff));
    }
    private sealed class RealDelay : IGw2RequestDelay
    { public Task DelayAsync(TimeSpan delay, CancellationToken token) => Task.Delay(delay, token); }
    private sealed class HeldDelay : IGw2RequestDelay
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TimeSpan Duration { get; private set; }
        public Task DelayAsync(TimeSpan duration, CancellationToken token) { Duration = duration; Started.TrySetResult(); return Release.Task.WaitAsync(token); }
    }
    private sealed class EmptyKey : IGw2ApiKeySource
    { public ValueTask<Gw2ApiKeyReadResult> ReadAsync(CancellationToken token = default) => ValueTask.FromResult(Gw2ApiKeyReadResult.NotConfigured); }
    private sealed class MemoryIncarnation : IStoreIncarnationStore
    {
        private string value = "initial";
        public Task<string> ReadOrCreateAsync(CancellationToken token) => Task.FromResult(value);
        public Task<string> RotateAsync(CancellationToken token) => Task.FromResult(value = Guid.NewGuid().ToString("N"));
    }
    private sealed class JsonHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("[{\"id\":1,\"name\":\"Item\"}]", Encoding.UTF8, "application/json") });
    }
}
