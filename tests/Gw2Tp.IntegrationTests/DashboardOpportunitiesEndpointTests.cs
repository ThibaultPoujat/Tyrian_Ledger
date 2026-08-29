using System.Net.Http.Json;
using System.Text.Json;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Infrastructure.Preferences;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Gw2Tp.IntegrationTests;

public sealed class DashboardOpportunitiesEndpointTests
{
    [Fact]
    public async Task Dashboard_screens_tracked_prices_without_requesting_listings_until_fees_are_configured()
    {
        var marketClient = new RecordingMarketClient(
            prices: SuccessfulPrices(
                Price(900_002, buy: 90, sell: 100),
                Price(900_001, buy: 250, sell: 100)),
            listings: SuccessfulListings(Listing(900_001, buy: 250, sell: 100)));
        using var factory = CreateFactory([Tracked(900_002), Tracked(900_001)], marketClient);
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/dashboard/opportunities");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("fee-configuration-required", root.GetProperty("status").GetString());
        Assert.Equal(2, root.GetProperty("trackedItemCount").GetInt32());
        var screened = Assert.Single(root.GetProperty("screenedCandidates").EnumerateArray());
        Assert.Equal(900_001, screened.GetProperty("itemId").GetInt32());
        Assert.Empty(root.GetProperty("opportunities").EnumerateArray());
        Assert.Equal(1, marketClient.PriceRequestCount);
        Assert.Equal([900_001, 900_002], marketClient.PriceRequests.Single());
        Assert.Equal(0, marketClient.ListingRequestCount);
    }

    [Fact]
    public async Task Configured_dashboard_returns_live_ranked_scenarios_without_an_effort_category()
    {
        var marketClient = new RecordingMarketClient(
            prices: SuccessfulPrices(Price(900_001, buy: 250, sell: 100)),
            listings: SuccessfulListings(Listing(900_001, buy: 250, sell: 100)));
        using var factory = CreateFactory([Tracked(900_001)], marketClient);
        var client = factory.CreateClient();

        using var saveResponse = await client.PutAsJsonAsync("/api/preferences/user-session", new
        {
            capitalLimitCopper = (long?)null,
            minimumProfitCopper = 0,
            riskPreference = "all",
            strategyPreference = "market-flip",
            allocationPercent = 100,
            analysisQuantity = 2,
            listingFeeBasisPoints = 500,
            listingFeeRounding = "down",
            exchangeFeeBasisPoints = 1_000,
            exchangeFeeRounding = "down",
        });
        saveResponse.EnsureSuccessStatusCode();

        using var response = await client.GetAsync("/api/dashboard/opportunities");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("complete", root.GetProperty("status").GetString());
        Assert.Contains("Read-only live market scan", root.GetProperty("sourceDescription").GetString());
        var opportunity = Assert.Single(root.GetProperty("opportunities").EnumerateArray());
        Assert.Equal(900_001, opportunity.GetProperty("itemId").GetInt32());
        Assert.Equal("Tracked market item #900001", opportunity.GetProperty("label").GetString());
        Assert.False(opportunity.TryGetProperty("effortCategory", out _));
        Assert.Equal(2, opportunity.GetProperty("detail").GetProperty("requestedQuantity").GetInt32());
        Assert.Equal(1, marketClient.PriceRequestCount);
        Assert.Equal(1, marketClient.ListingRequestCount);
        Assert.Equal([900_001], marketClient.ListingRequests.Single());
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IReadOnlyList<MarketTrackedItem> trackedItems,
        RecordingMarketClient marketClient) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    UserSessionPreferencesServiceCollectionExtensions.DatabasePathConfigurationKey,
                    CreateDatabasePath());
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IHostedService>();
                    services.RemoveAll<IMarketWatchlistStore>();
                    services.RemoveAll<IGw2ApiClient>();
                    services.AddSingleton<IMarketWatchlistStore>(new StubWatchlistStore(trackedItems));
                    services.AddSingleton<IGw2ApiClient>(marketClient);
                });
            });

    private static MarketTrackedItem Tracked(int itemId) => new(itemId, MarketSamplingClass.Watchlist);

    private static MarketPrice Price(int itemId, int buy, int sell) =>
        new(itemId, false, new MarketOrderSummary(20, buy), new MarketOrderSummary(20, sell));

    private static MarketListing Listing(int itemId, int buy, int sell) =>
        new(
            itemId,
            [new MarketOrderLevel(1, 20, buy)],
            [new MarketOrderLevel(1, 20, sell)]);

    private static Gw2ApiResult<IReadOnlyList<MarketPrice>> SuccessfulPrices(params MarketPrice[] prices) =>
        Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success(
            prices,
            freshness: new Gw2Tp.Domain.MarketData.DataFreshness(
                new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 29, 12, 2, 0, TimeSpan.Zero)));

    private static Gw2ApiResult<IReadOnlyList<MarketListing>> SuccessfulListings(params MarketListing[] listings) =>
        Gw2ApiResult<IReadOnlyList<MarketListing>>.Success(
            listings,
            freshness: new Gw2Tp.Domain.MarketData.DataFreshness(
                new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 29, 12, 2, 0, TimeSpan.Zero)));

    private static string CreateDatabasePath() => Path.Combine(
        Path.GetTempPath(),
        "TyrianLedger",
        "IntegrationTests",
        $"dashboard-market-scan-{Guid.NewGuid():N}.db");

    private sealed class StubWatchlistStore : IMarketWatchlistStore
    {
        private readonly IReadOnlyList<MarketTrackedItem> trackedItems;

        public StubWatchlistStore(IReadOnlyList<MarketTrackedItem> trackedItems) => this.trackedItems = trackedItems;

        public Task<IReadOnlyList<MarketTrackedItem>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(trackedItems);

        public Task AddAsync(MarketTrackedItem item, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateSamplingClassAsync(int itemId, MarketSamplingClass samplingClass, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveAsync(int itemId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingMarketClient : IGw2ApiClient
    {
        private readonly Gw2ApiResult<IReadOnlyList<MarketPrice>> prices;
        private readonly Gw2ApiResult<IReadOnlyList<MarketListing>> listings;

        public RecordingMarketClient(
            Gw2ApiResult<IReadOnlyList<MarketPrice>> prices,
            Gw2ApiResult<IReadOnlyList<MarketListing>> listings)
        {
            this.prices = prices;
            this.listings = listings;
        }

        public List<IReadOnlyCollection<int>> PriceRequests { get; } = [];

        public List<IReadOnlyCollection<int>> ListingRequests { get; } = [];

        public int PriceRequestCount => PriceRequests.Count;

        public int ListingRequestCount => ListingRequests.Count;

        public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
            IReadOnlyCollection<int> itemIds,
            CancellationToken cancellationToken = default)
        {
            PriceRequests.Add(itemIds.ToArray());
            return Task.FromResult(prices);
        }

        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
            IReadOnlyCollection<int> itemIds,
            CancellationToken cancellationToken = default)
        {
            ListingRequests.Add(itemIds.ToArray());
            return Task.FromResult(listings);
        }
    }
}
