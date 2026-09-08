using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketScanning;
using Gw2Tp.Application.MarketSnapshots;
using Gw2Tp.Testing;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class LiveMarketScannerTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Scan_returns_exact_one_unit_economics_maximum_bid_and_aggregate_quantities()
    {
        var scanner = CreateScanner(new StubMarketDataClient(
            itemIds: [42],
            prices: _ => Success([Price(42, buyQuantity: 17, buyPrice: 100, sellQuantity: 31, sellPrice: 200)])));

        var result = await scanner.ScanAsync(LiveMarketScannerSettings.Default);

        Assert.Equal(LiveMarketScannerState.Ready, result.State);
        Assert.Equal(ObservedAt, result.ObservedAtUtc);
        Assert.False(result.IsFeeRoundingExternallyVerified);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(42, candidate.Item.ItemId);
        Assert.Equal(17, candidate.BestBuy.Quantity);
        Assert.Equal(31, candidate.LowestSell.Quantity);
        Assert.Equal(101, candidate.PlannedBid.Copper);
        Assert.Equal(199, candidate.PlannedListPrice.Copper);
        Assert.Equal(10, candidate.ProfitScenario.ListingFee.Copper);
        Assert.Equal(20, candidate.ProfitScenario.ExchangeFee.Copper);
        Assert.Equal(169, candidate.ProfitScenario.NetSaleProceeds.Copper);
        Assert.Equal(68, candidate.ProfitScenario.NetProfit.Copper);
        Assert.Equal(111, candidate.TotalCost.Copper);
        Assert.Equal(168, candidate.MaximumBid.Copper);
        Assert.Equal(1, result.QualifyingCandidateCount);
        Assert.False(result.IsTruncated);
        Assert.Contains(LiveMarketScannerInclusionReason.MeetsMinimumRoi, candidate.InclusionReasons);
        Assert.Equal(1, candidate.Liquidity.Acquisition.RequestedQuantity);
        Assert.Equal(1, candidate.Liquidity.Liquidation.RequestedQuantity);
        Assert.Equal(1, candidate.Liquidity.ParticipationCapQuantity);
    }

    [Fact]
    public async Task Scan_uses_exact_integer_roi_and_profit_for_maximum_bid_rounding_edges()
    {
        var scanner = CreateScanner(new StubMarketDataClient(
            prices: _ => Success([Price(1, 10, 100, 10, 200)])));
        var settings = new LiveMarketScannerSettings(
            MinimumRoiBasisPoints: 5_000,
            MinimumNetProfit: new(1),
            BidIncrementCopper: 1,
            ListUndercutCopper: 1,
            IntendedQuantity: 1);

        var result = await scanner.ScanAsync(settings);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(109, candidate.MaximumBid.Copper);
        Assert.True(candidate.ModeledRoi.MeetsOrExceedsBasisPoints(5_000));
    }

    [Fact]
    public async Task Scan_applies_non_default_bid_and_list_price_policy_exactly()
    {
        var scanner = CreateScanner(new StubMarketDataClient(
            prices: _ => Success([Price(1, 10, 100, 10, 200)])));
        var settings = new LiveMarketScannerSettings(0, new(1), BidIncrementCopper: 5, ListUndercutCopper: 4, IntendedQuantity: 1);

        var result = await scanner.ScanAsync(settings);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(105, candidate.PlannedBid.Copper);
        Assert.Equal(196, candidate.PlannedListPrice.Copper);
        Assert.Equal(10, candidate.ProfitScenario.ListingFee.Copper);
        Assert.Equal(20, candidate.ProfitScenario.ExchangeFee.Copper);
        Assert.Equal(61, candidate.ProfitScenario.NetProfit.Copper);
        Assert.Equal(165, candidate.MaximumBid.Copper);
    }

    [Fact]
    public async Task Scan_reports_a_market_that_misses_only_the_minimum_roi_filter()
    {
        var scanner = CreateScanner(new StubMarketDataClient(
            prices: _ => Success([Price(1, 10, 100, 10, 200)])));
        var settings = new LiveMarketScannerSettings(MinimumRoiBasisPoints: 7_000, MinimumNetProfit: new(60), 1, 1, IntendedQuantity: 1);

        var result = await scanner.ScanAsync(settings);

        Assert.Empty(result.Candidates);
        Assert.Contains(result.Exclusions, exclusion =>
            exclusion.Reason == LiveMarketScannerExclusionReason.MinimumRoiNotMet && exclusion.Count == 1);
        Assert.DoesNotContain(result.Exclusions, exclusion =>
            exclusion.Reason == LiveMarketScannerExclusionReason.MinimumNetProfitNotMet);
    }

    [Fact]
    public async Task Scan_preserves_independent_fee_round_up_at_small_copper_values()
    {
        var scanner = CreateScanner(new StubMarketDataClient(
            prices: _ => Success([Price(1, 10, 1, 10, 22)])));

        var result = await scanner.ScanAsync(LiveMarketScannerSettings.Default);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(21, candidate.PlannedListPrice.Copper);
        Assert.Equal(2, candidate.ProfitScenario.ListingFee.Copper);
        Assert.Equal(3, candidate.ProfitScenario.ExchangeFee.Copper);
        Assert.Equal(15, candidate.MaximumBid.Copper);
    }

    [Fact]
    public async Task Scan_rejects_fee_losing_and_filter_failing_markets_with_explanations()
    {
        var scanner = CreateScanner(new StubMarketDataClient(
            itemIds: [1, 2, 3],
            prices: _ => Success([
                Price(1, 10, 100, 10, 115),
                Price(2, 10, 100, 10, 200),
                Price(3, 10, 100, 10, 200),
            ])));
        var settings = new LiveMarketScannerSettings(7_000, new(70), 1, 1, IntendedQuantity: 1);

        var result = await scanner.ScanAsync(settings);

        Assert.Empty(result.Candidates);
        Assert.Contains(result.Exclusions, exclusion => exclusion.Reason == LiveMarketScannerExclusionReason.FeeLosing && exclusion.Count == 1);
        Assert.Contains(result.Exclusions, exclusion => exclusion.Reason == LiveMarketScannerExclusionReason.MinimumNetProfitNotMet && exclusion.Count == 2);
    }

    [Fact]
    public async Task Scan_rejects_invalid_policy_price_and_never_reads_metadata_when_nothing_qualifies()
    {
        var client = new StubMarketDataClient(prices: _ => Success([Price(1, 10, 100, 10, 1)]));
        var scanner = CreateScanner(client);

        var result = await scanner.ScanAsync(LiveMarketScannerSettings.Default);

        Assert.Empty(result.Candidates);
        Assert.Contains(result.Exclusions, exclusion => exclusion.Reason == LiveMarketScannerExclusionReason.PricePolicyInvalid);
        Assert.Empty(client.MetadataRequests);
        Assert.Empty(client.ListingRequests);
    }

    [Fact]
    public async Task Scan_exposes_simulator_depth_price_cliffs_and_participation_cap_for_intended_quantity()
    {
        var scanner = CreateScanner(new StubMarketDataClient(
            listings: _ => Success([
                new MarketListing(
                    1,
                    [new MarketOrderLevel(2, 3, 100), new MarketOrderLevel(1, 10, 80)],
                    [new MarketOrderLevel(2, 3, 200), new MarketOrderLevel(1, 10, 240)]),
            ])));

        var result = await scanner.ScanAsync(new LiveMarketScannerSettings(0, new(1), 1, 1, IntendedQuantity: 7));

        var liquidity = Assert.Single(result.Candidates).Liquidity;
        Assert.Equal(13, liquidity.TotalBuyQuantity);
        Assert.Equal(13, liquidity.TotalSellQuantity);
        Assert.Equal(3, liquidity.NearBestBuyQuantity);
        Assert.Equal(3, liquidity.NearBestSellQuantity);
        Assert.Equal(2, liquidity.NearBestBuyListings);
        Assert.Equal(2, liquidity.NearBestSellListings);
        Assert.Equal(20, liquidity.BuyNextLevelGap!.Value.Copper);
        Assert.Equal(40, liquidity.SellNextLevelGap!.Value.Copper);
        Assert.True(liquidity.HasBuyPriceCliff);
        Assert.True(liquidity.HasSellPriceCliff);
        Assert.Equal(1_560, liquidity.Acquisition.TotalValue.Copper);
        Assert.Equal(160, liquidity.Acquisition.PriceImpact.Copper);
        Assert.Equal(620, liquidity.Liquidation.TotalValue.Copper);
        Assert.Equal(80, liquidity.Liquidation.PriceImpact.Copper);
        Assert.Equal(1, liquidity.ParticipationCapQuantity);
        Assert.Contains(LiveMarketScannerLiquidityReason.BuyPriceCliff, liquidity.Reasons);
        Assert.Contains(LiveMarketScannerLiquidityReason.SellPriceCliff, liquidity.Reasons);
        Assert.Contains(LiveMarketScannerLiquidityReason.ParticipationCapBelowIntendedQuantity, liquidity.Reasons);
    }

    [Fact]
    public async Task Scan_flags_one_unit_top_and_insufficient_depth_without_hiding_profitable_candidate()
    {
        var scanner = CreateScanner(new StubMarketDataClient(
            listings: _ => Success([
                new MarketListing(
                    1,
                    [new MarketOrderLevel(1, 1, 100)],
                    [new MarketOrderLevel(1, 1, 200)]),
            ])));

        var result = await scanner.ScanAsync(new LiveMarketScannerSettings(0, new(1), 1, 1, IntendedQuantity: 5));

        var liquidity = Assert.Single(result.Candidates).Liquidity;
        Assert.False(liquidity.Acquisition.IsFullyFilled);
        Assert.False(liquidity.Liquidation.IsFullyFilled);
        Assert.Equal(4, liquidity.Acquisition.RemainingQuantity);
        Assert.Equal(0, liquidity.ParticipationCapQuantity);
        Assert.Contains(LiveMarketScannerLiquidityReason.InsufficientBuyListingDepth, liquidity.Reasons);
        Assert.Contains(LiveMarketScannerLiquidityReason.InsufficientSellListingDepth, liquidity.Reasons);
        Assert.Contains(LiveMarketScannerLiquidityReason.InsufficientBuyQuantityDepth, liquidity.Reasons);
        Assert.Contains(LiveMarketScannerLiquidityReason.InsufficientSellQuantityDepth, liquidity.Reasons);
        Assert.Contains(LiveMarketScannerLiquidityReason.IntendedQuantityCannotFullyAcquire, liquidity.Reasons);
        Assert.Contains(LiveMarketScannerLiquidityReason.IntendedQuantityCannotFullyLiquidate, liquidity.Reasons);
    }

    [Fact]
    public async Task Scan_orders_by_profit_then_aggregate_quantity_not_raw_roi()
    {
        var scanner = CreateScanner(new StubMarketDataClient(
            itemIds: [2, 1],
            prices: _ => Success([
                Price(1, buyQuantity: 2, buyPrice: 100, sellQuantity: 2, sellPrice: 200),
                Price(2, buyQuantity: 50, buyPrice: 100, sellQuantity: 50, sellPrice: 200),
            ])));

        var result = await scanner.ScanAsync(LiveMarketScannerSettings.Default);

        Assert.Equal([2, 1], result.Candidates.Select(candidate => candidate.Item.ItemId));
    }

    [Fact]
    public async Task Scan_discloses_when_the_bounded_result_set_is_truncated()
    {
        var itemIds = Enumerable.Range(1, LiveMarketScanner.MaximumCandidateCount + 1).ToArray();
        var scanner = CreateScanner(new StubMarketDataClient(
            itemIds: itemIds,
            prices: _ => Success(itemIds.Select(itemId => Price(itemId, 10, 100, 10, 200)))));

        var result = await scanner.ScanAsync(LiveMarketScannerSettings.Default);

        Assert.Equal(LiveMarketScanner.MaximumCandidateCount, result.Candidates.Count);
        Assert.Equal(LiveMarketScanner.MaximumCandidateCount + 1, result.QualifyingCandidateCount);
        Assert.True(result.IsTruncated);
    }

    [Fact]
    public async Task Scan_rejects_incomplete_metadata_without_returning_partial_candidates()
    {
        var scanner = CreateScanner(new StubMarketDataClient(
            itemIds: [1, 2],
            prices: _ => Success([Price(1, 10, 100, 10, 200), Price(2, 10, 100, 10, 200)]),
            metadata: _ => Success([new MarketItemMetadata(1, "Only one", MarketItemStackPolicy.NormalStackLimit)])));

        var result = await scanner.ScanAsync(LiveMarketScannerSettings.Default);

        Assert.Equal(LiveMarketScannerState.Unavailable, result.State);
        Assert.Equal(Gw2ApiErrorCategory.IncompleteData, result.ErrorCategory);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task Scan_rejects_incomplete_detailed_listings_without_returning_partial_candidates()
    {
        var scanner = CreateScanner(new StubMarketDataClient(
            itemIds: [1, 2],
            prices: _ => Success([Price(1, 10, 100, 10, 200), Price(2, 10, 100, 10, 200)]),
            listings: _ => Success([new MarketListing(
                1,
                [new MarketOrderLevel(3, 10, 100)],
                [new MarketOrderLevel(3, 10, 200)])])));

        var result = await scanner.ScanAsync(LiveMarketScannerSettings.Default);

        Assert.Equal(LiveMarketScannerState.Unavailable, result.State);
        Assert.Equal(Gw2ApiErrorCategory.IncompleteData, result.ErrorCategory);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task Scan_reports_invalid_payload_for_malformed_detailed_listing()
    {
        var scanner = CreateScanner(new StubMarketDataClient(
            listings: _ => Success([new MarketListing(
                1,
                [new MarketOrderLevel(0, 10, 100)],
                [new MarketOrderLevel(3, 10, 200)])])));

        var result = await scanner.ScanAsync(LiveMarketScannerSettings.Default);

        Assert.Equal(LiveMarketScannerState.Unavailable, result.State);
        Assert.Equal(Gw2ApiErrorCategory.InvalidPayload, result.ErrorCategory);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task Scan_exposes_an_empty_book_side_as_insufficient_liquidity_evidence()
    {
        var scanner = CreateScanner(new StubMarketDataClient(
            listings: _ => Success([new MarketListing(
                1,
                [],
                [new MarketOrderLevel(3, 10, 200)])])));

        var result = await scanner.ScanAsync(new LiveMarketScannerSettings(0, new(1), 1, 1, IntendedQuantity: 5));

        Assert.Equal(LiveMarketScannerState.Ready, result.State);
        var liquidity = Assert.Single(result.Candidates).Liquidity;
        Assert.Equal(0, liquidity.TotalBuyQuantity);
        Assert.Equal(0, liquidity.NearBestBuyQuantity);
        Assert.Null(liquidity.BuyNextLevelGap);
        Assert.False(liquidity.Liquidation.IsFullyFilled);
        Assert.Equal(5, liquidity.Liquidation.RemainingQuantity);
        Assert.Contains(LiveMarketScannerLiquidityReason.InsufficientBuyListingDepth, liquidity.Reasons);
        Assert.Contains(LiveMarketScannerLiquidityReason.InsufficientBuyQuantityDepth, liquidity.Reasons);
        Assert.Contains(LiveMarketScannerLiquidityReason.IntendedQuantityCannotFullyLiquidate, liquidity.Reasons);
    }

    [Fact]
    public async Task Scanner_returns_the_best_ten_order_book_levels_in_execution_order()
    {
        var scanner = CreateScanner(new StubMarketDataClient(
            listings: ids => Success(ids.Select(itemId => new MarketListing(
                itemId,
                Enumerable.Range(0, 12).Select(index => new MarketOrderLevel(1, 10, 100 + index)).ToArray(),
                Enumerable.Range(0, 12).Reverse().Select(index => new MarketOrderLevel(1, 10, 200 + index)).ToArray())))));

        var liquidity = Assert.Single((await scanner.ScanAsync(LiveMarketScannerSettings.Default)).Candidates).Liquidity;

        Assert.Equal(10, liquidity.TopBuyLevels.Count);
        Assert.Equal(Enumerable.Range(102, 10).Reverse(), liquidity.TopBuyLevels.Select(level => level.UnitPriceInCopper));
        Assert.Equal(10, liquidity.TopSellLevels.Count);
        Assert.Equal(Enumerable.Range(200, 10), liquidity.TopSellLevels.Select(level => level.UnitPriceInCopper));
    }

    [Fact]
    public async Task Aggregate_collection_reads_only_complete_price_data()
    {
        var client = new StubMarketDataClient();
        var collector = new PublicMarketSnapshotCollector(client, new FrozenClock(ObservedAt));

        var result = await collector.CollectAggregatePricesAsync();

        Assert.Equal(ObservedAt, result.GeneratedAtUtc);
        Assert.Equal([1], result.ItemIds);
        Assert.Equal(["price-item-ids", "prices"], client.Calls);
        Assert.Empty(client.ListingRequests);
        Assert.Empty(client.MetadataRequests);
    }

    [Fact]
    public async Task Aggregate_collection_rejects_partial_price_data()
    {
        var client = new StubMarketDataClient(
            prices: _ => Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success(
                [Price(1, 10, 100, 10, 200)],
                isPartialData: true));
        var collector = new PublicMarketSnapshotCollector(client, new FrozenClock(ObservedAt));

        var exception = await Assert.ThrowsAsync<PublicMarketSnapshotCollectionException>(
            () => collector.CollectAggregatePricesAsync());

        Assert.Equal(Gw2ApiErrorCategory.IncompleteData, exception.ErrorCategory);
        Assert.Empty(client.ListingRequests);
        Assert.Empty(client.MetadataRequests);
    }

    private static LiveMarketScanner CreateScanner(StubMarketDataClient client) => new(
        new PublicMarketSnapshotCollector(client, new FrozenClock(ObservedAt)),
        client);

    private static MarketPrice Price(int itemId, int buyQuantity, int buyPrice, int sellQuantity, int sellPrice) => new(
        itemId,
        IsWhitelisted: false,
        new MarketOrderSummary(buyQuantity, buyPrice),
        new MarketOrderSummary(sellQuantity, sellPrice));

    private static Gw2ApiResult<IReadOnlyList<T>> Success<T>(IEnumerable<T> values) =>
        Gw2ApiResult<IReadOnlyList<T>>.Success(values.ToArray());

    private sealed class StubMarketDataClient : IGw2ApiClient
    {
        private readonly IReadOnlyList<int> itemIds;
        private readonly Func<IReadOnlyCollection<int>, Gw2ApiResult<IReadOnlyList<MarketPrice>>> prices;
        private readonly Func<IReadOnlyCollection<int>, Gw2ApiResult<IReadOnlyList<MarketListing>>> listings;
        private readonly Func<IReadOnlyCollection<int>, Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> metadata;

        public StubMarketDataClient(
            IReadOnlyList<int>? itemIds = null,
            Func<IReadOnlyCollection<int>, Gw2ApiResult<IReadOnlyList<MarketPrice>>>? prices = null,
            Func<IReadOnlyCollection<int>, Gw2ApiResult<IReadOnlyList<MarketListing>>>? listings = null,
            Func<IReadOnlyCollection<int>, Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>>? metadata = null)
        {
            this.itemIds = itemIds ?? [1];
            this.prices = prices ?? (ids => Success(ids.Select(itemId => Price(itemId, 10, 100, 10, 200))));
            this.listings = listings ?? (ids => Success(ids.Select(Listing)));
            this.metadata = metadata ?? (ids => Success(ids.Select(itemId => new MarketItemMetadata(
                itemId,
                $"Item {itemId}",
                MarketItemStackPolicy.NormalStackLimit))));
        }

        public List<string> Calls { get; } = [];

        public List<IReadOnlyList<int>> ListingRequests { get; } = [];

        public List<IReadOnlyList<int>> MetadataRequests { get; } = [];

        private static MarketListing Listing(int itemId) => new(
            itemId,
            [new MarketOrderLevel(3, 10, 100)],
            [new MarketOrderLevel(3, 10, 200)]);

        public Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("price-item-ids");
            return Task.FromResult(Success(itemIds));
        }

        public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
            IReadOnlyCollection<int> requestedItemIds,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("prices");
            return Task.FromResult(prices(requestedItemIds));
        }

        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
            IReadOnlyCollection<int> requestedItemIds,
            CancellationToken cancellationToken = default)
        {
            ListingRequests.Add(requestedItemIds.ToArray());
            return Task.FromResult(listings(requestedItemIds));
        }

        public Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(
            IReadOnlyCollection<int> requestedItemIds,
            CancellationToken cancellationToken = default)
        {
            MetadataRequests.Add(requestedItemIds.ToArray());
            return Task.FromResult(metadata(requestedItemIds));
        }
    }
}
