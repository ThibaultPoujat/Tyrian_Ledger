using System.Net;
using System.Text;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Infrastructure.AccountConnection;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.PersonalTradingPost;
using Gw2Tp.Infrastructure.Secrets;
using Gw2Tp.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class PersonalTradingPostGatewayTests
{
    private const string SyntheticKey = "synthetic-gw2-api-key-that-must-not-cross-the-boundary";

    [Theory]
    [InlineData("current/buys", "current-buys.json", 910000001L, 850, false)]
    [InlineData("current/sells", "current-sells.json", 910000002L, 905, false)]
    [InlineData("history/buys", "history-buys.json", 910000003L, 1200, true)]
    [InlineData("history/sells", "history-sells.json", 910000004L, 1500, true)]
    public async Task Personal_transaction_reads_use_typed_authenticated_paged_requests_and_map_fixtures(
        string endpoint,
        string fixtureName,
        long expectedTransactionId,
        int expectedPriceInCopper,
        bool hasPurchasedTimestamp)
    {
        var handler = new RecordingHandler(_ => CreatePagedJsonResponse(HttpStatusCode.OK, LoadTransactionFixture(fixtureName)));
        using var httpClient = CreateHttpClient(handler);
        var scheduler = new ImmediateRequestScheduler();
        var gateway = new PersonalTradingPostGateway(new FixedKeySource(SyntheticKey), httpClient, scheduler);

        var result = await GetTransactionsAsync(gateway, endpoint, page: 3);

        Assert.True(result.IsSuccess);
        var transactionPage = Assert.IsType<PersonalTransactionPage>(result.Value);
        var transaction = Assert.Single(transactionPage.Transactions);
        Assert.Equal(expectedTransactionId, transaction.TransactionId);
        Assert.Equal(expectedPriceInCopper, transaction.PriceInCopper);
        Assert.Equal(TimeSpan.Zero, transaction.CreatedAtUtc.Offset);
        Assert.Equal(hasPurchasedTimestamp, transaction.PurchasedAtUtc is not null);
        Assert.Equal(3, transactionPage.PageNumber);
        Assert.Equal(50, transactionPage.PageSize);
        Assert.Equal(4, transactionPage.PageCount);
        Assert.Equal(151, transactionPage.ResultTotal);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"/v2/commerce/transactions/{endpoint}", request.Uri.AbsolutePath);
        Assert.Equal("3", GetQueryParameters(request.Uri)["page"]);
        Assert.Equal(PersonalTradingPostGateway.SchemaVersion, GetQueryParameters(request.Uri)["v"]);
        Assert.DoesNotContain("access_token", GetQueryParameters(request.Uri).Keys);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal(SyntheticKey, request.AuthorizationParameter);
        var requestKey = Assert.Single(scheduler.RequestKeys).Value;
        Assert.Equal($"personal/transactions/{endpoint}/page-3/credential-scope-1", requestKey);
        Assert.DoesNotContain(SyntheticKey, requestKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Account_scope_maps_only_the_persistent_account_id()
    {
        var handler = new RecordingHandler(_ => CreateJsonResponse(HttpStatusCode.OK, LoadFixture("gw2/account/scope.json")));
        using var httpClient = CreateHttpClient(handler);
        var scheduler = new ImmediateRequestScheduler();
        var gateway = new PersonalTradingPostGateway(new FixedKeySource(SyntheticKey), httpClient, scheduler);

        var result = await gateway.GetAccountScopeAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("synthetic-account-guid-900001", result.Value?.AccountId);
        Assert.DoesNotContain("mutable name", result.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v2/account", request.Uri.AbsolutePath);
        Assert.Equal(PersonalTradingPostGateway.SchemaVersion, GetQueryParameters(request.Uri)["v"]);
        Assert.Equal("personal/account/credential-scope-1", Assert.Single(scheduler.RequestKeys).Value);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, Gw2ApiErrorCategory.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, Gw2ApiErrorCategory.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests, Gw2ApiErrorCategory.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, Gw2ApiErrorCategory.UpstreamUnavailable)]
    public async Task Authenticated_http_failures_map_to_stable_application_categories(
        HttpStatusCode statusCode,
        Gw2ApiErrorCategory expectedError)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(statusCode));
        using var httpClient = CreateHttpClient(handler);
        var gateway = new PersonalTradingPostGateway(new FixedKeySource(SyntheticKey), httpClient, new ImmediateRequestScheduler());

        var result = await gateway.GetCurrentBuyOrdersAsync(0);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.ErrorCategory);
    }

    [Fact]
    public async Task Partial_malformed_and_incomplete_completed_history_responses_map_to_stable_failures()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            CreatePagedJsonResponse(HttpStatusCode.PartialContent, "[]"),
            CreatePagedJsonResponse(HttpStatusCode.OK, "[{\"id\":1,\"item_id\":2,\"price\":3,\"quantity\":1,\"created\":\"2026-09-06T00:00:00Z\"}]"),
            CreatePagedJsonResponse(HttpStatusCode.OK, "{\"not\":\"an array\"}"),
        ]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        using var httpClient = CreateHttpClient(handler);
        var gateway = new PersonalTradingPostGateway(new FixedKeySource(SyntheticKey), httpClient, new ImmediateRequestScheduler());

        var partial = await gateway.GetCompletedBuyHistoryAsync(3);
        var missingPurchaseTimestamp = await gateway.GetCompletedBuyHistoryAsync(3);
        var malformed = await gateway.GetCompletedBuyHistoryAsync(3);

        Assert.Equal(Gw2ApiErrorCategory.IncompleteData, partial.ErrorCategory);
        Assert.Equal(Gw2ApiErrorCategory.InvalidPayload, missingPurchaseTimestamp.ErrorCategory);
        Assert.Equal(Gw2ApiErrorCategory.InvalidPayload, malformed.ErrorCategory);
    }

    [Fact]
    public async Task Omitted_numeric_fields_non_timestamp_dates_and_inconsistent_page_headers_are_invalid_payloads()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            CreatePagedJsonResponse(HttpStatusCode.OK, "[{\"id\":1,\"item_id\":2,\"quantity\":1,\"created\":\"2026-09-06T00:00:00Z\"}]"),
            CreatePagedJsonResponse(HttpStatusCode.OK, "[{\"id\":1,\"item_id\":2,\"price\":3,\"quantity\":1,\"created\":\"2026-09-06\"}]"),
            CreatePagedJsonResponse(HttpStatusCode.OK, "[{\"id\":1,\"item_id\":2,\"price\":3,\"quantity\":1,\"created\":\"2026-09-06T00:00:00Z\"}]", resultTotal: 173),
            CreatePagedJsonResponse(HttpStatusCode.OK, "[]", pageSize: 0),
        ]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        using var httpClient = CreateHttpClient(handler);
        var gateway = new PersonalTradingPostGateway(new FixedKeySource(SyntheticKey), httpClient, new ImmediateRequestScheduler());

        var missingPrice = await gateway.GetCurrentBuyOrdersAsync(3);
        var dateWithoutTimeZone = await gateway.GetCurrentBuyOrdersAsync(3);
        var inconsistentHeaders = await gateway.GetCurrentBuyOrdersAsync(3);
        var zeroPageSize = await gateway.GetCurrentBuyOrdersAsync(3);

        Assert.Equal(Gw2ApiErrorCategory.InvalidPayload, missingPrice.ErrorCategory);
        Assert.Equal(Gw2ApiErrorCategory.InvalidPayload, dateWithoutTimeZone.ErrorCategory);
        Assert.Equal(Gw2ApiErrorCategory.InvalidPayload, inconsistentHeaders.ErrorCategory);
        Assert.Equal(Gw2ApiErrorCategory.InvalidPayload, zeroPageSize.ErrorCategory);
    }

    [Fact]
    public async Task Empty_first_page_with_zero_paging_totals_is_preserved_as_an_empty_successful_page()
    {
        var handler = new RecordingHandler(_ => CreatePagedJsonResponse(
            HttpStatusCode.OK,
            "[]",
            resultCount: 0,
            resultTotal: 0,
            pageTotal: 0));
        using var httpClient = CreateHttpClient(handler);
        var gateway = new PersonalTradingPostGateway(new FixedKeySource(SyntheticKey), httpClient, new ImmediateRequestScheduler());

        var result = await gateway.GetCurrentBuyOrdersAsync(0);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value?.Transactions ?? []);
        Assert.Equal(0, result.Value?.PageCount);
    }

    [Fact]
    public async Task Personal_reads_coalesce_for_one_credential_but_not_after_a_credential_change()
    {
        var handler = new CredentialScopedBlockingHandler();
        using var httpClient = CreateHttpClient(handler);
        using var scheduler = CreateScheduler();
        var keySource = new MutableKeySource(SyntheticKey);
        var gateway = new PersonalTradingPostGateway(keySource, httpClient, scheduler);

        var firstRequest = gateway.GetCurrentBuyOrdersAsync(3);
        await handler.WaitForFirstRequestAsync();
        var coalescedRequest = gateway.GetCurrentBuyOrdersAsync(3);
        await Task.Yield();
        Assert.Equal(1, handler.RequestCount);

        keySource.Set("synthetic-replacement-gw2-api-key");
        var changedCredentialRequest = gateway.GetCurrentBuyOrdersAsync(3);
        await handler.WaitForSecondRequestAsync();
        Assert.Equal(2, handler.RequestCount);

        handler.CompleteAll(() => CreatePagedJsonResponse(HttpStatusCode.OK, LoadTransactionFixture("current-buys.json")));
        var results = await Task.WhenAll(firstRequest, coalescedRequest, changedCredentialRequest);

        Assert.All(results, result => Assert.True(result.IsSuccess));
    }

    [Fact]
    public async Task Missing_or_unavailable_credentials_do_not_make_an_upstream_request()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No request expected."));
        using var httpClient = CreateHttpClient(handler);
        var missingKeyGateway = new PersonalTradingPostGateway(new FixedKeySource(null), httpClient, new ImmediateRequestScheduler());
        var unavailableKeyGateway = new PersonalTradingPostGateway(new UnavailableKeySource(), httpClient, new ImmediateRequestScheduler());

        var missingKey = await missingKeyGateway.GetAccountScopeAsync();
        var unavailableKey = await unavailableKeyGateway.GetAccountScopeAsync();

        Assert.Equal(Gw2ApiErrorCategory.CredentialNotConfigured, missingKey.ErrorCategory);
        Assert.Equal(Gw2ApiErrorCategory.CredentialUnavailable, unavailableKey.ErrorCategory);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Transport_failures_map_to_a_stable_category_without_disclosing_the_credential()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException(SyntheticKey));
        using var httpClient = CreateHttpClient(handler);
        var gateway = new PersonalTradingPostGateway(new FixedKeySource(SyntheticKey), httpClient, new ImmediateRequestScheduler());

        var result = await gateway.GetAccountScopeAsync();

        Assert.Equal(Gw2ApiErrorCategory.TransportFailure, result.ErrorCategory);
        Assert.DoesNotContain(SyntheticKey, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_converted_to_a_gateway_failure()
    {
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return CreatePagedJsonResponse(HttpStatusCode.OK, "[]");
        });
        using var httpClient = CreateHttpClient(handler);
        var gateway = new PersonalTradingPostGateway(new FixedKeySource(SyntheticKey), httpClient, new ImmediateRequestScheduler());
        using var cancellationSource = new CancellationTokenSource();

        var operation = gateway.GetCurrentSellListingsAsync(0, cancellationSource.Token);
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    [Fact]
    public async Task Negative_pages_are_rejected_without_reading_a_credential_or_sending_a_request()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No request expected."));
        using var httpClient = CreateHttpClient(handler);
        var gateway = new PersonalTradingPostGateway(new ThrowingKeySource(), httpClient, new ImmediateRequestScheduler());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => gateway.GetCurrentBuyOrdersAsync(-1));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Registered_gateway_redacts_authenticated_headers_and_never_logs_the_key()
    {
        const string sensitiveValue = "synthetic-sensitive-personal-gateway-key";
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var loggerProvider = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders().AddProvider(loggerProvider));
        services.AddTyrianLedgerAccountConnection(new TestingHostEnvironment(), new ConfigurationBuilder().Build());
        services.RemoveAll<IGw2ApiKeySource>();
        services.AddSingleton<IGw2ApiKeySource>(new FixedKeySource(sensitiveValue));
        services.RemoveAll<IGw2RequestScheduler>();
        services.AddSingleton<IGw2RequestScheduler>(new ImmediateRequestScheduler());
        services.Configure<HttpClientFactoryOptions>(PersonalTradingPostGateway.HttpClientName, options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = handler));
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IPersonalTradingPostGateway>().GetAccountScopeAsync();
        var clientOptions = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
            .Get(PersonalTradingPostGateway.HttpClientName);

        Assert.Equal(Gw2ApiErrorCategory.UpstreamUnavailable, result.ErrorCategory);
        Assert.Equal(sensitiveValue, Assert.Single(handler.Requests).AuthorizationParameter);
        Assert.True(clientOptions.ShouldRedactHeaderValue("Authorization"));
        Assert.True(clientOptions.ShouldRedactHeaderValue("X-Any-Header"));
        Assert.DoesNotContain(sensitiveValue, loggerProvider.RenderedMessages, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveValue, result.ToString(), StringComparison.Ordinal);
    }

    private static Task<Gw2ApiResult<PersonalTransactionPage>> GetTransactionsAsync(
        IPersonalTradingPostGateway gateway,
        string endpoint,
        int page) => endpoint switch
    {
        "current/buys" => gateway.GetCurrentBuyOrdersAsync(page),
        "current/sells" => gateway.GetCurrentSellListingsAsync(page),
        "history/buys" => gateway.GetCompletedBuyHistoryAsync(page),
        "history/sells" => gateway.GetCompletedSellHistoryAsync(page),
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint)),
    };

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://api.guildwars2.com/v2/"),
    };

    private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string payload) =>
        new(statusCode) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage CreatePagedJsonResponse(
        HttpStatusCode statusCode,
        string payload,
        int pageSize = 50,
        int resultCount = 1,
        int resultTotal = 151,
        int pageTotal = 4)
    {
        var response = CreateJsonResponse(statusCode, payload);
        response.Headers.Add("X-Page-Size", pageSize.ToString());
        response.Headers.Add("X-Page-Total", pageTotal.ToString());
        response.Headers.Add("X-Result-Count", resultCount.ToString());
        response.Headers.Add("X-Result-Total", resultTotal.ToString());
        return response;
    }

    private static string LoadTransactionFixture(string name) =>
        LoadFixture($"gw2/commerce/transactions/{name}");

    private static string LoadFixture(string relativePath)
    {
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var loader = new JsonFixtureLoader(fixtureRoot);
        using var document = loader.LoadAsync(relativePath).GetAwaiter().GetResult();
        return document.RootElement.GetRawText();
    }

    private static IReadOnlyDictionary<string, string> GetQueryParameters(Uri requestUri) =>
        requestUri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty,
                StringComparer.Ordinal);

    private sealed class FixedKeySource(string? value) : IGw2ApiKeySource
    {
        public ValueTask<Gw2ApiKeyReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Gw2ApiKeyReadResult.FromValue(value));
    }

    private sealed class UnavailableKeySource : IGw2ApiKeySource
    {
        public ValueTask<Gw2ApiKeyReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Gw2ApiKeyReadResult.Unavailable);
    }

    private sealed class MutableKeySource(string value) : IGw2ApiKeySource
    {
        private string _value = value;

        public ValueTask<Gw2ApiKeyReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Gw2ApiKeyReadResult.FromValue(Volatile.Read(ref _value)));

        public void Set(string value) => Volatile.Write(ref _value, value);
    }

    private sealed class ThrowingKeySource : IGw2ApiKeySource
    {
        public ValueTask<Gw2ApiKeyReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromException<Gw2ApiKeyReadResult>(new InvalidOperationException("No credential read expected."));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responseFactory;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
            : this((request, _) => Task.FromResult(responseFactory(request)))
        {
        }

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) =>
            _responseFactory = responseFactory;

        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request.RequestUri);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));
            return _responseFactory(request, cancellationToken);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string? AuthorizationScheme,
        string? AuthorizationParameter);

    private sealed class ImmediateRequestScheduler : IGw2RequestScheduler
    {
        public List<Gw2RequestKey> RequestKeys { get; } = [];

        public async Task<T> ScheduleAsync<T>(
            Gw2RequestKey requestKey,
            Func<CancellationToken, Task<Gw2ScheduledResult<T>>> sendAsync,
            CancellationToken cancellationToken)
        {
            RequestKeys.Add(requestKey);
            return (await sendAsync(cancellationToken)).Result;
        }
    }

    private static Gw2RequestScheduler CreateScheduler() => new(
        new Gw2ApiSchedulerOptions
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
                On429 = new Gw2BackoffOptions { InitialBackoffMs = 1, MaxBackoffMs = 1, MaxAttempts = 1 },
                On5xx = new Gw2BackoffOptions { InitialBackoffMs = 1, MaxBackoffMs = 1, MaxAttempts = 1 },
            },
            RequestTimeoutMs = 10_000,
        },
        NoDelay.Instance);

    private sealed class NoDelay : IGw2RequestDelay
    {
        public static NoDelay Instance { get; } = new();

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CredentialScopedBlockingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _firstRequestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondRequestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<TaskCompletionSource<HttpResponseMessage>> _responses = [];
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        public Task WaitForFirstRequestAsync() => _firstRequestStarted.Task;

        public Task WaitForSecondRequestAsync() => _secondRequestStarted.Task;

        public void CompleteAll(Func<HttpResponseMessage> responseFactory)
        {
            foreach (var responseSource in _responses)
            {
                responseSource.TrySetResult(responseFactory());
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var responseSource = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            _responses.Add(responseSource);
            var requestCount = Interlocked.Increment(ref _requestCount);
            if (requestCount == 1)
            {
                _firstRequestStarted.TrySetResult();
            }
            else if (requestCount == 2)
            {
                _secondRequestStarted.TrySetResult();
            }

            return responseSource.Task;
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _renderedMessages = [];

        public string RenderedMessages => string.Join(Environment.NewLine, _renderedMessages);

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_renderedMessages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<string> renderedMessages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                renderedMessages.Add(formatter(state, exception));
                if (exception is not null)
                {
                    renderedMessages.Add(exception.ToString());
                }
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class TestingHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = "Gw2Tp.Infrastructure.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
