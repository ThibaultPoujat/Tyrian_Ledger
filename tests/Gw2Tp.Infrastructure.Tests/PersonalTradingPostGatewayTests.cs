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
    [InlineData("current/buys", "current-buys.json", 910000001L, false)]
    [InlineData("current/sells", "current-sells.json", 910000002L, false)]
    [InlineData("history/buys", "history-buys.json", 910000003L, true)]
    [InlineData("history/sells", "history-sells.json", 910000004L, true)]
    public async Task Personal_transaction_reads_use_typed_authenticated_paged_requests_and_map_fixtures(
        string endpoint,
        string fixtureName,
        long expectedTransactionId,
        bool hasPurchasedTimestamp)
    {
        var handler = new RecordingHandler(_ => CreatePagedJsonResponse(HttpStatusCode.OK, LoadTransactionFixture(fixtureName)));
        using var httpClient = CreateHttpClient(handler);
        var scheduler = new ImmediateRequestScheduler();
        var gateway = new PersonalTradingPostGateway(new FixedKeySource(SyntheticKey), httpClient, scheduler);

        var result = await GetTransactionsAsync(gateway, endpoint, page: 2);

        Assert.True(result.IsSuccess);
        var transactionPage = Assert.IsType<PersonalTransactionPage>(result.Value);
        var transaction = Assert.Single(transactionPage.Transactions);
        Assert.Equal(expectedTransactionId, transaction.TransactionId);
        Assert.Equal(TimeSpan.Zero, transaction.CreatedAtUtc.Offset);
        Assert.Equal(hasPurchasedTimestamp, transaction.PurchasedAtUtc is not null);
        Assert.Equal(2, transactionPage.PageNumber);
        Assert.Equal(50, transactionPage.PageSize);
        Assert.Equal(4, transactionPage.PageCount);
        Assert.Equal(173, transactionPage.ResultTotal);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"/v2/commerce/transactions/{endpoint}", request.Uri.AbsolutePath);
        Assert.Equal("2", GetQueryParameters(request.Uri)["page"]);
        Assert.Equal(PersonalTradingPostGateway.SchemaVersion, GetQueryParameters(request.Uri)["v"]);
        Assert.DoesNotContain("access_token", GetQueryParameters(request.Uri).Keys);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal(SyntheticKey, request.AuthorizationParameter);
        var requestKey = Assert.Single(scheduler.RequestKeys).Value;
        Assert.Equal($"personal/transactions/{endpoint}/page-2", requestKey);
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
        Assert.Equal("personal/account", Assert.Single(scheduler.RequestKeys).Value);
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

        var partial = await gateway.GetCompletedBuyHistoryAsync(0);
        var missingPurchaseTimestamp = await gateway.GetCompletedBuyHistoryAsync(0);
        var malformed = await gateway.GetCompletedBuyHistoryAsync(0);

        Assert.Equal(Gw2ApiErrorCategory.IncompleteData, partial.ErrorCategory);
        Assert.Equal(Gw2ApiErrorCategory.InvalidPayload, missingPurchaseTimestamp.ErrorCategory);
        Assert.Equal(Gw2ApiErrorCategory.InvalidPayload, malformed.ErrorCategory);
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

    private static HttpResponseMessage CreatePagedJsonResponse(HttpStatusCode statusCode, string payload)
    {
        var response = CreateJsonResponse(statusCode, payload);
        response.Headers.Add("X-Page-Size", "50");
        response.Headers.Add("X-Page-Total", "4");
        response.Headers.Add("X-Result-Count", "1");
        response.Headers.Add("X-Result-Total", "173");
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
