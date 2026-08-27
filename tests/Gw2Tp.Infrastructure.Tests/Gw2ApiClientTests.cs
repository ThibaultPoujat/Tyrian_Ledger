using System.Net;
using System.Text;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class Gw2ApiClientTests
{
    [Fact]
    public async Task Get_prices_uses_a_get_batch_request_and_maps_the_fixture_to_application_contracts()
    {
        var payload = await LoadFixturePayloadAsync("gw2/commerce/prices.json");
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, payload)));
        using var httpClient = CreateHttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        var result = await apiClient.GetPricesAsync([900001, 900002, 900003]);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsPartialData);
        Assert.Null(result.ErrorCategory);
        var prices = Assert.IsAssignableFrom<IReadOnlyList<MarketPrice>>(result.Value);
        Assert.Equal(3, prices.Count);
        Assert.Equal(900001, prices[0].ItemId);
        Assert.False(prices[0].IsWhitelisted);
        Assert.Equal(1200, prices[0].Buys.Quantity);
        Assert.Equal(850, prices[0].Buys.UnitPriceInCopper);
        Assert.Equal(905, prices[0].Sells.UnitPriceInCopper);

        AssertBatchGetRequest(
            Assert.Single(handler.Requests),
            "commerce/prices",
            "900001,900002,900003");
    }

    [Fact]
    public async Task Get_listings_uses_a_get_batch_request_and_maps_the_fixture_to_application_contracts()
    {
        var payload = await LoadFixturePayloadAsync("gw2/commerce/listings.json");
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, payload)));
        using var httpClient = CreateHttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        var result = await apiClient.GetListingsAsync([900001, 900002]);

        Assert.True(result.IsSuccess);
        var listings = Assert.IsAssignableFrom<IReadOnlyList<MarketListing>>(result.Value);
        Assert.Equal(2, listings.Count);
        Assert.Equal(900001, listings[0].ItemId);
        Assert.Equal(3, listings[0].Buys.Count);
        Assert.Equal(4, listings[0].Buys[0].Listings);
        Assert.Equal(1200, listings[0].Buys[0].Quantity);
        Assert.Equal(850, listings[0].Buys[0].UnitPriceInCopper);
        Assert.Empty(listings[1].Buys);

        AssertBatchGetRequest(
            Assert.Single(handler.Requests),
            "commerce/listings",
            "900001,900002");
    }

    [Fact]
    public async Task Unknown_json_fields_are_ignored_when_the_required_market_shape_is_present()
    {
        const string payload = """
            [
              {
                "id": 900001,
                "whitelisted": false,
                "unknown_market_flag": true,
                "buys": { "quantity": 1200, "unit_price": 850, "unexpected": "ignored" },
                "sells": { "quantity": 40, "unit_price": 905 }
              }
            ]
            """;
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, payload)));
        using var httpClient = CreateHttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        var result = await apiClient.GetPricesAsync([900001]);

        Assert.True(result.IsSuccess);
        var price = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<MarketPrice>>(result.Value));
        Assert.Equal(850, price.Buys.UnitPriceInCopper);
    }

    [Theory]
    [InlineData(400, Gw2ApiErrorCategory.InvalidRequest)]
    [InlineData(401, Gw2ApiErrorCategory.Unauthorized)]
    [InlineData(403, Gw2ApiErrorCategory.Forbidden)]
    [InlineData(404, Gw2ApiErrorCategory.NotFound)]
    [InlineData(429, Gw2ApiErrorCategory.RateLimited)]
    [InlineData(500, Gw2ApiErrorCategory.UpstreamUnavailable)]
    [InlineData(502, Gw2ApiErrorCategory.UpstreamUnavailable)]
    [InlineData(503, Gw2ApiErrorCategory.UpstreamUnavailable)]
    [InlineData(504, Gw2ApiErrorCategory.UpstreamUnavailable)]
    [InlineData(201, Gw2ApiErrorCategory.UnexpectedResponse)]
    [InlineData(202, Gw2ApiErrorCategory.UnexpectedResponse)]
    [InlineData(207, Gw2ApiErrorCategory.UnexpectedResponse)]
    [InlineData(418, Gw2ApiErrorCategory.UnexpectedResponse)]
    public async Task Http_errors_map_to_stable_categories(int statusCode, Gw2ApiErrorCategory expectedCategory)
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)statusCode)));
        using var httpClient = CreateHttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        var result = await apiClient.GetPricesAsync([900001]);

        Assert.False(result.IsSuccess);
        Assert.False(result.IsPartialData);
        Assert.Null(result.Value);
        Assert.Equal(expectedCategory, result.ErrorCategory);
    }

    [Fact]
    public async Task Partial_content_is_a_successful_result_marked_as_partial()
    {
        var payload = await LoadFixturePayloadAsync("gw2/commerce/prices.json");
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(CreateJsonResponse(HttpStatusCode.PartialContent, payload)));
        using var httpClient = CreateHttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        var result = await apiClient.GetPricesAsync([900001, 999999]);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsPartialData);
        Assert.Null(result.ErrorCategory);
        Assert.NotEmpty(Assert.IsAssignableFrom<IReadOnlyList<MarketPrice>>(result.Value));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[{\"id\":900001,\"whitelisted\":false,\"buys\":{\"quantity\":1,\"unit_price\":2}}]")]
    public async Task Malformed_or_structurally_incomplete_json_maps_to_invalid_payload(string payload)
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, payload)));
        using var httpClient = CreateHttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        var result = await apiClient.GetPricesAsync([900001]);

        Assert.False(result.IsSuccess);
        Assert.Equal(Gw2ApiErrorCategory.InvalidPayload, result.ErrorCategory);
    }

    [Fact]
    public async Task Transport_failures_map_to_a_stable_category()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("synthetic transport failure")));
        using var httpClient = CreateHttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        var result = await apiClient.GetPricesAsync([900001]);

        Assert.False(result.IsSuccess);
        Assert.Equal(Gw2ApiErrorCategory.TransportFailure, result.ErrorCategory);
    }

    [Fact]
    public async Task Response_body_transport_failures_map_to_a_stable_category()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ThrowingResponseContent(),
            }));
        using var httpClient = CreateHttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        var result = await apiClient.GetPricesAsync([900001]);

        Assert.False(result.IsSuccess);
        Assert.Equal(Gw2ApiErrorCategory.TransportFailure, result.ErrorCategory);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_converted_to_a_gateway_failure()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return CreateJsonResponse(HttpStatusCode.OK, "[]");
        });
        using var httpClient = CreateHttpClient(handler);
        var apiClient = CreateApiClient(httpClient);
        using var cancellationTokenSource = new CancellationTokenSource();

        var operation = apiClient.GetPricesAsync([900001], cancellationTokenSource.Token);
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    [Fact]
    public async Task Empty_or_non_positive_item_ids_are_rejected_as_caller_misuse()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, "[]")));
        using var httpClient = CreateHttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(() => apiClient.GetPricesAsync([]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => apiClient.GetPricesAsync([0]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => apiClient.GetListingsAsync([-1]));
        await Assert.ThrowsAsync<ArgumentNullException>(() => apiClient.GetPricesAsync(null!));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Client_does_not_enforce_the_unverified_two_hundred_id_batch_limit()
    {
        var itemIds = Enumerable.Range(1, 201).ToArray();
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, "[]")));
        using var httpClient = CreateHttpClient(handler);
        var apiClient = CreateApiClient(httpClient);

        var result = await apiClient.GetPricesAsync(itemIds);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(string.Join(',', itemIds), GetQueryParameters(request.RequestUri)["ids"]);
        Assert.DoesNotContain("%2C", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Service_registration_exposes_the_application_gateway_abstraction()
    {
        var services = new ServiceCollection();
        services.AddTyrianLedgerGw2ApiClient(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IGw2ApiClient>();

        Assert.IsType<CachingGw2ApiClient>(client);
        Assert.IsType<Gw2ApiClient>(provider.GetRequiredService<IGw2ApiTransport>());
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://api.guildwars2.com/v2/"),
    };

    private static Gw2ApiClient CreateApiClient(HttpClient httpClient) =>
        new(httpClient, PassthroughRequestScheduler.Instance);

    private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string payload) => new(statusCode)
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json"),
    };

    private static async Task<string> LoadFixturePayloadAsync(string relativePath)
    {
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var loader = new JsonFixtureLoader(fixtureRoot);

        using var document = await loader.LoadAsync(relativePath);
        return document.RootElement.GetRawText();
    }

    private static void AssertBatchGetRequest(
        CapturedRequest request,
        string resourcePath,
        string expectedIds)
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"/v2/{resourcePath}", request.RequestUri.AbsolutePath);

        var queryParameters = GetQueryParameters(request.RequestUri);
        Assert.Equal(expectedIds, queryParameters["ids"]);
        Assert.Equal(Gw2ApiClient.SchemaVersion, queryParameters["v"]);
    }

    private static IReadOnlyDictionary<string, string> GetQueryParameters(Uri requestUri) =>
        requestUri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(parameter => parameter.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty,
                StringComparer.Ordinal);

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responseFactory;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request.RequestUri);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri));
            return _responseFactory(request, cancellationToken);
        }
    }

    private sealed class ThrowingResponseContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(new IOException("synthetic response-body transport failure"));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri RequestUri);

    private sealed class PassthroughRequestScheduler : IGw2RequestScheduler
    {
        public static readonly PassthroughRequestScheduler Instance = new();

        private PassthroughRequestScheduler()
        {
        }

        public async Task<T> ScheduleAsync<T>(
            Gw2RequestKey requestKey,
            Func<CancellationToken, Task<Gw2ScheduledResult<T>>> sendAsync,
            CancellationToken cancellationToken) =>
            (await sendAsync(cancellationToken)).Result;
    }
}
