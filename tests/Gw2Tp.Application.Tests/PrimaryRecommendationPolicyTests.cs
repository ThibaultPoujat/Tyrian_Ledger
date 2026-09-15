using Gw2Tp.Application.MarketScanning;
using Gw2Tp.Application.Recommendations;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class PrimaryRecommendationPolicyTests
{
    [Theory]
    [MemberData(nameof(AllActions))]
    public void Graduated_policy_covers_every_supported_action(
        PrimaryRecommendationEvidence evidence, PrimaryRecommendationAction expected)
    {
        var action = Assert.Single(new PrimaryRecommendationPolicy().Evaluate([evidence]));
        Assert.Equal(expected, action.Action);
        Assert.NotEmpty(action.Reasons);
        Assert.Equal(PrimaryRecommendationReasonCode.ReadOnlyManualAction, action.Reasons[^1].Code);
    }

    public static IEnumerable<object[]> AllActions()
    {
        yield return [NewOpportunity(), PrimaryRecommendationAction.Buy];
        yield return [NewOpportunity(history: History(OpportunityHistoricalConfidence.Partial)), PrimaryRecommendationAction.BuySmall];
        yield return [NewOpportunity(history: History(OpportunityHistoricalConfidence.Insufficient)), PrimaryRecommendationAction.Wait];
        yield return [BuyOrder(PrimaryRecommendationOrderState.Competitive), PrimaryRecommendationAction.KeepBid];
        yield return [BuyOrder(PrimaryRecommendationOrderState.Outbid, 10, 10), PrimaryRecommendationAction.UpdateBid];
        yield return [BuyOrder(PrimaryRecommendationOrderState.Outbid, 11, 10), PrimaryRecommendationAction.StopBidding];
        yield return [BuyOrder(PrimaryRecommendationOrderState.AboveMaximumBid), PrimaryRecommendationAction.CancelBid];
        yield return [Inventory(listingPositive: true), PrimaryRecommendationAction.List];
        yield return [SellListing(PrimaryRecommendationOrderState.Competitive), PrimaryRecommendationAction.LeaveSellListing];
        yield return [Inventory(), PrimaryRecommendationAction.Hold];
        yield return [Inventory(exposureExceeded: true, suggestedQuantity: 1), PrimaryRecommendationAction.Reduce];
        yield return [Inventory(partialPositive: true, suggestedQuantity: 1), PrimaryRecommendationAction.SellPartial];
        yield return [Inventory(fullPositive: true, suggestedQuantity: 2), PrimaryRecommendationAction.Sell];
        yield return [NewOpportunity(score: Score(0m)), PrimaryRecommendationAction.Skip];
        yield return [NewOpportunity(complete: false), PrimaryRecommendationAction.Review];
    }

    [Fact]
    public void Bid_at_maximum_can_update_but_one_copper_above_must_cancel()
    {
        var policy = new PrimaryRecommendationPolicy();
        var atMaximum = BuyOrder(PrimaryRecommendationOrderState.Outbid, 10, 10);
        var above = atMaximum with { OrderState = PrimaryRecommendationOrderState.AboveMaximumBid, Prices = atMaximum.Prices with { CurrentOrderUnitPrice = new Money(111) } };
        Assert.Equal(PrimaryRecommendationAction.UpdateBid, Assert.Single(policy.Evaluate([atMaximum])).Action);
        Assert.Equal(PrimaryRecommendationAction.CancelBid, Assert.Single(policy.Evaluate([above])).Action);
    }

    [Fact]
    public void Competitive_bid_does_not_require_shortlist_score_but_outbid_update_does()
    {
        var policy = new PrimaryRecommendationPolicy();
        Assert.Equal(PrimaryRecommendationAction.KeepBid,
            Assert.Single(policy.Evaluate([BuyOrder(PrimaryRecommendationOrderState.Competitive) with { Score = null }])).Action);
        Assert.Equal(PrimaryRecommendationAction.Review,
            Assert.Single(policy.Evaluate([BuyOrder(PrimaryRecommendationOrderState.Outbid, 10, 10) with { Score = null }])).Action);
    }

    [Fact]
    public void One_copper_undercut_is_protected_but_larger_undercut_requires_review()
    {
        var policy = new PrimaryRecommendationPolicy();
        Assert.Equal(PrimaryRecommendationAction.LeaveSellListing,
            Assert.Single(policy.Evaluate([SellListing(PrimaryRecommendationOrderState.UndercutOneCopper)])).Action);
        Assert.Equal(PrimaryRecommendationAction.Review,
            Assert.Single(policy.Evaluate([SellListing(PrimaryRecommendationOrderState.UndercutMoreThanOneCopper)])).Action);
    }

    [Fact]
    public void Unknown_basis_and_hard_anomalies_fail_safe()
    {
        var anomaly = NewOpportunity(score: Score(80m,
            [new OpportunityScoreAnomaly(OpportunityAnomalyFlag.AbruptPriceSpike, 5m)]));
        Assert.Equal(PrimaryRecommendationAction.Review,
            Assert.Single(new PrimaryRecommendationPolicy().Evaluate([Inventory(unknownBasis: true)])).Action);
        Assert.Equal(PrimaryRecommendationAction.Skip,
            Assert.Single(new PrimaryRecommendationPolicy().Evaluate([anomaly])).Action);
    }

    [Fact]
    public void Soft_anomaly_reduces_position_and_explains_penalty()
    {
        var action = Assert.Single(new PrimaryRecommendationPolicy().Evaluate([
            NewOpportunity(score: Score(80m, [new OpportunityScoreAnomaly(OpportunityAnomalyFlag.ShallowBestLevels, 5m)])),
        ]));
        Assert.Equal(PrimaryRecommendationAction.BuySmall, action.Action);
        Assert.Contains(action.Reasons, reason => reason.Code == PrimaryRecommendationReasonCode.PenalizedEvidence);
        Assert.DoesNotContain(action.Reasons, reason => reason.Code == PrimaryRecommendationReasonCode.LiquidityRisk);
    }

    [Fact]
    public void Buy_small_preserves_the_pre_sized_reduced_quantity_and_exact_total_economics()
    {
        var fullEconomics = new PrimaryRecommendationEconomicsCalculator().CalculateUnitPrices(
            new Money(110), new Money(199), 10);
        var smallEconomics = new PrimaryRecommendationEconomicsCalculator().CalculateUnitPrices(
            new Money(110), new Money(199), 5);
        var fullEvidence = NewOpportunity() with
        {
            SuggestedQuantity = 10,
            SuggestedCapital = fullEconomics.TotalCost,
            Economics = fullEconomics,
        };
        var smallEvidence = fullEvidence with
        {
            History = History(OpportunityHistoricalConfidence.Partial),
            SuggestedQuantity = 5,
            SuggestedCapital = smallEconomics.TotalCost,
            Economics = smallEconomics,
        };
        var policy = new PrimaryRecommendationPolicy();

        var full = Assert.Single(policy.Evaluate([fullEvidence]));
        var small = Assert.Single(policy.Evaluate([smallEvidence]));

        Assert.Equal(PrimaryRecommendationAction.Buy, full.Action);
        Assert.Equal(10, full.Quantity);
        Assert.Equal(PrimaryRecommendationAction.BuySmall, small.Action);
        Assert.Equal(5, small.Quantity);
        Assert.Equal(600, small.Capital.Copper);
        Assert.Equal(600, small.Economics!.TotalCost.Copper);
        Assert.Equal(995, small.Economics.GrossSaleValue.Copper);
        Assert.Equal(50, small.Economics.ListingFee.Copper);
        Assert.Equal(100, small.Economics.ExchangeFee.Copper);
    }

    [Fact]
    public void Competitive_bid_with_a_binding_portfolio_cap_requires_review()
    {
        var evidence = BuyOrder(PrimaryRecommendationOrderState.Competitive) with
        {
            IsExposureExceeded = true,
            PortfolioConstraints =
            [
                new PositionSizingConstraint(PositionSizingConstraintName.ItemExposure, new Money(500), 5, true),
                new PositionSizingConstraint(PositionSizingConstraintName.StrategyConcentration, new Money(2_000), 20, true),
                new PositionSizingConstraint(PositionSizingConstraintName.CategoryConcentration, new Money(2_500), 25, true),
            ],
        };

        var action = Assert.Single(new PrimaryRecommendationPolicy().Evaluate([evidence]));

        Assert.Equal(PrimaryRecommendationAction.Review, action.Action);
        Assert.Contains(action.Reasons, reason => reason.Code == PrimaryRecommendationReasonCode.ItemExposureExceeded);
        Assert.Contains(action.Reasons, reason => reason.Code == PrimaryRecommendationReasonCode.StrategyExposureExceeded);
        Assert.Contains(action.Reasons, reason => reason.Code == PrimaryRecommendationReasonCode.CategoryExposureExceeded);
    }

    [Fact]
    public void Exposure_breach_without_safe_reduction_depth_requires_review()
    {
        var evidence = Inventory(exposureExceeded: true, suggestedQuantity: 0) with
        {
            Economics = null,
            SuggestedCapital = Money.Zero,
            PortfolioConstraints =
            [
                new PositionSizingConstraint(PositionSizingConstraintName.ItemExposure, new Money(500), 5, true),
                new PositionSizingConstraint(PositionSizingConstraintName.LiquidityParticipation, Money.Zero, 0, true),
            ],
        };

        var action = Assert.Single(new PrimaryRecommendationPolicy().Evaluate([evidence]));

        Assert.Equal(PrimaryRecommendationAction.Review, action.Action);
        Assert.Equal(0, action.Quantity);
        Assert.Equal(0, action.Capital.Copper);
        Assert.Null(action.Economics);
        Assert.Contains(action.Reasons, reason => reason.Code == PrimaryRecommendationReasonCode.ItemExposureExceeded);
        Assert.Contains(action.Reasons, reason => reason.Code == PrimaryRecommendationReasonCode.SafeDepthLimited);
    }

    [Fact]
    public void Known_zero_capacity_waits_and_noncritical_liquidity_risk_buys_small()
    {
        var noCapacity = NewOpportunity() with { SuggestedQuantity = 0 };
        var mediumLiquidity = NewOpportunity() with
        {
            Liquidity = Liquidity() with { Classification = PositionSizingLiquidity.Medium },
        };

        Assert.Equal(PrimaryRecommendationAction.Wait,
            Assert.Single(new PrimaryRecommendationPolicy().Evaluate([noCapacity])).Action);
        Assert.Equal(PrimaryRecommendationAction.BuySmall,
            Assert.Single(new PrimaryRecommendationPolicy().Evaluate([mediumLiquidity])).Action);
    }

    [Theory]
    [InlineData(OpportunityAnomalyFlag.ExtremeCurrentRoiVersusHistory)]
    [InlineData(OpportunityAnomalyFlag.AbruptPriceSpike)]
    [InlineData(OpportunityAnomalyFlag.AbruptPriceDrop)]
    [InlineData(OpportunityAnomalyFlag.AbruptDepthChange)]
    public void Every_hard_anomaly_skips_instead_of_recommending_a_purchase(OpportunityAnomalyFlag flag)
    {
        var evidence = NewOpportunity(score: Score(80m, [new OpportunityScoreAnomaly(flag, 5m)]));

        Assert.Equal(PrimaryRecommendationAction.Skip,
            Assert.Single(new PrimaryRecommendationPolicy().Evaluate([evidence])).Action);
    }

    [Fact]
    public void Liquidity_classification_is_conservative_and_deterministic()
    {
        Assert.Equal(PositionSizingLiquidity.High, PrimaryRecommendationService.ClassifyLiquidity([]));
        Assert.Equal(PositionSizingLiquidity.Medium, PrimaryRecommendationService.ClassifyLiquidity([
            LiveMarketScannerLiquidityReason.BuyPriceCliff,
            LiveMarketScannerLiquidityReason.SellPriceCliff,
        ]));
        Assert.Equal(PositionSizingLiquidity.Low, PrimaryRecommendationService.ClassifyLiquidity([
            LiveMarketScannerLiquidityReason.BuyPriceCliff,
            LiveMarketScannerLiquidityReason.ParticipationCapBelowIntendedQuantity,
        ]));
    }

    [Fact]
    public void Attention_actions_are_deterministically_ordered_and_policy_has_no_mutation_dependency()
    {
        var policy = new PrimaryRecommendationPolicy();
        var actions = policy.Evaluate([
            NewOpportunity(), Inventory(), BuyOrder(PrimaryRecommendationOrderState.AboveMaximumBid),
            SellListing(PrimaryRecommendationOrderState.UndercutMoreThanOneCopper),
        ]);
        Assert.Equal(
            [PrimaryRecommendationAction.CancelBid, PrimaryRecommendationAction.Review, PrimaryRecommendationAction.Buy, PrimaryRecommendationAction.Hold],
            actions.Select(action => action.Action));
        Assert.DoesNotContain(
            typeof(PrimaryRecommendationPolicy).GetConstructors().SelectMany(constructor => constructor.GetParameters()),
            parameter => parameter.ParameterType.Name.Contains("Gateway", StringComparison.Ordinal) || parameter.ParameterType.Name.Contains("Repository", StringComparison.Ordinal));
    }

    [Fact]
    public void Full_quantity_economics_apply_fees_to_total_and_detect_overflow()
    {
        var calculator = new PrimaryRecommendationEconomicsCalculator();
        var economics = calculator.CalculateUnitPrices(new Money(1), new Money(10), 10);
        Assert.Equal(10, economics.AcquisitionCost.Copper);
        Assert.Equal(100, economics.GrossSaleValue.Copper);
        Assert.Equal(5, economics.ListingFee.Copper);
        Assert.Equal(10, economics.ExchangeFee.Copper);
        Assert.Equal(75, economics.NetProfit.Copper);
        Assert.Equal(15, economics.TotalCost.Copper);
        Assert.Equal("500.00%", economics.RoiDisplayPercent);
        Assert.Throws<OverflowException>(() => calculator.CalculateUnitPrices(new Money(long.MaxValue), new Money(1), 2));
    }

    [Fact]
    public void Reserve_restoration_uses_existing_cancels_then_unscored_and_lowest_scored_bids()
    {
        var selected = ReserveRestorationSelector.Select([
            new ReserveRestorationBid(1, 1, new Money(30), true, 90m),
            new ReserveRestorationBid(2, 2, new Money(40), false, 10m),
            new ReserveRestorationBid(3, 3, new Money(30), false, null),
            new ReserveRestorationBid(4, 4, new Money(50), false, 5m),
        ], new Money(105));
        Assert.Equal([3L, 4L], selected.OrderBy(value => value));
    }

    private static PrimaryRecommendationEvidence NewOpportunity(
        PrimaryRecommendationScore? score = null, PrimaryRecommendationHistory? history = null, bool complete = true) =>
        Evidence(PrimaryRecommendationSource.NewOpportunity, PrimaryRecommendationOrderState.NotApplicable,
            score ?? Score(), history ?? History(OpportunityHistoricalConfidence.Strong), Liquidity(),
            complete, sizing: true, suggestedQuantity: 2);
    private static PrimaryRecommendationEvidence BuyOrder(PrimaryRecommendationOrderState state, long required = 0, long capacity = 0) =>
        Evidence(PrimaryRecommendationSource.BuyOrder, state, Score(), History(OpportunityHistoricalConfidence.Strong),
            Liquidity(), complete: true, sizing: true, suggestedQuantity: 2, incrementalRequired: required, incrementalCapacity: capacity);
    private static PrimaryRecommendationEvidence SellListing(PrimaryRecommendationOrderState state) =>
        Evidence(PrimaryRecommendationSource.SellListing, state, null, null, null, complete: true, suggestedQuantity: 2);
    private static PrimaryRecommendationEvidence Inventory(
        bool unknownBasis = false, bool exposureExceeded = false, bool fullPositive = false,
        bool partialPositive = false, bool listingPositive = false, int suggestedQuantity = 2) =>
        Evidence(PrimaryRecommendationSource.Inventory, PrimaryRecommendationOrderState.NotApplicable,
            Score(), History(OpportunityHistoricalConfidence.Strong), Liquidity(), complete: true,
            suggestedQuantity: suggestedQuantity, unknownBasis: unknownBasis, exposureExceeded: exposureExceeded,
            fullPositive: fullPositive, partialPositive: partialPositive, listingPositive: listingPositive);

    private static PrimaryRecommendationEvidence Evidence(
        PrimaryRecommendationSource source, PrimaryRecommendationOrderState state,
        PrimaryRecommendationScore? score, PrimaryRecommendationHistory? history,
        PrimaryRecommendationLiquidity? liquidity, bool complete, bool sizing = false,
        int suggestedQuantity = 0, bool unknownBasis = false, bool exposureExceeded = false,
        bool fullPositive = false, bool partialPositive = false, bool listingPositive = false,
        long incrementalRequired = 0, long incrementalCapacity = 0) => new(
            source, state, source is PrimaryRecommendationSource.BuyOrder or PrimaryRecommendationSource.SellListing ? "9001" : null,
            42, "Synthetic Item", 2, suggestedQuantity, new Money(200),
            new PrimaryRecommendationPriceState(
                source is PrimaryRecommendationSource.BuyOrder or PrimaryRecommendationSource.SellListing ? new Money(100) : null,
                new Money(100), new Money(200), new Money(110), new Money(199), new Money(110)),
            null, score, history, liquidity, [], complete, sizing, false, unknownBasis, exposureExceeded,
            fullPositive, partialPositive, listingPositive, new Money(incrementalRequired), new Money(incrementalCapacity));

    private static PrimaryRecommendationScore Score(decimal total = 80m, IReadOnlyList<OpportunityScoreAnomaly>? anomalies = null) =>
        new(1, total, total, 0m, [], anomalies ?? []);
    private static PrimaryRecommendationHistory History(OpportunityHistoricalConfidence confidence) =>
        new(confidence, new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.Zero), []);
    private static PrimaryRecommendationLiquidity Liquidity() =>
        new(PositionSizingLiquidity.High, 100, 100, 50, 50, 10, 10, []);
}
