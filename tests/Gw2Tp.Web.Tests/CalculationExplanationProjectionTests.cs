using System.Text.Json;
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
            new PrimaryRecommendationPortfolio(new Money(1_000), new Money(2_000), new Money(300), CashReserveStatus.Satisfied, Money.Zero, new Money(780)),
            [action], now.AddMinutes(5));
        var snapshot = new PlanDecisionSnapshot(
            new AccountProfile(1, "private-account-scope", now, now),
            new AccountPortfolioSnapshot(new AccountScope("private-account-scope"), new Money(1_000)),
            new Dictionary<string, long>(), recommendation, [], [], true, new PlanDecisionTiming(), now,
            new PlanDecisionSelectionTrace(14, 14, 14, 0, false, ["private-order-identity"], new HashSet<string>(StringComparer.Ordinal), null));

        var json = JsonSerializer.Serialize(CalculationExplanationEndpoints.ToResponse(snapshot), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"listingFee\":{\"copper\":\"15\"}", json, StringComparison.Ordinal);
        Assert.Contains("\"exchangeFee\":{\"copper\":\"30\"}", json, StringComparison.Ordinal);
        Assert.Contains("\"generatedCandidates\":14", json, StringComparison.Ordinal);
        Assert.Contains("\"selectedCandidates\":0", json, StringComparison.Ordinal);
        Assert.Contains("\"purchaseOnly\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"fractionalCopperRoundingExternallyVerified\":false", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private-account-scope", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private-order-identity", json, StringComparison.Ordinal);
    }
}
