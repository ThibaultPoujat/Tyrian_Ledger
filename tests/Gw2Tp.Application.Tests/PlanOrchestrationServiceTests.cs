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
        Assert.Equal(1_000, restored.EffectiveCash.Copper);
        Assert.Equal(0, undone.CurrentStepOrdinal);
        Assert.Equal(PlanStepState.Current, undone.Steps[0].State);
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
