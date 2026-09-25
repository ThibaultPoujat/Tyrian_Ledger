using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.PersonalTradingPost;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class CraftingOpportunityServiceTests
{
    [Fact]
    public async Task Unclassified_listing_response_omission_degrades_the_entire_bounded_crafting_read()
    {
        var diagnostics = new RecordingCraftingDiagnostics();
        var service = new CraftingOpportunityService(
            new FixedPersonalGateway(),
            new FixedSnapshotService(Snapshot()),
            new FixedRecipeGateway(
            [
                Recipe(1, 100, 10),
                Recipe(2, 200, 20),
            ]),
            new IncompleteListingMarketClient(),
            new AvailableHistory(),
            new CraftingOpportunityPlanner(new CraftingEconomicsCalculator()),
            diagnostics);

        var result = await service.GetAsync();

        Assert.Equal(CraftingOpportunityState.Degraded, result.State);
        Assert.Equal("market_listings_unavailable", result.EvidenceFailureCode);

        var diagnostic = Assert.Single(diagnostics.Events);
        Assert.Equal(4, diagnostic.RequestedItemIdCount);
        Assert.Null(diagnostic.ListingResponseItemCount);
        Assert.Equal(0, diagnostic.MissingListingItemIdCount);
        Assert.Equal(Gw2ApiErrorCategory.IncompleteData, diagnostic.ListingsErrorCategory);
        Assert.Equal("market_listings_unavailable", diagnostic.Outcome);
    }

    [Fact]
    public async Task Actual_listing_gateway_failure_degrades_the_entire_bounded_crafting_read()
    {
        var diagnostics = new RecordingCraftingDiagnostics();
        var service = new CraftingOpportunityService(
            new FixedPersonalGateway(), new FixedSnapshotService(Snapshot()),
            new FixedRecipeGateway([Recipe(1, 100, 10)]),
            new FailingListingMarketClient(), new AvailableHistory(),
            new CraftingOpportunityPlanner(new CraftingEconomicsCalculator()), diagnostics);

        var result = await service.GetAsync();

        Assert.Equal(CraftingOpportunityState.Degraded, result.State);
        Assert.Equal("market_listings_unavailable", result.EvidenceFailureCode);
        var diagnostic = Assert.Single(diagnostics.Events);
        Assert.Equal(Gw2ApiErrorCategory.RateLimited, diagnostic.ListingsErrorCategory);
        Assert.Equal("market_listings_unavailable", diagnostic.Outcome);
    }

    [Fact]
    public async Task History_reads_are_limited_to_outputs_within_the_bounded_complete_market_set()
    {
        var history = new RecordingHistory();
        var recipes = Enumerable.Range(1, 300)
            .Select(id => Recipe(id, 10_000 + id, 20_000 + id)).ToArray();
        var market = new CompleteMarketClient();
        var service = new CraftingOpportunityService(
            new FixedPersonalGateway(), new FixedSnapshotService(Snapshot()),
            new FixedRecipeGateway(recipes), market, history,
            new CraftingOpportunityPlanner(new CraftingEconomicsCalculator()));

        await service.GetAsync();

        Assert.True(history.RequestedItemIds.Count <= 200);
        Assert.All(history.RequestedItemIds, itemId => Assert.InRange(itemId, 10_001, 10_200));
    }

    private static AccountCraftingSnapshot Snapshot() => new(
        new AccountScope("test-account"), DateTimeOffset.UtcNow,
        CraftingFeatureResult<IReadOnlyList<AccountInventoryEntry>>.Available([]),
        CraftingFeatureResult<IReadOnlyList<AccountMaterialEntry>>.Available([]),
        CraftingFeatureResult<IReadOnlyList<int>>.Available([1, 2]),
        CraftingFeatureResult<IReadOnlyList<CraftingDisciplineCapability>>.Available([new("Artificer", 500, true)]));

    private static CraftingRecipe Recipe(int recipeId, int outputItemId, int inputItemId) =>
        new(recipeId, outputItemId, 1, ["Artificer"], 1, [], [new("Item", inputItemId, 1)]);

    private static MarketListing Listing(int itemId, int sell, int buy) => new(
        itemId, [new MarketOrderLevel(1, 100, buy)], [new MarketOrderLevel(1, 100, sell)]);

    private sealed class RecordingCraftingDiagnostics : ICraftingEvidenceDiagnostics
    {
        public List<CraftingMarketEvidenceDiagnostic> Events { get; } = [];
        public void Record(CraftingMarketEvidenceDiagnostic diagnostic) => Events.Add(diagnostic);
    }

    private sealed class FixedPersonalGateway : IPersonalTradingPostGateway
    {
        public Task<Gw2ApiResult<AccountScope>> GetAccountScopeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<AccountScope>.Success(new AccountScope("test-account")));
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCurrentBuyOrdersAsync(int page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCurrentSellListingsAsync(int page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCompletedBuyHistoryAsync(int page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCompletedSellHistoryAsync(int page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedSnapshotService(AccountCraftingSnapshot snapshot) : IAccountCraftingSnapshotService
    {
        public Task<Gw2ApiResult<AccountCraftingSnapshot>> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<AccountCraftingSnapshot>.Success(snapshot));
        public Task<AccountCraftingSnapshot?> GetLatestAsync(AccountScope accountScope, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountCraftingSnapshot?>(snapshot);
    }

    private sealed class FixedRecipeGateway(IReadOnlyList<CraftingRecipe> recipes) : ICraftingReferenceGateway
    {
        public Task<Gw2ApiResult<IReadOnlyList<CraftingRecipe>>> GetRecipesAsync(IReadOnlyCollection<int> recipeIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<IReadOnlyList<CraftingRecipe>>.Success(recipes));
    }

    private sealed class IncompleteListingMarketClient : IGw2ApiClient
    {
        public Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<IReadOnlyList<MarketListing>>.Failure(Gw2ApiErrorCategory.IncompleteData));
        public Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>.Success(itemIds.Select(itemId => new MarketItemMetadata(itemId, $"Item {itemId}", 250)).ToArray()));
    }

    private sealed class FailingListingMarketClient : IGw2ApiClient
    {
        public Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<IReadOnlyList<MarketListing>>.Failure(Gw2ApiErrorCategory.RateLimited));
        public Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>.Success([]));
    }

    private sealed class CompleteMarketClient : IGw2ApiClient
    {
        public Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<IReadOnlyList<MarketListing>>.Success(itemIds.Select(id => Listing(id, 1_000, 1_000)).ToArray()));
        public Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>.Success(itemIds.Select(id => new MarketItemMetadata(id, $"Item {id}", 250)).ToArray()));
    }

    private sealed class AvailableHistory : IHistoricalMarketAnalyticsService
    {
        public Task<HistoricalMarketAnalytics> GetAsync(int itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(History(itemId));
        public Task<HistoricalMarketAnalytics> GetAtAsync(int itemId, DateTimeOffset asOfUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(History(itemId, asOfUtc));

        private static HistoricalMarketAnalytics History(int itemId, DateTimeOffset? now = null)
        {
            var asOf = now ?? DateTimeOffset.UtcNow;
            var windows = new[] { TimeSpan.FromDays(7), TimeSpan.FromDays(30) }.Select(duration =>
                new HistoricalMarketWindowAnalytics(HistoricalMarketWindowState.Available,
                    new HistoricalMarketWindowCoverage(asOf - duration, asOf, 10, 10, 0, asOf - duration, asOf, 100m, TimeSpan.FromHours(1)), null)).ToArray();
            return new(itemId, asOf, false, HistoricalMarketAnalyticsSettings.Default, null, windows);
        }
    }

    private sealed class RecordingHistory : IHistoricalMarketAnalyticsService
    {
        public List<int> RequestedItemIds { get; } = [];
        public Task<HistoricalMarketAnalytics> GetAsync(int itemId, CancellationToken cancellationToken = default)
        {
            RequestedItemIds.Add(itemId);
            return Task.FromResult(Available(itemId));
        }

        public Task<HistoricalMarketAnalytics> GetAtAsync(int itemId, DateTimeOffset asOfUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(Available(itemId, asOfUtc));

        private static HistoricalMarketAnalytics Available(int itemId, DateTimeOffset? now = null)
        {
            var asOf = now ?? DateTimeOffset.UtcNow;
            var windows = new[] { TimeSpan.FromDays(7), TimeSpan.FromDays(30) }.Select(duration =>
                new HistoricalMarketWindowAnalytics(HistoricalMarketWindowState.Available,
                    new HistoricalMarketWindowCoverage(asOf - duration, asOf, 10, 10, 0, asOf - duration, asOf, 100m, TimeSpan.FromHours(1)), null)).ToArray();
            return new(itemId, asOf, false, HistoricalMarketAnalyticsSettings.Default, null, windows);
        }
    }
}
