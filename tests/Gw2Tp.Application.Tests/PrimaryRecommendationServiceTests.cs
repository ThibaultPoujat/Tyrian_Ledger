using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.MarketHistory;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Application.Finance;
using Gw2Tp.Application.LocalData;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.MarketScanning;
using Gw2Tp.Application.MarketSnapshots;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Application.Recommendations;
using Gw2Tp.Application.Time;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class PrimaryRecommendationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);
    private static readonly AccountProfile Profile = new(1, "opaque-account-scope", Now.AddDays(-40), Now.AddMinutes(-5));

    [Fact]
    public async Task Account_failure_is_safe_and_does_not_open_the_personal_data_gate()
    {
        var gate = new RecordingGate();
        var repository = new FakeRepository();
        var service = CreateService(
            new FakePortfolioGateway(Gw2ApiResult<AccountPortfolioSnapshot>.Failure(Gw2ApiErrorCategory.Unauthorized)),
            repository,
            gate: gate);

        var result = await service.GetAsync();

        Assert.Equal(PrimaryRecommendationState.AccountUnavailable, result.State);
        Assert.Equal(nameof(Gw2ApiErrorCategory.Unauthorized), result.EvidenceError);
        Assert.Null(result.Portfolio);
        Assert.Empty(result.Actions);
        Assert.Equal(0, gate.AcquisitionCount);
        Assert.Equal(0, repository.ReadCount);
        Assert.Equal(0, repository.MutationCount);
    }

    [Fact]
    public async Task Empty_synchronized_portfolio_uses_wallet_cash_defaults_and_one_coherent_read_gate()
    {
        var gate = new RecordingGate();
        var repository = new FakeRepository
        {
            Profile = Profile,
            CurrentOrders = new CurrentPersonalTradingPostOrderSnapshot(Now.AddMinutes(-1), []),
        };
        var service = CreateService(SuccessfulPortfolio(100_000), repository, gate: gate);

        var result = await service.GetAsync();

        Assert.Equal(PrimaryRecommendationState.Ready, result.State);
        Assert.Equal(100_000, result.Portfolio!.AvailableCash.Copper);
        Assert.Equal(100_000, result.Portfolio.TotalBankroll.Copper);
        Assert.Equal(15_000, result.Portfolio.CashReserve.Copper);
        Assert.Equal(100_000, result.Portfolio.RemainingCashAfterSizing.Copper);
        Assert.Equal(1_500, result.Policies.CashReserveBasisPoints);
        Assert.Equal("FastFlip", result.Policies.Strategy);
        Assert.Equal("TradingPost", result.Policies.Category);
        Assert.Equal(Now.AddMinutes(10), result.AccountEvidenceExpiresAtUtc);
        Assert.Equal(1, gate.AcquisitionCount);
        Assert.Equal(1, gate.DisposalCount);
        Assert.Equal(0, repository.MutationCount);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Stale_account_evidence_suppresses_all_actions_until_a_new_sync(
        bool staleSuccessfulSync,
        bool staleCurrentOrders)
    {
        var staleAtUtc = Now - PrimaryRecommendationFreshnessPolicy.Default.PersonalSyncMaximumAge - TimeSpan.FromSeconds(1);
        var repository = new FakeRepository
        {
            Profile = Profile with { LastSuccessfulSyncAtUtc = staleSuccessfulSync ? staleAtUtc : Now.AddMinutes(-1) },
            CurrentOrders = new CurrentPersonalTradingPostOrderSnapshot(
                staleCurrentOrders ? staleAtUtc : Now.AddMinutes(-1), []),
        };
        var scanner = new RecordingScanner();
        var service = CreateService(SuccessfulPortfolio(100_000), repository, scanner: scanner);

        var result = await service.GetAsync();

        Assert.Equal(PrimaryRecommendationState.AccountEvidenceStale, result.State);
        Assert.Equal("account_evidence_stale", result.EvidenceError);
        Assert.Empty(result.Actions);
        Assert.Null(result.Portfolio);
        Assert.Null(scanner.Settings);
        Assert.Equal(staleAtUtc + TimeSpan.FromMinutes(15), result.AccountEvidenceExpiresAtUtc);
    }

    [Fact]
    public async Task Account_evidence_that_expires_during_generation_is_not_returned_ready()
    {
        var repository = SynchronizedRepository();
        var clock = new SequenceClock(Now, Now, Now.AddMinutes(10).AddSeconds(1));
        var service = CreateService(SuccessfulPortfolio(100_000), repository, clock: clock);

        var result = await service.GetAsync();

        Assert.Equal(PrimaryRecommendationState.AccountEvidenceStale, result.State);
        Assert.Empty(result.Actions);
        Assert.Null(result.Portfolio);
        Assert.Equal(Now.AddMinutes(10), result.AccountEvidenceExpiresAtUtc);
    }

    [Fact]
    public async Task Recommendation_scan_receives_the_bankroll_risk_discovery_limit()
    {
        var scanner = new RecordingScanner();
        var repository = new FakeRepository
        {
            Profile = Profile,
            CurrentOrders = new CurrentPersonalTradingPostOrderSnapshot(Now.AddMinutes(-1), []),
        };
        var service = CreateService(SuccessfulPortfolio(100_000), repository, scanner: scanner);

        var result = await service.GetAsync();

        Assert.Equal(PrimaryRecommendationState.Ready, result.State);
        Assert.Equal(5_000, scanner.Settings?.MaximumCandidateTotalCost?.Copper);
    }

    [Fact]
    public async Task Sell_quantity_beyond_fifo_inventory_keeps_basis_unknown_and_disables_buy_sizing()
    {
        var repository = PersonalRepository(
            buyQuantity: 2,
            sellQuantity: 3,
            sellUnitPrice: 201);
        var service = CreateService(SuccessfulPortfolio(100_000), repository, MarketListingFor(42));

        var result = await service.GetAsync();

        Assert.Equal(PrimaryRecommendationState.Ready, result.State);
        Assert.Equal("buy_sizing_unavailable", result.EvidenceError);
        Assert.Null(result.Portfolio);
        var listing = Assert.Single(result.Actions, action => action.Source == PrimaryRecommendationSource.SellListing);
        Assert.Equal(PrimaryRecommendationAction.Review, listing.Action);
        Assert.Null(listing.Economics);
        Assert.Contains(listing.Reasons, reason => reason.Code == PrimaryRecommendationReasonCode.UnknownCostBasis);
        Assert.Equal(0, repository.MutationCount);
    }

    [Fact]
    public async Task Known_sell_listing_uses_fifo_basis_and_recalculates_fees_for_the_full_quantity()
    {
        var repository = PersonalRepository(
            buyQuantity: 3,
            sellQuantity: 3,
            sellUnitPrice: 201);
        var service = CreateService(SuccessfulPortfolio(100_000), repository, MarketListingFor(42));

        var result = await service.GetAsync();

        var listing = Assert.Single(result.Actions, action => action.Source == PrimaryRecommendationSource.SellListing);
        Assert.Equal(PrimaryRecommendationAction.LeaveSellListing, listing.Action);
        Assert.Equal(300, listing.Capital.Copper);
        Assert.Equal(603, listing.Economics!.GrossSaleValue.Copper);
        Assert.Equal(31, listing.Economics.ListingFee.Copper);
        Assert.Equal(61, listing.Economics.ExchangeFee.Copper);
        Assert.Equal(511, listing.Economics.NetSaleProceeds.Copper);
        Assert.Equal(211, listing.Economics.NetProfit.Copper);
        Assert.Equal(331, listing.Economics.TotalCost.Copper);
        Assert.Equal("63.75%", listing.Economics.RoiDisplayPercent);
        Assert.Equal(300, result.Portfolio!.TotalBankroll.Copper - result.Portfolio.AvailableCash.Copper);
        Assert.Equal(0, repository.MutationCount);
    }

    [Fact]
    public async Task Composed_new_opportunities_size_full_buy_and_half_sized_buy_small_with_exact_economics()
    {
        var repository = SynchronizedRepository();
        var candidates = new[] { Candidate(1), Candidate(2) };
        var histories = new Dictionary<int, HistoricalMarketAnalytics>
        {
            [1] = History(1, sevenDayAvailable: true, thirtyDayAvailable: true),
            [2] = History(2, sevenDayAvailable: true, thirtyDayAvailable: false),
        };
        var marketClient = new ScenarioMarketClient(
            listings: candidates.Select(candidate => Listing(candidate.Item.ItemId, 100)).ToDictionary(value => value.ItemId));
        var service = CreateService(
            SuccessfulPortfolio(1_000_000),
            repository,
            scanner: new StaticScanner(candidates),
            marketClient: marketClient,
            historyService: new FakeHistoryService(histories));

        var result = await service.GetAsync();

        var buy = Assert.Single(result.Actions, action =>
            action.Source == PrimaryRecommendationSource.NewOpportunity && action.ItemId == 1);
        var buySmall = Assert.Single(result.Actions, action =>
            action.Source == PrimaryRecommendationSource.NewOpportunity && action.ItemId == 2);
        Assert.Equal(PrimaryRecommendationAction.Buy, buy.Action);
        Assert.Equal("https://render.guildwars2.com/file/synthetic-1.png", buy.ItemIconUrl);
        Assert.Equal(10, buy.Quantity);
        Assert.Equal(1_110, buy.Capital.Copper);
        Assert.Equal(1_110, buy.Economics!.TotalCost.Copper);
        Assert.Equal(PrimaryRecommendationAction.BuySmall, buySmall.Action);
        Assert.Equal(5, buySmall.Quantity);
        Assert.Equal(buy.Quantity / 2, buySmall.Quantity);
        Assert.Equal(555, buySmall.Capital.Copper);
        Assert.Equal(555, buySmall.Economics!.TotalCost.Copper);
        Assert.Equal(995, buySmall.Economics.GrossSaleValue.Copper);
        Assert.Equal(50, buySmall.Economics.ListingFee.Copper);
        Assert.Equal(100, buySmall.Economics.ExchangeFee.Copper);
        Assert.DoesNotContain(buySmall.PortfolioConstraints, constraint => constraint.IsBinding);
        Assert.Equal(
            result.Portfolio!.AvailableCash.Copper - new[] { buy, buySmall }.Sum(action => action.Capital.Copper),
            result.Portfolio.RemainingCashAfterSizing.Copper);
        Assert.Equal(0, repository.MutationCount);
    }

    [Fact]
    public async Task Composed_buy_small_reduction_releases_shared_headroom_for_later_ranked_candidates()
    {
        var itemIds = Enumerable.Range(1, 19).ToArray();
        var candidates = itemIds.Select(itemId => Candidate(itemId)).ToArray();
        var histories = itemIds.ToDictionary(itemId => itemId, itemId => History(itemId, true, false));
        var listings = itemIds.ToDictionary(itemId => itemId, itemId => Listing(itemId, 100));
        var repository = SynchronizedRepository();
        var service = CreateService(
            SuccessfulPortfolio(100_000),
            repository,
            scanner: new StaticScanner(candidates),
            marketClient: new ScenarioMarketClient(listings: listings),
            historyService: new FakeHistoryService(histories));

        var result = await service.GetAsync();

        var purchases = result.Actions.Where(action => action.Source == PrimaryRecommendationSource.NewOpportunity).ToArray();
        Assert.Equal(19, purchases.Length);
        Assert.All(purchases, action =>
        {
            Assert.Equal(PrimaryRecommendationAction.BuySmall, action.Action);
            Assert.Equal(5, action.Quantity);
            Assert.Equal(555, action.Capital.Copper);
        });
        Assert.Equal(5, purchases.Single(action => action.Score!.Rank == 19).Quantity);
        Assert.Equal(
            result.Portfolio!.AvailableCash.Copper - purchases.Sum(action => action.Capital.Copper),
            result.Portfolio.RemainingCashAfterSizing.Copper);
        Assert.Equal(0, repository.MutationCount);
    }

    [Fact]
    public async Task Primary_portfolio_uses_exact_returned_capital_without_relaxing_conservative_sizing()
    {
        var candidate = Candidate(1, marketQuantity: 10_000);
        var repository = SynchronizedRepository();
        var service = CreateService(
            SuccessfulPortfolio(100_000),
            repository,
            scanner: new StaticScanner([candidate]),
            marketClient: new ScenarioMarketClient(
                listings: new Dictionary<int, MarketListing> { [1] = Listing(1, 10_000) }),
            historyService: new FakeHistoryService(
                new Dictionary<int, HistoricalMarketAnalytics> { [1] = History(1, true, true) }));

        var result = await service.GetAsync();

        var purchase = Assert.Single(result.Actions, action =>
            action.Source == PrimaryRecommendationSource.NewOpportunity);
        Assert.Equal(PrimaryRecommendationAction.Buy, purchase.Action);
        Assert.Equal(45, purchase.Quantity);
        Assert.Equal(4_993, purchase.Capital.Copper);
        Assert.Equal(4_993, purchase.Economics!.TotalCost.Copper);
        Assert.Equal(95_007, result.Portfolio!.RemainingCashAfterSizing.Copper);
        var itemConstraint = Assert.Single(purchase.PortfolioConstraints, constraint =>
            constraint.Name == PositionSizingConstraintName.ItemExposure);
        Assert.True(itemConstraint.IsBinding);
        Assert.Equal(5_000, itemConstraint.CapitalCapacity.Copper);
        Assert.Equal(45, itemConstraint.QuantityCapacity);
        Assert.Equal(0, repository.MutationCount);
    }

    [Fact]
    public async Task Composed_competitive_buy_order_is_kept_only_when_current_exposure_is_within_every_cap()
    {
        var repository = SynchronizedRepository([
            new CurrentPersonalTradingPostOrder(77, PersonalTradingPostSide.Buy, 42, 100, 10, Now.AddHours(-1)),
        ]);
        var candidate = Candidate(42);
        var service = CreateService(
            SuccessfulPortfolio(99_000),
            repository,
            scanner: new StaticScanner([candidate]),
            marketClient: new ScenarioMarketClient(listings: new Dictionary<int, MarketListing> { [42] = Listing(42, 100) }),
            historyService: new FakeHistoryService(new Dictionary<int, HistoricalMarketAnalytics> { [42] = History(42, true, true) }));

        var result = await service.GetAsync();

        var order = Assert.Single(result.Actions, action => action.Source == PrimaryRecommendationSource.BuyOrder);
        Assert.Equal(PrimaryRecommendationAction.KeepBid, order.Action);
        Assert.Equal(3, order.PortfolioConstraints.Count);
        Assert.DoesNotContain(order.PortfolioConstraints, constraint => constraint.IsBinding);
        Assert.Equal(0, repository.MutationCount);
    }

    [Fact]
    public async Task Composed_competitive_buy_order_requires_review_when_item_strategy_and_category_caps_are_breached()
    {
        var repository = SynchronizedRepository([
            new CurrentPersonalTradingPostOrder(77, PersonalTradingPostSide.Buy, 42, 100, 300, Now.AddHours(-1)),
        ]);
        var candidate = Candidate(42);
        var service = CreateService(
            SuccessfulPortfolio(70_000),
            repository,
            scanner: new StaticScanner([candidate]),
            marketClient: new ScenarioMarketClient(listings: new Dictionary<int, MarketListing> { [42] = Listing(42, 400) }),
            historyService: new FakeHistoryService(new Dictionary<int, HistoricalMarketAnalytics> { [42] = History(42, true, true) }));

        var result = await service.GetAsync();

        var order = Assert.Single(result.Actions, action => action.Source == PrimaryRecommendationSource.BuyOrder);
        Assert.Equal(PrimaryRecommendationAction.Review, order.Action);
        Assert.Equal(
            [PositionSizingConstraintName.ItemExposure, PositionSizingConstraintName.StrategyConcentration, PositionSizingConstraintName.CategoryConcentration],
            order.PortfolioConstraints.Where(constraint => constraint.IsBinding).Select(constraint => constraint.Name));
        Assert.Equal(PositionSizingLiquidity.Low, order.Liquidity!.Classification);
        Assert.Equal([1_500L, 20_000L, 25_000L], order.PortfolioConstraints.Select(constraint => constraint.CapitalCapacity.Copper));
        Assert.Contains(order.Reasons, reason => reason.Code == PrimaryRecommendationReasonCode.ItemExposureExceeded);
        Assert.Contains(order.Reasons, reason => reason.Code == PrimaryRecommendationReasonCode.StrategyExposureExceeded);
        Assert.Contains(order.Reasons, reason => reason.Code == PrimaryRecommendationReasonCode.CategoryExposureExceeded);
        Assert.Equal(0, repository.MutationCount);
    }

    [Fact]
    public async Task Candidate_present_buy_order_rebuilds_liquidity_for_current_quantity_before_applying_item_cap()
    {
        var repository = SynchronizedRepository([
            new CurrentPersonalTradingPostOrder(78, PersonalTradingPostSide.Buy, 42, 100, 30, Now.AddHours(-1)),
        ]);
        var candidate = Candidate(42);
        var service = CreateService(
            SuccessfulPortfolio(97_000),
            repository,
            scanner: new StaticScanner([candidate]),
            marketClient: new ScenarioMarketClient(listings: new Dictionary<int, MarketListing> { [42] = Listing(42, 100) }),
            historyService: new FakeHistoryService(new Dictionary<int, HistoricalMarketAnalytics> { [42] = History(42, true, true) }));

        var result = await service.GetAsync();

        var order = Assert.Single(result.Actions, action => action.Source == PrimaryRecommendationSource.BuyOrder);
        Assert.Equal(PrimaryRecommendationAction.Review, order.Action);
        Assert.Equal(PositionSizingLiquidity.Low, order.Liquidity!.Classification);
        var itemConstraint = Assert.Single(order.PortfolioConstraints, constraint =>
            constraint.Name == PositionSizingConstraintName.ItemExposure);
        Assert.True(itemConstraint.IsBinding);
        Assert.Equal(1_500, itemConstraint.CapitalCapacity.Copper);
        Assert.Equal(15, itemConstraint.QuantityCapacity);
        Assert.Contains(order.Liquidity.Reasons, reason =>
            reason == LiveMarketScannerLiquidityReason.ParticipationCapBelowIntendedQuantity);
        Assert.Equal(0, repository.MutationCount);
    }

    [Fact]
    public async Task Composed_inventory_reduction_uses_only_safe_depth_and_exact_fifo_scope()
    {
        var repository = InventoryRepository(quantity: 10);
        var service = CreateService(
            SuccessfulPortfolio(9_000),
            repository,
            scanner: new EmptyScanner(),
            marketClient: new ScenarioMarketClient(listings: new Dictionary<int, MarketListing> { [42] = Listing(42, 100) }),
            historyService: new FakeHistoryService(new Dictionary<int, HistoricalMarketAnalytics> { [42] = History(42, true, true) }));

        var result = await service.GetAsync();

        var inventory = Assert.Single(result.Actions, action => action.Source == PrimaryRecommendationSource.Inventory);
        Assert.Equal(PrimaryRecommendationAction.Reduce, inventory.Action);
        Assert.Equal(5, inventory.Quantity);
        Assert.Equal(500, inventory.Capital.Copper);
        Assert.Equal(500, inventory.Economics!.AcquisitionCost.Copper);
        Assert.Equal(500, inventory.Economics.GrossSaleValue.Copper);
        Assert.Contains(inventory.PortfolioConstraints, constraint =>
            constraint.Name == PositionSizingConstraintName.ItemExposure && constraint.IsBinding);
        Assert.Equal(0, repository.MutationCount);
    }

    [Fact]
    public async Task Immediate_sale_uses_the_backend_simulated_price_range_for_the_suggested_quantity()
    {
        var repository = InventoryRepository(quantity: 100);
        var listing = new MarketListing(
            42,
            [new MarketOrderLevel(3, 10, 200), new MarketOrderLevel(3, 990, 190)],
            [new MarketOrderLevel(3, 1_000, 300)]);
        var service = CreateService(
            SuccessfulPortfolio(400_000),
            repository,
            scanner: new EmptyScanner(),
            marketClient: new ScenarioMarketClient(listings: new Dictionary<int, MarketListing> { [42] = listing }),
            historyService: new FakeHistoryService(new Dictionary<int, HistoricalMarketAnalytics> { [42] = History(42, true, true) }));

        var result = await service.GetAsync();

        var inventory = Assert.Single(result.Actions, action => action.Source == PrimaryRecommendationSource.Inventory);
        Assert.Equal(PrimaryRecommendationAction.Sell, inventory.Action);
        Assert.Equal(100, inventory.Quantity);
        Assert.Equal(19_100, inventory.Economics!.GrossSaleValue.Copper);
        var range = Assert.IsType<PrimaryRecommendationImmediateSalePriceRange>(inventory.Prices.ImmediateSalePriceRange);
        Assert.Equal(190, range.LowestUnitPrice.Copper);
        Assert.Equal(200, range.HighestUnitPrice.Copper);
        Assert.Equal(0, repository.MutationCount);
    }

    [Fact]
    public async Task Optional_icons_for_non_scanner_items_do_not_add_a_public_metadata_read()
    {
        var repository = InventoryRepository(quantity: 1);
        var marketClient = new ScenarioMarketClient(listings: new Dictionary<int, MarketListing>
        {
            [42] = Listing(42, 100),
        });
        var service = CreateService(
            SuccessfulPortfolio(100_000),
            repository,
            scanner: new EmptyScanner(),
            marketClient: marketClient,
            historyService: new FakeHistoryService(new Dictionary<int, HistoricalMarketAnalytics> { [42] = History(42, true, true) }));

        var result = await service.GetAsync();

        var inventory = Assert.Single(result.Actions, action => action.Source == PrimaryRecommendationSource.Inventory);
        Assert.Null(inventory.ItemIconUrl);
        Assert.Equal(0, marketClient.MetadataRequestCount);
    }

    [Fact]
    public async Task Composed_inventory_breach_with_zero_safe_depth_reviews_with_zero_scoped_economics()
    {
        var repository = InventoryRepository(quantity: 10);
        var service = CreateService(
            SuccessfulPortfolio(9_000),
            repository,
            scanner: new EmptyScanner(),
            marketClient: new ScenarioMarketClient(listings: new Dictionary<int, MarketListing> { [42] = Listing(42, 1) }),
            historyService: new FakeHistoryService(new Dictionary<int, HistoricalMarketAnalytics> { [42] = History(42, true, true) }));

        var result = await service.GetAsync();

        var inventory = Assert.Single(result.Actions, action => action.Source == PrimaryRecommendationSource.Inventory);
        Assert.Equal(PrimaryRecommendationAction.Review, inventory.Action);
        Assert.Equal(0, inventory.Quantity);
        Assert.Equal(0, inventory.Capital.Copper);
        Assert.Null(inventory.Economics);
        Assert.Contains(inventory.PortfolioConstraints, constraint =>
            constraint.Name == PositionSizingConstraintName.ItemExposure && constraint.IsBinding);
        Assert.Contains(inventory.PortfolioConstraints, constraint =>
            constraint.Name == PositionSizingConstraintName.LiquidityParticipation && constraint.IsBinding && constraint.QuantityCapacity == 0);
        Assert.Contains(inventory.Reasons, reason => reason.Code == PrimaryRecommendationReasonCode.ItemExposureExceeded);
        Assert.Contains(inventory.Reasons, reason => reason.Code == PrimaryRecommendationReasonCode.SafeDepthLimited);
        Assert.Equal(0, repository.MutationCount);
    }

    [Fact]
    public async Task Two_hundred_and_first_affordable_market_survives_scan_and_becomes_the_final_recommendation()
    {
        var expensiveItemIds = Enumerable.Range(1, LiveMarketScanner.MaximumCandidateCount).ToArray();
        var affordableItemId = LiveMarketScanner.MaximumCandidateCount + 1;
        var itemIds = expensiveItemIds.Append(affordableItemId).ToArray();
        var prices = expensiveItemIds
            .Select(itemId => Price(itemId, 10_000, 20_000))
            .Append(Price(affordableItemId, 100, 200))
            .ToDictionary(value => value.ItemId);
        var listings = itemIds.ToDictionary(itemId => itemId, itemId =>
            itemId == affordableItemId ? Listing(itemId, 100) : Listing(itemId, 100, 10_000, 20_000));
        var marketClient = new ScenarioMarketClient(itemIds, prices, listings);
        var scanner = new LiveMarketScanner(new PublicMarketSnapshotCollector(marketClient, new FixedClock(Now)), marketClient);
        var repository = SynchronizedRepository();
        var service = CreateService(
            SuccessfulPortfolio(100_000),
            repository,
            scanner: scanner,
            marketClient: marketClient,
            historyService: new FakeHistoryService(new Dictionary<int, HistoricalMarketAnalytics>
            {
                [affordableItemId] = History(affordableItemId, true, true),
            }));

        var result = await service.GetAsync();

        var action = Assert.Single(result.Actions, value => value.Source == PrimaryRecommendationSource.NewOpportunity);
        Assert.Equal(affordableItemId, action.ItemId);
        Assert.Equal(PrimaryRecommendationAction.Buy, action.Action);
        Assert.True(action.Quantity > 0);
        Assert.Equal(0, repository.MutationCount);
    }

    [Fact]
    public async Task Recommendation_score_uses_item_scoped_supported_personal_realized_evidence()
    {
        var repository = SupportedPersonalEvidenceRepository(itemId: 42);
        var candidate = Candidate(42);
        var service = CreateService(
            SuccessfulPortfolio(100_000),
            repository,
            scanner: new StaticScanner([candidate]),
            marketClient: new ScenarioMarketClient(listings: new Dictionary<int, MarketListing> { [42] = Listing(42, 100) }),
            historyService: new FakeHistoryService(new Dictionary<int, HistoricalMarketAnalytics> { [42] = History(42, true, true) }));

        var result = await service.GetAsync();

        var score = Assert.Single(result.Actions, action => action.Source == PrimaryRecommendationSource.NewOpportunity).Score;
        var personal = Assert.IsType<PrimaryRecommendationPersonalEvidence>(score!.PersonalEvidence);
        Assert.Equal(OpportunityPersonalEvidenceState.Supported, personal.State);
        Assert.Equal(3, personal.KnownBasisSampleCount);
        Assert.Equal(3, personal.RealizedRoiSaleCount);
        Assert.NotNull(personal.MedianRealizedRoiBasisPoints);
        Assert.NotNull(personal.RealizedProfitPerDayNumerator);
        Assert.NotNull(personal.CapitalTurnsPerDayNumerator);
        Assert.Equal(0, repository.MutationCount);
    }

    private static PrimaryRecommendationService CreateService(
        IAccountPortfolioGateway portfolioGateway,
        FakeRepository repository,
        MarketListing? listing = null,
        RecordingGate? gate = null,
        ILiveMarketScanner? scanner = null,
        IGw2ApiClient? marketClient = null,
        IHistoricalMarketAnalyticsService? historyService = null,
        IClock? clock = null) => new(
            portfolioGateway,
            repository,
            new FakeMetadataRepository(),
            new FakeSettingsRepository(),
            gate ?? new RecordingGate(),
            scanner ?? new EmptyScanner(),
            marketClient ?? new FakeMarketClient(listing),
            historyService ?? new FakeHistoryService(),
            clock ?? new FixedClock(Now),
            new OpportunityScoreService(),
            new PrimaryRecommendationPolicy());

    private static IAccountPortfolioGateway SuccessfulPortfolio(long cash) =>
        new FakePortfolioGateway(Gw2ApiResult<AccountPortfolioSnapshot>.Success(
            new AccountPortfolioSnapshot(new AccountScope(Profile.AccountScopeId), new Money(cash))));

    private static FakeRepository PersonalRepository(int buyQuantity, int sellQuantity, int sellUnitPrice)
    {
        var buy = new CompletedPersonalTradingPostTransaction(
            10, PersonalTradingPostSide.Buy, 42, 100, buyQuantity, Now.AddDays(-2), Now.AddDays(-2));
        return new FakeRepository
        {
            Profile = Profile,
            Coverage = new PersonalTradingPostHistoryCoverage(Now.AddDays(-30), Now),
            Completed = [new StoredCompletedPersonalTradingPostTransaction(buy, Now.AddDays(-2), Now.AddDays(-2))],
            CurrentOrders = new CurrentPersonalTradingPostOrderSnapshot(Now.AddMinutes(-1), [
                new CurrentPersonalTradingPostOrder(99, PersonalTradingPostSide.Sell, 42, sellUnitPrice, sellQuantity, Now.AddHours(-1)),
            ]),
        };
    }

    private static FakeRepository SynchronizedRepository(IReadOnlyList<CurrentPersonalTradingPostOrder>? orders = null) => new()
    {
        Profile = Profile,
        CurrentOrders = new CurrentPersonalTradingPostOrderSnapshot(Now.AddMinutes(-1), orders ?? []),
    };

    private static FakeRepository InventoryRepository(int quantity)
    {
        var buy = new CompletedPersonalTradingPostTransaction(
            10, PersonalTradingPostSide.Buy, 42, 100, quantity, Now.AddDays(-2), Now.AddDays(-2));
        return new FakeRepository
        {
            Profile = Profile,
            Coverage = new PersonalTradingPostHistoryCoverage(Now.AddDays(-30), Now),
            Completed = [new StoredCompletedPersonalTradingPostTransaction(buy, Now.AddDays(-2), Now.AddDays(-2))],
            CurrentOrders = new CurrentPersonalTradingPostOrderSnapshot(Now.AddMinutes(-1), []),
        };
    }

    private static FakeRepository SupportedPersonalEvidenceRepository(int itemId)
    {
        var completed = new List<StoredCompletedPersonalTradingPostTransaction>();
        for (var index = 0; index < 3; index++)
        {
            var buyCompletedAtUtc = Now.AddDays(-9 + (index * 2));
            var sellCompletedAtUtc = buyCompletedAtUtc.AddDays(1);
            var buy = new CompletedPersonalTradingPostTransaction(
                100 + (index * 2), PersonalTradingPostSide.Buy, itemId, 100, 1, buyCompletedAtUtc, buyCompletedAtUtc);
            var sell = new CompletedPersonalTradingPostTransaction(
                101 + (index * 2), PersonalTradingPostSide.Sell, itemId, 200, 1, buyCompletedAtUtc, sellCompletedAtUtc);
            completed.Add(new StoredCompletedPersonalTradingPostTransaction(buy, buyCompletedAtUtc, buyCompletedAtUtc));
            completed.Add(new StoredCompletedPersonalTradingPostTransaction(sell, sellCompletedAtUtc, sellCompletedAtUtc));
        }
        return new FakeRepository
        {
            Profile = Profile,
            Coverage = new PersonalTradingPostHistoryCoverage(Now.AddDays(-30), Now),
            Completed = completed,
            CurrentOrders = new CurrentPersonalTradingPostOrderSnapshot(Now.AddMinutes(-1), []),
        };
    }

    private static MarketListing MarketListingFor(int itemId) => new(
        itemId,
        [new MarketOrderLevel(3, 100, 100)],
        [new MarketOrderLevel(3, 100, 200)]);

    private static LiveMarketScannerCandidate Candidate(int itemId, int marketQuantity = 100)
    {
        var plannedBid = new Money(101);
        var plannedList = new Money(199);
        var profit = new FlipProfitCalculator(Gw2TradingPostFeePolicy.Create()).Calculate(plannedBid, plannedList);
        var totalCost = Gw2TradingPostFeePolicy.CalculateFullUpFrontCost(plannedBid, profit.ListingFee);
        var buys = new[] { new MarketOrderLevel(3, marketQuantity, 100) };
        var sells = new[] { new MarketOrderLevel(3, marketQuantity, 200) };
        var simulator = new OrderBookExecutionSimulator();
        var liquidity = new LiveMarketScannerLiquidityEvidence(
            marketQuantity, marketQuantity, marketQuantity, marketQuantity, 3, 3, null, null, false, false,
            simulator.SimulateAcquisition([new OrderBookLevel(marketQuantity, new Money(200))], 1),
            simulator.SimulateLiquidation([new OrderBookLevel(marketQuantity, new Money(100))], 1),
            marketQuantity / 10, [], buys, sells);
        return new LiveMarketScannerCandidate(
            new MarketItemMetadata(
                itemId,
                $"Item {itemId}",
                MarketItemStackPolicy.NormalStackLimit,
                $"https://render.guildwars2.com/file/synthetic-{itemId}.png"),
            new MarketOrderSummary(100, 100), new MarketOrderSummary(100, 200),
            plannedBid, plannedList, profit, totalCost, new ExactRoi(profit.NetProfit, totalCost),
            new Money(168), [], liquidity);
    }

    private static MarketListing Listing(int itemId, int quantity, int buyPrice = 100, int sellPrice = 200) => new(
        itemId,
        [new MarketOrderLevel(3, quantity, buyPrice)],
        [new MarketOrderLevel(3, quantity, sellPrice)]);

    private static MarketPrice Price(int itemId, int buyPrice, int sellPrice) => new(
        itemId, false, new MarketOrderSummary(100, buyPrice), new MarketOrderSummary(100, sellPrice));

    private static HistoricalMarketAnalytics History(
        int itemId,
        bool sevenDayAvailable,
        bool thirtyDayAvailable)
    {
        var summary = new HistoricalMarketMetricSummary(
            6_000m,
            [new(1_500, 100m), new(2_000, 100m)],
            100m,
            0d,
            0d,
            0d,
            100m,
            100m,
            0d,
            new HistoricalPriceRange(100, 100),
            new HistoricalPriceRange(200, 200),
            0d);
        return new HistoricalMarketAnalytics(
            itemId,
            Now,
            false,
            HistoricalMarketAnalyticsSettings.Default,
            null,
            [
                HistoryWindow(TimeSpan.FromDays(7), sevenDayAvailable, summary),
                HistoryWindow(TimeSpan.FromDays(30), thirtyDayAvailable, summary),
            ]);
    }

    private static HistoricalMarketWindowAnalytics HistoryWindow(
        TimeSpan duration,
        bool available,
        HistoricalMarketMetricSummary summary) => new(
        available ? HistoricalMarketWindowState.Available : HistoricalMarketWindowState.InsufficientData,
        new HistoricalMarketWindowCoverage(
            Now - duration,
            Now,
            available ? 100 : 0,
            available ? 100 : 0,
            0,
            available ? Now - duration : null,
            available ? Now : null,
            available ? 100m : 0m,
            available ? TimeSpan.FromHours(8) : null),
        available ? summary : null);

    private sealed class FakePortfolioGateway(Gw2ApiResult<AccountPortfolioSnapshot> result) : IAccountPortfolioGateway
    {
        public Task<Gw2ApiResult<AccountPortfolioSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class FakeRepository : IPersonalTradingPostRepository
    {
        public AccountProfile? Profile { get; init; }
        public PersonalTradingPostHistoryCoverage Coverage { get; init; } = new(null, null);
        public IReadOnlyList<StoredCompletedPersonalTradingPostTransaction> Completed { get; init; } = [];
        public CurrentPersonalTradingPostOrderSnapshot? CurrentOrders { get; init; }
        public int ReadCount { get; private set; }
        public int MutationCount { get; private set; }

        public Task<AccountProfile?> FindAccountProfileAsync(string accountScopeId, CancellationToken cancellationToken = default)
        { ReadCount++; return Task.FromResult(Profile); }
        public Task<IReadOnlyList<StoredCompletedPersonalTradingPostTransaction>> GetCompletedTransactionsAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default)
        { ReadCount++; return Task.FromResult(Completed); }
        public Task<PersonalTradingPostHistoryCoverage> GetHistoryCoverageAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default)
        { ReadCount++; return Task.FromResult(Coverage); }
        public Task<CurrentPersonalTradingPostOrderSnapshot?> GetLatestCurrentOrderSnapshotAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default)
        { ReadCount++; return Task.FromResult(CurrentOrders); }

        public Task<AccountProfile> GetOrCreateAccountProfileAsync(string accountScopeId, DateTimeOffset observedAtUtc, CancellationToken cancellationToken = default)
        { MutationCount++; throw new NotSupportedException(); }
        public Task RecordSuccessfulSyncAsync(AccountProfile accountProfile, DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default)
        { MutationCount++; throw new NotSupportedException(); }
        public Task UpsertCompletedTransactionsAsync(AccountProfile accountProfile, IReadOnlyCollection<CompletedPersonalTradingPostTransaction> transactions, DateTimeOffset observedAtUtc, CancellationToken cancellationToken = default)
        { MutationCount++; throw new NotSupportedException(); }
        public Task ReplaceCurrentOrderSnapshotAsync(AccountProfile accountProfile, CurrentPersonalTradingPostOrderSnapshot snapshot, CancellationToken cancellationToken = default)
        { MutationCount++; throw new NotSupportedException(); }
        public Task<IReadOnlyList<CurrentPersonalTradingPostOrder>> GetCurrentOrdersAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CurrentPersonalTradingPostOrder>>(CurrentOrders?.Orders ?? []);
        public Task<IReadOnlyList<CurrentPersonalTradingPostOrderSnapshot>> GetCurrentOrderObservationsAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CurrentPersonalTradingPostOrderSnapshot>>([]);
    }

    private sealed class FakeMetadataRepository : IItemMetadataRepository
    {
        public Task<IReadOnlyList<StoredItemMetadata>> GetManyAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredItemMetadata>>(itemIds.Select(itemId => new StoredItemMetadata(itemId, $"Item {itemId}", Now)).ToArray());
        public Task<StoredItemMetadata?> GetAsync(int itemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpsertAsync(IReadOnlyCollection<StoredItemMetadata> items, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeSettingsRepository : IUserSettingsRepository
    {
        public Task<UserSettings?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult<UserSettings?>(null);
        public Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingGate : IPersonalDataOperationGate
    {
        public int AcquisitionCount { get; private set; }
        public int DisposalCount { get; private set; }
        public ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
        {
            AcquisitionCount++;
            return ValueTask.FromResult<IAsyncDisposable>(new Releaser(() => DisposalCount++));
        }
        private sealed class Releaser(Action release) : IAsyncDisposable
        {
            public ValueTask DisposeAsync() { release(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class EmptyScanner : ILiveMarketScanner
    {
        public Task<LiveMarketScannerResult> ScanAsync(LiveMarketScannerSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LiveMarketScannerResult(
                LiveMarketScannerState.Ready, null, Now, settings, false, 0, false, [], []));
    }

    private sealed class RecordingScanner : ILiveMarketScanner
    {
        public LiveMarketScannerSettings? Settings { get; private set; }

        public Task<LiveMarketScannerResult> ScanAsync(
            LiveMarketScannerSettings settings,
            CancellationToken cancellationToken = default)
        {
            Settings = settings;
            return Task.FromResult(new LiveMarketScannerResult(
                LiveMarketScannerState.Ready, null, Now, settings, false, 0, false, [], []));
        }
    }

    private sealed class StaticScanner(IReadOnlyList<LiveMarketScannerCandidate> candidates) : ILiveMarketScanner
    {
        public Task<LiveMarketScannerResult> ScanAsync(
            LiveMarketScannerSettings settings,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LiveMarketScannerResult(
                LiveMarketScannerState.Ready,
                null,
                Now,
                settings,
                false,
                candidates.Count,
                false,
                candidates,
                []));
    }

    private sealed class ScenarioMarketClient(
        IReadOnlyList<int>? itemIds = null,
        IReadOnlyDictionary<int, MarketPrice>? prices = null,
        IReadOnlyDictionary<int, MarketListing>? listings = null) : IGw2ApiClient
    {
        private readonly IReadOnlyList<int> itemIds = itemIds ?? [];
        private readonly IReadOnlyDictionary<int, MarketPrice> prices = prices ?? new Dictionary<int, MarketPrice>();
        private readonly IReadOnlyDictionary<int, MarketListing> listings = listings ?? new Dictionary<int, MarketListing>();

        public int MetadataRequestCount { get; private set; }

        public Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Success<IReadOnlyList<int>>(itemIds));

        public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
            IReadOnlyCollection<int> requestedItemIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Success<IReadOnlyList<MarketPrice>>(
                requestedItemIds.Where(prices.ContainsKey).Select(itemId => prices[itemId]).ToArray()));

        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
            IReadOnlyCollection<int> requestedItemIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Success<IReadOnlyList<MarketListing>>(
                requestedItemIds.Where(listings.ContainsKey).Select(itemId => listings[itemId]).ToArray()));

        public Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(
            IReadOnlyCollection<int> requestedItemIds,
            CancellationToken cancellationToken = default)
        {
            MetadataRequestCount++;
            return Task.FromResult(Success<IReadOnlyList<MarketItemMetadata>>(
                requestedItemIds.Select(itemId => new MarketItemMetadata(
                    itemId,
                    $"Item {itemId}",
                    MarketItemStackPolicy.NormalStackLimit,
                    $"https://render.guildwars2.com/file/synthetic-{itemId}.png")).ToArray()));
        }

        private static Gw2ApiResult<T> Success<T>(T value) => Gw2ApiResult<T>.Success(value);
    }

    private sealed class FakeMarketClient(MarketListing? listing) : IGw2ApiClient
    {
        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<IReadOnlyList<MarketListing>>.Success(
                listing is null || !itemIds.Contains(listing.ItemId) ? [] : [listing]));
        public Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>.Success(
                itemIds.Select(itemId => new MarketItemMetadata(
                    itemId,
                    $"Item {itemId}",
                    MarketItemStackPolicy.NormalStackLimit,
                    $"https://render.guildwars2.com/file/synthetic-{itemId}.png")).ToArray()));
    }

    private sealed class FakeHistoryService(IReadOnlyDictionary<int, HistoricalMarketAnalytics>? histories = null) : IHistoricalMarketAnalyticsService
    {
        public Task<HistoricalMarketAnalytics> GetAsync(int itemId, CancellationToken cancellationToken = default) =>
            GetAtAsync(itemId, Now, cancellationToken);
        public Task<HistoricalMarketAnalytics> GetAtAsync(int itemId, DateTimeOffset asOfUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(histories?.GetValueOrDefault(itemId) ?? new HistoricalMarketAnalytics(
                itemId, asOfUtc, false, HistoricalMarketAnalyticsSettings.Default, null, []));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class SequenceClock(params DateTimeOffset[] values) : IClock
    {
        private int currentIndex;

        public DateTimeOffset UtcNow => values[Math.Min(currentIndex++, values.Length - 1)];
    }
}
