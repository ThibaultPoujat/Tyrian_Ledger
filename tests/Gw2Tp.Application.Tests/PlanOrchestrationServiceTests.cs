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
    public void Partial_verified_quantity_confirms_a_shadow_event_and_expired_repeated_mismatch_pauses_it()
    {
        var buy = Candidate("partial", 200, 50, 2, steps: [Step("partial-step", PlanStepAction.BuyNow, 2)]);
        var reported = service.ReportStep(service.Start(buy, Now, new Money(1_000), new Dictionary<string, long> { ["2:42"] = 0 }), 2, new Money(100), Now);
        var partial = service.ReconcileWithVerifiedState(reported, new Money(1_000), new Dictionary<string, long> { ["2:42"] = 1 }, Now.AddMinutes(1));

        Assert.Equal(PlanShadowEventState.Confirmed, partial.Events[0].State);
        Assert.Equal(1, partial.Events[0].VerifiedQuantity);

        var pending = service.ReportStep(service.Start(buy, Now, new Money(1_000), new Dictionary<string, long> { ["2:42"] = 0 }), 2, new Money(100), Now);
        var firstMismatch = service.ReconcileWithVerifiedState(pending, new Money(1_000), new Dictionary<string, long> { ["2:42"] = 0 }, Now.AddMinutes(16));
        var secondMismatch = service.ReconcileWithVerifiedState(firstMismatch, new Money(1_000), new Dictionary<string, long> { ["2:42"] = 0 }, Now.AddMinutes(17));

        Assert.Equal(PlanState.RecheckRequired, firstMismatch.State);
        Assert.Equal(PlanState.ReconciliationRequired, secondMismatch.State);
    }

    [Fact]
    public void Reconciliation_carries_confirmed_effects_into_the_next_pending_step()
    {
        var candidate = Candidate("chain", 100, 50, 1,
            steps: [Step("buy", PlanStepAction.BuyNow), Step("list", PlanStepAction.List)]);
        var started = service.Start(candidate, Now, new Money(1_000), new Dictionary<string, long> { ["2:42"] = 0 });
        var first = service.ReportStep(started, 1, new Money(100), Now);
        var firstConfirmed = service.ReconcileWithVerifiedState(first, new Money(900), new Dictionary<string, long> { ["2:42"] = 1 }, Now.AddMinutes(1));
        var second = service.ReportStep(firstConfirmed, 1, new Money(200), Now.AddMinutes(2));
        var secondConfirmed = service.ReconcileWithVerifiedState(second, new Money(890), new Dictionary<string, long> { ["2:42"] = 0 }, Now.AddMinutes(3));

        Assert.Equal(PlanShadowEventState.Confirmed, secondConfirmed.Events[1].State);
        Assert.Equal(PlanReconciliationState.Compatible, secondConfirmed.ReconciliationState);
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
}
