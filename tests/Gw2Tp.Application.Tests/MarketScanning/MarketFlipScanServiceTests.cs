using System.Diagnostics;
using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.FlipOpportunities;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.MarketScanning;
using Gw2Tp.Application.Preferences;
using Gw2Tp.Application.Time;
using Gw2Tp.Domain.Finance;
using Gw2Tp.Domain.MarketData;
using Xunit;

namespace Gw2Tp.Application.Tests.MarketScanning;

public sealed class MarketFlipScanServiceTests
{
    private static readonly DateTimeOffset ScanTime = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly DataFreshness Freshness = new(ScanTime, ScanTime.AddMinutes(2));

    [Fact]
    public async Task Empty_tracked_list_makes_no_market_requests()
    {
        var marketClient = new RecordingMarketClient();
        var service = CreateService([], marketClient);

        var result = await service.ScanAsync(UserSessionPreferences.Default, ScoringConfiguration(), CancellationToken.None);

        Assert.Equal(MarketFlipScanStatus.NoTrackedItems, result.Status);
        Assert.Empty(result.ScreenedCandidates);
        Assert.Empty(result.Opportunities);
        Assert.Equal(0, marketClient.PriceRequestCount);
        Assert.Equal(0, marketClient.ListingRequestCount);
    }

    [Fact]
    public async Task Unconfigured_fees_screen_prices_but_do_not_request_listings()
    {
        var marketClient = new RecordingMarketClient
        {
            Prices = SuccessfulPrices(
                Price(3, buy: 180, sell: 120),
                Price(2, buy: 100, sell: 110),
                Price(1, buy: 0, sell: 100)),
        };
        var service = CreateService(
            [Tracked(3, MarketSamplingClass.Background), Tracked(2), Tracked(1)],
            marketClient);

        var result = await service.ScanAsync(UserSessionPreferences.Default, ScoringConfiguration(), CancellationToken.None);

        Assert.Equal(MarketFlipScanStatus.FeeConfigurationRequired, result.Status);
        Assert.Equal([3], result.ScreenedCandidates.Select(candidate => candidate.ItemId));
        Assert.Empty(result.Opportunities);
        Assert.Equal(1, marketClient.PriceRequestCount);
        Assert.Equal([1, 2, 3], marketClient.PriceRequests.Single());
        Assert.Equal(0, marketClient.ListingRequestCount);
    }

    [Fact]
    public async Task Configured_scan_requests_listings_only_for_screened_candidates_and_applies_preferences()
    {
        var marketClient = new RecordingMarketClient
        {
            Prices = SuccessfulPrices(
                Price(1, buy: 250, sell: 100),
                Price(2, buy: 150, sell: 100),
                Price(3, buy: 90, sell: 100)),
            Listings = SuccessfulListings(
                Listing(1, buy: 250, sell: 100),
                Listing(2, buy: 150, sell: 100)),
        };
        var preferences = ConfiguredPreferences(minimumProfitCopper: 100, analysisQuantity: 2);
        var service = CreateService([Tracked(3), Tracked(1), Tracked(2)], marketClient);

        var result = await service.ScanAsync(preferences, ScoringConfiguration(), CancellationToken.None);

        Assert.Equal(MarketFlipScanStatus.Complete, result.Status);
        Assert.Equal([1, 2], result.ScreenedCandidates.Select(candidate => candidate.ItemId));
        Assert.Equal([1], result.Opportunities.Select(opportunity => opportunity.Analysis.Scenario.ItemId));
        Assert.Equal(2, result.Opportunities.Single().Analysis.Scenario.RequestedQuantity);
        Assert.Equal(1, marketClient.PriceRequestCount);
        Assert.Equal(1, marketClient.ListingRequestCount);
        Assert.Equal([1, 2], marketClient.ListingRequests.Single());
    }

    [Fact]
    public async Task Failed_or_partial_market_data_is_not_ranked_or_followed_by_listing_requests()
    {
        var marketClient = new RecordingMarketClient
        {
            Prices = Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success([Price(1, buy: 200, sell: 100)], isPartialData: true, Freshness),
        };
        var service = CreateService([Tracked(1)], marketClient);

        var result = await service.ScanAsync(ConfiguredPreferences(), ScoringConfiguration(), CancellationToken.None);

        Assert.Equal(MarketFlipScanStatus.Unavailable, result.Status);
        Assert.Empty(result.Opportunities);
        Assert.Equal(1, marketClient.PriceRequestCount);
        Assert.Equal(0, marketClient.ListingRequestCount);
    }

    [Fact]
    public async Task Missing_price_data_is_unavailable_and_not_followed_by_listing_requests()
    {
        var marketClient = new RecordingMarketClient
        {
            Prices = SuccessfulPrices(Price(1, buy: 200, sell: 100)),
        };
        var service = CreateService([Tracked(1), Tracked(2)], marketClient);

        var result = await service.ScanAsync(ConfiguredPreferences(), ScoringConfiguration(), CancellationToken.None);

        Assert.Equal(MarketFlipScanStatus.Unavailable, result.Status);
        Assert.Equal(Gw2ApiErrorCategory.InvalidPayload, result.ErrorCategory);
        Assert.Empty(result.ScreenedCandidates);
        Assert.Empty(result.Opportunities);
        Assert.Equal(1, marketClient.PriceRequestCount);
        Assert.Equal(0, marketClient.ListingRequestCount);
    }

    [Fact]
    public async Task Configured_scan_rejects_zero_screened_candidates_without_requesting_listings()
    {
        var marketClient = new RecordingMarketClient
        {
            Prices = SuccessfulPrices(
                Price(1, buy: 100, sell: 100),
                Price(2, buy: 90, sell: 100)),
        };
        var service = CreateService([Tracked(1), Tracked(2)], marketClient);

        var result = await service.ScanAsync(ConfiguredPreferences(), ScoringConfiguration(), CancellationToken.None);

        Assert.Equal(MarketFlipScanStatus.Complete, result.Status);
        Assert.Empty(result.ScreenedCandidates);
        Assert.Empty(result.Opportunities);
        Assert.Equal(1, marketClient.PriceRequestCount);
        Assert.Equal(0, marketClient.ListingRequestCount);
    }

    [Fact]
    public async Task Missing_listing_data_is_unavailable_and_not_ranked()
    {
        var marketClient = new RecordingMarketClient
        {
            Prices = SuccessfulPrices(
                Price(1, buy: 250, sell: 100),
                Price(2, buy: 150, sell: 100)),
            Listings = SuccessfulListings(Listing(1, buy: 250, sell: 100)),
        };
        var service = CreateService([Tracked(1), Tracked(2)], marketClient);

        var result = await service.ScanAsync(ConfiguredPreferences(), ScoringConfiguration(), CancellationToken.None);

        Assert.Equal(MarketFlipScanStatus.Unavailable, result.Status);
        Assert.Equal(Gw2ApiErrorCategory.InvalidPayload, result.ErrorCategory);
        Assert.Empty(result.Opportunities);
        Assert.Equal(1, marketClient.PriceRequestCount);
        Assert.Equal(1, marketClient.ListingRequestCount);
    }

    [Fact]
    public async Task Fixture_backed_25_item_scan_completes_within_one_second()
    {
        var itemIds = Enumerable.Range(900_001, 25).ToArray();
        var marketClient = new RecordingMarketClient
        {
            Prices = SuccessfulPrices(itemIds.Select(itemId => Price(itemId, buy: 250, sell: 100)).ToArray()),
            Listings = SuccessfulListings(itemIds.Select(itemId => Listing(itemId, buy: 250, sell: 100)).ToArray()),
        };
        var service = CreateService(itemIds.Select(itemId => Tracked(itemId)).ToArray(), marketClient);
        var stopwatch = Stopwatch.StartNew();

        var result = await service.ScanAsync(ConfiguredPreferences(), ScoringConfiguration(), CancellationToken.None);

        stopwatch.Stop();
        Assert.Equal(MarketFlipScanStatus.Complete, result.Status);
        Assert.Equal(25, result.Opportunities.Count);
        Assert.Equal(1, marketClient.PriceRequestCount);
        Assert.Equal(1, marketClient.ListingRequestCount);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"25-item scan took {stopwatch.Elapsed}.");
    }

    private static MarketFlipScanService CreateService(
        IReadOnlyList<MarketTrackedItem> trackedItems,
        RecordingMarketClient marketClient) =>
        new(new StubWatchlistStore(trackedItems), marketClient, new FrozenClock(ScanTime));

    private static MarketTrackedItem Tracked(
        int itemId,
        MarketSamplingClass samplingClass = MarketSamplingClass.Watchlist) => new(itemId, samplingClass);

    private static UserSessionPreferences ConfiguredPreferences(
        long? minimumProfitCopper = null,
        int analysisQuantity = 1) =>
        UserSessionPreferences.Create(
            capitalLimitCopper: null,
            minimumProfitCopper,
            OpportunityRiskPreference.All,
            OpportunityStrategyPreference.MarketFlip,
            allocationPercent: 100,
            analysisQuantity,
            listingFeeBasisPoints: 500,
            listingFeeRounding: FeeRounding.Down,
            exchangeFeeBasisPoints: 1_000,
            exchangeFeeRounding: FeeRounding.Down);

    private static FlipOpportunityScoringConfiguration ScoringConfiguration() =>
        new(
            new Money(100),
            targetReturnOnInvestmentBasisPoints: 1_000,
            acceptablePriceImpactBasisPoints: 1_000,
            new OpportunityScoringWeights(4, 3, 1, 1, 1, 1),
            freshDataScoreBasisPoints: 10_000,
            staleDataScoreBasisPoints: 0,
            normalConfidenceRiskScoreBasisPoints: 10_000,
            reducedConfidenceRiskScoreBasisPoints: 0,
            twoLegFlipComplexityScoreBasisPoints: 10_000);

    private static MarketPrice Price(int itemId, int buy, int sell) =>
        new(itemId, false, new MarketOrderSummary(10, buy), new MarketOrderSummary(10, sell));

    private static MarketListing Listing(int itemId, int buy, int sell) =>
        new(
            itemId,
            [new MarketOrderLevel(1, 100, buy)],
            [new MarketOrderLevel(1, 100, sell)]);

    private static Gw2ApiResult<IReadOnlyList<MarketPrice>> SuccessfulPrices(params MarketPrice[] prices) =>
        Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success(prices, freshness: Freshness);

    private static Gw2ApiResult<IReadOnlyList<MarketListing>> SuccessfulListings(params MarketListing[] listings) =>
        Gw2ApiResult<IReadOnlyList<MarketListing>>.Success(listings, freshness: Freshness);

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
        public Gw2ApiResult<IReadOnlyList<MarketPrice>> Prices { get; init; } = SuccessfulPrices();

        public Gw2ApiResult<IReadOnlyList<MarketListing>> Listings { get; init; } = SuccessfulListings();

        public List<IReadOnlyCollection<int>> PriceRequests { get; } = [];

        public List<IReadOnlyCollection<int>> ListingRequests { get; } = [];

        public int PriceRequestCount => PriceRequests.Count;

        public int ListingRequestCount => ListingRequests.Count;

        public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
            IReadOnlyCollection<int> itemIds,
            CancellationToken cancellationToken = default)
        {
            PriceRequests.Add(itemIds.ToArray());
            return Task.FromResult(Prices);
        }

        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
            IReadOnlyCollection<int> itemIds,
            CancellationToken cancellationToken = default)
        {
            ListingRequests.Add(itemIds.ToArray());
            return Task.FromResult(Listings);
        }
    }

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }
    }
}
