using System.Globalization;
using System.Numerics;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Application.Accounting;
using Gw2Tp.Application.Finance;
using Gw2Tp.Application.LocalData;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.MarketScanning;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Application.Time;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Recommendations;

/// <summary>
/// Composes a read-only decision snapshot from authenticated cash, coherent local
/// personal evidence, current books, retained history, scoring, and position sizing.
/// </summary>
public sealed class PrimaryRecommendationService : IPrimaryRecommendationService
{
    public const string Strategy = "FastFlip";
    public const string Category = "TradingPost";

    private readonly IAccountPortfolioGateway accountPortfolioGateway;
    private readonly IPersonalTradingPostRepository repository;
    private readonly IItemMetadataRepository itemMetadataRepository;
    private readonly IUserSettingsRepository settingsRepository;
    private readonly IPersonalDataOperationGate operationGate;
    private readonly ILiveMarketScanner scanner;
    private readonly IGw2ApiClient marketDataClient;
    private readonly IHistoricalMarketAnalyticsService historyService;
    private readonly IClock clock;
    private readonly IOpportunityScoreService scoreService;
    private readonly IPrimaryRecommendationPolicy actionPolicy;
    private readonly FifoLotMatcher fifoLotMatcher = new();
    private readonly PrimaryRecommendationEconomicsCalculator economicsCalculator = new();
    private readonly OrderBookExecutionSimulator executionSimulator = new();

    public PrimaryRecommendationService(
        IAccountPortfolioGateway accountPortfolioGateway,
        IPersonalTradingPostRepository repository,
        IItemMetadataRepository itemMetadataRepository,
        IUserSettingsRepository settingsRepository,
        IPersonalDataOperationGate operationGate,
        ILiveMarketScanner scanner,
        IGw2ApiClient marketDataClient,
        IHistoricalMarketAnalyticsService historyService,
        IClock clock,
        IOpportunityScoreService scoreService,
        IPrimaryRecommendationPolicy actionPolicy)
    {
        this.accountPortfolioGateway = accountPortfolioGateway ?? throw new ArgumentNullException(nameof(accountPortfolioGateway));
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.itemMetadataRepository = itemMetadataRepository ?? throw new ArgumentNullException(nameof(itemMetadataRepository));
        this.settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        this.operationGate = operationGate ?? throw new ArgumentNullException(nameof(operationGate));
        this.scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        this.marketDataClient = marketDataClient ?? throw new ArgumentNullException(nameof(marketDataClient));
        this.historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.scoreService = scoreService ?? throw new ArgumentNullException(nameof(scoreService));
        this.actionPolicy = actionPolicy ?? throw new ArgumentNullException(nameof(actionPolicy));
    }

    public async Task<PrimaryRecommendationResult> GetAsync(CancellationToken cancellationToken = default)
    {
        var portfolioResult = await accountPortfolioGateway.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var defaultPolicies = BuildPolicies(null, PositionSizingPolicy.Default);
        if (!portfolioResult.IsSuccess || portfolioResult.IsPartialData || portfolioResult.Value is null)
        {
            return PrimaryRecommendationResult.Unavailable(
                PrimaryRecommendationState.AccountUnavailable,
                ErrorName(portfolioResult.ErrorCategory),
                defaultPolicies);
        }

        LocalSnapshot local;
        await using (await operationGate.AcquireAsync(cancellationToken).ConfigureAwait(false))
        {
            var profile = await repository.FindAccountProfileAsync(
                portfolioResult.Value.AccountScope.AccountId, cancellationToken).ConfigureAwait(false);
            var settings = await settingsRepository.GetAsync(cancellationToken).ConfigureAwait(false);
            var sizingPolicy = WithReserve(settings?.CashReserveBasisPoints);
            var policies = BuildPolicies(settings, sizingPolicy);
            if (profile is null || profile.LastSuccessfulSyncAtUtc is null)
            {
                return PrimaryRecommendationResult.Unavailable(PrimaryRecommendationState.NotSynchronized, null, policies);
            }

            var coverage = await repository.GetHistoryCoverageAsync(profile, cancellationToken).ConfigureAwait(false);
            var storedTransactions = await repository.GetCompletedTransactionsAsync(profile, cancellationToken).ConfigureAwait(false);
            var currentOrders = await repository.GetLatestCurrentOrderSnapshotAsync(profile, cancellationToken).ConfigureAwait(false);
            if (currentOrders is null)
            {
                return PrimaryRecommendationResult.Unavailable(PrimaryRecommendationState.NotSynchronized, null, policies);
            }

            var coveredTransactions = coverage.StartUtc is { } start && coverage.EndUtc is { } end
                ? storedTransactions.Where(value => value.Transaction.CompletedAtUtc >= start && value.Transaction.CompletedAtUtc <= end)
                    .Select(value => new AccountScopedCompletedTransaction(profile.Id, value.Transaction)).ToArray()
                : [];
            var fifo = fifoLotMatcher.Rebuild(coveredTransactions);
            var itemIds = fifo.OpenLots.Select(lot => lot.ItemId)
                .Concat(currentOrders.Orders.Select(order => order.ItemId))
                .Distinct().OrderBy(value => value).ToArray();
            var metadata = await itemMetadataRepository.GetManyAsync(itemIds, cancellationToken).ConfigureAwait(false);
            local = new LocalSnapshot(
                profile, currentOrders, fifo,
                metadata.ToDictionary(value => value.ItemId, value => value.Name),
                settings, sizingPolicy, policies);
        }

        var sellQuantityByItem = local.CurrentOrders.Orders
            .Where(order => order.Side == PersonalTradingPostSide.Sell)
            .GroupBy(order => order.ItemId)
            .ToDictionary(group => group.Key, group => checked((int)group.Sum(order => (long)order.Quantity)));
        var knownQuantityByItem = local.Fifo.OpenLots.GroupBy(lot => lot.ItemId)
            .ToDictionary(group => group.Key, group => checked((int)group.Sum(lot => (long)lot.RemainingQuantity)));
        var hasUnknownSellBasis = sellQuantityByItem.Any(pair => pair.Value > knownQuantityByItem.GetValueOrDefault(pair.Key));

        PortfolioSizingSnapshot sizingSnapshot;
        try
        {
            var exposures = BuildExposures(local);
            sizingSnapshot = hasUnknownSellBasis
                ? new PortfolioSizingSnapshot(PortfolioSizingSnapshotState.Unknown, null, null)
                : new PortfolioSizingSnapshot(PortfolioSizingSnapshotState.Available, portfolioResult.Value.AvailableCash, exposures);
        }
        catch (OverflowException)
        {
            sizingSnapshot = new PortfolioSizingSnapshot(PortfolioSizingSnapshotState.Unknown, null, null);
        }

        var sizingService = new PositionSizingService(local.SizingPolicy);
        var discoveryCapitalLimit = sizingService.CalculateDiscoveryCapitalLimit(sizingSnapshot, Strategy, Category);
        var scannerSettings = new LiveMarketScannerSettings(
            local.Settings?.MinimumRoiBasisPoints ?? LiveMarketScannerSettings.Default.MinimumRoiBasisPoints,
            new Money(local.Settings?.MinimumProfitInCopper ?? checked((int)LiveMarketScannerSettings.Default.MinimumNetProfit.Copper)),
            LiveMarketScannerSettings.Default.BidIncrementCopper,
            LiveMarketScannerSettings.Default.ListUndercutCopper,
            LiveMarketScannerSettings.Default.IntendedQuantity,
            discoveryCapitalLimit);
        var scan = await scanner.ScanAsync(scannerSettings, cancellationToken).ConfigureAwait(false);
        if (scan.State != LiveMarketScannerState.Ready)
        {
            return PrimaryRecommendationResult.Unavailable(
                PrimaryRecommendationState.EvidenceUnavailable, ErrorName(scan.ErrorCategory), local.Policies);
        }

        var asOfUtc = RequireUtc(clock.UtcNow);
        var allItemIds = scan.Candidates.Select(candidate => candidate.Item.ItemId)
            .Concat(local.CurrentOrders.Orders.Select(order => order.ItemId))
            .Concat(local.Fifo.OpenLots.Select(lot => lot.ItemId))
            .Distinct().OrderBy(value => value).ToArray();

        IReadOnlyDictionary<int, HistoricalMarketAnalytics> historyByItem;
        IReadOnlyDictionary<int, MarketListing> listingsByItem;
        try
        {
            var historyTasks = allItemIds.ToDictionary(
                itemId => itemId,
                itemId => historyService.GetAtAsync(itemId, asOfUtc, cancellationToken));
            var listingsTask = allItemIds.Length == 0
                ? Task.FromResult(Gw2ApiResult<IReadOnlyList<MarketListing>>.Success([]))
                : marketDataClient.GetListingsAsync(allItemIds, cancellationToken);
            await Task.WhenAll(historyTasks.Values).ConfigureAwait(false);
            var listingsResult = await listingsTask.ConfigureAwait(false);
            historyByItem = historyTasks.ToDictionary(pair => pair.Key, pair => pair.Value.Result);
            if (!listingsResult.IsSuccess || listingsResult.IsPartialData || listingsResult.Value is null)
            {
                return PrimaryRecommendationResult.Unavailable(
                    PrimaryRecommendationState.EvidenceUnavailable, ErrorName(listingsResult.ErrorCategory), local.Policies);
            }
            listingsByItem = listingsResult.Value.Where(IsValidListing)
                .GroupBy(value => value.ItemId).Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return PrimaryRecommendationResult.Unavailable(
                PrimaryRecommendationState.EvidenceUnavailable, "history_or_market_read_failed", local.Policies);
        }

        IReadOnlyList<OpportunityScore> scores;
        try
        {
            scores = scoreService.Calculate(scan.Candidates.Select(candidate =>
                new OpportunityScoreCandidate(candidate, historyByItem[candidate.Item.ItemId])).ToArray());
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            return PrimaryRecommendationResult.Unavailable(
                PrimaryRecommendationState.EvidenceUnavailable, "inconsistent_scoring_evidence", local.Policies);
        }

        var scoresByItem = scores.ToDictionary(value => value.ItemId);
        var candidatesByItem = scan.Candidates.ToDictionary(value => value.Item.ItemId);
        var sizingCandidates = scan.Candidates.Select(candidate => new PositionSizingCandidate(
            scoresByItem[candidate.Item.ItemId], candidate,
            ClassifyLiquidity(candidate.Liquidity.Reasons), Strategy, Category)).ToArray();
        var sizing = sizingService.Size(sizingSnapshot, sizingCandidates);
        var allocationsByItem = sizing.Allocations.ToDictionary(value => value.ItemId);
        var reserveCancellations = SelectReserveCancellations(
            local, listingsByItem, candidatesByItem, scoresByItem, sizing);

        var evidence = new List<PrimaryRecommendationEvidence>();
        evidence.AddRange(BuildNewOpportunityEvidence(
            scan.Candidates, scoresByItem, historyByItem, allocationsByItem,
            sizing.State == PositionSizingResultState.Sized));
        evidence.AddRange(BuildOrderEvidence(
            local, historyByItem, listingsByItem, candidatesByItem,
            scoresByItem, allocationsByItem, reserveCancellations));
        evidence.AddRange(BuildInventoryEvidence(
            local, historyByItem, listingsByItem, candidatesByItem,
            scoresByItem, sellQuantityByItem, sizing));

        var actions = actionPolicy.Evaluate(evidence);
        var portfolio = sizing.State == PositionSizingResultState.Sized &&
            sizing.TotalBankroll is { } bankroll && sizing.CashReserve is { } reserve &&
            sizing.CashReserveStatus is { } reserveStatus && sizing.CashReserveShortfall is { } shortfall &&
            sizing.RemainingCashAfterSizing is { } remaining
            ? new PrimaryRecommendationPortfolio(
                portfolioResult.Value.AvailableCash, bankroll, reserve, reserveStatus, shortfall, remaining)
            : null;

        return new PrimaryRecommendationResult(
            PrimaryRecommendationState.Ready,
            sizing.State == PositionSizingResultState.Unavailable ? "buy_sizing_unavailable" : null,
            asOfUtc, local.Profile.LastSuccessfulSyncAtUtc, local.CurrentOrders.ObservedAtUtc,
            scan.ObservedAtUtc, local.Policies, portfolio, actions);
    }

    private IReadOnlyList<PrimaryRecommendationEvidence> BuildNewOpportunityEvidence(
        IReadOnlyList<LiveMarketScannerCandidate> candidates,
        IReadOnlyDictionary<int, OpportunityScore> scores,
        IReadOnlyDictionary<int, HistoricalMarketAnalytics> history,
        IReadOnlyDictionary<int, PositionSizingAllocation> allocations,
        bool sizingAvailable) => candidates.Select(candidate =>
    {
        var score = scores[candidate.Item.ItemId];
        var allocation = allocations[candidate.Item.ItemId];
        var quantity = allocation.SuggestedQuantity;
        return new PrimaryRecommendationEvidence(
            PrimaryRecommendationSource.NewOpportunity, PrimaryRecommendationOrderState.NotApplicable,
            null, candidate.Item.ItemId, candidate.Item.Name, 0, quantity,
            ExactCapital(candidate.PlannedBid, candidate.PlannedListPrice, quantity),
            Prices(candidate, null), quantity > 0 ? Economics(candidate.PlannedBid, candidate.PlannedListPrice, quantity) : null,
            Score(score), History(history[candidate.Item.ItemId]),
            Liquidity(candidate.Liquidity, ClassifyLiquidity(candidate.Liquidity.Reasons), candidate.Liquidity.ParticipationCapQuantity),
            allocation.Constraints, true, sizingAvailable, false, false, false, false, false, false,
            Money.Zero, Money.Zero);
    }).ToArray();

    private IReadOnlyList<PrimaryRecommendationEvidence> BuildOrderEvidence(
        LocalSnapshot local,
        IReadOnlyDictionary<int, HistoricalMarketAnalytics> history,
        IReadOnlyDictionary<int, MarketListing> listings,
        IReadOnlyDictionary<int, LiveMarketScannerCandidate> candidates,
        IReadOnlyDictionary<int, OpportunityScore> scores,
        IReadOnlyDictionary<int, PositionSizingAllocation> allocations,
        IReadOnlySet<long> reserveCancellations)
    {
        var result = new List<PrimaryRecommendationEvidence>();
        var sellBasisByOrder = BuildSellListingBasis(local);
        foreach (var order in local.CurrentOrders.Orders.OrderBy(value => value.ExternalOrderId))
        {
            candidates.TryGetValue(order.ItemId, out var candidate);
            scores.TryGetValue(order.ItemId, out var score);
            history.TryGetValue(order.ItemId, out var itemHistory);
            listings.TryGetValue(order.ItemId, out var listing);
            allocations.TryGetValue(order.ItemId, out var allocation);
            var model = candidate is null && listing is not null ? TryBuildMarketModel(listing, local.Settings) : null;
            var bestBuy = candidate is not null ? candidate.BestBuy.UnitPriceInCopper : BestBuy(listing);
            var lowestSell = candidate is not null ? candidate.LowestSell.UnitPriceInCopper : LowestSell(listing);
            var plannedBid = candidate?.PlannedBid ?? model?.PlannedBid;
            var plannedList = candidate?.PlannedListPrice ?? model?.PlannedList;
            var maximumBid = candidate?.MaximumBid ?? model?.MaximumBid;
            var liquidity = candidate?.Liquidity ?? (listing is not null ? BuildLiquidity(listing, Math.Max(1, order.Quantity)) : null);
            var classification = liquidity is null ? (PositionSizingLiquidity?)null : ClassifyLiquidity(liquidity.Reasons);
            var capital = Multiply(order.UnitPriceInCopper, order.Quantity);
            var incremental = order.Side == PersonalTradingPostSide.Buy && plannedBid is { } replacement
                ? new Money(Math.Max(0L, checked((replacement.Copper - order.UnitPriceInCopper) * order.Quantity)))
                : Money.Zero;
            var capacity = allocation?.SuggestedCapital ?? Money.Zero;
            var orderState = order.Side == PersonalTradingPostSide.Buy
                ? BuyOrderState(order.UnitPriceInCopper, bestBuy, maximumBid)
                : SellOrderState(order.UnitPriceInCopper, lowestSell);
            var hasKnownSellBasis = order.Side != PersonalTradingPostSide.Sell || sellBasisByOrder.ContainsKey(order.ExternalOrderId);
            var complete = order.Side == PersonalTradingPostSide.Buy
                ? itemHistory is not null && liquidity is not null && maximumBid is not null
                : listing is not null && lowestSell > 0 && hasKnownSellBasis;
            var quantity = order.Quantity;
            var economics = order.Side == PersonalTradingPostSide.Buy && plannedList is { } list
                ? Economics(
                    orderState == PrimaryRecommendationOrderState.Outbid && plannedBid is { } replacementBid
                        ? replacementBid : new Money(order.UnitPriceInCopper),
                    list, quantity)
                : order.Side == PersonalTradingPostSide.Sell && sellBasisByOrder.TryGetValue(order.ExternalOrderId, out var basis)
                    ? EconomicsFromBasis(basis, new Money(order.UnitPriceInCopper), quantity)
                    : null;
            var displayedCapital = order.Side == PersonalTradingPostSide.Sell && sellBasisByOrder.TryGetValue(order.ExternalOrderId, out basis)
                ? basis : capital;
            result.Add(new PrimaryRecommendationEvidence(
                order.Side == PersonalTradingPostSide.Buy ? PrimaryRecommendationSource.BuyOrder : PrimaryRecommendationSource.SellListing,
                orderState, order.ExternalOrderId.ToString(CultureInfo.InvariantCulture), order.ItemId,
                NameFor(order.ItemId, local.Metadata, candidate?.Item.Name), order.Quantity, quantity, displayedCapital,
                new PrimaryRecommendationPriceState(
                    new Money(order.UnitPriceInCopper), bestBuy > 0 ? new Money(bestBuy) : null,
                    lowestSell > 0 ? new Money(lowestSell) : null, plannedBid, plannedList, maximumBid),
                economics, score is null ? null : Score(score), itemHistory is null ? null : History(itemHistory),
                liquidity is null || classification is null ? null : Liquidity(liquidity, classification.Value, liquidity.ParticipationCapQuantity),
                allocation?.Constraints ?? [], complete, allocation?.State == PositionSizingAllocationState.Suggested,
                reserveCancellations.Contains(order.ExternalOrderId), !hasKnownSellBasis,
                false, false, false, false, incremental, capacity));
        }
        return result;
    }

    private IReadOnlyList<PrimaryRecommendationEvidence> BuildInventoryEvidence(
        LocalSnapshot local,
        IReadOnlyDictionary<int, HistoricalMarketAnalytics> history,
        IReadOnlyDictionary<int, MarketListing> listings,
        IReadOnlyDictionary<int, LiveMarketScannerCandidate> candidates,
        IReadOnlyDictionary<int, OpportunityScore> scores,
        IReadOnlyDictionary<int, int> listedQuantity,
        PositionSizingResult sizing)
    {
        var result = new List<PrimaryRecommendationEvidence>();
        foreach (var group in local.Fifo.OpenLots.GroupBy(lot => lot.ItemId).OrderBy(group => group.Key))
        {
            var itemId = group.Key;
            var allLots = group.OrderBy(lot => lot.CompletedAtUtc).ThenBy(lot => lot.BuyTransactionId).ToArray();
            var totalQuantity = checked((int)allLots.Sum(lot => (long)lot.RemainingQuantity));
            var listed = listedQuantity.GetValueOrDefault(itemId);
            if (listed >= totalQuantity) continue;
            var unlistedLots = RemoveQuantity(allLots, listed);
            var unlistedQuantity = checked((int)unlistedLots.Sum(lot => (long)lot.Quantity));
            history.TryGetValue(itemId, out var itemHistory);
            listings.TryGetValue(itemId, out var listing);
            candidates.TryGetValue(itemId, out var candidate);
            scores.TryGetValue(itemId, out var score);
            var liquidity = listing is null ? null : BuildLiquidity(listing, Math.Max(1, unlistedQuantity));
            var classification = liquidity is null ? PositionSizingLiquidity.Low : ClassifyLiquidity(liquidity.Reasons);
            var safeQuantity = liquidity?.ParticipationCapQuantity ?? 0;
            var totalBankroll = sizing.TotalBankroll?.Copper ?? 0L;
            var itemCap = PercentageRoundDown(totalBankroll, ItemCapBasisPoints(local.SizingPolicy, classification));
            var itemExposure = ItemExposure(local, itemId);
            var exposureExceeded = sizing.State == PositionSizingResultState.Sized && itemExposure > itemCap;
            var requiredReduction = exposureExceeded ? QuantityToRelease(unlistedLots, itemExposure - itemCap) : 0;
            var fullImmediate = listing is not null && safeQuantity >= unlistedQuantity
                ? TryLiquidationEconomics(listing, unlistedLots, unlistedQuantity) : null;
            var partialQuantity = Math.Min(unlistedQuantity, safeQuantity);
            var partialImmediate = listing is not null && partialQuantity > 0
                ? TryLiquidationEconomics(listing, unlistedLots, partialQuantity) : null;
            var plannedList = LowestSell(listing) > 1 ? new Money(LowestSell(listing) - 1L) : (Money?)null;
            var listingEconomics = plannedList is { } listPrice
                ? EconomicsFromBasis(BasisFor(unlistedLots, unlistedQuantity), listPrice, unlistedQuantity) : null;
            var suggestedQuantity = exposureExceeded
                ? Math.Min(requiredReduction, safeQuantity)
                : fullImmediate?.NetProfit.Copper > 0 ? unlistedQuantity
                : partialImmediate?.NetProfit.Copper > 0 ? partialQuantity : unlistedQuantity;
            var selectedEconomics = exposureExceeded && suggestedQuantity > 0 && listing is not null
                ? TryLiquidationEconomics(listing, unlistedLots, suggestedQuantity)
                : fullImmediate?.NetProfit.Copper > 0 ? fullImmediate
                : partialImmediate?.NetProfit.Copper > 0 ? partialImmediate : listingEconomics;
            result.Add(new PrimaryRecommendationEvidence(
                PrimaryRecommendationSource.Inventory, PrimaryRecommendationOrderState.NotApplicable,
                null, itemId, NameFor(itemId, local.Metadata, candidate?.Item.Name),
                unlistedQuantity, suggestedQuantity, BasisFor(unlistedLots, suggestedQuantity),
                new PrimaryRecommendationPriceState(
                    null, BestBuy(listing) > 0 ? new Money(BestBuy(listing)) : null,
                    LowestSell(listing) > 0 ? new Money(LowestSell(listing)) : null,
                    candidate?.PlannedBid, plannedList, candidate?.MaximumBid),
                selectedEconomics, score is null ? null : Score(score), itemHistory is null ? null : History(itemHistory),
                liquidity is null ? null : Liquidity(liquidity, classification, safeQuantity), [],
                listing is not null && itemHistory is not null && BestBuy(listing) > 0 && LowestSell(listing) > 1,
                sizing.State == PositionSizingResultState.Sized, false, false, exposureExceeded,
                fullImmediate?.NetProfit.Copper > 0,
                partialQuantity < unlistedQuantity && partialImmediate?.NetProfit.Copper > 0,
                listingEconomics?.NetProfit.Copper > 0, Money.Zero, Money.Zero));
        }
        return result;
    }

    private PrimaryRecommendationEconomics? TryLiquidationEconomics(MarketListing listing, IReadOnlyList<LotSlice> lots, int quantity)
    {
        if (quantity <= 0) return null;
        var scenario = executionSimulator.SimulateLiquidation(
            listing.Buys.OrderByDescending(level => level.UnitPriceInCopper)
                .Select(level => new OrderBookLevel(level.Quantity, new Money(level.UnitPriceInCopper))).ToArray(), quantity);
        return scenario.IsFullyFilled ? EconomicsFromTotals(BasisFor(lots, quantity), scenario.TotalValue) : null;
    }

    private PrimaryRecommendationEconomics Economics(Money unitBuy, Money unitSell, int quantity) =>
        economicsCalculator.CalculateUnitPrices(unitBuy, unitSell, quantity);
    private PrimaryRecommendationEconomics EconomicsFromBasis(Money basis, Money unitSell, int quantity) =>
        economicsCalculator.CalculateTotals(basis, Multiply(unitSell, quantity));
    private PrimaryRecommendationEconomics EconomicsFromTotals(Money acquisition, Money grossSale) =>
        economicsCalculator.CalculateTotals(acquisition, grossSale);
    private Money ExactCapital(Money unitBuy, Money unitSell, int quantity) =>
        quantity <= 0 ? Money.Zero : Economics(unitBuy, unitSell, quantity).TotalCost;

    private MarketModel? TryBuildMarketModel(MarketListing listing, UserSettings? settings)
    {
        var bestBuy = BestBuy(listing);
        var lowestSell = LowestSell(listing);
        if (bestBuy <= 0 || lowestSell <= 1) return null;
        try
        {
            var plannedBid = new Money(checked((long)bestBuy + LiveMarketScannerSettings.Default.BidIncrementCopper));
            var plannedList = new Money(checked((long)lowestSell - LiveMarketScannerSettings.Default.ListUndercutCopper));
            var minimumProfit = settings?.MinimumProfitInCopper ?? checked((int)LiveMarketScannerSettings.Default.MinimumNetProfit.Copper);
            var minimumRoi = settings?.MinimumRoiBasisPoints ?? LiveMarketScannerSettings.Default.MinimumRoiBasisPoints;
            long low = 0;
            long high = plannedList.Copper;
            while (low < high)
            {
                var bid = low + ((high - low + 1) / 2);
                var economics = Economics(new Money(bid), plannedList, 1);
                if (economics.NetProfit.Copper > 0 && economics.NetProfit.Copper >= minimumProfit &&
                    MeetsRoi(economics.NetProfit, economics.TotalCost, minimumRoi)) low = bid;
                else high = bid - 1;
            }
            return low > 0 ? new MarketModel(plannedBid, plannedList, new Money(low)) : null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private LiveMarketScannerLiquidityEvidence BuildLiquidity(MarketListing listing, int intendedQuantity)
    {
        var buys = listing.Buys.OrderByDescending(level => level.UnitPriceInCopper).ToArray();
        var sells = listing.Sells.OrderBy(level => level.UnitPriceInCopper).ToArray();
        var totalBuys = buys.Sum(level => (long)level.Quantity);
        var totalSells = sells.Sum(level => (long)level.Quantity);
        var bestBuy = buys.FirstOrDefault()?.UnitPriceInCopper;
        var bestSell = sells.FirstOrDefault()?.UnitPriceInCopper;
        var nearBuys = bestBuy is null ? [] : buys.Where(level => level.UnitPriceInCopper == bestBuy).ToArray();
        var nearSells = bestSell is null ? [] : sells.Where(level => level.UnitPriceInCopper == bestSell).ToArray();
        var reasons = new List<LiveMarketScannerLiquidityReason>();
        if (buys.Sum(level => (long)level.Listings) < 3) reasons.Add(LiveMarketScannerLiquidityReason.InsufficientBuyListingDepth);
        if (sells.Sum(level => (long)level.Listings) < 3) reasons.Add(LiveMarketScannerLiquidityReason.InsufficientSellListingDepth);
        if (totalBuys < 10) reasons.Add(LiveMarketScannerLiquidityReason.InsufficientBuyQuantityDepth);
        if (totalSells < 10) reasons.Add(LiveMarketScannerLiquidityReason.InsufficientSellQuantityDepth);
        var buyGap = Gap(buys, bestBuy, true);
        var sellGap = Gap(sells, bestSell, false);
        var buyCliff = IsCliff(buyGap, bestBuy);
        var sellCliff = IsCliff(sellGap, bestSell);
        if (buyCliff) reasons.Add(LiveMarketScannerLiquidityReason.BuyPriceCliff);
        if (sellCliff) reasons.Add(LiveMarketScannerLiquidityReason.SellPriceCliff);
        var acquisition = executionSimulator.SimulateAcquisition(
            sells.Select(level => new OrderBookLevel(level.Quantity, new Money(level.UnitPriceInCopper))).ToArray(), intendedQuantity);
        var liquidation = executionSimulator.SimulateLiquidation(
            buys.Select(level => new OrderBookLevel(level.Quantity, new Money(level.UnitPriceInCopper))).ToArray(), intendedQuantity);
        if (!acquisition.IsFullyFilled) reasons.Add(LiveMarketScannerLiquidityReason.IntendedQuantityCannotFullyAcquire);
        if (!liquidation.IsFullyFilled) reasons.Add(LiveMarketScannerLiquidityReason.IntendedQuantityCannotFullyLiquidate);
        var cap = (int)Math.Min(int.MaxValue, Math.Min(totalBuys, totalSells) / 10);
        if (cap < intendedQuantity) reasons.Add(LiveMarketScannerLiquidityReason.ParticipationCapBelowIntendedQuantity);
        return new LiveMarketScannerLiquidityEvidence(
            totalBuys, totalSells,
            nearBuys.Sum(level => (long)level.Quantity), nearSells.Sum(level => (long)level.Quantity),
            nearBuys.Sum(level => (long)level.Listings), nearSells.Sum(level => (long)level.Listings),
            buyGap, sellGap, buyCliff, sellCliff, acquisition, liquidation, cap,
            reasons.OrderBy(value => value).ToArray(),
            buys.Take(LiveMarketScanner.MaximumOrderBookDetailLevels).ToArray(),
            sells.Take(LiveMarketScanner.MaximumOrderBookDetailLevels).ToArray());
    }

    private static IReadOnlyList<PortfolioExposure> BuildExposures(LocalSnapshot local) =>
        local.CurrentOrders.Orders.Where(order => order.Side == PersonalTradingPostSide.Buy)
            .Select(order => new PortfolioExposure(
                $"buy-{order.ExternalOrderId.ToString(CultureInfo.InvariantCulture)}",
                PortfolioExposureKind.CurrentBuyOrder, order.ItemId, Strategy, Category,
                Multiply(order.UnitPriceInCopper, order.Quantity)))
            .Concat(local.Fifo.OpenLots.Select(lot => new PortfolioExposure(
                $"lot-{lot.BuyTransactionId.ToString(CultureInfo.InvariantCulture)}",
                PortfolioExposureKind.HeldPosition, lot.ItemId, Strategy, Category, lot.RemainingAcquisitionBasis)))
            .ToArray();

    private IReadOnlySet<long> SelectReserveCancellations(
        LocalSnapshot local,
        IReadOnlyDictionary<int, MarketListing> listings,
        IReadOnlyDictionary<int, LiveMarketScannerCandidate> candidates,
        IReadOnlyDictionary<int, OpportunityScore> scores,
        PositionSizingResult sizing)
    {
        if (sizing.State != PositionSizingResultState.Sized || sizing.CashReserveShortfall is not { Copper: > 0 } shortfall)
            return new HashSet<long>();
        return ReserveRestorationSelector.Select(
            local.CurrentOrders.Orders.Where(order => order.Side == PersonalTradingPostSide.Buy).Select(order =>
            {
                candidates.TryGetValue(order.ItemId, out var candidate);
                listings.TryGetValue(order.ItemId, out var listing);
                var maximumBid = candidate?.MaximumBid ?? (listing is null ? null : TryBuildMarketModel(listing, local.Settings)?.MaximumBid);
                return new ReserveRestorationBid(
                    order.ExternalOrderId, order.ItemId, Multiply(order.UnitPriceInCopper, order.Quantity),
                    maximumBid is { } maximum && order.UnitPriceInCopper > maximum.Copper,
                    scores.TryGetValue(order.ItemId, out var score) ? score.TotalPoints : null);
            }).ToArray(), shortfall);
    }

    private static PrimaryRecommendationPolicies BuildPolicies(UserSettings? settings, PositionSizingPolicy sizing) => new(
        PrimaryRecommendationPolicy.Version, OpportunityScorePolicy.CurrentVersion, sizing.Version,
        FifoAccountingPolicy.Version, Gw2TradingPostFeePolicy.PolicyVersion,
        settings?.MinimumProfitInCopper ?? checked((int)LiveMarketScannerSettings.Default.MinimumNetProfit.Copper),
        settings?.MinimumRoiBasisPoints ?? LiveMarketScannerSettings.Default.MinimumRoiBasisPoints,
        sizing.CashReserveBasisPoints, Strategy, Category);

    private static PositionSizingPolicy WithReserve(int? cashReserveBasisPoints)
    {
        var policy = PositionSizingPolicy.Default with
        {
            CashReserveBasisPoints = cashReserveBasisPoints ?? PositionSizingPolicy.Default.CashReserveBasisPoints,
        };
        policy.Validate();
        return policy;
    }

    private static PrimaryRecommendationPriceState Prices(LiveMarketScannerCandidate candidate, Money? currentOrder) => new(
        currentOrder, new Money(candidate.BestBuy.UnitPriceInCopper), new Money(candidate.LowestSell.UnitPriceInCopper),
        candidate.PlannedBid, candidate.PlannedListPrice, candidate.MaximumBid);
    private static PrimaryRecommendationScore Score(OpportunityScore score) => new(
        score.Rank, score.TotalPoints, score.BasePoints, score.AppliedPenaltyPoints, score.Components, score.Anomalies);
    private static PrimaryRecommendationHistory History(HistoricalMarketAnalytics history) => new(
        Confidence(history), history.AsOfUtc,
        history.Windows.Select(window => new PrimaryRecommendationHistoryWindow(
            (int)(window.Coverage.ToInclusiveUtc - window.Coverage.FromInclusiveUtc).TotalDays,
            window.State == HistoricalMarketWindowState.Available,
            window.Coverage.RawObservationCount, window.Coverage.EligibleObservationCount,
            window.Coverage.ObservedSpanPercent)).ToArray());
    private static OpportunityHistoricalConfidence Confidence(HistoricalMarketAnalytics history) =>
        history.Windows.Count(window => window.State == HistoricalMarketWindowState.Available) switch
        {
            0 => OpportunityHistoricalConfidence.Insufficient,
            1 => OpportunityHistoricalConfidence.Partial,
            _ => OpportunityHistoricalConfidence.Strong,
        };
    private static PrimaryRecommendationLiquidity Liquidity(
        LiveMarketScannerLiquidityEvidence liquidity, PositionSizingLiquidity classification, int safeQuantity) => new(
        classification, liquidity.TotalBuyQuantity, liquidity.TotalSellQuantity,
        liquidity.NearBestBuyQuantity, liquidity.NearBestSellQuantity,
        liquidity.ParticipationCapQuantity, safeQuantity, liquidity.Reasons);

    internal static PositionSizingLiquidity ClassifyLiquidity(IReadOnlyCollection<LiveMarketScannerLiquidityReason> reasons)
    {
        if (reasons.Count == 0) return PositionSizingLiquidity.High;
        return reasons.All(reason => reason is LiveMarketScannerLiquidityReason.BuyPriceCliff or LiveMarketScannerLiquidityReason.SellPriceCliff)
            ? PositionSizingLiquidity.Medium : PositionSizingLiquidity.Low;
    }

    private static PrimaryRecommendationOrderState BuyOrderState(int price, int bestBuy, Money? maximumBid)
    {
        if (maximumBid is { } maximum && price > maximum.Copper) return PrimaryRecommendationOrderState.AboveMaximumBid;
        if (bestBuy <= 0) return PrimaryRecommendationOrderState.MarketUnavailable;
        return price >= bestBuy ? PrimaryRecommendationOrderState.Competitive : PrimaryRecommendationOrderState.Outbid;
    }
    private static PrimaryRecommendationOrderState SellOrderState(int price, int lowestSell)
    {
        if (lowestSell <= 0) return PrimaryRecommendationOrderState.MarketUnavailable;
        var difference = checked((long)price - lowestSell);
        if (difference <= 0) return PrimaryRecommendationOrderState.Competitive;
        return difference == 1 ? PrimaryRecommendationOrderState.UndercutOneCopper : PrimaryRecommendationOrderState.UndercutMoreThanOneCopper;
    }

    private static IReadOnlyList<LotSlice> RemoveQuantity(IReadOnlyList<FifoInventoryLot> lots, int quantity)
    {
        var remainingToRemove = quantity;
        var result = new List<LotSlice>();
        foreach (var lot in lots)
        {
            var removed = Math.Min(remainingToRemove, lot.RemainingQuantity);
            remainingToRemove -= removed;
            var remaining = lot.RemainingQuantity - removed;
            if (remaining > 0) result.Add(new LotSlice(lot.UnitAcquisitionPrice, remaining));
        }
        return result;
    }

    private static IReadOnlyDictionary<long, Money> BuildSellListingBasis(LocalSnapshot local)
    {
        var result = new Dictionary<long, Money>();
        foreach (var orders in local.CurrentOrders.Orders.Where(order => order.Side == PersonalTradingPostSide.Sell).GroupBy(order => order.ItemId))
        {
            var lots = local.Fifo.OpenLots.Where(lot => lot.ItemId == orders.Key)
                .OrderBy(lot => lot.CompletedAtUtc).ThenBy(lot => lot.BuyTransactionId).ToArray();
            if (orders.Sum(order => (long)order.Quantity) > lots.Sum(lot => (long)lot.RemainingQuantity)) continue;
            var assignedQuantity = 0;
            foreach (var order in orders.OrderBy(order => order.CreatedAtUtc).ThenBy(order => order.ExternalOrderId))
            {
                var availableLots = RemoveQuantity(lots, assignedQuantity);
                result.Add(order.ExternalOrderId, BasisFor(availableLots, order.Quantity));
                assignedQuantity = checked(assignedQuantity + order.Quantity);
            }
        }
        return result;
    }

    private static Money BasisFor(IReadOnlyList<LotSlice> lots, int quantity)
    {
        var remaining = quantity;
        var total = Money.Zero;
        foreach (var lot in lots)
        {
            var taken = Math.Min(remaining, lot.Quantity);
            total += Multiply(lot.UnitPrice, taken);
            remaining -= taken;
            if (remaining == 0) break;
        }
        if (remaining != 0) throw new InvalidOperationException("FIFO quantity exceeds known basis.");
        return total;
    }

    private static int QuantityToRelease(IReadOnlyList<LotSlice> lots, long requiredCapital)
    {
        if (requiredCapital <= 0) return 0;
        long released = 0;
        var quantity = 0;
        foreach (var lot in lots)
        {
            if (lot.UnitPrice.Copper <= 0) continue;
            var remainingCapital = requiredCapital - released;
            var unitsNeeded = checked(((remainingCapital - 1) / lot.UnitPrice.Copper) + 1);
            var taken = (int)Math.Min(lot.Quantity, unitsNeeded);
            released = checked(released + checked(lot.UnitPrice.Copper * taken));
            quantity = checked(quantity + taken);
            if (released >= requiredCapital) break;
        }
        return quantity;
    }

    private static long ItemExposure(LocalSnapshot local, int itemId) => checked(
        local.Fifo.OpenLots.Where(lot => lot.ItemId == itemId).Sum(lot => lot.RemainingAcquisitionBasis.Copper) +
        local.CurrentOrders.Orders.Where(order => order.Side == PersonalTradingPostSide.Buy && order.ItemId == itemId)
            .Sum(order => checked((long)order.UnitPriceInCopper * order.Quantity)));
    private static int ItemCapBasisPoints(PositionSizingPolicy policy, PositionSizingLiquidity liquidity) => liquidity switch
    {
        PositionSizingLiquidity.High => policy.HighLiquidityItemCapBasisPoints,
        PositionSizingLiquidity.Medium => policy.MediumLiquidityItemCapBasisPoints,
        _ => policy.LowLiquidityItemCapBasisPoints,
    };
    private static long PercentageRoundDown(long total, int basisPoints) => checked(
        (total / PositionSizingPolicy.BasisPointsPerWhole) * basisPoints +
        ((total % PositionSizingPolicy.BasisPointsPerWhole) * basisPoints / PositionSizingPolicy.BasisPointsPerWhole));
    private static int BestBuy(MarketListing? listing) => listing?.Buys.Select(level => level.UnitPriceInCopper).DefaultIfEmpty().Max() ?? 0;
    private static int LowestSell(MarketListing? listing) => listing?.Sells.Select(level => level.UnitPriceInCopper).DefaultIfEmpty().Min() ?? 0;
    private static Money? Gap(IReadOnlyList<MarketOrderLevel> levels, int? bestPrice, bool buy)
    {
        if (bestPrice is null) return null;
        var next = levels.FirstOrDefault(level => level.UnitPriceInCopper != bestPrice.Value);
        return next is null ? null : new Money(buy ? bestPrice.Value - next.UnitPriceInCopper : next.UnitPriceInCopper - bestPrice.Value);
    }
    private static bool IsCliff(Money? gap, int? bestPrice) => gap is not null && bestPrice is not null &&
        gap.Value.Copper * 10_000m >= bestPrice.Value * 500m;
    private static bool IsValidListing(MarketListing? listing) => listing is not null && listing.ItemId > 0 &&
        listing.Buys is not null && listing.Sells is not null &&
        listing.Buys.All(level => level is not null && level.Listings > 0 && level.Quantity > 0 && level.UnitPriceInCopper > 0) &&
        listing.Sells.All(level => level is not null && level.Listings > 0 && level.Quantity > 0 && level.UnitPriceInCopper > 0);
    private static bool MeetsRoi(Money profit, Money totalCost, int minimumBasisPoints) =>
        new BigInteger(profit.Copper) * PositionSizingPolicy.BasisPointsPerWhole >=
        new BigInteger(totalCost.Copper) * minimumBasisPoints;
    private static Money Multiply(int unitPrice, int quantity) => new(checked((long)unitPrice * quantity));
    private static Money Multiply(Money unitPrice, int quantity) => new(checked(unitPrice.Copper * quantity));
    private static string NameFor(int itemId, IReadOnlyDictionary<int, string> metadata, string? scannerName) =>
        !string.IsNullOrWhiteSpace(scannerName) ? scannerName : metadata.GetValueOrDefault(itemId) ?? $"Item #{itemId}";
    private static DateTimeOffset RequireUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero
        ? value : throw new InvalidOperationException("The recommendation clock must return UTC.");
    private static string? ErrorName(Gw2ApiErrorCategory? error) => error?.ToString();

    private sealed record LocalSnapshot(
        AccountProfile Profile, CurrentPersonalTradingPostOrderSnapshot CurrentOrders,
        FifoAccountingRebuild Fifo, IReadOnlyDictionary<int, string> Metadata,
        UserSettings? Settings, PositionSizingPolicy SizingPolicy, PrimaryRecommendationPolicies Policies);
    private sealed record MarketModel(Money PlannedBid, Money PlannedList, Money MaximumBid);
    private sealed record LotSlice(Money UnitPrice, int Quantity);
}
