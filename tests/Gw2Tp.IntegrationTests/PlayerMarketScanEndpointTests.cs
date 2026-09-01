using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gw2Tp.Application.MarketData;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Gw2Tp.IntegrationTests;

public sealed class PlayerMarketScanEndpointTests
{
    [Fact]
    public async Task Scan_endpoints_expose_idle_then_running_without_partial_results_then_complete_groups()
    {
        var metadataGate = new TaskCompletionSource<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new StubMarketDataClient(
            metadataAsync: (_, _) => metadataGate.Task);
        using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();

        using var idle = await client.GetAsync("/api/recommendations/scan");
        Assert.Equal(HttpStatusCode.OK, idle.StatusCode);
        await AssertScanStateAsync(idle, "idle", hasResult: false);

        using var start = await client.PostAsJsonAsync("/api/recommendations/scan", new
        {
            capitalCopper = 22_000,
            riskProfile = "cautious",
        });
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        await AssertScanStateAsync(start, "running", hasResult: false);

        await gateway.MetadataStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var running = await client.GetAsync("/api/recommendations/scan");
        Assert.Equal(HttpStatusCode.OK, running.StatusCode);
        await AssertScanStateAsync(running, "running", hasResult: false, "reading-finalist-metadata", 1);

        metadataGate.SetResult(Success([Metadata(1)]));
        using var completed = await WaitForStateAsync(client, "complete");
        using var completedDocument = JsonDocument.Parse(await completed.Content.ReadAsStringAsync());
        var result = completedDocument.RootElement.GetProperty("result");
        var recommendation = result.GetProperty("placeOrderAndWait")[0];

        Assert.True(result.TryGetProperty("scanCompletedAtUtc", out _));
        Assert.Equal(1, recommendation.GetProperty("itemId").GetInt32());
        Assert.True(recommendation.TryGetProperty("modeledProfitCopper", out _));
        Assert.True(recommendation.TryGetProperty("routeEvidence", out _));
        Assert.Contains("current-order-book-depth-and-spread-guard", recommendation.GetProperty("assumptions").EnumerateArray().Select(value => value.GetString()));
        Assert.False(recommendation.TryGetProperty("buys", out _));
        Assert.False(recommendation.TryGetProperty("sells", out _));
        Assert.False(recommendation.TryGetProperty("whitelisted", out _));
    }

    [Fact]
    public async Task Delete_cancels_the_active_scan_and_the_status_exposes_no_result()
    {
        var pricesStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new StubMarketDataClient(
            pricesAsync: async (_, cancellationToken) =>
            {
                pricesStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success([]);
            });
        using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();

        using var start = await client.PostAsJsonAsync("/api/recommendations/scan", new
        {
            capitalCopper = 22_000,
            riskProfile = "cautious",
        });
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        await pricesStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var cancelled = await client.DeleteAsync("/api/recommendations/scan");
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        await AssertScanStateAsync(cancelled, "cancelled", hasResult: false);

        using var status = await client.GetAsync("/api/recommendations/scan");
        await AssertScanStateAsync(status, "cancelled", hasResult: false);
    }

    [Fact]
    public async Task Invalid_concurrent_and_failed_scan_outcomes_are_safe_and_retryable()
    {
        var priceGate = new TaskCompletionSource<Gw2ApiResult<IReadOnlyList<MarketPrice>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new StubMarketDataClient(pricesAsync: (_, _) => priceGate.Task);
        using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();

        using var invalid = await client.PostAsJsonAsync("/api/recommendations/scan", new
        {
            capitalCopper = -1,
            riskProfile = "unknown",
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var first = await client.PostAsJsonAsync("/api/recommendations/scan", new
        {
            capitalCopper = 22_000,
            riskProfile = "cautious",
        });
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        await gateway.PricesStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var concurrent = await client.PostAsJsonAsync("/api/recommendations/scan", new
        {
            capitalCopper = 22_000,
            riskProfile = "cautious",
        });
        Assert.Equal(HttpStatusCode.Conflict, concurrent.StatusCode);
        await AssertScanStateAsync(concurrent, "running", hasResult: false);

        priceGate.SetResult(Gw2ApiResult<IReadOnlyList<MarketPrice>>.Failure(Gw2ApiErrorCategory.IncompleteData));
        using var failed = await WaitForStateAsync(client, "failed");
        await AssertScanStateAsync(failed, "failed", hasResult: false, isRetryable: true);
    }

    [Fact]
    public async Task Sparse_detailed_order_books_publish_a_complete_empty_result()
    {
        var gateway = new StubMarketDataClient(
            listings: itemIds => Success(itemIds.Select(itemId => new MarketListing(
                itemId,
                [new MarketOrderLevel(3, 100, 999)],
                [new MarketOrderLevel(1, 1, 4_420_033)]))));
        using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();

        using var start = await client.PostAsJsonAsync("/api/recommendations/scan", new
        {
            capitalCopper = 1_000_000,
            riskProfile = "adventurous",
        });
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);

        using var completed = await WaitForStateAsync(client, "complete");
        using var document = JsonDocument.Parse(await completed.Content.ReadAsStringAsync());
        var result = document.RootElement.GetProperty("result");

        Assert.Empty(result.GetProperty("canActNow").EnumerateArray());
        Assert.Empty(result.GetProperty("placeOrderAndWait").EnumerateArray());
    }

    private static WebApplicationFactory<Program> CreateFactory(StubMarketDataClient gateway) =>
        new TestWebApplicationFactory()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGw2ApiClient>();
                services.AddSingleton<IGw2ApiClient>(gateway);
            }));

    private static async Task<HttpResponseMessage> WaitForStateAsync(HttpClient client, string state)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var response = await client.GetAsync("/api/recommendations/scan", timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.GetProperty("state").GetString() == state)
            {
                return response;
            }

            response.Dispose();
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task AssertScanStateAsync(
        HttpResponseMessage response,
        string expectedState,
        bool hasResult,
        string? expectedStage = null,
        int? expectedFinalistCount = null,
        bool? isRetryable = null)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(expectedState, root.GetProperty("state").GetString());
        Assert.Equal(hasResult, root.GetProperty("result").ValueKind != JsonValueKind.Null);
        if (isRetryable is { } expectedRetryable)
        {
            Assert.Equal(expectedRetryable, root.GetProperty("isRetryable").GetBoolean());
        }

        if (expectedStage is null)
        {
            return;
        }

        var progress = root.GetProperty("progress");
        Assert.Equal(expectedStage, progress.GetProperty("stage").GetString());
        Assert.Equal(expectedFinalistCount, progress.GetProperty("finalistCount").GetInt32());
    }

    private static Gw2ApiResult<IReadOnlyList<T>> Success<T>(IEnumerable<T> values) =>
        Gw2ApiResult<IReadOnlyList<T>>.Success(values.ToArray());

    private sealed class StubMarketDataClient : IGw2ApiClient
    {
        private readonly Func<IReadOnlyCollection<int>, CancellationToken, Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>>> pricesAsync;
        private readonly Func<IReadOnlyCollection<int>, CancellationToken, Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>>> metadataAsync;
        private readonly Func<IReadOnlyCollection<int>, Gw2ApiResult<IReadOnlyList<MarketListing>>> listings;

        public StubMarketDataClient(
            Func<IReadOnlyCollection<int>, CancellationToken, Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>>>? pricesAsync = null,
            Func<IReadOnlyCollection<int>, CancellationToken, Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>>>? metadataAsync = null,
            Func<IReadOnlyCollection<int>, Gw2ApiResult<IReadOnlyList<MarketListing>>>? listings = null)
        {
            this.pricesAsync = pricesAsync ?? ((itemIds, _) => Task.FromResult(Success(itemIds.Select(Price))));
            this.metadataAsync = metadataAsync ?? ((itemIds, _) => Task.FromResult(Success(itemIds.Select(Metadata))));
            this.listings = listings ?? (itemIds => Success(itemIds.Select(Listing)));
        }

        public TaskCompletionSource MetadataStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource PricesStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Success([1]));

        public async Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
            IReadOnlyCollection<int> itemIds,
            CancellationToken cancellationToken = default)
        {
            PricesStarted.TrySetResult();
            return await pricesAsync(itemIds, cancellationToken);
        }

        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
            IReadOnlyCollection<int> itemIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(listings(itemIds));

        public async Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(
            IReadOnlyCollection<int> itemIds,
            CancellationToken cancellationToken = default)
        {
            MetadataStarted.TrySetResult();
            return await metadataAsync(itemIds, cancellationToken);
        }

        private static MarketPrice Price(int itemId) => new(
            itemId,
            IsWhitelisted: false,
            new MarketOrderSummary(100, 999),
            new MarketOrderSummary(100, 2_001));

        private static MarketListing Listing(int itemId) => new(
            itemId,
            [new MarketOrderLevel(3, 100, 999)],
            [new MarketOrderLevel(3, 100, 2_001)]);
    }

    private static MarketItemMetadata Metadata(int itemId) => new(
        itemId,
        $"Item {itemId}",
        MarketItemStackPolicy.NormalStackLimit);
}
