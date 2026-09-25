using Gw2Tp.Application.Recommendations;
using Gw2Tp.Application.Plans;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Domain.Finance;
using Gw2Tp.Web.Hosting;
using Xunit;

namespace Gw2Tp.Web.Tests;

public sealed class PlanEndpointMappingTests
{
    [Fact]
    public void Buy_recommendation_remains_a_manual_buy_order_with_bid_price()
    {
        var candidate = PlanEndpointService.ToCandidate(Recommendation(PrimaryRecommendationAction.Buy, PrimaryRecommendationSource.NewOpportunity, null));

        var step = Assert.Single(candidate.Steps);
        Assert.Equal(PlanStepAction.PlaceBuyOrder, step.Action);
        Assert.Equal(25, step.UnitPrice?.Copper);
        Assert.Equal(PlanAttention.Passive, candidate.Attention);
    }

    [Fact]
    public void Update_bid_is_an_explicit_cancel_then_place_compound_path()
    {
        var candidate = PlanEndpointService.ToCandidate(Recommendation(PrimaryRecommendationAction.UpdateBid, PrimaryRecommendationSource.BuyOrder, "order-7"));

        Assert.Equal([PlanStepAction.CancelBuyOrder, PlanStepAction.PlaceBuyOrder], candidate.Steps.Select(step => step.Action));
        Assert.Equal(10, candidate.Steps[0].UnitPrice?.Copper);
        Assert.Equal(25, candidate.Steps[1].UnitPrice?.Copper);
        Assert.Equal(candidate.Steps[0].Id, Assert.Single(candidate.Steps[1].DependsOnStepIds));
    }

    [Fact]
    public void Sale_and_listing_use_action_specific_prices()
    {
        var listing = PlanEndpointService.ToCandidate(Recommendation(PrimaryRecommendationAction.List, PrimaryRecommendationSource.Inventory, null));
        var sale = PlanEndpointService.ToCandidate(Recommendation(PrimaryRecommendationAction.Sell, PrimaryRecommendationSource.Inventory, null));

        Assert.Equal(PlanStepAction.List, Assert.Single(listing.Steps).Action);
        Assert.Equal(35, listing.Steps[0].UnitPrice?.Copper);
        Assert.Equal(PlanStepAction.SellNow, Assert.Single(sale.Steps).Action);
        Assert.Equal(31, sale.Steps[0].UnitPrice?.Copper);
    }

    [Fact]
    public void Plan_decision_snapshots_are_transport_only_and_never_reused_as_market_truth()
    {
        var now = DateTimeOffset.UtcNow;
        var fresh = RecommendationResult(now);
        var profile = new AccountProfile(1, "scope-a", now, now);
        var snapshot = new PlanDecisionSnapshot(profile, fresh, [], [], true, now);

        Assert.Same(fresh, snapshot.Recommendations);
        Assert.Equal("scope-a", snapshot.Profile.AccountScopeId);
        // The endpoint rebuilds its recommendations on every /api/plans read,
        // so this record can never become a source of stale market evidence.
    }

    private static PrimaryRecommendationRecord Recommendation(PrimaryRecommendationAction action, PrimaryRecommendationSource source, string? orderId) =>
        new(action, source, PrimaryRecommendationOrderState.NotApplicable, orderId, 42, "Objet", 2, new(50),
            new(new(10), new(20), new(30), new(25), new(35), new(40), new(new(31), new(32))),
            null, null, null, null, [], []);

    private static PrimaryRecommendationResult RecommendationResult(DateTimeOffset now) => new(
        PrimaryRecommendationState.Ready,
        null,
        now,
        now,
        now,
        now,
        new PrimaryRecommendationPolicies(1, 1, 1, 1, 1, 1, 1, 1_500, "FastFlip", "TradingPost"),
        new PrimaryRecommendationPortfolio(new Money(100), new Money(100), new Money(15), CashReserveStatus.Satisfied, Money.Zero, new Money(85)),
        [],
        now.AddMinutes(10));
}
