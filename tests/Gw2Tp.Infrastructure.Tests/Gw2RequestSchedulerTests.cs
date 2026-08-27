using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Infrastructure.Gw2Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class Gw2RequestSchedulerTests
{
    [Fact]
    public async Task Concurrent_identical_requests_share_one_outbound_request()
    {
        var responseSource = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHttpMessageHandler((_, _) => responseSource.Task);
        using var httpClient = CreateHttpClient(handler);
        using var scheduler = CreateScheduler();
        var apiClient = new Gw2ApiClient(httpClient, scheduler);

        var firstRequest = apiClient.GetPricesAsync([900001]);
        await handler.WaitForRequestAsync();

        var secondRequest = apiClient.GetPricesAsync([900001]);
        await Task.Yield();
        Assert.Equal(1, handler.RequestCount);

        responseSource.SetResult(CreateJsonResponse(HttpStatusCode.OK, "[]"));
        var results = await Task.WhenAll(firstRequest, secondRequest);

        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Completed_shared_request_is_removed_after_completion_when_all_callers_cancel()
    {
        var responseSource = new TaskCompletionSource<Gw2ScheduledResult<int>>();
        using var firstCancellationSource = new CancellationTokenSource();
        using var secondCancellationSource = new CancellationTokenSource();
        using var scheduler = CreateScheduler();
        var firstSendCount = 0;

        var firstCancelledRequest = scheduler.ScheduleAsync<int>(
            new Gw2RequestKey("prices:900001"),
            _ =>
            {
                Interlocked.Increment(ref firstSendCount);
                return responseSource.Task;
            },
            firstCancellationSource.Token);
        var secondCancelledRequest = scheduler.ScheduleAsync<int>(
            new Gw2RequestKey("prices:900001"),
            _ => throw new InvalidOperationException("The deduplicated send must not run."),
            secondCancellationSource.Token);

        firstCancellationSource.Cancel();
        secondCancellationSource.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstCancelledRequest);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondCancelledRequest);
        Assert.Equal(1, firstSendCount);

        responseSource.SetResult(new Gw2ScheduledResult<int>(1));

        var nextRequest = await scheduler.ScheduleAsync<int>(
            new Gw2RequestKey("prices:900001"),
            _ =>
            {
                Interlocked.Increment(ref firstSendCount);
                return Task.FromResult(new Gw2ScheduledResult<int>(2));
            },
            CancellationToken.None);

        Assert.Equal(2, nextRequest);
        Assert.Equal(2, firstSendCount);
    }

    [Fact]
    public async Task Scheduler_enforces_the_configured_concurrent_request_limit()
    {
        var firstResponse = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var outboundAttempt = 0;
        var handler = new DelegateHttpMessageHandler((_, _) =>
            Interlocked.Increment(ref outboundAttempt) == 1
                ? firstResponse.Task
                : Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, "[]")));
        var options = CreateOptions();
        options.RateLimit.MaxConcurrentRequests = 1;
        using var httpClient = CreateHttpClient(handler);
        using var scheduler = CreateScheduler(options);
        var apiClient = new Gw2ApiClient(httpClient, scheduler);

        var firstRequest = apiClient.GetPricesAsync([900001]);
        await handler.WaitForRequestAsync();

        var secondRequest = apiClient.GetPricesAsync([900002]);
        await Task.Yield();
        Assert.Equal(1, handler.RequestCount);

        firstResponse.SetResult(CreateJsonResponse(HttpStatusCode.OK, "[]"));
        var results = await Task.WhenAll(firstRequest, secondRequest);

        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Token_bucket_queue_capacity_exhaustion_returns_a_stable_failure()
    {
        var firstResponse = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var outboundAttempt = 0;
        var handler = new DelegateHttpMessageHandler((_, _) =>
            Interlocked.Increment(ref outboundAttempt) == 1
                ? firstResponse.Task
                : Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, "[]")));
        var options = CreateOptions();
        options.RateLimit.BurstSize = 1;
        options.RateLimit.RefillTokensPerSecond = 1;
        options.RateLimit.MaxConcurrentRequests = 2;
        options.RateLimit.MaxQueuedRequests = 0;
        using var httpClient = CreateHttpClient(handler);
        using var scheduler = CreateScheduler(options);
        var apiClient = new Gw2ApiClient(httpClient, scheduler);

        var firstRequest = apiClient.GetPricesAsync([900001]);
        await handler.WaitForRequestAsync();

        var queuedRequest = await apiClient.GetPricesAsync([900002]);

        Assert.False(queuedRequest.IsSuccess);
        Assert.Equal(Gw2ApiErrorCategory.UpstreamUnavailable, queuedRequest.ErrorCategory);
        Assert.Equal(1, handler.RequestCount);

        firstResponse.SetResult(CreateJsonResponse(HttpStatusCode.OK, "[]"));
        Assert.True((await firstRequest).IsSuccess);
    }

    [Fact]
    public async Task Concurrent_identical_requests_remain_deduplicated_during_429_backoff()
    {
        var delay = new BlockingDelay();
        var handler = new SequenceHttpMessageHandler(
            CreateResponseWithRetryAfter(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(2)),
            CreateJsonResponse(HttpStatusCode.OK, "[]"));
        using var httpClient = CreateHttpClient(handler);
        using var scheduler = CreateScheduler(delay: delay);
        var apiClient = new Gw2ApiClient(httpClient, scheduler);

        var firstRequest = apiClient.GetPricesAsync([900001]);
        await delay.WaitForDelayAsync();

        var secondRequest = apiClient.GetPricesAsync([900001]);
        await Task.Yield();
        Assert.Equal(1, handler.RequestCount);

        delay.Release();
        var results = await Task.WhenAll(firstRequest, secondRequest);

        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Rate_limited_response_honors_server_retry_after_before_retrying()
    {
        var delay = new RecordingDelay();
        var handler = new SequenceHttpMessageHandler(
            CreateResponseWithRetryAfter(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(2)),
            CreateJsonResponse(HttpStatusCode.OK, "[]"));
        using var httpClient = CreateHttpClient(handler);
        using var scheduler = CreateScheduler(delay: delay);
        var apiClient = new Gw2ApiClient(httpClient, scheduler);

        var result = await apiClient.GetPricesAsync([900001]);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal([TimeSpan.FromSeconds(2)], delay.Delays);
    }

    [Fact]
    public async Task Bare_rate_limited_responses_use_bounded_exponential_backoff()
    {
        var delay = new RecordingDelay();
        var handler = new SequenceHttpMessageHandler(
            CreateJsonResponse(HttpStatusCode.TooManyRequests, "{}"),
            CreateJsonResponse(HttpStatusCode.TooManyRequests, "{}"),
            CreateJsonResponse(HttpStatusCode.OK, "[]"));
        var options = CreateOptions(on429: new Gw2BackoffOptions
        {
            InitialBackoffMs = 100,
            MaxBackoffMs = 150,
            MaxAttempts = 3,
        });
        using var httpClient = CreateHttpClient(handler);
        using var scheduler = CreateScheduler(options, delay);
        var apiClient = new Gw2ApiClient(httpClient, scheduler);

        var result = await apiClient.GetPricesAsync([900001]);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, handler.RequestCount);
        Assert.Equal(
            [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(150)],
            delay.Delays);
    }

    [Fact]
    public async Task Persistent_rate_limits_stop_at_the_configured_attempt_limit()
    {
        var delay = new RecordingDelay();
        var handler = new SequenceHttpMessageHandler(
            CreateJsonResponse(HttpStatusCode.TooManyRequests, "{}"),
            CreateJsonResponse(HttpStatusCode.TooManyRequests, "{}"));
        var options = CreateOptions(on429: new Gw2BackoffOptions
        {
            InitialBackoffMs = 100,
            MaxBackoffMs = 100,
            MaxAttempts = 2,
        });
        using var httpClient = CreateHttpClient(handler);
        using var scheduler = CreateScheduler(options, delay);
        var apiClient = new Gw2ApiClient(httpClient, scheduler);

        var result = await apiClient.GetPricesAsync([900001]);

        Assert.False(result.IsSuccess);
        Assert.Equal(Gw2ApiErrorCategory.RateLimited, result.ErrorCategory);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal([TimeSpan.FromMilliseconds(100)], delay.Delays);
    }

    [Theory]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task Transient_upstream_errors_retry_with_the_configured_policy(int statusCode)
    {
        var delay = new RecordingDelay();
        var handler = new SequenceHttpMessageHandler(
            CreateJsonResponse((HttpStatusCode)statusCode, "{}"),
            CreateJsonResponse(HttpStatusCode.OK, "[]"));
        var options = CreateOptions(on5xx: new Gw2BackoffOptions
        {
            InitialBackoffMs = 50,
            MaxBackoffMs = 50,
            MaxAttempts = 2,
        });
        using var httpClient = CreateHttpClient(handler);
        using var scheduler = CreateScheduler(options, delay);
        var apiClient = new Gw2ApiClient(httpClient, scheduler);

        var result = await apiClient.GetPricesAsync([900001]);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal([TimeSpan.FromMilliseconds(50)], delay.Delays);
    }

    [Theory]
    [InlineData(401, Gw2ApiErrorCategory.Unauthorized)]
    [InlineData(403, Gw2ApiErrorCategory.Forbidden)]
    [InlineData(500, Gw2ApiErrorCategory.UpstreamUnavailable)]
    public async Task Permanent_failures_do_not_retry(
        int statusCode,
        Gw2ApiErrorCategory expectedError)
    {
        var delay = new RecordingDelay();
        var handler = new SequenceHttpMessageHandler(
            CreateJsonResponse((HttpStatusCode)statusCode, "{}"));
        using var httpClient = CreateHttpClient(handler);
        using var scheduler = CreateScheduler(delay: delay);
        var apiClient = new Gw2ApiClient(httpClient, scheduler);

        var result = await apiClient.GetPricesAsync([900001]);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.ErrorCategory);
        Assert.Equal(1, handler.RequestCount);
        Assert.Empty(delay.Delays);
    }

    [Fact]
    public void Scheduler_options_bind_configurable_limits_and_retry_policies()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gw2Api:RateLimit:BurstSize"] = "9",
                ["Gw2Api:RateLimit:RefillTokensPerSecond"] = "2",
                ["Gw2Api:RateLimit:MaxConcurrentRequests"] = "3",
                ["Gw2Api:RateLimit:MaxQueuedRequests"] = "4",
                ["Gw2Api:Retry:On429:InitialBackoffMs"] = "25",
                ["Gw2Api:Retry:On429:MaxBackoffMs"] = "50",
                ["Gw2Api:Retry:On429:MaxAttempts"] = "2",
                ["Gw2Api:Retry:HonorServerRetryAfter"] = "false",
                ["Gw2Api:Retry:On5xx:InitialBackoffMs"] = "40",
                ["Gw2Api:Retry:On5xx:MaxBackoffMs"] = "80",
                ["Gw2Api:Retry:On5xx:MaxAttempts"] = "4",
                ["Gw2Api:RequestTimeoutMs"] = "2500",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddTyrianLedgerGw2ApiClient(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<Gw2ApiSchedulerOptions>>().Value;

        Assert.Equal(9, options.RateLimit.BurstSize);
        Assert.Equal(2, options.RateLimit.RefillTokensPerSecond);
        Assert.Equal(3, options.RateLimit.MaxConcurrentRequests);
        Assert.Equal(4, options.RateLimit.MaxQueuedRequests);
        Assert.Equal(25, options.Retry.On429.InitialBackoffMs);
        Assert.Equal(50, options.Retry.On429.MaxBackoffMs);
        Assert.Equal(2, options.Retry.On429.MaxAttempts);
        Assert.False(options.Retry.HonorServerRetryAfter);
        Assert.Equal(40, options.Retry.On5xx.InitialBackoffMs);
        Assert.Equal(80, options.Retry.On5xx.MaxBackoffMs);
        Assert.Equal(4, options.Retry.On5xx.MaxAttempts);
        Assert.Equal(2500, options.RequestTimeoutMs);
    }

    [Fact]
    public void Invalid_scheduler_configuration_reports_the_invalid_setting()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gw2Api:RateLimit:MaxConcurrentRequests"] = "0",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddTyrianLedgerGw2ApiClient(configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<Gw2ApiSchedulerOptions>>().Value);

        Assert.Contains("Gw2Api:RateLimit:MaxConcurrentRequests", exception.Message);
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://api.guildwars2.com/v2/"),
    };

    private static Gw2RequestScheduler CreateScheduler(
        Gw2ApiSchedulerOptions? options = null,
        IGw2RequestDelay? delay = null) =>
        new(options ?? CreateOptions(), delay ?? new RecordingDelay());

    private static Gw2ApiSchedulerOptions CreateOptions(
        Gw2BackoffOptions? on429 = null,
        Gw2BackoffOptions? on5xx = null) => new()
        {
            RateLimit = new Gw2RateLimitOptions
            {
                BurstSize = 20,
                RefillTokensPerSecond = 20,
                MaxConcurrentRequests = 5,
                MaxQueuedRequests = 20,
            },
            Retry = new Gw2RetryOptions
            {
                On429 = on429 ?? new Gw2BackoffOptions
                {
                    InitialBackoffMs = 100,
                    MaxBackoffMs = 1_000,
                    MaxAttempts = 3,
                },
                HonorServerRetryAfter = true,
                On5xx = on5xx ?? new Gw2BackoffOptions
                {
                    InitialBackoffMs = 100,
                    MaxBackoffMs = 1_000,
                    MaxAttempts = 3,
                },
            },
            RequestTimeoutMs = 10_000,
        };

    private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string payload) => new(statusCode)
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage CreateResponseWithRetryAfter(
        HttpStatusCode statusCode,
        TimeSpan retryAfter)
    {
        var response = CreateJsonResponse(statusCode, "{}");
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
        return response;
    }

    private sealed class DelegateHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responseFactory;
        private readonly TaskCompletionSource _requestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestCount;

        public DelegateHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public int RequestCount => Volatile.Read(ref _requestCount);

        public Task WaitForRequestAsync() => _requestStarted.Task;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            _requestStarted.TrySetResult();
            return _responseFactory(request, cancellationToken);
        }
    }

    private sealed class SequenceHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        private readonly object _gate = new();
        private int _requestCount;

        public SequenceHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);

            lock (_gate)
            {
                return Task.FromResult(_responses.Dequeue());
            }
        }
    }

    private sealed class RecordingDelay : IGw2RequestDelay
    {
        private readonly List<TimeSpan> _delays = [];
        private readonly object _gate = new();

        public IReadOnlyList<TimeSpan> Delays
        {
            get
            {
                lock (_gate)
                {
                    return _delays.ToArray();
                }
            }
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _delays.Add(delay);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class BlockingDelay : IGw2RequestDelay
    {
        private readonly TaskCompletionSource _delayStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitForDelayAsync() => _delayStarted.Task;

        public void Release() => _release.TrySetResult();

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            _delayStarted.TrySetResult();
            return _release.Task;
        }
    }
}
