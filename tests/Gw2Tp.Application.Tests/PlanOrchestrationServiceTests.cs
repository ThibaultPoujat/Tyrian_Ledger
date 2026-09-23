using Gw2Tp.Application.Plans;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class PlanOrchestrationServiceTests
{
    private readonly PlanOrchestrationService service = new();
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Selection_keeps_hard_reserve_and_prefers_compatible_smaller_bundle()
    {
        var large = Candidate("large", 700, 100, 80);
        var first = Candidate("first", 350, 60, 60);
        var second = Candidate("second", 350, 60, 60);

        var result = await service.SelectAsync([large, first, second], new Money(1_000), new Money(100));

        Assert.Equal(["first", "second"], result.Plans.Select(plan => plan.Id));
        Assert.Equal(700, result.ReservedCash.Copper);
        Assert.Equal(200, result.IntentionallyFreeCash.Copper);
    }

    [Fact]
    public async Task Selection_rejects_shared_inventory_and_non_terminal_active_buy_order()
    {
        var first = Candidate("inventory-a", 0, 50, 20, resource: new PlanResourceRequirement(PlanResourceKind.Inventory, "42", 3, Money.Zero));
        var second = Candidate("inventory-b", 0, 50, 20, resource: new PlanResourceRequirement(PlanResourceKind.Inventory, "42", 2, Money.Zero));
        var invalidActive = Candidate("invalid-active", 0, 99, 20, attention: PlanAttention.Active,
            steps: [Step("one", PlanStepAction.PlaceBuyOrder), Step("two", PlanStepAction.List)]);

        var result = await service.SelectAsync([first, second, invalidActive], new Money(1_000), Money.Zero);

        Assert.Single(result.Plans);
        Assert.DoesNotContain(result.Plans, plan => plan.Id == "invalid-active");
    }

    [Fact]
    public async Task Selection_uses_verified_inventory_capacity_instead_of_treating_every_shared_stack_as_a_conflict()
    {
        var first = Candidate("inventory-two", 0, 50, 2, resource: new PlanResourceRequirement(PlanResourceKind.Inventory, "42", 2, Money.Zero));
        var second = Candidate("inventory-three", 0, 40, 3, resource: new PlanResourceRequirement(PlanResourceKind.Inventory, "42", 3, Money.Zero));

        var result = await service.SelectAsync([first, second], new Money(1_000), Money.Zero,
            availableQuantities: new Dictionary<string, long> { ["2:42"] = 10 });

        Assert.Equal(["inventory-two", "inventory-three"], result.Plans.Select(plan => plan.Id));
    }

    [Fact]
    public async Task Selection_preselects_by_utility_so_a_late_high_value_candidate_is_not_lost()
    {
        var candidates = Enumerable.Range(0, 20).Select(index => Candidate($"low-{index:00}", 1, 1, 1)).ToList();
        candidates.Add(Candidate("late-high", 1, 1_000, 1));

        var result = await service.SelectAsync(candidates, new Money(100), Money.Zero);

        Assert.Contains(result.Plans, plan => plan.Id == "late-high");
    }

    [Fact]
    public void Report_and_undo_restore_the_current_step_without_erasing_event_history()
    {
        var plan = service.Start(Candidate("undo", 100, 50, 10), Now);
        var reported = service.ReportStep(plan, 1, new Money(100), Now);
        var projected = PlanOrchestrationService.ProjectEffectiveResources(new Money(1_000), new Dictionary<string, long>(), reported.Events);

        var undone = service.UndoLastStep(reported, Now);
        var restored = PlanOrchestrationService.ProjectEffectiveResources(new Money(1_000), new Dictionary<string, long>(), undone.Events);

        Assert.Single(undone.Events);
        Assert.Equal(PlanShadowEventState.Reversed, undone.Events[0].State);
        Assert.Equal(900, projected.EffectiveCash.Copper);
        Assert.Empty(PlanOrchestrationService.OutstandingReservations(reported));
        Assert.Equal(1_000, restored.EffectiveCash.Copper);
        Assert.Equal(0, undone.CurrentStepOrdinal);
        Assert.Equal(PlanStepState.Current, undone.Steps[0].State);
        Assert.Equal(100, PlanOrchestrationService.OutstandingReservations(undone).Single(value => value.Kind == PlanResourceKind.Cash).Cash.Copper);
    }

    [Fact]
    public void Listing_shadow_effect_uses_the_canonical_non_refundable_listing_fee()
    {
        var candidate = Candidate("listing", 0, 50, 2,
            resource: new PlanResourceRequirement(PlanResourceKind.Inventory, "42", 2, Money.Zero),
            steps: [Step("listing-step", PlanStepAction.List, 2)]);

        var reported = service.ReportStep(service.Start(candidate, Now), 2, new Money(100), Now);

        Assert.Equal(-10, reported.Events[0].Effects.Single(effect => effect.Kind == PlanResourceKind.Cash).Cash.Copper);
    }

    [Fact]
    public void Partial_listing_projects_the_canonical_fee_for_only_the_remaining_quantity()
    {
        var candidate = Candidate("partial-listing", 0, 50, 2,
            resource: new PlanResourceRequirement(PlanResourceKind.Inventory, "42", 2, Money.Zero),
            steps: [Step("listing", PlanStepAction.List, 2)]);
        var reported = service.ReportStep(service.Start(candidate, Now), 2, new Money(101), Now.AddSeconds(10));
        var partial = service.ReconcileWithVerifiedState(reported, new Money(994), new Dictionary<string, long> { ["2:42"] = 1 }, Now.AddMinutes(1),
            [new PlanVerifiedEvidence("listing-1", PlanEvidenceKind.SellListing, 42, 1, new Money(101), Now.AddSeconds(5), Now.AddMinutes(1))], Now.AddMinutes(1),
            Complete(PlanEvidenceKind.SellListing, PlanEvidenceKind.CompletedSell));

        var effective = PlanOrchestrationService.ProjectEffectiveResources(new Money(994), new Dictionary<string, long> { ["2:42"] = 1 }, partial.Events);

        Assert.Equal(988, effective.EffectiveCash.Copper);
        Assert.Equal(0, effective.Quantities["2:42"]);
    }

    [Fact]
    public void Partial_verified_quantity_keeps_only_the_unverified_cash_shadow_and_can_later_fully_confirm()
    {
        var buy = Candidate("partial", 200, 50, 2, steps: [Step("partial-step", PlanStepAction.BuyNow, 2)]);
        var reported = service.ReportStep(service.Start(buy, Now, new Money(1_000), new Dictionary<string, long> { ["2:42"] = 0 }), 2, new Money(100), Now.AddSeconds(10));
        var partial = service.ReconcileWithVerifiedState(reported, new Money(900), new Dictionary<string, long> { ["2:42"] = 1 }, Now.AddMinutes(1),
            [new PlanVerifiedEvidence("buy-1", PlanEvidenceKind.CompletedBuy, 42, 1, new Money(100), Now.AddSeconds(5), Now.AddMinutes(1))], Now.AddMinutes(1), Complete(PlanEvidenceKind.CompletedBuy));

        Assert.Equal(PlanShadowEventState.PartiallyConfirmed, partial.Events[0].State);
        Assert.Equal(1, partial.Events[0].VerifiedQuantity);
        Assert.Equal(800, PlanOrchestrationService.ProjectEffectiveResources(new Money(900), new Dictionary<string, long> { ["2:42"] = 1 }, partial.Events).EffectiveCash.Copper);

        var confirmed = service.ReconcileWithVerifiedState(partial, new Money(800), new Dictionary<string, long> { ["2:42"] = 2 }, Now.AddMinutes(2),
            [new PlanVerifiedEvidence("buy-2", PlanEvidenceKind.CompletedBuy, 42, 2, new Money(100), Now.AddSeconds(5), Now.AddMinutes(2))], Now.AddMinutes(2), Complete(PlanEvidenceKind.CompletedBuy));

        Assert.Equal(PlanShadowEventState.Confirmed, confirmed.Events[0].State);
        Assert.Equal(2, confirmed.Events[0].VerifiedQuantity);
        Assert.Equal(800, PlanOrchestrationService.ProjectEffectiveResources(new Money(800), new Dictionary<string, long> { ["2:42"] = 2 }, confirmed.Events).EffectiveCash.Copper);
    }

    [Fact]
    public void Only_complete_relevant_evidence_can_create_a_contradiction()
    {
        var buy = Candidate("contradiction", 200, 50, 2, steps: [Step("step", PlanStepAction.BuyNow, 2)]);
        var pending = service.ReportStep(service.Start(buy, Now, new Money(1_000), new Dictionary<string, long> { ["2:42"] = 0 }), 2, new Money(100), Now);
        var firstMismatch = service.ReconcileWithVerifiedState(pending, new Money(1_000), new Dictionary<string, long> { ["2:42"] = 0 }, Now.AddMinutes(16), [], Now.AddMinutes(16), Complete(PlanEvidenceKind.CompletedBuy));
        var unrelated = service.ReconcileWithVerifiedState(firstMismatch, new Money(1_000), new Dictionary<string, long> { ["2:42"] = 0 }, Now.AddMinutes(17),
            [new PlanVerifiedEvidence("unrelated", PlanEvidenceKind.SellListing, 42, 1, new Money(200), Now.AddMinutes(17), Now.AddMinutes(17))], Now.AddMinutes(17), Complete(PlanEvidenceKind.CompletedBuy));
        var secondMismatch = service.ReconcileWithVerifiedState(unrelated, new Money(1_000), new Dictionary<string, long> { ["2:42"] = 0 }, Now.AddMinutes(18),
            [new PlanVerifiedEvidence("wrong-price", PlanEvidenceKind.CompletedBuy, 42, 1, new Money(101), Now.AddMinutes(1), Now.AddMinutes(18))], Now.AddMinutes(18), Complete(PlanEvidenceKind.CompletedBuy));

        Assert.Equal(PlanState.RecheckRequired, firstMismatch.State);
        Assert.Equal(PlanState.RecheckRequired, unrelated.State);
        Assert.Equal(PlanState.ReconciliationRequired, secondMismatch.State);
    }

    [Fact]
    public void Evidence_created_after_issue_but_before_terminé_confirms_a_fast_filled_buy_order()
    {
        var buyOrder = Candidate("fast-fill", 100, 50, 1, attention: PlanAttention.Passive,
            steps: [Step("order", PlanStepAction.PlaceBuyOrder)]);
        var started = service.Start(buyOrder, Now);
        var reported = service.ReportStep(started, 1, new Money(100), Now.AddSeconds(10));

        var confirmed = service.ReconcileWithVerifiedState(reported, new Money(900), new Dictionary<string, long> { ["2:42"] = 1 }, Now.AddMinutes(1),
            [new PlanVerifiedEvidence("filled", PlanEvidenceKind.CompletedBuy, 42, 1, new Money(100), Now.AddSeconds(5), Now.AddMinutes(1))], Now.AddMinutes(1),
            Complete(PlanEvidenceKind.BuyOrder, PlanEvidenceKind.CompletedBuy));

        Assert.Equal(PlanShadowEventState.Confirmed, confirmed.Events[0].State);
        Assert.Equal(PlanStepState.Confirmed, confirmed.Steps[0].State);
        Assert.DoesNotContain(PlanOrchestrationService.OutstandingReservations(confirmed), value => value.Kind == PlanResourceKind.ExpectedIncoming);
    }

    [Fact]
    public void Cancelling_an_unperformed_current_step_releases_resources_for_a_restart()
    {
        var candidate = Candidate("restart", 100, 50, 1);
        var started = service.Start(candidate, Now);

        var cancelled = service.CancelUnperformedStep(started);

        Assert.Equal(PlanState.Invalid, cancelled.State);
        Assert.Empty(PlanOrchestrationService.OutstandingReservations(cancelled));
        Assert.Equal(PlanStepState.Current, service.Start(candidate, Now.AddMinutes(1)).Steps[0].State);
    }

    [Fact]
    public void Cancelling_a_later_unperformed_step_keeps_earlier_pending_shadow_effects_until_reconciled()
    {
        var candidate = Candidate("cancel-suffix", 100, 50, 1,
            steps: [Step("buy", PlanStepAction.BuyNow), Step("list", PlanStepAction.List)]);
        var reported = service.ReportStep(service.Start(candidate, Now), 1, new Money(100), Now);

        var cancelled = service.CancelUnperformedStep(reported);
        var effective = PlanOrchestrationService.ProjectEffectiveResources(new Money(1_000), new Dictionary<string, long>(), cancelled.Events);

        Assert.Equal(PlanState.ReconciliationRequired, cancelled.State);
        Assert.Equal(PlanReconciliationState.AwaitingEvidence, cancelled.ReconciliationState);
        Assert.Equal(PlanStepState.AwaitingConfirmation, cancelled.Steps[0].State);
        Assert.Equal(PlanStepState.Invalidated, cancelled.Steps[1].State);
        Assert.Equal(900, effective.EffectiveCash.Copper);
    }

    [Fact]
    public void Two_stabilized_complete_observations_confirm_a_reported_bid_cancellation_by_absence()
    {
        var candidate = Candidate("cancel-bid", 0, 50, 1,
            steps: [new PlanStep("cancel", PlanStepAction.CancelBuyOrder, 42, "Objet", 1, new Money(100), [], PlanStepState.Pending, "order-7")]);
        var reported = service.ReportStep(service.Start(candidate, Now), 1, new Money(100), Now);

        var provisional = service.ReconcileWithVerifiedState(reported, new Money(1_000), new Dictionary<string, long>(), Now.AddMinutes(1),
            [], Now.AddMinutes(1), Complete(PlanEvidenceKind.BuyOrder, PlanEvidenceKind.CompletedBuy));
        var reconciled = service.ReconcileWithVerifiedState(provisional, new Money(1_000), new Dictionary<string, long>(), Now.AddMinutes(16),
            [], Now.AddMinutes(16), Complete(PlanEvidenceKind.BuyOrder, PlanEvidenceKind.CompletedBuy));

        Assert.Equal(PlanShadowEventState.PendingConfirmation, provisional.Events[0].State);
        Assert.Equal(PlanShadowEventState.Confirmed, reconciled.Events[0].State);
        Assert.Equal(PlanStepState.Confirmed, reconciled.Steps[0].State);
        Assert.Equal(PlanReconciliationState.Compatible, reconciled.ReconciliationState);
    }

    [Fact]
    public void A_still_visible_current_buy_order_does_not_confirm_a_reported_bid_cancellation()
    {
        var candidate = Candidate("cancel-still-visible", 0, 50, 1,
            steps: [new PlanStep("cancel", PlanStepAction.CancelBuyOrder, 42, "Objet", 1, new Money(100), [], PlanStepState.Pending, "order-7")]);
        var reported = service.ReportStep(service.Start(candidate, Now), 1, new Money(100), Now);

        var reconciled = service.ReconcileWithVerifiedState(reported, new Money(1_000), new Dictionary<string, long>(), Now.AddMinutes(1),
            [new PlanVerifiedEvidence("BuyOrder:7", PlanEvidenceKind.BuyOrder, 42, 1, new Money(100), Now.AddSeconds(1), Now.AddMinutes(1), "order-7")],
            Now.AddMinutes(1), Complete(PlanEvidenceKind.BuyOrder, PlanEvidenceKind.CompletedBuy));

        Assert.Equal(PlanShadowEventState.PendingConfirmation, reconciled.Events[0].State);
        Assert.Equal(PlanStepState.AwaitingConfirmation, reconciled.Steps[0].State);
    }

    [Fact]
    public void A_completed_buy_that_can_belong_to_the_cancelled_order_pauses_the_plan_instead_of_confirming_cancellation()
    {
        var candidate = Candidate("cancel-filled", 0, 50, 1,
            steps: [new PlanStep("cancel", PlanStepAction.CancelBuyOrder, 42, "Objet", 2, new Money(100), [], PlanStepState.Pending, "order-7")]);
        var reported = service.ReportStep(service.Start(candidate, Now), 2, new Money(100), Now);

        var reconciled = service.ReconcileWithVerifiedState(reported, new Money(800), new Dictionary<string, long> { ["2:42"] = 2 }, Now.AddMinutes(1),
            [new PlanVerifiedEvidence("CompletedBuy:7", PlanEvidenceKind.CompletedBuy, 42, 2, new Money(100), Now.AddSeconds(1), Now.AddMinutes(1), "order-7")],
            Now.AddMinutes(1), Complete(PlanEvidenceKind.BuyOrder, PlanEvidenceKind.CompletedBuy));

        Assert.Equal(PlanShadowEventState.PendingConfirmation, reconciled.Events[0].State);
        Assert.Equal(PlanState.ReconciliationRequired, reconciled.State);
        Assert.Equal(PlanReconciliationState.Contradicted, reconciled.ReconciliationState);
    }

    [Fact]
    public void A_fill_between_instruction_and_done_report_pauses_cancellation()
    {
        var candidate = Candidate("cancel-filled-before-report", 0, 50, 1,
            steps: [new PlanStep("cancel", PlanStepAction.CancelBuyOrder, 42, "Objet", 2, new Money(100), [], PlanStepState.Pending, "order-7")]);
        var reported = service.ReportStep(service.Start(candidate, Now), 2, new Money(100), Now.AddSeconds(10));

        var reconciled = service.ReconcileWithVerifiedState(reported, new Money(800), new Dictionary<string, long> { ["2:42"] = 2 }, Now.AddMinutes(1),
            [new PlanVerifiedEvidence("CompletedBuy:7", PlanEvidenceKind.CompletedBuy, 42, 2, new Money(100), Now.AddSeconds(5), Now.AddMinutes(1), "order-7")],
            Now.AddMinutes(1), Complete(PlanEvidenceKind.BuyOrder, PlanEvidenceKind.CompletedBuy));

        Assert.Equal(PlanShadowEventState.PendingConfirmation, reconciled.Events[0].State);
        Assert.Equal(PlanState.ReconciliationRequired, reconciled.State);
        Assert.Equal(PlanReconciliationState.Contradicted, reconciled.ReconciliationState);
    }

    [Fact]
    public void Late_exact_completed_buy_evidence_reopens_a_stabilized_cancellation()
    {
        var candidate = Candidate("cancel-late-history", 0, 50, 1,
            steps: [new PlanStep("cancel", PlanStepAction.CancelBuyOrder, 42, "Objet", 1, new Money(100), [], PlanStepState.Pending, "order-7")]);
        var reported = service.ReportStep(service.Start(candidate, Now), 1, new Money(100), Now.AddSeconds(10));
        var firstAbsence = service.ReconcileWithVerifiedState(reported, new Money(1_000), new Dictionary<string, long>(), Now.AddMinutes(1),
            [], Now.AddMinutes(1), Complete(PlanEvidenceKind.BuyOrder, PlanEvidenceKind.CompletedBuy));
        var confirmed = service.ReconcileWithVerifiedState(firstAbsence, new Money(1_000), new Dictionary<string, long>(), Now.AddMinutes(16),
            [], Now.AddMinutes(16), Complete(PlanEvidenceKind.BuyOrder, PlanEvidenceKind.CompletedBuy));

        var reopened = service.ReconcileWithVerifiedState(confirmed, new Money(900), new Dictionary<string, long> { ["2:42"] = 1 }, Now.AddMinutes(17),
            [new PlanVerifiedEvidence("CompletedBuy:7", PlanEvidenceKind.CompletedBuy, 42, 1, new Money(100), Now.AddSeconds(5), Now.AddMinutes(17), "order-7")],
            Now.AddMinutes(17), Complete(PlanEvidenceKind.BuyOrder, PlanEvidenceKind.CompletedBuy));

        Assert.Equal(PlanShadowEventState.Confirmed, confirmed.Events[0].State);
        Assert.Equal(PlanState.ReconciliationRequired, reopened.State);
        Assert.Equal(PlanReconciliationState.Contradicted, reopened.ReconciliationState);
    }

    [Fact]
    public void Two_complete_post_deadline_observations_of_a_still_visible_order_pause_cancellation()
    {
        var candidate = Candidate("cancel-persistent", 0, 50, 1,
            steps: [new PlanStep("cancel", PlanStepAction.CancelBuyOrder, 42, "Objet", 1, new Money(100), [], PlanStepState.Pending, "order-7")]);
        var reported = service.ReportStep(service.Start(candidate, Now), 1, new Money(100), Now);
        var current = new PlanVerifiedEvidence("BuyOrder:7", PlanEvidenceKind.BuyOrder, 42, 1, new Money(100), Now.AddDays(-1), Now.AddMinutes(16), "order-7");

        var first = service.ReconcileWithVerifiedState(reported, new Money(1_000), new Dictionary<string, long>(), Now.AddMinutes(16),
            [current], Now.AddMinutes(16), Complete(PlanEvidenceKind.BuyOrder, PlanEvidenceKind.CompletedBuy));
        var second = service.ReconcileWithVerifiedState(first, new Money(1_000), new Dictionary<string, long>(), Now.AddMinutes(17),
            [current with { ObservedAtUtc = Now.AddMinutes(17) }], Now.AddMinutes(17), Complete(PlanEvidenceKind.BuyOrder, PlanEvidenceKind.CompletedBuy));

        Assert.Equal(PlanState.RecheckRequired, first.State);
        Assert.Equal(PlanState.ReconciliationRequired, second.State);
        Assert.Equal(PlanReconciliationState.Contradicted, second.ReconciliationState);
    }

    [Fact]
    public void Undo_after_cancelling_a_later_step_restores_an_executable_plan()
    {
        var candidate = Candidate("cancel-undo", 100, 50, 1,
            steps: [Step("buy", PlanStepAction.BuyNow), Step("list", PlanStepAction.List)]);
        var reported = service.ReportStep(service.Start(candidate, Now), 1, new Money(100), Now);
        var cancelled = service.CancelUnperformedStep(reported);

        var undone = service.UndoLastStep(cancelled, Now.AddMinutes(1));
        var reconciled = service.ReconcileWithVerifiedState(undone, new Money(1_000), new Dictionary<string, long>(), Now.AddMinutes(2));

        Assert.False(undone.IsCancelled);
        Assert.Equal(PlanState.InProgress, reconciled.State);
        Assert.Equal(PlanStepState.Current, reconciled.Steps[0].State);
    }

    [Fact]
    public void Reconciliation_carries_confirmed_effects_into_the_next_pending_step()
    {
        var candidate = Candidate("chain", 100, 50, 1,
            steps: [Step("buy", PlanStepAction.BuyNow), Step("list", PlanStepAction.List)]);
        var started = service.Start(candidate, Now, new Money(1_000), new Dictionary<string, long> { ["2:42"] = 0 });
        var first = service.ReportStep(started, 1, new Money(100), Now);
        var firstConfirmed = service.ReconcileWithVerifiedState(first, new Money(900), new Dictionary<string, long> { ["2:42"] = 1 }, Now.AddMinutes(1),
            [new PlanVerifiedEvidence("buy-1", PlanEvidenceKind.CompletedBuy, 42, 1, new Money(100), Now.AddSeconds(1), Now.AddMinutes(1))], Now.AddMinutes(1));
        var second = service.ReportStep(firstConfirmed, 1, new Money(200), Now.AddMinutes(2));
        var secondConfirmed = service.ReconcileWithVerifiedState(second, new Money(890), new Dictionary<string, long> { ["2:42"] = 0 }, Now.AddMinutes(3),
            [new PlanVerifiedEvidence("sell-1", PlanEvidenceKind.SellListing, 42, 1, new Money(200), Now.AddMinutes(2).AddSeconds(1), Now.AddMinutes(3))], Now.AddMinutes(3));

        Assert.Equal(PlanShadowEventState.Confirmed, secondConfirmed.Events[1].State);
        Assert.Equal(PlanReconciliationState.Compatible, secondConfirmed.ReconciliationState);
    }

    [Fact]
    public void Reconciliation_never_confirms_a_later_step_while_an_earlier_step_is_pending()
    {
        var candidate = Candidate("ordered", 100, 50, 1,
            steps: [Step("buy", PlanStepAction.BuyNow), Step("list", PlanStepAction.List)]);
        var first = service.ReportStep(service.Start(candidate, Now), 1, new Money(100), Now);
        var second = service.ReportStep(first with { Steps = first.Steps.Select((step, index) => index == 1 ? step with { State = PlanStepState.Current } : step).ToArray(), CurrentStepOrdinal = 1 }, 1, new Money(200), Now.AddMinutes(1));
        var observed = service.ReconcileWithVerifiedState(second, new Money(800), new Dictionary<string, long> { ["2:42"] = 0 }, Now.AddMinutes(2),
            [new PlanVerifiedEvidence("listing", PlanEvidenceKind.SellListing, 42, 1, new Money(200), Now.AddMinutes(1).AddSeconds(1), Now.AddMinutes(2))], Now.AddMinutes(2));

        Assert.Equal(PlanShadowEventState.PendingConfirmation, observed.Events[0].State);
        Assert.Equal(PlanShadowEventState.PendingConfirmation, observed.Events[1].State);
    }

    [Fact]
    public void A_verified_transaction_is_not_reused_to_confirm_two_steps()
    {
        var candidate = Candidate("one-to-one", 200, 50, 1,
            steps: [Step("first", PlanStepAction.BuyNow), Step("second", PlanStepAction.BuyNow)]);
        var first = service.ReportStep(service.Start(candidate, Now), 1, new Money(100), Now);
        var second = service.ReportStep(first with
        {
            Steps = first.Steps.Select((step, index) => index == 1 ? step with { State = PlanStepState.Current, IssuedAtUtc = Now } : step).ToArray(),
            CurrentStepOrdinal = 1,
        }, 1, new Money(100), Now.AddSeconds(10));

        var reconciled = service.ReconcileWithVerifiedState(second, new Money(900), new Dictionary<string, long> { ["2:42"] = 1 }, Now.AddMinutes(1),
            [new PlanVerifiedEvidence("one-buy", PlanEvidenceKind.CompletedBuy, 42, 1, new Money(100), Now.AddSeconds(5), Now.AddMinutes(1))], Now.AddMinutes(1), Complete(PlanEvidenceKind.CompletedBuy));

        Assert.Equal(PlanShadowEventState.Confirmed, reconciled.Events[0].State);
        Assert.Equal(PlanShadowEventState.PendingConfirmation, reconciled.Events[1].State);
    }

    [Fact]
    public void Undo_pauses_when_a_confirmed_dependent_event_would_make_history_impossible()
    {
        var candidate = Candidate("undo-dependent", 100, 50, 1,
            steps: [Step("buy", PlanStepAction.BuyNow), Step("list", PlanStepAction.List)]);
        var first = service.ReportStep(service.Start(candidate, Now), 1, new Money(100), Now);
        var dependent = new PlanExecutionEvent("dependent", first.Id, "list", 2, Now.AddMinutes(2), 1, new Money(200), [],
            PlanShadowEventState.Confirmed, null, [first.Events[0].Id]);
        var withDependent = first with { Events = first.Events.Append(dependent).ToArray() };

        var undone = service.UndoLastStep(withDependent, Now.AddMinutes(3));

        Assert.Equal(PlanState.ReconciliationRequired, undone.State);
        Assert.Equal(PlanReconciliationState.Contradicted, undone.ReconciliationState);
    }

    [Fact]
    public void Passive_empty_plan_enters_waiting_and_material_refresh_does_not_rewrite_the_step()
    {
        var waiting = new PlanCandidate("waiting", 1, "waiting", PlanAttention.Passive, [], [], Money.Zero, Money.Zero, 0, 0, 0, 1, true, []);
        Assert.Equal(PlanState.Waiting, service.Start(waiting, Now).State);

        var plan = service.Start(Candidate("freeze", 100, 50, 1), Now);
        var changed = service.ApplyRefresh(plan, Candidate("freeze", 200, 50, 2), evidenceReady: true);
        Assert.Equal(PlanState.RecheckRequired, changed.State);
        Assert.Equal(1, changed.Steps[0].Quantity);
    }

    [Fact]
    public void Reconciliation_confirms_without_replacing_the_current_instruction_and_pauses_material_contradiction()
    {
        var candidate = Candidate("reconcile", 100, 50, 10, steps: [Step("one", PlanStepAction.BuyNow), Step("two", PlanStepAction.List)]);
        var reported = service.ReportStep(service.Start(candidate, Now), 1, new Money(100), Now);

        var confirmed = service.Reconcile(reported, [reported.Events[0].Id], materiallyContradicted: false);
        var paused = service.Reconcile(confirmed, [], materiallyContradicted: true);

        Assert.Equal(PlanStepState.Confirmed, confirmed.Steps[0].State);
        Assert.Equal(PlanStepState.Current, confirmed.Steps[1].State);
        Assert.Equal(PlanState.ReconciliationRequired, paused.State);
        Assert.Equal(PlanReconciliationState.Contradicted, paused.ReconciliationState);
    }

    private static PlanCandidate Candidate(string id, long cash, long utility, int quantity, PlanResourceRequirement? resource = null, PlanAttention attention = PlanAttention.Active, IReadOnlyList<PlanStep>? steps = null) =>
        new(id, 1, id, attention, steps ?? [Step($"{id}-step", PlanStepAction.BuyNow, quantity)],
            resource is null ? [new(PlanResourceKind.Cash, "cash", 0, new Money(cash))] : [resource], new Money(utility), new Money(cash), 8_000, 0, 600, utility, true, []);

    private static PlanStep Step(string id, PlanStepAction action, int quantity = 1) =>
        new(id, action, 42, "Objet", quantity, new Money(100), [], PlanStepState.Pending);

    private static IReadOnlySet<PlanEvidenceKind> Complete(params PlanEvidenceKind[] kinds) => new HashSet<PlanEvidenceKind>(kinds);
}
