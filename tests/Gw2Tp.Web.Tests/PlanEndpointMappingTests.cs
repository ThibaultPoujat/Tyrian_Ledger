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

        Assert.True(store.TryPublishAndObserve(snapshot, store.BeginLoopRun(), static () => { }));

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

        Assert.True(store.TryPublishAndObserve(snapshot, store.BeginLoopRun(), static () => { }));
        clock = now.AddMinutes(3);
        Assert.False(store.TryGet("scope-a", out _));

        clock = now;
        Assert.True(store.TryPublishAndObserve(snapshot, store.BeginLoopRun(), static () => { }));
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
        Assert.True(store.TryPublishAndObserve(snapshot, 1, static () => { }));

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
        Assert.False(store.TryPublishAndObserve(snapshot, oldGeneration, static () => throw new InvalidOperationException("A stale loop must not observe notifications.")));
        store.CompleteLoopRun(oldGeneration);

        Assert.False(store.TryGet("scope-a", out _));
    }

    [Fact]
    public async Task A_plan_mutation_cannot_interleave_between_projection_publication_and_notification_observation()
    {
        var now = DateTimeOffset.UtcNow;
        var profile = new AccountProfile(1, "scope-a", now, now);
        var portfolio = new AccountPortfolioSnapshot(new AccountScope("scope-a"), new Money(100), CapturedAtUtc: now);
        var snapshot = new PlanDecisionSnapshot(profile, portfolio, new Dictionary<string, long>(), RecommendationResult(now), [], [], true, new PlanDecisionTiming(), now);
        var store = new PlanDecisionProjectionStore(new DecisionLoopSchedulerSettings(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(15)), () => now);
        var generation = store.BeginLoopRun();
        var observationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowObservationToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invalidationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var publish = Task.Run(() => store.TryPublishAndObserve(snapshot, generation, () =>
        {
            observationStarted.TrySetResult(true);
            allowObservationToFinish.Task.GetAwaiter().GetResult();
        }));
        await observationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var invalidate = Task.Run(() =>
        {
            invalidationStarted.TrySetResult(true);
            store.Invalidate();
        });
        await invalidationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(invalidate.IsCompleted);
        allowObservationToFinish.TrySetResult(true);
        Assert.True(await publish.WaitAsync(TimeSpan.FromSeconds(2)));
        await invalidate.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(store.TryGet("scope-a", out _));
    }

    [Fact]
    public async Task Loop_notification_candidates_require_the_same_verified_inventory_as_plan_selection()
    {
        var now = DateTimeOffset.UtcNow;
        var profile = new AccountProfile(1, "scope-a", now, now);
        var portfolio = new AccountPortfolioSnapshot(new AccountScope("scope-a"), new Money(100), CapturedAtUtc: now);
        var candidate = new PlanCandidate(
            "recommendation:Sell:Inventory:42", 1, "recommendation:Sell:Inventory:42", PlanAttention.Active,
            [new PlanStep("sell", PlanStepAction.SellNow, 42, "Objet", 2, new Money(25), [], PlanStepState.Pending)],
            [new PlanResourceRequirement(PlanResourceKind.Inventory, "42", 2, Money.Zero)],
            new Money(10), Money.Zero, 10_000, 0, 600, 1, true, []);
        var service = new PlanEndpointService(null!, null!, null!, null!, null!, null!, null!, new PlanOrchestrationService(), null!);
        var insufficient = new PlanDecisionSnapshot(profile, portfolio, new Dictionary<string, long> { ["2:42"] = 1 }, RecommendationResult(now), [], [candidate], true, new PlanDecisionTiming(), now);
        var sufficient = insufficient with { VerifiedQuantities = new Dictionary<string, long> { ["2:42"] = 2 } };

        Assert.Empty(await service.GetExecutableSignalCandidateIdsAsync(insufficient, CancellationToken.None));
        Assert.Contains(candidate.Id, await service.GetExecutableSignalCandidateIdsAsync(sufficient, CancellationToken.None));
    }

    [Fact]
    public async Task Loop_notification_candidates_exclude_generic_resource_conflicts_and_negative_utility()
    {
        var now = DateTimeOffset.UtcNow;
        var profile = new AccountProfile(1, "scope-a", now, now);
        var portfolio = new AccountPortfolioSnapshot(new AccountScope("scope-a"), new Money(100), CapturedAtUtc: now);
        var genericConflict = Candidate("recommendation:Sell:Inventory:42", new PlanResourceRequirement(PlanResourceKind.ExpectedIncoming, "42", 1, Money.Zero), utility: 1);
        var negativeUtility = Candidate("recommendation:Sell:Inventory:43", new PlanResourceRequirement(PlanResourceKind.Inventory, "43", 1, Money.Zero), utility: -1);
        var existingPlan = new PlanRecord("existing", 1, "existing", PlanAttention.Passive, PlanState.InProgress, PlanReconciliationState.None, now,
            [new PlanResourceRequirement(PlanResourceKind.ExpectedIncoming, "42", 1, Money.Zero)], Money.Zero, 0,
            [new PlanStep("buy", PlanStepAction.PlaceBuyOrder, 42, "Objet", 1, new Money(1), [], PlanStepState.Current)], [], 0, PlanHysteresisPolicy.Default);
        var service = new PlanEndpointService(null!, null!, null!, null!, null!, null!, null!, new PlanOrchestrationService(), null!);
        var decision = new PlanDecisionSnapshot(profile, portfolio, new Dictionary<string, long> { ["2:43"] = 1 }, RecommendationResult(now), [existingPlan], [genericConflict, negativeUtility], true, new PlanDecisionTiming(), now);

        Assert.Empty(await service.GetExecutableSignalCandidateIdsAsync(decision, CancellationToken.None));
    }

    private static PlanCandidate Candidate(string id, PlanResourceRequirement requirement, long utility) => new(
        id, 1, id, PlanAttention.Active,
        [new PlanStep($"{id}:step", PlanStepAction.SellNow, 42, "Objet", 1, new Money(25), [], PlanStepState.Pending)],
        [requirement], new Money(1), Money.Zero, 10_000, 0, 600, utility, true, []);

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
