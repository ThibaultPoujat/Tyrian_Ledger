using Gw2Tp.Analytics.Finance;
using Gw2Tp.Application.Finance;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketSnapshots;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.MarketScanning;

/// <summary>
/// Screens the complete current aggregate market through the typed gateway.
/// It owns no persistence, browser policy, detailed order-book assessment, or
/// recommendation/sizing behavior.
/// </summary>
public sealed class LiveMarketScanner : ILiveMarketScanner
{
    public const int MaximumCandidateCount = 200;

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

        var metadataResult = await marketDataClient.GetItemMetadataAsync(
            selected.Select(candidate => candidate.ItemId).ToArray(),
            cancellationToken).ConfigureAwait(false);
        if (!metadataResult.IsSuccess || metadataResult.IsPartialData || metadataResult.Value is null)
        {
            return LiveMarketScannerResult.Unavailable(
                settings,
                metadataResult.ErrorCategory ?? Gw2ApiErrorCategory.IncompleteData);
        }

        var metadata = metadataResult.Value;
        if (metadata.Count != selected.Length || metadata.Any(item => item is null || item.ItemId <= 0 || string.IsNullOrWhiteSpace(item.Name)) ||
            metadata.Select(item => item.ItemId).Distinct().Count() != selected.Length)
        {
            return LiveMarketScannerResult.Unavailable(settings, Gw2ApiErrorCategory.IncompleteData);
        }

        var metadataByItemId = metadata.ToDictionary(item => item.ItemId);
        if (selected.Any(candidate => !metadataByItemId.ContainsKey(candidate.ItemId)))
        {
            return LiveMarketScannerResult.Unavailable(settings, Gw2ApiErrorCategory.IncompleteData);
        }

        return Ready(
            snapshot,
            settings,
            qualifyingCandidateCount,
            selected.Select(candidate => candidate.ToContract(metadataByItemId[candidate.ItemId])).ToArray(),
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
        public LiveMarketScannerCandidate ToContract(MarketItemMetadata item) => new(
            item,
            BestBuy,
            LowestSell,
            PlannedBid,
            PlannedListPrice,
            ProfitScenario,
            TotalCost,
            ModeledRoi,
            MaximumBid,
            InclusionReasons);
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
