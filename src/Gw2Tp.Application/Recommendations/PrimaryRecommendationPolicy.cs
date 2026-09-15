namespace Gw2Tp.Application.Recommendations;

/// <summary>Pure, deterministic version-one graduated action policy.</summary>
public sealed class PrimaryRecommendationPolicy : IPrimaryRecommendationPolicy
{
    public const int Version = 1;
    internal const int BuySmallAllocationBasisPoints = PositionSizingPolicy.BasisPointsPerWhole / 2;

    private static readonly HashSet<OpportunityAnomalyFlag> HardAnomalies =
    [
        OpportunityAnomalyFlag.ExtremeCurrentRoiVersusHistory,
        OpportunityAnomalyFlag.AbruptPriceSpike,
        OpportunityAnomalyFlag.AbruptPriceDrop,
        OpportunityAnomalyFlag.AbruptDepthChange,
    ];

    public IReadOnlyList<PrimaryRecommendationRecord> Evaluate(IReadOnlyCollection<PrimaryRecommendationEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Any(item => item is null || item.ItemId <= 0 || string.IsNullOrWhiteSpace(item.ItemName)))
        {
            throw new ArgumentException("Recommendation evidence is structurally invalid.", nameof(evidence));
        }

        return evidence.Select(EvaluateOne)
            .OrderBy(record => Priority(record.Action))
            .ThenBy(record => record.Score?.Rank ?? int.MaxValue)
            .ThenBy(record => record.ItemName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.ItemId)
            .ThenBy(record => record.OrderId, StringComparer.Ordinal)
            .ToArray();
    }

    private static PrimaryRecommendationRecord EvaluateOne(PrimaryRecommendationEvidence item)
    {
        var (action, reasons) = item.Source switch
        {
            PrimaryRecommendationSource.NewOpportunity => NewOpportunity(item),
            PrimaryRecommendationSource.BuyOrder => BuyOrder(item),
            PrimaryRecommendationSource.SellListing => SellListing(item),
            PrimaryRecommendationSource.Inventory => Inventory(item),
            _ => Review(PrimaryRecommendationReasonCode.EvidenceMissing),
        };
        reasons.Add(Reason(PrimaryRecommendationReasonCode.ReadOnlyManualAction));
        return new PrimaryRecommendationRecord(
            action, item.Source, item.OrderState, item.OrderId, item.ItemId, item.ItemName,
            item.SuggestedQuantity, item.SuggestedCapital, item.Prices, item.Economics,
            item.Score, item.History, item.Liquidity, item.PortfolioConstraints, reasons);
    }

    public int AllocationBasisPoints(OpportunityScore score, PositionSizingLiquidity liquidity)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (score.TotalPoints <= 0m || score.HistoricalConfidence == OpportunityHistoricalConfidence.Insufficient ||
            HasHardAnomaly(score.Anomalies))
        {
            return 0;
        }
        return QualifiesForFullBuy(score.HistoricalConfidence, liquidity, score.Anomalies)
            ? PositionSizingPolicy.BasisPointsPerWhole
            : BuySmallAllocationBasisPoints;
    }

    private static (PrimaryRecommendationAction, List<PrimaryRecommendationReason>) NewOpportunity(PrimaryRecommendationEvidence item)
    {
        if (!item.IsEvidenceComplete || item.Score is null || item.History is null || item.Liquidity is null)
            return Review(PrimaryRecommendationReasonCode.EvidenceMissing);
        if (item.Score.TotalPoints <= 0m) return With(PrimaryRecommendationAction.Skip, PrimaryRecommendationReasonCode.ZeroScore);
        if (HasHardAnomaly(item.Score.Anomalies))
            return With(PrimaryRecommendationAction.Skip, PrimaryRecommendationReasonCode.HardAnomaly);
        if (item.History.Confidence == OpportunityHistoricalConfidence.Insufficient)
            return With(PrimaryRecommendationAction.Wait, PrimaryRecommendationReasonCode.InsufficientHistory);
        if (!item.IsSizingAvailable || item.SuggestedQuantity == 0)
            return With(PrimaryRecommendationAction.Wait, PrimaryRecommendationReasonCode.NoSizingCapacity);
        if (QualifiesForFullBuy(item.History.Confidence, item.Liquidity.Classification, item.Score.Anomalies))
        {
            return With(PrimaryRecommendationAction.Buy,
                PrimaryRecommendationReasonCode.StrongEvidence, PrimaryRecommendationReasonCode.HighLiquidity);
        }

        var codes = new List<PrimaryRecommendationReasonCode>();
        if (item.History.Confidence == OpportunityHistoricalConfidence.Partial)
            codes.Add(PrimaryRecommendationReasonCode.PartialHistory);
        if (item.Liquidity.Classification != PositionSizingLiquidity.High)
            codes.Add(PrimaryRecommendationReasonCode.LiquidityRisk);
        if (item.Score.Anomalies.Count > 0)
            codes.Add(PrimaryRecommendationReasonCode.PenalizedEvidence);
        return With(PrimaryRecommendationAction.BuySmall, codes.ToArray());
    }

    private static (PrimaryRecommendationAction, List<PrimaryRecommendationReason>) BuyOrder(PrimaryRecommendationEvidence item)
    {
        if (item.OrderState == PrimaryRecommendationOrderState.AboveMaximumBid)
            return With(PrimaryRecommendationAction.CancelBid, PrimaryRecommendationReasonCode.BidAboveMaximum);
        if (item.IsReserveCancellation)
            return With(PrimaryRecommendationAction.CancelBid, PrimaryRecommendationReasonCode.ReserveRestoration);
        if (item.IsExposureExceeded)
            return (PrimaryRecommendationAction.Review, ExposureReasons(item));
        if (!item.IsEvidenceComplete || item.History is null || item.Prices.MaximumBid is null)
            return Review(PrimaryRecommendationReasonCode.EvidenceMissing);
        if (item.OrderState == PrimaryRecommendationOrderState.Competitive &&
            item.History.Confidence != OpportunityHistoricalConfidence.Insufficient)
            return With(PrimaryRecommendationAction.KeepBid, PrimaryRecommendationReasonCode.BidCompetitive);
        if (item.OrderState == PrimaryRecommendationOrderState.Outbid)
        {
            if (item.Score is null || item.Liquidity is null)
                return Review(PrimaryRecommendationReasonCode.EvidenceMissing);
            var qualifies = item.Score.TotalPoints > 0m &&
                QualifiesForFullBuy(item.History.Confidence, item.Liquidity.Classification, item.Score.Anomalies);
            var withinMaximum = item.Prices.PlannedBid is { } planned && planned.Copper <= item.Prices.MaximumBid.Value.Copper;
            var hasCapital = item.IncrementalCapitalRequired.Copper > 0 &&
                item.IncrementalCapitalCapacity.Copper >= item.IncrementalCapitalRequired.Copper;
            if (qualifies && withinMaximum && hasCapital)
            {
                return With(PrimaryRecommendationAction.UpdateBid,
                    PrimaryRecommendationReasonCode.BidOutbid,
                    PrimaryRecommendationReasonCode.UpdateWithinMaximum,
                    PrimaryRecommendationReasonCode.IncrementalCapitalAvailable);
            }
            return With(PrimaryRecommendationAction.StopBidding,
                PrimaryRecommendationReasonCode.BidOutbid,
                hasCapital ? PrimaryRecommendationReasonCode.FullBuyEvidenceNotMet : PrimaryRecommendationReasonCode.IncrementalCapitalUnavailable);
        }
        return Review(PrimaryRecommendationReasonCode.EvidenceMissing);
    }

    private static (PrimaryRecommendationAction, List<PrimaryRecommendationReason>) SellListing(PrimaryRecommendationEvidence item)
    {
        if (item.IsUnknownBasis) return Review(PrimaryRecommendationReasonCode.UnknownCostBasis);
        if (!item.IsEvidenceComplete) return Review(PrimaryRecommendationReasonCode.EvidenceMissing);
        return item.OrderState switch
        {
            PrimaryRecommendationOrderState.Competitive => With(PrimaryRecommendationAction.LeaveSellListing, PrimaryRecommendationReasonCode.SellCompetitive),
            PrimaryRecommendationOrderState.UndercutOneCopper => With(PrimaryRecommendationAction.LeaveSellListing, PrimaryRecommendationReasonCode.OneCopperUndercutProtected),
            PrimaryRecommendationOrderState.UndercutMoreThanOneCopper => Review(PrimaryRecommendationReasonCode.SellMateriallyUndercut),
            _ => Review(PrimaryRecommendationReasonCode.EvidenceMissing),
        };
    }

    private static (PrimaryRecommendationAction, List<PrimaryRecommendationReason>) Inventory(PrimaryRecommendationEvidence item)
    {
        if (item.IsUnknownBasis) return Review(PrimaryRecommendationReasonCode.UnknownCostBasis);
        if (!item.IsEvidenceComplete || item.History is null || item.Liquidity is null)
            return Review(PrimaryRecommendationReasonCode.EvidenceMissing);
        if (item.IsExposureExceeded)
            return item.SuggestedQuantity > 0
                ? With(PrimaryRecommendationAction.Reduce, PrimaryRecommendationReasonCode.ItemExposureExceeded, PrimaryRecommendationReasonCode.SafeDepthLimited)
                : With(PrimaryRecommendationAction.Review, PrimaryRecommendationReasonCode.ItemExposureExceeded, PrimaryRecommendationReasonCode.SafeDepthLimited);
        if (item.IsImmediateFullExitPositive)
            return With(PrimaryRecommendationAction.Sell, PrimaryRecommendationReasonCode.PositiveImmediateExit);
        if (item.IsImmediatePartialExitPositive && item.SuggestedQuantity > 0)
            return With(PrimaryRecommendationAction.SellPartial, PrimaryRecommendationReasonCode.PositiveImmediateExit, PrimaryRecommendationReasonCode.SafeDepthLimited);
        if (item.IsListingExitPositive)
            return With(PrimaryRecommendationAction.List, PrimaryRecommendationReasonCode.PositiveListingExit);
        if (item.History.Confidence == OpportunityHistoricalConfidence.Strong)
            return With(PrimaryRecommendationAction.Hold, PrimaryRecommendationReasonCode.NoPositiveExit);
        return Review(PrimaryRecommendationReasonCode.InsufficientHistory);
    }

    private static (PrimaryRecommendationAction, List<PrimaryRecommendationReason>) Review(PrimaryRecommendationReasonCode reason) =>
        With(PrimaryRecommendationAction.Review, reason);

    private static List<PrimaryRecommendationReason> ExposureReasons(PrimaryRecommendationEvidence item)
    {
        var codes = item.PortfolioConstraints.Where(constraint => constraint.IsBinding)
            .Select(constraint => constraint.Name switch
            {
                PositionSizingConstraintName.ItemExposure => PrimaryRecommendationReasonCode.ItemExposureExceeded,
                PositionSizingConstraintName.StrategyConcentration => PrimaryRecommendationReasonCode.StrategyExposureExceeded,
                PositionSizingConstraintName.CategoryConcentration => PrimaryRecommendationReasonCode.CategoryExposureExceeded,
                _ => PrimaryRecommendationReasonCode.EvidenceMissing,
            })
            .Distinct()
            .ToArray();
        return (codes.Length == 0 ? [PrimaryRecommendationReasonCode.EvidenceMissing] : codes)
            .Select(Reason)
            .ToList();
    }

    private static bool HasHardAnomaly(IEnumerable<OpportunityScoreAnomaly> anomalies) =>
        anomalies.Any(anomaly => HardAnomalies.Contains(anomaly.Flag));

    private static bool QualifiesForFullBuy(
        OpportunityHistoricalConfidence confidence,
        PositionSizingLiquidity liquidity,
        IReadOnlyCollection<OpportunityScoreAnomaly> anomalies) =>
        confidence == OpportunityHistoricalConfidence.Strong &&
        liquidity == PositionSizingLiquidity.High &&
        anomalies.Count == 0;

    private static (PrimaryRecommendationAction, List<PrimaryRecommendationReason>) With(
        PrimaryRecommendationAction action, params PrimaryRecommendationReasonCode[] reasons) =>
        (action, reasons.Select(Reason).ToList());

    private static PrimaryRecommendationReason Reason(PrimaryRecommendationReasonCode code) => new(code, code switch
    {
        PrimaryRecommendationReasonCode.StrongEvidence => "Both retained-history windows support this opportunity.",
        PrimaryRecommendationReasonCode.PartialHistory => "Only one retained-history window has enough coverage, so use a smaller position.",
        PrimaryRecommendationReasonCode.InsufficientHistory => "Retained history is not yet sufficient for a confident action.",
        PrimaryRecommendationReasonCode.HighLiquidity => "Current visible depth has no scanner liquidity warnings.",
        PrimaryRecommendationReasonCode.LiquidityRisk => "Current depth or a price cliff warrants a smaller position.",
        PrimaryRecommendationReasonCode.HardAnomaly => "Current price, depth, or ROI is an extreme departure from retained history.",
        PrimaryRecommendationReasonCode.ZeroScore => "The disclosed opportunity score is zero.",
        PrimaryRecommendationReasonCode.NoSizingCapacity => "The portfolio policy has no safe capacity for this item now.",
        PrimaryRecommendationReasonCode.EvidenceMissing => "Required market, history, score, or portfolio evidence is unavailable or inconsistent.",
        PrimaryRecommendationReasonCode.BidAboveMaximum => "The current bid is above the maximum allowed by the configured profit and ROI policy.",
        PrimaryRecommendationReasonCode.ReserveRestoration => "Canceling this bid is selected to restore the cash reserve.",
        PrimaryRecommendationReasonCode.BidCompetitive => "The current bid is competitive and remains within the maximum bid.",
        PrimaryRecommendationReasonCode.BidOutbid => "The current bid is below the best visible buy price; being outbid alone does not justify chasing.",
        PrimaryRecommendationReasonCode.UpdateWithinMaximum => "The replacement bid stays at or below the maximum allowed bid.",
        PrimaryRecommendationReasonCode.IncrementalCapitalAvailable => "Sizing covers the exact additional capital required by the replacement bid.",
        PrimaryRecommendationReasonCode.IncrementalCapitalUnavailable => "Sizing does not cover the exact additional capital required by a replacement bid.",
        PrimaryRecommendationReasonCode.SellCompetitive => "The current sell listing is competitive with the lowest visible ask.",
        PrimaryRecommendationReasonCode.OneCopperUndercutProtected => "A one-copper undercut alone is not a reason to cancel and relist.",
        PrimaryRecommendationReasonCode.SellMateriallyUndercut => "The listing is undercut by more than one copper and needs manual review.",
        PrimaryRecommendationReasonCode.ItemExposureExceeded => "Known item exposure exceeds the current portfolio cap.",
        PrimaryRecommendationReasonCode.SafeDepthLimited => "The quantity is bounded by currently visible safe depth.",
        PrimaryRecommendationReasonCode.PositiveImmediateExit => "The modeled immediate liquidation has positive net profit after canonical fees.",
        PrimaryRecommendationReasonCode.PositiveListingExit => "Immediate liquidation is not positive, but the modeled current listing is positive after fees.",
        PrimaryRecommendationReasonCode.NoPositiveExit => "Complete strong evidence supports neither a positive immediate sale nor a positive listing now.",
        PrimaryRecommendationReasonCode.UnknownCostBasis => "Historical acquisition basis is unknown and is never treated as free.",
        PrimaryRecommendationReasonCode.ReadOnlyManualAction => "This is read-only decision support; you must perform any Trading Post action manually.",
        PrimaryRecommendationReasonCode.PenalizedEvidence => "The disclosed score contains a non-critical anomaly penalty, so the position is reduced.",
        PrimaryRecommendationReasonCode.FullBuyEvidenceNotMet => "The evidence does not meet the strict full-BUY threshold required to update an outbid order.",
        PrimaryRecommendationReasonCode.StrategyExposureExceeded => "Current exposure exceeds the disclosed strategy concentration cap.",
        PrimaryRecommendationReasonCode.CategoryExposureExceeded => "Current exposure exceeds the disclosed category concentration cap.",
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    });

    private static int Priority(PrimaryRecommendationAction action) => action switch
    {
        PrimaryRecommendationAction.CancelBid => 0,
        PrimaryRecommendationAction.Review => 1,
        PrimaryRecommendationAction.Reduce => 2,
        PrimaryRecommendationAction.Sell or PrimaryRecommendationAction.SellPartial => 3,
        PrimaryRecommendationAction.UpdateBid => 4,
        PrimaryRecommendationAction.Buy or PrimaryRecommendationAction.BuySmall => 5,
        PrimaryRecommendationAction.List => 6,
        PrimaryRecommendationAction.StopBidding => 7,
        PrimaryRecommendationAction.Wait or PrimaryRecommendationAction.Skip => 8,
        PrimaryRecommendationAction.KeepBid or PrimaryRecommendationAction.LeaveSellListing or PrimaryRecommendationAction.Hold => 9,
        _ => 10,
    };
}
