using Gw2Tp.Application.Recommendations;
using Gw2Tp.Application.Plans;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Application.PersonalTradingPost;
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
    public void Loop_projection_is_reused_only_for_its_account_before_the_existing_freshness_bounds()
    {
        var now = DateTimeOffset.UtcNow;
        var fresh = RecommendationResult(now);
        var profile = new AccountProfile(1, "scope-a", now, now);
        var portfolio = new AccountPortfolioSnapshot(new AccountScope("scope-a"), new Money(100), CapturedAtUtc: now);
        var snapshot = new PlanDecisionSnapshot(profile, portfolio, new Dictionary<string, long>(), fresh, [], [], true, new PlanDecisionTiming(), now);
        var store = new PlanDecisionProjectionStore(
            new DecisionLoopSchedulerSettings(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(15)),
            () => now.AddMinutes(1));

        store.Publish(snapshot, store.BeginLoopRun());

        Assert.Same(fresh, snapshot.Recommendations);
        Assert.Equal("scope-a", snapshot.Profile.AccountScopeId);
        Assert.True(store.TryGet("scope-a", out var reused));
        Assert.Same(snapshot, reused);
        Assert.False(store.TryGet("scope-b", out _));
    }

    [Fact]
    public void Loop_projection_is_never_reused_after_account_evidence_or_cycle_expiry_or_mutation()
    {
        var now = DateTimeOffset.UtcNow;
        var profile = new AccountProfile(1, "scope-a", now, now);
        var portfolio = new AccountPortfolioSnapshot(new AccountScope("scope-a"), new Money(100), CapturedAtUtc: now);
        var fresh = RecommendationResult(now) with { AccountEvidenceExpiresAtUtc = now.AddMinutes(2) };
        var snapshot = new PlanDecisionSnapshot(profile, portfolio, new Dictionary<string, long>(), fresh, [], [], true, new PlanDecisionTiming(), now);
        var clock = now;
        var store = new PlanDecisionProjectionStore(
            new DecisionLoopSchedulerSettings(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(15)),
            () => clock);

        store.Publish(snapshot, store.BeginLoopRun());
        clock = now.AddMinutes(3);
        Assert.False(store.TryGet("scope-a", out _));

        clock = now;
        store.Publish(snapshot, store.BeginLoopRun());
        store.Invalidate();
        Assert.False(store.TryGet("scope-a", out _));
    }

    [Fact]
    public async Task Plan_reads_can_coalesce_with_an_in_flight_loop_without_becoming_a_completed_cache()
    {
        var now = DateTimeOffset.UtcNow;
        var profile = new AccountProfile(1, "scope-a", now, now);
        var portfolio = new AccountPortfolioSnapshot(new AccountScope("scope-a"), new Money(100), CapturedAtUtc: now);
        var snapshot = new PlanDecisionSnapshot(profile, portfolio, new Dictionary<string, long>(), RecommendationResult(now), [], [], true, new PlanDecisionTiming(), now);
        var store = new PlanDecisionProjectionStore(new DecisionLoopSchedulerSettings(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(15)), () => now);

        _ = store.BeginLoopRun();
        Assert.True(store.TryGetActive(out var active));
        store.Publish(snapshot, 1);

        Assert.Same(snapshot, await active!);
        store.CompleteLoopRun(1);
        Assert.True(store.TryGet("scope-a", out var reused));
        Assert.Same(snapshot, reused);
    }

    [Fact]
    public void A_plan_mutation_invalidates_the_active_generation_before_it_can_publish_an_old_decision()
    {
        var now = DateTimeOffset.UtcNow;
        var profile = new AccountProfile(1, "scope-a", now, now);
        var portfolio = new AccountPortfolioSnapshot(new AccountScope("scope-a"), new Money(100), CapturedAtUtc: now);
        var snapshot = new PlanDecisionSnapshot(profile, portfolio, new Dictionary<string, long>(), RecommendationResult(now), [], [], true, new PlanDecisionTiming(), now);
        var store = new PlanDecisionProjectionStore(new DecisionLoopSchedulerSettings(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(15)), () => now);

        var oldGeneration = store.BeginLoopRun();
        store.Invalidate(); // Simulates a successful Start, Complete, or Undo while that loop is still running.
        store.Publish(snapshot, oldGeneration);
        store.CompleteLoopRun(oldGeneration);

        Assert.False(store.TryGet("scope-a", out _));
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
