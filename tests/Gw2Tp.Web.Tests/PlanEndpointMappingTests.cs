using Gw2Tp.Application.Recommendations;
using Gw2Tp.Application.LocalData;
using Gw2Tp.Application.Plans;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Domain.Finance;
using Gw2Tp.Web.Hosting;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Xunit;
using Gw2Tp.Application.AccountEvidence;
using Gw2Tp.Testing;

namespace Gw2Tp.Web.Tests;

public sealed class PlanEndpointMappingTests
{
    [Fact]
    public async Task Portfolio_and_bank_cannot_admit_the_same_ten_units_twice()
    {
        var now = DateTimeOffset.UtcNow;
        var scope = new AccountScope("scope-a");
        var profile = new AccountProfile(1, scope.AccountId, now, now);
        var crafting = new AccountCraftingSnapshot(scope, now,
            CraftingFeatureResult<IReadOnlyList<AccountInventoryEntry>>.Available([new(42, 10, AccountItemBinding.Unspecified)]),
            CraftingFeatureResult<IReadOnlyList<AccountMaterialEntry>>.Available([]),
            CraftingFeatureResult<IReadOnlyList<int>>.Available([]),
            CraftingFeatureResult<IReadOnlyList<CraftingDisciplineCapability>>.Available([]));
        var plan = new PlanRecord("terminal", 1, "terminal", PlanAttention.Passive, PlanState.ExecutionComplete,
            PlanReconciliationState.Compatible, now, [], Money.Zero, -1, [], [], 0, PlanHysteresisPolicy.Default);
        var holdings = HoldingsEvidenceFixture.Snapshot(now, [HoldingsEvidenceFixture.Item(42, 10)]);
        holdings = holdings with { Capture = holdings.Capture with { AccountScope = scope },
            Rules = holdings.Rules with { AccountScope = scope }, ProtectionFloor = holdings.ProtectionFloor with { AccountScope = scope } };
        var service = new PlanEndpointService(new FixedRecommendations(RecommendationResult(now)),
            new FixedPortfolio(scope, new Money(100), now, new Dictionary<string, long> { ["2:42"] = 10 }),
            new FixedAccountScopeGateway(scope.AccountId), new FixedCraftingSnapshots(crafting), new EmptyCraftingOpportunities(),
            new FixedPersonalTradingPostRepository(profile, now), new CountingPlanRepository(plan), new PlanOrchestrationService(),
            new PlanDecisionProjectionStore(DecisionLoopSchedulerSettings.Default),
            holdings: new FixedHoldingsSnapshotService(holdings));
        var decision = Assert.IsType<PlanDecisionSnapshot>(await service.GetDecisionSnapshotAsync(CancellationToken.None));
        Assert.InRange(decision.VerifiedQuantities.GetValueOrDefault("2:42"), 0, 10);
        Assert.Equal(10, decision.VerifiedQuantities["2:42"]);
        Assert.Equal(PlanEvidenceCompleteness.Partial, decision.EvidenceFrame!.PhysicalInventory.Provenance.Completeness);
        Assert.Contains(decision.EvidenceFrame.PhysicalSources!, source => source.Source == AccountHoldingsSource.Bank);
    }

    [Fact]
    public async Task Applied_completion_invalidates_projection_even_when_browser_cancels_after_commit()
    {
        using var browser = new CancellationTokenSource();
        var now = DateTimeOffset.UtcNow;
        var profile = new AccountProfile(1, "A", now, now);
        var projections = new PlanDecisionProjectionStore(DecisionLoopSchedulerSettings.Default);
        var scopes = new AccountViewScopeTokenService();
        var fence = new CancellationCheckingFence();
        var completion = new AppliedThenCancelledCompletion(browser);
        var service = new PlanEndpointService(null!, null!, new FixedAccountScopeGateway("A"), null!, null!,
            new FixedPersonalTradingPostRepository(profile, now), null!, new PlanOrchestrationService(),
            projections, scopes, completion, fence);
        var generation = service.BeginLoopDecision();
        Assert.True(projections.TryGetActive(out var pending));
        var result = await service.CompleteAsync("plan", new("step", "1", "command", "ReportPerformed", 1, "100"),
            scopes.GetToken("A"), browser.Token);
        Assert.True(browser.IsCancellationRequested);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(((IValueHttpResult)result).Value));
        Assert.Equal("applied", payload.RootElement.GetProperty("acknowledgement").GetProperty("status").GetString());
        Assert.False(projections.TryGetActive(out _));
        Assert.True(pending!.IsCompleted);
        Assert.Equal(1, fence.Commits);
        service.CompleteLoopDecision(generation);
    }

    [Fact]
    public async Task Partial_report_response_preserves_instruction_remaining_quantity_and_paused_eligibility()
    {
        var now = DateTimeOffset.UtcNow;
        var scope = new AccountScope("scope-a");
        var profile = new AccountProfile(1, scope.AccountId, now, now);
        var orchestration = new PlanOrchestrationService();
        var candidate = new PlanCandidate("partial", 1, "partial", PlanAttention.Active,
            [new("sell", PlanStepAction.List, 42, "Objet", 10, new Money(200), [], PlanStepState.Pending)],
            [new(PlanResourceKind.Inventory, "42", 10, Money.Zero)], Money.Zero, Money.Zero, 0, 0, 1, 1, true, []);
        var reported = orchestration.ReportStep(orchestration.Start(candidate, now), 4, null, now);
        var plans = new CountingPlanRepository(reported);
        var service = new PlanEndpointService(new FixedRecommendations(RecommendationResult(now)),
            new FixedPortfolio(scope, new Money(5_000), now), new FixedAccountScopeGateway(scope.AccountId),
            new EmptyCraftingSnapshots(), new EmptyCraftingOpportunities(), new FixedPersonalTradingPostRepository(profile, now),
            plans, orchestration, new PlanDecisionProjectionStore(new DecisionLoopSchedulerSettings(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(15))));
        var response = await service.GetAsync(CancellationToken.None);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(response.Payload));
        var plan = payload.RootElement.GetProperty("plans")[0];
        Assert.Equal("ReconciliationRequired", plan.GetProperty("state").GetString());
        Assert.True(plan.GetProperty("isExecutionPaused").GetBoolean());
        Assert.Equal("UnsupportedPartialCompletion", plan.GetProperty("residualReasonCode").GetString());
        Assert.Equal(-1, plan.GetProperty("currentStepOrdinal").GetInt32());
        var step = plan.GetProperty("steps")[0];
        Assert.Equal(10, step.GetProperty("quantity").GetInt32());
        Assert.Equal(10, step.GetProperty("originalInstructedQuantity").GetInt32());
        Assert.Equal(4, step.GetProperty("reportedQuantity").GetInt32());
        Assert.Equal(6, step.GetProperty("remainingQuantity").GetInt32());
    }

    [Fact]
    public async Task Full_unchanged_plan_read_does_not_save_or_increment_its_revision()
    {
        var now = DateTimeOffset.UtcNow;
        var scope = new AccountScope("scope-a");
        var profile = new AccountProfile(1, scope.AccountId, now, now);
        var plan = new PlanRecord("unchanged", 1, "opportunity", PlanAttention.Passive,
            PlanState.Waiting, PlanReconciliationState.Compatible, now, [], Money.Zero, -1, [], [], 0,
            PlanHysteresisPolicy.Default, BaselineVerifiedCash: new Money(100),
            BaselineVerifiedQuantities: new Dictionary<string, long>(),
            LastEvidenceCapturedAtUtc: now, LastEvidenceFingerprint: string.Empty,
            LastEvidenceCaptureId: $"trading-post-sync:{now.UtcTicks}");
        var plans = new CountingPlanRepository(plan);
        var service = new PlanEndpointService(
            new FixedRecommendations(RecommendationResult(now)),
            new FixedPortfolio(scope, new Money(100), now),
            new FixedAccountScopeGateway(scope.AccountId),
            new EmptyCraftingSnapshots(), new EmptyCraftingOpportunities(),
            new FixedPersonalTradingPostRepository(profile, now), plans,
            new PlanOrchestrationService(),
            new PlanDecisionProjectionStore(new DecisionLoopSchedulerSettings(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(15))));

        var response = await service.GetAsync(CancellationToken.None);

        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(response.Payload));
        Assert.Equal("None", payload.RootElement.GetProperty("plans")[0].GetProperty("reconciliationReasonCode").GetString());

        Assert.Equal(0, plans.SaveCalls);
        Assert.Equal(0, plans.Plan.Revision);
    }

    [Fact]
    public async Task Undo_response_and_persistence_clear_a_resolved_contradiction_reason()
    {
        var now = DateTimeOffset.UtcNow;
        var scope = new AccountScope("scope-a");
        var profile = new AccountProfile(1, scope.AccountId, now, now);
        var candidate = new PlanCandidate("craft-plan", 1, "craft-plan", PlanAttention.Active, [
            new PlanStep("craft-step", PlanStepAction.Craft, 42, "Objet", 1, null, [], PlanStepState.Current,
                CraftEffects: [new(PlanResourceKind.Inventory, "42", -1, Money.Zero)]),
        ], [], Money.Zero, Money.Zero, 8_000, 0, 1, 1, true, []);
        var orchestration = new PlanOrchestrationService();
        var reported = orchestration.ReportStep(orchestration.Start(candidate, now) with { Id = "plan-undo-reason" }, 1, null, now);
        var contradicted = reported with {
            State = PlanState.ReconciliationRequired,
            ReconciliationState = PlanReconciliationState.Contradicted,
            ConsecutiveContradictionCount = 2,
            ReconciliationReason = PlanReconciliationReason.CraftInventoryMismatch,
        };
        var plans = new CountingPlanRepository(contradicted);
        var service = new PlanEndpointService(
            new FixedRecommendations(RecommendationResult(now)), new FixedPortfolio(scope, Money.Zero, now),
            new FixedAccountScopeGateway(scope.AccountId), new EmptyCraftingSnapshots(), new EmptyCraftingOpportunities(),
            new FixedPersonalTradingPostRepository(profile, now), plans, orchestration,
            new PlanDecisionProjectionStore(new DecisionLoopSchedulerSettings(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(15))));

        var response = await service.UndoAsync(contradicted.Id, CancellationToken.None);

        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(response).Value));
        var responsePlan = payload.RootElement.GetProperty("plan");
        Assert.Equal("InProgress", responsePlan.GetProperty("state").GetString());
        Assert.Equal("None", responsePlan.GetProperty("reconciliationState").GetString());
        Assert.Equal("None", responsePlan.GetProperty("reconciliationReasonCode").GetString());
        Assert.Equal(PlanState.InProgress, plans.Plan.State);
        Assert.Equal(PlanReconciliationState.None, plans.Plan.ReconciliationState);
        Assert.Equal(PlanReconciliationReason.None, plans.Plan.ReconciliationReason);
    }

    [Fact]
    public async Task Bank_and_material_sources_remain_partial_after_two_new_complete_trading_post_syncs()
    {
        var now = DateTimeOffset.UtcNow;
        var firstCapture = now.AddMinutes(-1);
        var secondCapture = now;
        var scope = new AccountScope("scope-a");
        var profile = new AccountProfile(1, scope.AccountId, firstCapture, firstCapture);
        var craft = new PlanStep("craft", PlanStepAction.Craft, 100, "Insigne", 1, null, [], PlanStepState.Pending,
            CraftEffects: [new(PlanResourceKind.Inventory, "10", -2, Money.Zero), new(PlanResourceKind.Inventory, "100", 1, Money.Zero)]);
        var candidate = new PlanCandidate("craft", 1, "craft", PlanAttention.Active, [craft], [], Money.Zero,
            Money.Zero, 8_000, 0, 1, 1, true, []);
        var started = new PlanOrchestrationService().Start(candidate, now.AddMinutes(-5), new Money(10_000),
            new Dictionary<string, long> { ["2:10"] = 2, ["2:100"] = 0 });
        var reported = new PlanOrchestrationService().ReportStep(started, 1, null, now.AddMinutes(-4));
        var plans = new CountingPlanRepository(reported);
        var tpRepository = new FixedPersonalTradingPostRepository(profile, firstCapture);
        var crafting = new AccountCraftingSnapshot(scope, now.AddMinutes(-2),
            CraftingFeatureResult<IReadOnlyList<AccountInventoryEntry>>.Available([new(10, 2, AccountItemBinding.AccountBound)]),
            CraftingFeatureResult<IReadOnlyList<AccountMaterialEntry>>.Available([new(100, 1, 0, AccountItemBinding.AccountBound)]),
            CraftingFeatureResult<IReadOnlyList<int>>.Available([]),
            CraftingFeatureResult<IReadOnlyList<CraftingDisciplineCapability>>.Available([]));
        var service = new PlanEndpointService(
            new FixedRecommendations(RecommendationResult(now)),
            new FixedPortfolio(scope, new Money(10_000), now.AddMinutes(-2)),
            new FixedAccountScopeGateway(scope.AccountId),
            new FixedCraftingSnapshots(crafting), new EmptyCraftingOpportunities(),
            tpRepository, plans, new PlanOrchestrationService(),
            new PlanDecisionProjectionStore(new DecisionLoopSchedulerSettings(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(15))));

        _ = await service.GetAsync(CancellationToken.None);
        tpRepository.SetCapture(secondCapture);
        _ = await service.GetAsync(CancellationToken.None);

        var pending = Assert.Single(plans.Plan.Events);
        Assert.Equal(PlanShadowEventState.PendingConfirmation, pending.State);
        Assert.Equal(0, pending.NegativeEvidenceCaptureCount);
        Assert.Equal(PlanReconciliationState.AwaitingEvidence, plans.Plan.ReconciliationState);
        Assert.Equal(2, plans.SaveCalls);
    }

    [Fact]
    public async Task Completion_scope_guard_rejects_a_switch_before_profile_or_receipt_lookup()
    {
        var scopeTokens = new AccountViewScopeTokenService();
        var originalScope = scopeTokens.GetToken("account-a");
        var completion = new CompletionServiceSpy();
        var service = new PlanEndpointService(
            null!, null!, new FixedAccountScopeGateway("account-b"), null!, null!, null!, null!,
            new PlanOrchestrationService(), null!, scopeTokens, completion);

        var result = await service.CompleteAsync("plan-1",
            new PlanStepCompletion("step-a", "1", "command-a", "ReportPerformed", 1, "100"),
            originalScope, CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal(0, completion.Calls);
    }

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
    public async Task Loop_notification_candidates_reject_duplicate_inventory_demands_as_one_combined_requirement()
    {
        var now = DateTimeOffset.UtcNow;
        var profile = new AccountProfile(1, "scope-a", now, now);
        var portfolio = new AccountPortfolioSnapshot(new AccountScope("scope-a"), new Money(100), CapturedAtUtc: now);
        var candidate = new PlanCandidate(
            "recommendation:Sell:Inventory:42:duplicate", 1, "recommendation:Sell:Inventory:42:duplicate", PlanAttention.Active,
            [new PlanStep("sell-1", PlanStepAction.SellNow, 42, "Objet", 6, new Money(25), [], PlanStepState.Pending),
             new PlanStep("sell-2", PlanStepAction.SellNow, 42, "Objet", 6, new Money(25), [], PlanStepState.Pending)],
            [new PlanResourceRequirement(PlanResourceKind.Inventory, "42", 6, Money.Zero),
             new PlanResourceRequirement(PlanResourceKind.Inventory, "42", 6, Money.Zero)],
            new Money(10), Money.Zero, 10_000, 0, 600, 1, true, []);
        var service = new PlanEndpointService(null!, null!, null!, null!, null!, null!, null!, new PlanOrchestrationService(), null!);
        var decision = new PlanDecisionSnapshot(profile, portfolio, new Dictionary<string, long> { ["2:42"] = 10 },
            RecommendationResult(now), [], [candidate], true, new PlanDecisionTiming(), now);

        var trace = await service.GetSelectionTraceAsync(decision, CancellationToken.None);

        Assert.Equal(0, trace.ResourceEligibleCandidates);
        Assert.Empty(trace.ExecutableSignalCandidateIds);
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

    [Fact]
    public async Task Selection_trace_preserves_the_real_fourteen_candidate_zero_plan_regression_shape()
    {
        var now = DateTimeOffset.UtcNow;
        var profile = new AccountProfile(1, "scope-a", now, now);
        var portfolio = new AccountPortfolioSnapshot(new AccountScope("scope-a"), new Money(100), CapturedAtUtc: now);
        var existingPlan = new PlanRecord("existing", 1, "existing", PlanAttention.Passive, PlanState.InProgress, PlanReconciliationState.None, now,
            [new PlanResourceRequirement(PlanResourceKind.ExpectedIncoming, "42", 1, Money.Zero)], Money.Zero, 0,
            [new PlanStep("buy", PlanStepAction.PlaceBuyOrder, 42, "Objet", 1, new Money(1), [], PlanStepState.Current)], [], 0, PlanHysteresisPolicy.Default);
        var capacityExcluded = Enumerable.Range(0, 13)
            .Select(index => Candidate($"recommendation:Sell:Inventory:{100 + index}", new PlanResourceRequirement(PlanResourceKind.ExpectedIncoming, "42", 1, Money.Zero), utility: 1))
            .ToArray();
        var negativeUtility = Candidate("recommendation:Sell:Inventory:999", new PlanResourceRequirement(PlanResourceKind.Inventory, "43", 1, Money.Zero), utility: -1);
        var service = new PlanEndpointService(null!, null!, null!, null!, null!, null!, null!, new PlanOrchestrationService(), null!);
        var decision = new PlanDecisionSnapshot(profile, portfolio, new Dictionary<string, long> { ["2:43"] = 1 }, RecommendationResult(now), [existingPlan], capacityExcluded.Append(negativeUtility).ToArray(), true, new PlanDecisionTiming(), now);

        var trace = await service.GetSelectionTraceAsync(decision, CancellationToken.None);

        Assert.Equal(14, trace.GeneratedCandidates);
        Assert.Equal(14, trace.HardEligibleCandidates);
        Assert.Equal(1, trace.ResourceEligibleCandidates);
        Assert.Equal(0, trace.SelectedCandidates);
        Assert.Empty(trace.ExecutableSignalCandidateIds);
        Assert.Equal(13, trace.Candidates!.Count(candidate => candidate.Stage == "generic_resource_conflict"));
        Assert.Contains(trace.Candidates!, candidate => candidate.CandidateId == negativeUtility.Id && candidate.Stage == "negative_utility_empty_bundle");
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

    private sealed class CompletionServiceSpy : IPlanCompletionCommandService
    {
        public int Calls { get; private set; }

        public Task<PlanCompletionResult> CompleteAsync(long accountProfileId, PlanCompletionCommand command,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new PlanCompletionResult(PlanCompletionStatus.Invalid));
        }
    }

    private sealed class AppliedThenCancelledCompletion(CancellationTokenSource browser) : IPlanCompletionCommandService
    {
        public Task<PlanCompletionResult> CompleteAsync(long accountProfileId, PlanCompletionCommand command, CancellationToken cancellationToken = default)
        {
            var receipt = new PlanCompletionReceipt(command.PlanId, command.CommandId, command.StepId,
                command.ExpectedRevision, command.Operation, command.Quantity, command.UnitPrice, 2, "event", DateTimeOffset.UtcNow);
            browser.Cancel();
            return Task.FromResult(new PlanCompletionResult(PlanCompletionStatus.Applied, receipt));
        }
    }

    private sealed class CancellationCheckingFence : IAccountWorkFence
    {
        public AccountWorkContext? Current { get; } = new(new("A"), "session", "incarnation", "generation");
        public string Generation => "generation";
        public event Action? Invalidated { add { } remove { } }
        internal int Commits;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default) => work(cancellationToken);
        public Task BindAccountAsync(AccountScope account, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask<IAsyncDisposable> AcquireCommitAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commits++;
            return ValueTask.FromResult<IAsyncDisposable>(new Lease());
        }
        public ValueTask<IAccountWorkTransition> QuiesceAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        private sealed class Lease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }

    private sealed class FixedAccountScopeGateway(string accountId) : IPersonalTradingPostGateway
    {
        public Task<Gw2ApiResult<AccountScope>> GetAccountScopeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<AccountScope>.Success(new AccountScope(accountId)));

        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCurrentBuyOrdersAsync(int page, CancellationToken cancellationToken = default) => Failure();
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCurrentSellListingsAsync(int page, CancellationToken cancellationToken = default) => Failure();
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCompletedBuyHistoryAsync(int page, CancellationToken cancellationToken = default) => Failure();
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCompletedSellHistoryAsync(int page, CancellationToken cancellationToken = default) => Failure();

        private static Task<Gw2ApiResult<PersonalTransactionPage>> Failure() =>
            Task.FromResult(Gw2ApiResult<PersonalTransactionPage>.Failure(Gw2ApiErrorCategory.UpstreamUnavailable));
    }

    private sealed class FixedRecommendations(PrimaryRecommendationResult value) : IPrimaryRecommendationService
    {
        public Task<PrimaryRecommendationResult> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(value);
    }

    private sealed class FixedPortfolio(AccountScope scope, Money cash, DateTimeOffset capturedAtUtc, IReadOnlyDictionary<string, long>? quantities = null) : IAccountPortfolioGateway
    {
        public Task<Gw2ApiResult<AccountPortfolioSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<AccountPortfolioSnapshot>.Success(new AccountPortfolioSnapshot(scope, cash,
                quantities ?? new Dictionary<string, long>(), capturedAtUtc)));
    }

    private sealed class EmptyCraftingSnapshots : IAccountCraftingSnapshotService
    {
        public Task<Gw2ApiResult<AccountCraftingSnapshot>> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<AccountCraftingSnapshot>.Failure(Gw2ApiErrorCategory.UpstreamUnavailable));
        public Task<AccountCraftingSnapshot?> GetLatestAsync(AccountScope accountScope, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountCraftingSnapshot?>(null);
    }

    private sealed class FixedCraftingSnapshots(AccountCraftingSnapshot snapshot) : IAccountCraftingSnapshotService
    {
        public Task<Gw2ApiResult<AccountCraftingSnapshot>> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<AccountCraftingSnapshot>.Success(snapshot));
        public Task<AccountCraftingSnapshot?> GetLatestAsync(AccountScope accountScope, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountCraftingSnapshot?>(snapshot);
    }

    private sealed class EmptyCraftingOpportunities : ICraftingOpportunityService
    {
        public Task<CraftingPlannerResult> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CraftingPlannerResult(CraftingOpportunityState.Ready, [], [], []));
    }

    private sealed class FixedPersonalTradingPostRepository : IPersonalTradingPostRepository
    {
        private AccountProfile profile;
        private DateTimeOffset observedAtUtc;

        public FixedPersonalTradingPostRepository(AccountProfile profile, DateTimeOffset observedAtUtc)
        {
            this.profile = profile;
            this.observedAtUtc = observedAtUtc;
        }

        public void SetCapture(DateTimeOffset capturedAtUtc)
        {
            observedAtUtc = capturedAtUtc;
            profile = profile with { LastSuccessfulSyncAtUtc = capturedAtUtc };
        }

        public Task<AccountProfile?> FindAccountProfileAsync(string accountScopeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountProfile?>(accountScopeId == profile.AccountScopeId ? profile : null);
        public Task<CurrentPersonalTradingPostOrderSnapshot?> GetLatestCurrentOrderSnapshotAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default) =>
            Task.FromResult<CurrentPersonalTradingPostOrderSnapshot?>(new(observedAtUtc, []));
        public Task<IReadOnlyList<StoredCompletedPersonalTradingPostTransaction>> GetCompletedTransactionsAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredCompletedPersonalTradingPostTransaction>>([]);
        public Task<PersonalTradingPostReconciliationSnapshot> GetReconciliationSnapshotAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PersonalTradingPostReconciliationSnapshot(profile, new(observedAtUtc, []), []));

        public Task<AccountProfile> GetOrCreateAccountProfileAsync(string accountScopeId, DateTimeOffset observedAtUtc, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task RecordSuccessfulSyncAsync(AccountProfile accountProfile, DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpsertCompletedTransactionsAsync(AccountProfile accountProfile, IReadOnlyCollection<CompletedPersonalTradingPostTransaction> transactions, DateTimeOffset observedAtUtc, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PersonalTradingPostHistoryCoverage> GetHistoryCoverageAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ReplaceCurrentOrderSnapshotAsync(AccountProfile accountProfile, CurrentPersonalTradingPostOrderSnapshot snapshot, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<CurrentPersonalTradingPostOrder>> GetCurrentOrdersAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<CurrentPersonalTradingPostOrderSnapshot>> GetCurrentOrderObservationsAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class CountingPlanRepository(PlanRecord plan) : IPlanRepository
    {
        public int SaveCalls { get; private set; }
        public PlanRecord Plan { get; private set; } = plan;
        public Task<IReadOnlyList<PlanRecord>> GetStartedAsync(long accountProfileId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PlanRecord>>([Plan]);
        public Task<IReadOnlyList<PlanRecord>> GetReconciliationCandidatesAsync(long accountProfileId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PlanRecord>>([Plan]);
        public Task<PlanStartResult> TryStartAsync(long accountProfileId, PlanRecord value, Money verifiedCash, Money hardReserve, IReadOnlyDictionary<string, long> verifiedQuantities, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<PlanRecord>> ApplyAccountReconciliationAsync(long accountProfileId, Func<IReadOnlyList<PlanRecord>, IReadOnlyList<PlanRecord>> reconcile, CancellationToken cancellationToken = default)
        {
            var updated = reconcile([Plan]);
            if (updated.Count == 0) return Task.FromResult<IReadOnlyList<PlanRecord>>([]);
            if (!PlanRecordSemantics.AreEqual(Plan, updated[0]))
            {
                SaveCalls++;
                Plan = updated[0] with { Revision = updated[0].Revision + 1 };
            }
            return Task.FromResult<IReadOnlyList<PlanRecord>>([Plan]);
        }
        public Task<PlanCompletionResult> CompleteStepAsync(long accountProfileId, PlanCompletionCommand command, Func<PlanRecord, PlanRecord> transition, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SaveAsync(long accountProfileId, PlanRecord value, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            Plan = value with { Revision = value.Revision + 1 };
            return Task.CompletedTask;
        }
    }
}
