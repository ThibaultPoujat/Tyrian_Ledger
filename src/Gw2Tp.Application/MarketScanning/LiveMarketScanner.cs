using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Application.Finance;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketSnapshots;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.MarketScanning;

/// <summary>
/// Screens the complete current aggregate market through the typed gateway.
/// It owns no persistence, browser policy, or final recommendation/sizing behavior.
/// </summary>
public sealed class LiveMarketScanner : ILiveMarketScanner
{
    public const int MaximumOrderBookDetailLevels = 10;
    public const int MaximumCandidateCount = 200;
    private const int MinimumVisibleSideQuantity = 10;
    private const int MinimumVisibleSideListings = 3;
    private const int PriceCliffBasisPoints = 500;
    private const int ParticipationCapDivisor = 10;

    private static readonly IReadOnlyList<LiveMarketScannerInclusionReason> InclusionReasons = Array.AsReadOnly(
    [
        LiveMarketScannerInclusionReason.PositiveModeledProfit,
        LiveMarketScannerInclusionReason.MeetsMinimumNetProfit,
        LiveMarketScannerInclusionReason.MeetsMinimumRoi,
        LiveMarketScannerInclusionReason.UsesConfiguredPricePolicy,
    ]);

    private readonly PublicMarketSnapshotCollector collector;
    private readonly IGw2ApiClient marketDataClient;
    private readonly FlipProfitCalculator profitCalculator = new(Gw2TradingPostFeePolicy.Create());
    private readonly OrderBookExecutionSimulator orderBookExecutionSimulator = new();

    public LiveMarketScanner(PublicMarketSnapshotCollector collector, IGw2ApiClient marketDataClient)
    {
        this.collector = collector ?? throw new ArgumentNullException(nameof(collector));
        this.marketDataClient = marketDataClient ?? throw new ArgumentNullException(nameof(marketDataClient));
    }

    public async Task<LiveMarketScannerResult> ScanAsync(
        LiveMarketScannerSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        PublicMarketAggregateSnapshot snapshot;
        try
        {
            snapshot = await collector.CollectAggregatePricesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (PublicMarketSnapshotCollectionException exception)
        {
            return LiveMarketScannerResult.Unavailable(settings, exception.ErrorCategory);
        }

        var exclusions = new Dictionary<LiveMarketScannerExclusionReason, int>();
        var candidates = new List<CalculatedCandidate>();
        foreach (var price in snapshot.Prices)
        {
            var evaluation = TryCalculateCandidate(price, settings);
            if (evaluation.Candidate is { } candidate)
            {
                candidates.Add(candidate);
            }
            else
            {
                exclusions[evaluation.ExclusionReason!.Value] = exclusions.GetValueOrDefault(evaluation.ExclusionReason.Value) + 1;
            }
        }

        candidates.Sort(CalculatedCandidateComparer.Instance);
        var selected = candidates.Take(MaximumCandidateCount).ToArray();
        var qualifyingCandidateCount = candidates.Count;
        if (selected.Length == 0)
        {
            return Ready(snapshot, settings, qualifyingCandidateCount, [], exclusions);
        }

        var selectedItemIds = selected.Select(candidate => candidate.ItemId).ToArray();
        var metadataTask = marketDataClient.GetItemMetadataAsync(selectedItemIds, cancellationToken);
        var listingsTask = marketDataClient.GetListingsAsync(selectedItemIds, cancellationToken);
        await Task.WhenAll(metadataTask, listingsTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var metadataResult = await metadataTask.ConfigureAwait(false);
        var listingsResult = await listingsTask.ConfigureAwait(false);
        if (!metadataResult.IsSuccess || metadataResult.IsPartialData || metadataResult.Value is null ||
            !listingsResult.IsSuccess || listingsResult.IsPartialData || listingsResult.Value is null)
        {
            return LiveMarketScannerResult.Unavailable(
                settings,
                metadataResult.ErrorCategory ?? listingsResult.ErrorCategory ?? Gw2ApiErrorCategory.IncompleteData);
        }

        var metadata = metadataResult.Value;
        var listings = listingsResult.Value;
        if (!HasExactItemSet(selectedItemIds, metadata, item => item.ItemId) ||
            !HasExactItemSet(selectedItemIds, listings, listing => listing.ItemId))
        {
            return LiveMarketScannerResult.Unavailable(settings, Gw2ApiErrorCategory.IncompleteData);
        }

        if (metadata.Any(item => item is null || item.ItemId <= 0 || string.IsNullOrWhiteSpace(item.Name)) ||
            listings.Any(listing => !IsValidListing(listing)))
        {
            return LiveMarketScannerResult.Unavailable(settings, Gw2ApiErrorCategory.InvalidPayload);
        }

        var metadataByItemId = metadata.ToDictionary(item => item.ItemId);
        var listingByItemId = listings.ToDictionary(listing => listing.ItemId);
        if (selected.Any(candidate => !metadataByItemId.ContainsKey(candidate.ItemId) || !listingByItemId.ContainsKey(candidate.ItemId)))
        {
            return LiveMarketScannerResult.Unavailable(settings, Gw2ApiErrorCategory.IncompleteData);
        }

        return Ready(
            snapshot,
            settings,
            qualifyingCandidateCount,
            selected.Select(candidate => candidate.ToContract(
                metadataByItemId[candidate.ItemId],
                CalculateLiquidityEvidence(listingByItemId[candidate.ItemId], settings.IntendedQuantity))).ToArray(),
            exclusions);
    }

    private static LiveMarketScannerResult Ready(
        PublicMarketAggregateSnapshot snapshot,
        LiveMarketScannerSettings settings,
        int qualifyingCandidateCount,
        IReadOnlyList<LiveMarketScannerCandidate> candidates,
        IReadOnlyDictionary<LiveMarketScannerExclusionReason, int> exclusions) => new(
            LiveMarketScannerState.Ready,
            null,
            snapshot.GeneratedAtUtc,
            settings,
            Gw2TradingPostFeePolicy.IsFractionalCopperRoundingExternallyVerified,
            qualifyingCandidateCount,
            qualifyingCandidateCount > candidates.Count,
            candidates,
            exclusions
                .OrderBy(pair => pair.Key)
                .Select(pair => new LiveMarketScannerExclusionCount(pair.Key, pair.Value))
                .ToArray());

    private CandidateEvaluation TryCalculateCandidate(MarketPrice? price, LiveMarketScannerSettings settings)
    {
        if (price is null || price.ItemId <= 0 || price.Buys is null || price.Sells is null ||
            price.Buys.Quantity <= 0 || price.Sells.Quantity <= 0 ||
            price.Buys.UnitPriceInCopper <= 0 || price.Sells.UnitPriceInCopper <= 0)
        {
            return CandidateEvaluation.Excluded(LiveMarketScannerExclusionReason.InvalidMarketData);
        }

        try
        {
            var plannedBid = new Money(checked((long)price.Buys.UnitPriceInCopper + settings.BidIncrementCopper));
            var plannedListPrice = new Money(checked((long)price.Sells.UnitPriceInCopper - settings.ListUndercutCopper));
            if (plannedListPrice.Copper <= 0 || plannedBid.Copper <= 0)
            {
                return CandidateEvaluation.Excluded(LiveMarketScannerExclusionReason.PricePolicyInvalid);
            }

            var metrics = CalculateMetrics(plannedBid, plannedListPrice);
            if (metrics.ProfitScenario.NetProfit.Copper <= 0)
            {
                return CandidateEvaluation.Excluded(LiveMarketScannerExclusionReason.FeeLosing);
            }

            if (metrics.ProfitScenario.NetProfit.Copper < settings.MinimumNetProfit.Copper)
            {
                return CandidateEvaluation.Excluded(LiveMarketScannerExclusionReason.MinimumNetProfitNotMet);
            }

            if (!metrics.ModeledRoi.MeetsOrExceedsBasisPoints(settings.MinimumRoiBasisPoints))
            {
                return CandidateEvaluation.Excluded(LiveMarketScannerExclusionReason.MinimumRoiNotMet);
            }

            var maximumBid = FindMaximumBid(plannedListPrice, settings);
            if (maximumBid is null || plannedBid.Copper > maximumBid.Value.Copper)
            {
                return CandidateEvaluation.Excluded(LiveMarketScannerExclusionReason.MinimumRoiNotMet);
            }

            return CandidateEvaluation.Included(new CalculatedCandidate(
                price.ItemId,
                price.Buys,
                price.Sells,
                plannedBid,
                plannedListPrice,
                metrics.ProfitScenario,
                metrics.TotalCost,
                metrics.ModeledRoi,
                maximumBid.Value));
        }
        catch (OverflowException)
        {
            return CandidateEvaluation.Excluded(LiveMarketScannerExclusionReason.ArithmeticOverflow);
        }
    }

    private Money? FindMaximumBid(Money plannedListPrice, LiveMarketScannerSettings settings)
    {
        long lowerBound = 0;
        var upperBound = plannedListPrice.Copper;
        while (lowerBound < upperBound)
        {
            var candidateBid = lowerBound + ((upperBound - lowerBound + 1) / 2);
            if (MeetsPolicy(CalculateMetrics(new Money(candidateBid), plannedListPrice), settings))
            {
                lowerBound = candidateBid;
            }
            else
            {
                upperBound = candidateBid - 1;
            }
        }

        return lowerBound == 0 ? null : new Money(lowerBound);
    }

    private static bool MeetsPolicy(CandidateMetrics metrics, LiveMarketScannerSettings settings) =>
        metrics.ProfitScenario.NetProfit.Copper > 0 &&
        metrics.ProfitScenario.NetProfit.Copper >= settings.MinimumNetProfit.Copper &&
        metrics.ModeledRoi.MeetsOrExceedsBasisPoints(settings.MinimumRoiBasisPoints);

    private CandidateMetrics CalculateMetrics(Money bid, Money plannedListPrice)
    {
        var scenario = profitCalculator.Calculate(bid, plannedListPrice);
        var totalCost = Gw2TradingPostFeePolicy.CalculateFullUpFrontCost(bid, scenario.ListingFee);
        return new CandidateMetrics(
            scenario,
            totalCost,
            Gw2TradingPostFeePolicy.CalculateExactRoi(scenario.NetProfit, bid, scenario.ListingFee));
    }

    private LiveMarketScannerLiquidityEvidence CalculateLiquidityEvidence(MarketListing listing, int intendedQuantity)
    {
        var buys = listing.Buys.OrderByDescending(level => level.UnitPriceInCopper).ToArray();
        var sells = listing.Sells.OrderBy(level => level.UnitPriceInCopper).ToArray();
        var buyTotalQuantity = SumQuantities(buys);
        var sellTotalQuantity = SumQuantities(sells);
        var bestBuyPrice = buys.FirstOrDefault()?.UnitPriceInCopper;
        var bestSellPrice = sells.FirstOrDefault()?.UnitPriceInCopper;
        var nearBestBuys = bestBuyPrice is { } buyPrice ? SamePriceLevels(buys, buyPrice) : [];
        var nearBestSells = bestSellPrice is { } sellPrice ? SamePriceLevels(sells, sellPrice) : [];
        var buyNextLevelGap = FindNextLevelGap(buys, bestBuyPrice, isBuy: true);
        var sellNextLevelGap = FindNextLevelGap(sells, bestSellPrice, isBuy: false);
        var hasBuyPriceCliff = IsPriceCliff(buyNextLevelGap, bestBuyPrice);
        var hasSellPriceCliff = IsPriceCliff(sellNextLevelGap, bestSellPrice);
        var acquisition = orderBookExecutionSimulator.SimulateAcquisition(ToExecutionLevels(sells), intendedQuantity);
        var liquidation = orderBookExecutionSimulator.SimulateLiquidation(ToExecutionLevels(buys), intendedQuantity);
        var participationCap = CalculateParticipationCap(Math.Min(buyTotalQuantity, sellTotalQuantity));
        var reasons = new List<LiveMarketScannerLiquidityReason>();

        if (SumListings(buys) < MinimumVisibleSideListings) reasons.Add(LiveMarketScannerLiquidityReason.InsufficientBuyListingDepth);
        if (SumListings(sells) < MinimumVisibleSideListings) reasons.Add(LiveMarketScannerLiquidityReason.InsufficientSellListingDepth);
        if (buyTotalQuantity < MinimumVisibleSideQuantity) reasons.Add(LiveMarketScannerLiquidityReason.InsufficientBuyQuantityDepth);
        if (sellTotalQuantity < MinimumVisibleSideQuantity) reasons.Add(LiveMarketScannerLiquidityReason.InsufficientSellQuantityDepth);
        if (hasBuyPriceCliff) reasons.Add(LiveMarketScannerLiquidityReason.BuyPriceCliff);
        if (hasSellPriceCliff) reasons.Add(LiveMarketScannerLiquidityReason.SellPriceCliff);
        if (!acquisition.IsFullyFilled) reasons.Add(LiveMarketScannerLiquidityReason.IntendedQuantityCannotFullyAcquire);
        if (!liquidation.IsFullyFilled) reasons.Add(LiveMarketScannerLiquidityReason.IntendedQuantityCannotFullyLiquidate);
        if (participationCap < intendedQuantity) reasons.Add(LiveMarketScannerLiquidityReason.ParticipationCapBelowIntendedQuantity);

        return new LiveMarketScannerLiquidityEvidence(
            buyTotalQuantity,
            sellTotalQuantity,
            SumQuantities(nearBestBuys),
            SumQuantities(nearBestSells),
            SumListings(nearBestBuys),
            SumListings(nearBestSells),
            buyNextLevelGap,
            sellNextLevelGap,
            hasBuyPriceCliff,
            hasSellPriceCliff,
            acquisition,
            liquidation,
            participationCap,
            reasons.OrderBy(reason => reason).ToArray(),
            buys.Take(MaximumOrderBookDetailLevels).ToArray(),
            sells.Take(MaximumOrderBookDetailLevels).ToArray());
    }

    private static bool HasExactItemSet<T>(IReadOnlyCollection<int> expectedItemIds, IReadOnlyList<T> values, Func<T, int> itemId) =>
        values.Count == expectedItemIds.Count &&
        values.All(value => value is not null && expectedItemIds.Contains(itemId(value))) &&
        values.Select(itemId).Distinct().Count() == expectedItemIds.Count;

    private static bool IsValidListing(MarketListing? listing) => listing is not null && listing.ItemId > 0 &&
        listing.Buys is not null && listing.Sells is not null &&
        listing.Buys.All(IsValidLevel) && listing.Sells.All(IsValidLevel);

    private static bool IsValidLevel(MarketOrderLevel? level) => level is not null && level.Listings > 0 &&
        level.Quantity > 0 && level.UnitPriceInCopper > 0;

    private static MarketOrderLevel[] SamePriceLevels(IReadOnlyList<MarketOrderLevel> levels, int price) =>
        levels.Where(level => level.UnitPriceInCopper == price).ToArray();

    private static Money? FindNextLevelGap(IReadOnlyList<MarketOrderLevel> levels, int? bestPrice, bool isBuy)
    {
        if (bestPrice is null)
        {
            return null;
        }

        var next = levels.FirstOrDefault(level => level.UnitPriceInCopper != bestPrice.Value);
        return next is null ? null : new Money(isBuy ? bestPrice.Value - next.UnitPriceInCopper : next.UnitPriceInCopper - bestPrice.Value);
    }

    private static bool IsPriceCliff(Money? gap, int? bestPrice) => gap is not null && bestPrice is not null &&
        checked(gap.Value.Copper * 10_000) >= checked((long)bestPrice.Value * PriceCliffBasisPoints);

    private static long SumQuantities(IEnumerable<MarketOrderLevel> levels) => levels.Sum(level => (long)level.Quantity);

    private static long SumListings(IEnumerable<MarketOrderLevel> levels) => levels.Sum(level => (long)level.Listings);

    private static IReadOnlyList<OrderBookLevel> ToExecutionLevels(IEnumerable<MarketOrderLevel> levels) =>
        levels.Select(level => new OrderBookLevel(level.Quantity, new Money(level.UnitPriceInCopper))).ToArray();

    private static int CalculateParticipationCap(long smallerSideQuantity) =>
        (int)Math.Min(int.MaxValue, smallerSideQuantity / ParticipationCapDivisor);

    private sealed record CandidateMetrics(FlipProfitScenario ProfitScenario, Money TotalCost, ExactRoi ModeledRoi);

    private sealed record CandidateEvaluation(CalculatedCandidate? Candidate, LiveMarketScannerExclusionReason? ExclusionReason)
    {
        public static CandidateEvaluation Included(CalculatedCandidate candidate) => new(candidate, null);

        public static CandidateEvaluation Excluded(LiveMarketScannerExclusionReason reason) => new(null, reason);
    }

    private sealed record CalculatedCandidate(
        int ItemId,
        MarketOrderSummary BestBuy,
        MarketOrderSummary LowestSell,
        Money PlannedBid,
        Money PlannedListPrice,
        FlipProfitScenario ProfitScenario,
        Money TotalCost,
        ExactRoi ModeledRoi,
        Money MaximumBid)
    {
        public LiveMarketScannerCandidate ToContract(MarketItemMetadata item, LiveMarketScannerLiquidityEvidence liquidity) => new(
            item,
            BestBuy,
            LowestSell,
            PlannedBid,
            PlannedListPrice,
            ProfitScenario,
            TotalCost,
            ModeledRoi,
            MaximumBid,
            InclusionReasons,
            liquidity);
    }

    private sealed class CalculatedCandidateComparer : IComparer<CalculatedCandidate>
    {
        public static CalculatedCandidateComparer Instance { get; } = new();

        public int Compare(CalculatedCandidate? left, CalculatedCandidate? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return 1;
            if (right is null) return -1;

            var profitComparison = right.ProfitScenario.NetProfit.Copper.CompareTo(left.ProfitScenario.NetProfit.Copper);
            if (profitComparison != 0) return profitComparison;

            var quantityComparison = Math.Min(right.BestBuy.Quantity, right.LowestSell.Quantity)
                .CompareTo(Math.Min(left.BestBuy.Quantity, left.LowestSell.Quantity));
            if (quantityComparison != 0) return quantityComparison;

            var roiComparison = right.ModeledRoi.CompareTo(left.ModeledRoi);
            return roiComparison != 0 ? roiComparison : left.ItemId.CompareTo(right.ItemId);
        }
    }
}
