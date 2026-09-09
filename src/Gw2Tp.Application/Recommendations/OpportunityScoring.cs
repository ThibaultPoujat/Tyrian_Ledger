using Gw2Tp.Analytics.MarketHistory;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.MarketScanning;

namespace Gw2Tp.Application.Recommendations;

public enum OpportunityScoreComponentName
{
    ExpectedEconomics = 1,
    CurrentLiquidity = 2,
    HistoricalPersistence = 3,
    HistoricalStability = 4,
    HistoricalConfidence = 5,
    PersonalEvidence = 6,
}

public enum OpportunityScoreComponentState
{
    Available = 1,
    InsufficientData = 2,
    NotYetSupported = 3,
}

public enum OpportunityHistoricalConfidence
{
    Insufficient = 1,
    Partial = 2,
    Strong = 3,
}

public enum OpportunityAnomalyFlag
{
    ExtremeCurrentRoiVersusHistory = 1,
    ShallowBestLevels = 2,
    CurrentPriceCliff = 3,
    AbruptPriceSpike = 4,
    AbruptPriceDrop = 5,
    AbruptDepthChange = 6,
    InsufficientHistoricalCoverage = 7,
    IntendedPositionExceedsVisibleDepth = 8,
}

public enum OpportunityPersonalEvidenceState
{
    NotYetAvailable = 1,
}

/// <summary>
/// Versioned, bounded policy for the deterministic opportunity score. Points
/// are deliberately disclosed rather than treated as a prediction or truth.
/// </summary>
public sealed record OpportunityScorePolicy(
    int Version,
    decimal ExpectedEconomicsMaximumPoints,
    decimal CurrentLiquidityMaximumPoints,
    decimal HistoricalPersistenceMaximumPoints,
    decimal HistoricalStabilityMaximumPoints,
    decimal HistoricalConfidenceMaximumPoints,
    decimal MaximumAppliedPenaltyPoints,
    int RoiNormalizationCeilingBasisPoints,
    long ProfitNormalizationCeilingCopper,
    decimal VolatilityZeroQualityThreshold,
    int HistoricalPriceDeviationBasisPoints,
    int HistoricalDepthDeviationBasisPoints,
    int ExtremeRoiAbsoluteDeltaBasisPoints,
    int ExtremeRoiMultipleBasisPoints,
    int MinimumNearBestListings,
    decimal ExtremeRoiPenaltyPoints,
    decimal ShallowBestLevelsPenaltyPoints,
    decimal CurrentPriceCliffPenaltyPoints,
    decimal HistoricalPriceDeviationPenaltyPoints,
    decimal HistoricalDepthDeviationPenaltyPoints,
    decimal ExcessivePositionPenaltyPoints)
{
    public const int CurrentVersion = 1;
    public const decimal MaximumTotalPoints = 100m;

    public static OpportunityScorePolicy Default { get; } = new(
        CurrentVersion,
        ExpectedEconomicsMaximumPoints: 25m,
        CurrentLiquidityMaximumPoints: 25m,
        HistoricalPersistenceMaximumPoints: 20m,
        HistoricalStabilityMaximumPoints: 15m,
        HistoricalConfidenceMaximumPoints: 15m,
        MaximumAppliedPenaltyPoints: 40m,
        RoiNormalizationCeilingBasisPoints: 3_000,
        ProfitNormalizationCeilingCopper: 10_000,
        VolatilityZeroQualityThreshold: 0.5m,
        HistoricalPriceDeviationBasisPoints: 2_000,
        HistoricalDepthDeviationBasisPoints: 5_000,
        ExtremeRoiAbsoluteDeltaBasisPoints: 2_000,
        ExtremeRoiMultipleBasisPoints: 20_000,
        MinimumNearBestListings: 3,
        ExtremeRoiPenaltyPoints: 10m,
        ShallowBestLevelsPenaltyPoints: 10m,
        CurrentPriceCliffPenaltyPoints: 5m,
        HistoricalPriceDeviationPenaltyPoints: 5m,
        HistoricalDepthDeviationPenaltyPoints: 5m,
        ExcessivePositionPenaltyPoints: 15m);

    public void Validate()
    {
        var componentMaximums = new[]
        {
            ExpectedEconomicsMaximumPoints,
            CurrentLiquidityMaximumPoints,
            HistoricalPersistenceMaximumPoints,
            HistoricalStabilityMaximumPoints,
            HistoricalConfidenceMaximumPoints,
        };
        var penalties = new[]
        {
            ExtremeRoiPenaltyPoints,
            ShallowBestLevelsPenaltyPoints,
            CurrentPriceCliffPenaltyPoints,
            HistoricalPriceDeviationPenaltyPoints,
            HistoricalDepthDeviationPenaltyPoints,
            ExcessivePositionPenaltyPoints,
        };
        if (Version != CurrentVersion || componentMaximums.Any(value => value < 0m) ||
            componentMaximums.Sum() != MaximumTotalPoints ||
            penalties.Any(value => value is < 0m or > MaximumTotalPoints) ||
            MaximumAppliedPenaltyPoints is < 0m or > MaximumTotalPoints ||
            RoiNormalizationCeilingBasisPoints <= 0 || ProfitNormalizationCeilingCopper <= 0 ||
            VolatilityZeroQualityThreshold <= 0m ||
            HistoricalPriceDeviationBasisPoints is <= 0 or >= 10_000 ||
            HistoricalDepthDeviationBasisPoints is <= 0 or >= 10_000 ||
            ExtremeRoiAbsoluteDeltaBasisPoints < 0 || ExtremeRoiMultipleBasisPoints < 10_000 ||
            MinimumNearBestListings <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OpportunityScorePolicy));
        }
    }
}

public sealed record OpportunityScoreCandidate(
    LiveMarketScannerCandidate Current,
    HistoricalMarketAnalytics History);

public sealed record OpportunityScoreComponent(
    OpportunityScoreComponentName Name,
    OpportunityScoreComponentState State,
    decimal NormalizedPercent,
    decimal MaximumPoints,
    decimal AwardedPoints);

public sealed record OpportunityScoreAnomaly(
    OpportunityAnomalyFlag Flag,
    decimal PenaltyPoints);

public sealed record OpportunityScore(
    int Rank,
    int ItemId,
    int PolicyVersion,
    decimal TotalPoints,
    decimal BasePoints,
    decimal AppliedPenaltyPoints,
    OpportunityHistoricalConfidence HistoricalConfidence,
    OpportunityPersonalEvidenceState PersonalEvidenceState,
    IReadOnlyList<OpportunityScoreComponent> Components,
    IReadOnlyList<OpportunityScoreAnomaly> Anomalies);

public interface IOpportunityScoreService
{
    IReadOnlyList<OpportunityScore> Calculate(IReadOnlyCollection<OpportunityScoreCandidate> candidates);
}

/// <summary>
/// Pure, deterministic ranking over already-calculated current and historical
/// evidence. It performs no I/O, position sizing, or recommendation action.
/// </summary>
public sealed class OpportunityScoreService : IOpportunityScoreService
{
    private static readonly TimeSpan SevenDays = TimeSpan.FromDays(7);
    private static readonly TimeSpan ThirtyDays = TimeSpan.FromDays(30);
    private readonly OpportunityScorePolicy policy;

    public OpportunityScoreService(OpportunityScorePolicy? policy = null)
    {
        this.policy = policy ?? OpportunityScorePolicy.Default;
        this.policy.Validate();
    }

    public IReadOnlyList<OpportunityScore> Calculate(IReadOnlyCollection<OpportunityScoreCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Any(candidate => candidate is null))
        {
            throw new ArgumentException("Opportunity candidates must be non-null.", nameof(candidates));
        }

        foreach (var candidate in candidates)
        {
            Validate(candidate);
        }

        var duplicateItemId = candidates
            .GroupBy(candidate => candidate.Current.Item.ItemId)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateItemId is not null)
        {
            throw new ArgumentException($"Opportunity candidates contain duplicate item ID {duplicateItemId}.", nameof(candidates));
        }

        var calculated = candidates.Select(CalculateOne).ToArray();
        Array.Sort(calculated, static (left, right) =>
        {
            var score = right.Score.TotalPoints.CompareTo(left.Score.TotalPoints);
            if (score != 0) return score;
            var profit = right.ModeledProfitCopper.CompareTo(left.ModeledProfitCopper);
            return profit != 0 ? profit : left.Score.ItemId.CompareTo(right.Score.ItemId);
        });

        return calculated
            .Select((result, index) => result.Score with { Rank = index + 1 })
            .ToArray();
    }

    private CalculatedOpportunity CalculateOne(OpportunityScoreCandidate input)
    {
        var candidate = input.Current;
        var sevenDay = FindWindow(input.History, SevenDays);
        var thirtyDay = FindWindow(input.History, ThirtyDays);
        var baseline = AvailableSummary(thirtyDay) ?? AvailableSummary(sevenDay);
        var confidence = CalculateConfidence(sevenDay, thirtyDay);
        var components = new[]
        {
            Economics(candidate),
            Liquidity(candidate),
            Persistence(baseline),
            Stability(baseline),
            Confidence(sevenDay, thirtyDay),
            new OpportunityScoreComponent(
                OpportunityScoreComponentName.PersonalEvidence,
                OpportunityScoreComponentState.NotYetSupported,
                0m,
                0m,
                0m),
        };
        var anomalies = FindAnomalies(candidate, sevenDay, thirtyDay, baseline);
        var basePoints = Round(components.Sum(component => component.AwardedPoints));
        var appliedPenalty = Round(Math.Min(policy.MaximumAppliedPenaltyPoints, anomalies.Sum(anomaly => anomaly.PenaltyPoints)));
        var total = Round(Math.Clamp(basePoints - appliedPenalty, 0m, OpportunityScorePolicy.MaximumTotalPoints));

        return new CalculatedOpportunity(
            new OpportunityScore(
                Rank: 0,
                candidate.Item.ItemId,
                policy.Version,
                total,
                basePoints,
                appliedPenalty,
                confidence,
                OpportunityPersonalEvidenceState.NotYetAvailable,
                components,
                anomalies),
            candidate.ProfitScenario.NetProfit.Copper);
    }

    private OpportunityScoreComponent Economics(LiveMarketScannerCandidate candidate)
    {
        var roiBasisPoints = candidate.ModeledRoi.Profit.Copper * 10_000m / candidate.ModeledRoi.TotalCost.Copper;
        var roi = Normalize(roiBasisPoints, policy.RoiNormalizationCeilingBasisPoints);
        var profit = Normalize(candidate.ProfitScenario.NetProfit.Copper, policy.ProfitNormalizationCeilingCopper);
        return Component(
            OpportunityScoreComponentName.ExpectedEconomics,
            OpportunityScoreComponentState.Available,
            (roi * 0.6m) + (profit * 0.4m),
            policy.ExpectedEconomicsMaximumPoints);
    }

    private OpportunityScoreComponent Liquidity(LiveMarketScannerCandidate candidate)
    {
        var evidence = candidate.Liquidity;
        var intendedQuantity = evidence.Acquisition.RequestedQuantity;
        var totalDepth = Normalize(
            Math.Min(evidence.TotalBuyQuantity, evidence.TotalSellQuantity),
            intendedQuantity * 10m);
        var nearBestDepth = Normalize(
            Math.Min(evidence.NearBestBuyQuantity, evidence.NearBestSellQuantity),
            intendedQuantity);
        var nearBestListings = Normalize(
            Math.Min(evidence.NearBestBuyListings, evidence.NearBestSellListings),
            policy.MinimumNearBestListings);
        return Component(
            OpportunityScoreComponentName.CurrentLiquidity,
            OpportunityScoreComponentState.Available,
            (totalDepth * 0.4m) + (nearBestDepth * 0.4m) + (nearBestListings * 0.2m),
            policy.CurrentLiquidityMaximumPoints);
    }

    private OpportunityScoreComponent Persistence(HistoricalMarketMetricSummary? baseline)
    {
        if (baseline is null)
        {
            return Component(
                OpportunityScoreComponentName.HistoricalPersistence,
                OpportunityScoreComponentState.InsufficientData,
                0m,
                policy.HistoricalPersistenceMaximumPoints);
        }

        var thresholdRate = baseline.RoiThresholdRates.Count == 0
            ? 0m
            : baseline.RoiThresholdRates.Average(rate => ClampPercent(rate.Percent));
        return Component(
            OpportunityScoreComponentName.HistoricalPersistence,
            OpportunityScoreComponentState.Available,
            (ClampPercent(baseline.PositiveNetRoiPercent) + thresholdRate) / 2m,
            policy.HistoricalPersistenceMaximumPoints);
    }

    private OpportunityScoreComponent Stability(HistoricalMarketMetricSummary? baseline)
    {
        if (baseline is null)
        {
            return Component(
                OpportunityScoreComponentName.HistoricalStability,
                OpportunityScoreComponentState.InsufficientData,
                0m,
                policy.HistoricalStabilityMaximumPoints);
        }

        var qualities = new[]
        {
            StabilityQuality(baseline.BuyPricePopulationCoefficientOfVariation),
            StabilityQuality(baseline.SellPricePopulationCoefficientOfVariation),
            StabilityQuality(baseline.SpreadRatioPopulationCoefficientOfVariation),
            StabilityQuality(baseline.MinimumSideDepthPopulationCoefficientOfVariation),
        };
        return Component(
            OpportunityScoreComponentName.HistoricalStability,
            OpportunityScoreComponentState.Available,
            qualities.Average(),
            policy.HistoricalStabilityMaximumPoints);
    }

    private OpportunityScoreComponent Confidence(
        HistoricalMarketWindowAnalytics sevenDay,
        HistoricalMarketWindowAnalytics thirtyDay)
    {
        var normalized = 0m;
        if (IsAvailable(sevenDay)) normalized += 100m / 3m;
        if (IsAvailable(thirtyDay)) normalized += 200m / 3m;
        return Component(
            OpportunityScoreComponentName.HistoricalConfidence,
            normalized == 0m ? OpportunityScoreComponentState.InsufficientData : OpportunityScoreComponentState.Available,
            normalized,
            policy.HistoricalConfidenceMaximumPoints);
    }

    private IReadOnlyList<OpportunityScoreAnomaly> FindAnomalies(
        LiveMarketScannerCandidate candidate,
        HistoricalMarketWindowAnalytics sevenDay,
        HistoricalMarketWindowAnalytics thirtyDay,
        HistoricalMarketMetricSummary? baseline)
    {
        var anomalies = new List<OpportunityScoreAnomaly>();
        var currentRoi = candidate.ModeledRoi.Profit.Copper * 10_000m / candidate.ModeledRoi.TotalCost.Copper;
        var liquidity = candidate.Liquidity;
        var intendedQuantity = liquidity.Acquisition.RequestedQuantity;

        if (baseline is not null &&
            currentRoi >= baseline.MedianNetRoiBasisPoints + policy.ExtremeRoiAbsoluteDeltaBasisPoints &&
            currentRoi * 10_000m >= Math.Max(0m, baseline.MedianNetRoiBasisPoints) * policy.ExtremeRoiMultipleBasisPoints)
        {
            anomalies.Add(new(OpportunityAnomalyFlag.ExtremeCurrentRoiVersusHistory, policy.ExtremeRoiPenaltyPoints));
        }

        if (Math.Min(liquidity.NearBestBuyQuantity, liquidity.NearBestSellQuantity) < intendedQuantity ||
            Math.Min(liquidity.NearBestBuyListings, liquidity.NearBestSellListings) < policy.MinimumNearBestListings)
        {
            anomalies.Add(new(OpportunityAnomalyFlag.ShallowBestLevels, policy.ShallowBestLevelsPenaltyPoints));
        }

        if (liquidity.HasBuyPriceCliff || liquidity.HasSellPriceCliff)
        {
            anomalies.Add(new(OpportunityAnomalyFlag.CurrentPriceCliff, policy.CurrentPriceCliffPenaltyPoints));
        }

        if (baseline is not null)
        {
            if (AboveRange(candidate.BestBuy.UnitPriceInCopper, baseline.BuyPriceRange.MaximumCopper) ||
                AboveRange(candidate.LowestSell.UnitPriceInCopper, baseline.SellPriceRange.MaximumCopper))
            {
                anomalies.Add(new(OpportunityAnomalyFlag.AbruptPriceSpike, policy.HistoricalPriceDeviationPenaltyPoints));
            }
            if (BelowRange(candidate.BestBuy.UnitPriceInCopper, baseline.BuyPriceRange.MinimumCopper) ||
                BelowRange(candidate.LowestSell.UnitPriceInCopper, baseline.SellPriceRange.MinimumCopper))
            {
                anomalies.Add(new(OpportunityAnomalyFlag.AbruptPriceDrop, policy.HistoricalPriceDeviationPenaltyPoints));
            }
            if (DepthOutsideRange(candidate.BestBuy.Quantity, baseline.MedianAggregateBuyQuantity) ||
                DepthOutsideRange(candidate.LowestSell.Quantity, baseline.MedianAggregateSellQuantity))
            {
                anomalies.Add(new(OpportunityAnomalyFlag.AbruptDepthChange, policy.HistoricalDepthDeviationPenaltyPoints));
            }
        }

        if (!IsAvailable(sevenDay) || !IsAvailable(thirtyDay))
        {
            anomalies.Add(new(OpportunityAnomalyFlag.InsufficientHistoricalCoverage, 0m));
        }
        if (intendedQuantity > liquidity.ParticipationCapQuantity)
        {
            anomalies.Add(new(OpportunityAnomalyFlag.IntendedPositionExceedsVisibleDepth, policy.ExcessivePositionPenaltyPoints));
        }

        return anomalies.OrderBy(anomaly => anomaly.Flag).ToArray();
    }

    private bool AboveRange(long current, long historicalMaximum) =>
        current * 10_000m > historicalMaximum * (10_000m + policy.HistoricalPriceDeviationBasisPoints);

    private bool BelowRange(long current, long historicalMinimum) =>
        current * 10_000m < historicalMinimum * (10_000m - policy.HistoricalPriceDeviationBasisPoints);

    private bool DepthOutsideRange(long current, decimal historicalMedian)
    {
        var deviation = policy.HistoricalDepthDeviationBasisPoints;
        return current * 10_000m < historicalMedian * (10_000m - deviation) ||
            current * 10_000m > historicalMedian * (10_000m + deviation);
    }

    private static OpportunityHistoricalConfidence CalculateConfidence(
        HistoricalMarketWindowAnalytics sevenDay,
        HistoricalMarketWindowAnalytics thirtyDay)
    {
        var availableCount = (IsAvailable(sevenDay) ? 1 : 0) + (IsAvailable(thirtyDay) ? 1 : 0);
        return availableCount switch
        {
            0 => OpportunityHistoricalConfidence.Insufficient,
            1 => OpportunityHistoricalConfidence.Partial,
            _ => OpportunityHistoricalConfidence.Strong,
        };
    }

    private static HistoricalMarketWindowAnalytics FindWindow(HistoricalMarketAnalytics history, TimeSpan duration)
    {
        var matches = history.Windows.Where(window => window.Coverage.ToInclusiveUtc - window.Coverage.FromInclusiveUtc == duration).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new ArgumentException($"Historical evidence must contain exactly one {duration.TotalDays}-day window.");
    }

    private static HistoricalMarketMetricSummary? AvailableSummary(HistoricalMarketWindowAnalytics window) =>
        IsAvailable(window) ? window.Metrics : null;

    private static bool IsAvailable(HistoricalMarketWindowAnalytics window) =>
        window.State == HistoricalMarketWindowState.Available && window.Metrics is not null;

    private decimal StabilityQuality(double? coefficient)
    {
        if (coefficient is null) return 0m;
        if (!double.IsFinite(coefficient.Value) || coefficient.Value < 0d)
        {
            throw new ArgumentException("Historical volatility must be finite and non-negative.");
        }
        return 100m - Normalize(Convert.ToDecimal(coefficient.Value), policy.VolatilityZeroQualityThreshold);
    }

    private static OpportunityScoreComponent Component(
        OpportunityScoreComponentName name,
        OpportunityScoreComponentState state,
        decimal normalizedPercent,
        decimal maximumPoints)
    {
        var normalized = Round(ClampPercent(normalizedPercent));
        return new(name, state, normalized, maximumPoints, Round(maximumPoints * normalized / 100m));
    }

    private static decimal Normalize(decimal value, decimal ceiling) => ClampPercent(value * 100m / ceiling);
    private static decimal ClampPercent(decimal value) => Math.Clamp(value, 0m, 100m);
    private static decimal Round(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static void Validate(OpportunityScoreCandidate input)
    {
        ArgumentNullException.ThrowIfNull(input.Current);
        ArgumentNullException.ThrowIfNull(input.History);
        var current = input.Current;
        var liquidity = current.Liquidity;
        if (current.Item is null || current.Item.ItemId <= 0 || input.History.ItemId != current.Item.ItemId ||
            current.BestBuy.Quantity <= 0 || current.BestBuy.UnitPriceInCopper <= 0 ||
            current.LowestSell.Quantity <= 0 || current.LowestSell.UnitPriceInCopper <= 0 ||
            current.ProfitScenario.NetProfit.Copper <= 0 || current.TotalCost.Copper <= 0 ||
            current.ProfitScenario.AcquisitionCost != current.PlannedBid ||
            current.ProfitScenario.GrossSaleValue != current.PlannedListPrice ||
            current.ProfitScenario.ListingFee.Copper < 0 || current.ProfitScenario.ExchangeFee.Copper < 0 ||
            (decimal)current.ProfitScenario.NetSaleProceeds.Copper != current.ProfitScenario.GrossSaleValue.Copper -
                (decimal)current.ProfitScenario.ListingFee.Copper - current.ProfitScenario.ExchangeFee.Copper ||
            (decimal)current.ProfitScenario.NetProfit.Copper != current.ProfitScenario.NetSaleProceeds.Copper -
                (decimal)current.ProfitScenario.AcquisitionCost.Copper ||
            (decimal)current.TotalCost.Copper != current.ProfitScenario.AcquisitionCost.Copper +
                (decimal)current.ProfitScenario.ListingFee.Copper ||
            current.ModeledRoi.Profit != current.ProfitScenario.NetProfit || current.ModeledRoi.TotalCost != current.TotalCost ||
            liquidity is null || liquidity.TotalBuyQuantity < 0 || liquidity.TotalSellQuantity < 0 ||
            liquidity.NearBestBuyQuantity < 0 || liquidity.NearBestSellQuantity < 0 ||
            liquidity.NearBestBuyQuantity > liquidity.TotalBuyQuantity ||
            liquidity.NearBestSellQuantity > liquidity.TotalSellQuantity ||
            liquidity.NearBestBuyListings < 0 || liquidity.NearBestSellListings < 0 ||
            liquidity.ParticipationCapQuantity < 0 || liquidity.Acquisition.RequestedQuantity <= 0 ||
            liquidity.Acquisition.RequestedQuantity != liquidity.Liquidation.RequestedQuantity ||
            liquidity.ParticipationCapQuantity > Math.Min(liquidity.TotalBuyQuantity, liquidity.TotalSellQuantity) / 10 ||
            !IsConsistentExecution(liquidity.Acquisition, liquidity.TotalSellQuantity) ||
            !IsConsistentExecution(liquidity.Liquidation, liquidity.TotalBuyQuantity) ||
            input.History.AsOfUtc.Offset != TimeSpan.Zero || input.History.Windows is null ||
            input.History.Windows.Any(window => window is null ||
                window.Coverage.FromInclusiveUtc.Offset != TimeSpan.Zero ||
                window.Coverage.ToInclusiveUtc.Offset != TimeSpan.Zero ||
                window.Coverage.FromInclusiveUtc > window.Coverage.ToInclusiveUtc ||
                (window.State == HistoricalMarketWindowState.Available) != (window.Metrics is not null)))
        {
            throw new ArgumentException("Opportunity evidence is inconsistent or outside supported bounds.", nameof(input));
        }
    }

    private static bool IsConsistentExecution(OrderBookExecutionScenario scenario, long visibleSideQuantity) =>
        scenario.RequestedQuantity > 0 && scenario.FilledQuantity >= 0 &&
        scenario.FilledQuantity <= scenario.RequestedQuantity &&
        scenario.FilledQuantity <= visibleSideQuantity &&
        scenario.RemainingQuantity == scenario.RequestedQuantity - scenario.FilledQuantity &&
        scenario.IsFullyFilled == (scenario.RemainingQuantity == 0);

    private sealed record CalculatedOpportunity(OpportunityScore Score, long ModeledProfitCopper);
}
