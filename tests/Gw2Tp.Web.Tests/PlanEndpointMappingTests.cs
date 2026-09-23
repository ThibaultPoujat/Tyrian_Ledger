using Gw2Tp.Application.Recommendations;
using Gw2Tp.Application.Plans;
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

    private static PrimaryRecommendationRecord Recommendation(PrimaryRecommendationAction action, PrimaryRecommendationSource source, string? orderId) =>
        new(action, source, PrimaryRecommendationOrderState.NotApplicable, orderId, 42, "Objet", 2, new(50),
            new(new(10), new(20), new(30), new(25), new(35), new(40), new(new(31), new(32))),
            null, null, null, null, [], []);
}
