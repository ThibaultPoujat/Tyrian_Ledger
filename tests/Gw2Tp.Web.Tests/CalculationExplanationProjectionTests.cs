using System.Text.Json;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.Plans;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Application.Recommendations;
using Gw2Tp.Domain.Finance;
using Gw2Tp.Web.Hosting;
using Xunit;

namespace Gw2Tp.Web.Tests;

public sealed class CalculationExplanationProjectionTests
{
    [Fact]
    public void Projection_uses_the_same_fee_and_sizing_values_without_exposing_account_or_order_identity()
    {
        var now = DateTimeOffset.UtcNow;
        var action = new PrimaryRecommendationRecord(
            PrimaryRecommendationAction.Buy,
            PrimaryRecommendationSource.BuyOrder,
            PrimaryRecommendationOrderState.Outbid,
            "private-order-identity",
            42,
            "Objet testé",
            2,
            new Money(220),
            new PrimaryRecommendationPriceState(new Money(100), new Money(100), new Money(150), new Money(110), new Money(150), new Money(115)),
            new PrimaryRecommendationEconomics(new Money(220), new Money(300), new Money(15), new Money(30), new Money(255), new Money(35), new Money(235), "14.89%"),
            null,
            null,
            null,
            [new(PositionSizingConstraintName.CashAfterReserve, new Money(200), 1, true)],
            [new(PrimaryRecommendationReasonCode.StrongEvidence, "Strong evidence")]);
        var recommendation = new PrimaryRecommendationResult(
            PrimaryRecommendationState.Ready, null, now, now, now, now,
            new PrimaryRecommendationPolicies(1, 1, 1, 1, 1, 1, 1, 1_500, "FastFlip", "TradingPost"),
            new PrimaryRecommendationPortfolio(new Money(1_000), new Money(2_000), new Money(300), CashReserveStatus.Satisfied, Money.Zero, new Money(780),
                [new PortfolioExposure("private-exposure", PortfolioExposureKind.CurrentBuyOrder, 42, "FastFlip", "TradingPost", new Money(1_000))]),
            [action], now.AddMinutes(5));
        var snapshot = new PlanDecisionSnapshot(
            new AccountProfile(1, "private-account-scope", now, now),
            new AccountPortfolioSnapshot(new AccountScope("private-account-scope"), new Money(1_000)),
            new Dictionary<string, long>(), recommendation, [], [], true, new PlanDecisionTiming(), now,
            new PlanDecisionSelectionTrace(14, 14, 14, 0, false, ["private-order-identity"], new HashSet<string>(StringComparer.Ordinal), null,
                new PlanDecisionResourceTrace(new Money(1_000), new Money(220), new Money(780), new Money(300),
                    [new PlanResourceRequirement(PlanResourceKind.Cash, "private-reservation", 0, new Money(220))],
                    new Dictionary<string, long> { ["2:42"] = 5 }),
                [new PlanDecisionCandidateTrace("recommendation:Buy:BuyOrder:private-order-identity", "negative_utility_empty_bundle", "negative_utility_empty_bundle")]));

        var json = JsonSerializer.Serialize(CalculationExplanationEndpoints.ToResponse(snapshot), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"listingFee\":{\"copper\":\"15\"}", json, StringComparison.Ordinal);
        Assert.Contains("\"exchangeFee\":{\"copper\":\"30\"}", json, StringComparison.Ordinal);
        Assert.Contains("\"generatedCandidates\":14", json, StringComparison.Ordinal);
        Assert.Contains("\"selectedCandidates\":0", json, StringComparison.Ordinal);
        Assert.Contains("\"purchaseOnly\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"fractionalCopperRoundingExternallyVerified\":false", json, StringComparison.Ordinal);
        Assert.Contains("\"effectiveCash\":{\"copper\":\"1000\"}", json, StringComparison.Ordinal);
        Assert.Contains("\"stage\":\"negative_utility_empty_bundle\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private-account-scope", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private-order-identity", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private-exposure", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_preserves_complete_and_incomplete_crafting_evidence_without_inventing_costs()
    {
        var now = DateTimeOffset.UtcNow;
        var complete = Opportunity("craft:complete", new CraftingEconomics(
            CraftingEconomicsState.Available, 70, 1,
            [new CraftingIngredientEconomics(71, 2, 1, 0, 0, 1, CraftingInputStrategy.Mixed, new Money(12),
                new CraftingExecutionEvidence(1, 1, new Money(12)), new Money(20),
                new CraftingAcquisitionSelection(CraftingAcquisitionStrategy.InstantBuy, 1, new Money(20), new CraftingExecutionEvidence(1, 1, new Money(20))), new Money(32), [],
                [CraftingAcquisitionAlternative.FromExecution(CraftingAcquisitionStrategy.BuyOrder, new CraftingExecutionEvidence(1, 1, new Money(19)))])],
            new Money(32), new CraftingOutputEconomics(new Money(100), new Money(5), new Money(10), new Money(85)),
            new Money(53), new Money(32), null, null, null, true, false, []));
        var incomplete = Opportunity("craft:incomplete", new CraftingEconomics(
            CraftingEconomicsState.Incomplete, 72, 1,
            [new CraftingIngredientEconomics(73, 1, 0, 0, 1, 0, CraftingInputStrategy.Indeterminate, null, null, null, null, null,
                [CraftingEconomicsUncertainty.UnknownOwnedInput, CraftingEconomicsUncertainty.AcquisitionPriceUnavailable])],
            null, null, null, null, null, null, null, false, false,
            [CraftingEconomicsUncertainty.UnknownOwnedInput, CraftingEconomicsUncertainty.AcquisitionPriceUnavailable]));
        var snapshot = new PlanDecisionSnapshot(new AccountProfile(1, "private", now, now),
            new AccountPortfolioSnapshot(new AccountScope("private"), new Money(100)), new Dictionary<string, long>(),
            PrimaryRecommendationResult.Unavailable(PrimaryRecommendationState.EvidenceUnavailable, "missing", new PrimaryRecommendationPolicies(1, 1, 1, 1, 1, 1, 1, 1, "x", "y")),
            [], [], true, new PlanDecisionTiming(), now,
            new PlanDecisionSelectionTrace(2, 2, 2, 1, false, ["craft:incomplete"], new HashSet<string>(StringComparer.Ordinal), null,
                Candidates: [new PlanDecisionCandidateTrace("craft:complete", "selected", "selected")]),
            new CraftingPlannerResult(CraftingOpportunityState.NoOpportunities, [complete, incomplete], [], [], "market_listings_unavailable", AccountEvidenceCapturedAtUtc: now));

        var json = JsonSerializer.Serialize(CalculationExplanationEndpoints.ToResponse(snapshot), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"ownedOpportunityCost\":{\"copper\":\"12\"}", json, StringComparison.Ordinal);
        Assert.Contains("\"totalCost\":{\"copper\":\"20\"}", json, StringComparison.Ordinal);
        Assert.Contains("\"UnknownOwnedInput\"", json, StringComparison.Ordinal);
        Assert.Contains("\"evidenceFailureCode\":\"market_listings_unavailable\"", json, StringComparison.Ordinal);
        Assert.Contains("\"observedAtUtc\":", json, StringComparison.Ordinal);
        Assert.Contains("\"crafting\":{\"state\":\"known\"", json, StringComparison.Ordinal);
        Assert.Contains("\"strategy\":\"BuyOrder\",\"quantity\":1,\"totalCost\":{\"copper\":\"19\"}", json, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"craft:complete\",\"isHardEligible\":true,\"utility\":1,\"modeledProfit\":{\"copper\":\"0\"},\"committedCapital\":{\"copper\":\"0\"},\"selected\":true", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"economicInputCost\":{\"copper\":\"0\"}", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_keeps_non_actionable_observations_without_mapping_them_to_plans()
    {
        var now = DateTimeOffset.UtcNow;
        var review = new PrimaryRecommendationRecord(
            PrimaryRecommendationAction.Review, PrimaryRecommendationSource.Inventory,
            PrimaryRecommendationOrderState.NotApplicable, null, 42, "Objet observé", 1, new Money(25),
            new PrimaryRecommendationPriceState(null, new Money(20), new Money(30), null, new Money(30), null),
            null, null, null, null, [], [new(PrimaryRecommendationReasonCode.InsufficientHistory, "not for browser")]);
        var recommendations = new PrimaryRecommendationResult(PrimaryRecommendationState.Ready, "buy_sizing_unavailable", now, now, now, now,
            new PrimaryRecommendationPolicies(1, 1, 1, 1, 1, 1, 1, 1_500, "FastFlip", "TradingPost"), null, [review], now.AddMinutes(5));
        var snapshot = new PlanDecisionSnapshot(new AccountProfile(1, "private", now, now),
            new AccountPortfolioSnapshot(new AccountScope("private"), new Money(100)), new Dictionary<string, long>(), recommendations,
            [], [], true, new PlanDecisionTiming(), now);

        var json = JsonSerializer.Serialize(CalculationExplanationEndpoints.ToResponse(snapshot), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"action\":\"Review\"", json, StringComparison.Ordinal);
        Assert.Contains("\"selection\":null", json, StringComparison.Ordinal);
    }

    private static CraftingOpportunity Opportunity(string id, CraftingEconomics economics) => new(
        id, new CraftingRecipe(1, economics.OutputItemId, economics.OutputQuantity, ["Artificer"], 1, [], [new("Item", 71, 1)]),
        "Objet", null, economics, new PlanCandidate(id, 1, id, PlanAttention.Active, [], [], Money.Zero, Money.Zero, 0, 0, 0, 1, true, []), [], [], false);
}
