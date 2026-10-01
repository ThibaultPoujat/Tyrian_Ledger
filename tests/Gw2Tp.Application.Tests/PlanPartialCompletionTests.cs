using Gw2Tp.Application.Plans;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class PlanPartialCompletionTests
{
    private readonly PlanOrchestrationService service = new();
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(PlanStepAction.List, 40)]
    [InlineData(PlanStepAction.SellNow, 0)]
    public void Partial_acquisition_closes_original_instruction_and_reserves_only_its_exit(PlanStepAction action, long fee)
    {
        var result = service.ReportStep(Start(action), 4, new Money(100), Now);
        Assert.Equal(10, result.Steps[0].Quantity);
        Assert.Equal(4, result.Steps[0].ReportedQuantity);
        Assert.Equal(10, result.Steps[1].OriginalInstructedQuantity);
        Assert.Equal(4, result.Steps[1].Quantity);
        Assert.Equal(PlanStepState.Current, result.Steps[1].State);
        Assert.Equal(PlanResidualReason.None, result.ResidualReason);
        var reservations = PlanOrchestrationService.OutstandingReservations(result);
        Assert.Equal(4, reservations.Single(value => value.Kind == PlanResourceKind.Inventory).Quantity);
        Assert.Equal(fee, reservations.Sum(value => value.Cash.Copper));
        var resources = PlanOrchestrationService.ProjectEffectiveResources(new Money(5_000), new Dictionary<string, long>(), result.Events);
        Assert.Equal(4, resources.Quantities["2:42"]);
        Assert.Equal(4_600, resources.EffectiveCash.Copper);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void Invalid_quantity_has_structured_rejection_without_mutation(int quantity)
    {
        var original = Start();
        var exception = Assert.Throws<PlanCompletionValidationException>(() => service.ReportStep(original, quantity, new Money(100), Now));
        Assert.Equal(PlanCompletionReason.InvalidQuantity, exception.Reason);
        Assert.Empty(original.Events);
        Assert.Equal(10, original.Steps[1].Quantity);
    }

    [Fact]
    public void Partial_craft_is_rejected_without_effects()
    {
        var original = Start() with { Steps = [new("craft", PlanStepAction.Craft, 42, "Objet", 10, null, [], PlanStepState.Current)] };
        var exception = Assert.Throws<PlanCompletionValidationException>(() => service.ReportStep(original, 4, null, Now));
        Assert.Equal(PlanCompletionReason.CraftQuantityMismatch, exception.Reason);
        Assert.Empty(original.Events);
    }

    [Fact]
    public void Multi_input_craft_keeps_actual_purchase_and_conservative_remainder_paused()
    {
        var original = Start() with { Steps = [Start().Steps[0], new("craft", PlanStepAction.Craft, 99, "Résultat", 10, null,
            ["buy"], PlanStepState.Pending, CraftEffects: [new(PlanResourceKind.Inventory, "42", -10, Money.Zero),
                new(PlanResourceKind.Inventory, "43", -10, Money.Zero), new(PlanResourceKind.Inventory, "99", 10, Money.Zero)])] };
        var result = service.ReportStep(original, 4, new Money(100), Now);
        Assert.Equal(PlanState.ReconciliationRequired, result.State);
        Assert.Equal(PlanResidualReason.UnsupportedPartialCompletion, result.ResidualReason);
        Assert.Equal(-1, result.CurrentStepOrdinal);
        Assert.Equal(10, result.Steps[1].Quantity);
        Assert.Equal(PlanStepState.RecheckRequired, result.Steps[1].State);
        var reservations = PlanOrchestrationService.OutstandingReservations(result);
        Assert.Equal(600, reservations.Sum(value => value.Cash.Copper));
        Assert.Equal(4, reservations.Single(value => value.ResourceId == "42").Quantity);
        Assert.Equal(10, reservations.Single(value => value.ResourceId == "43").Quantity);
        Assert.Equal(4, Assert.Single(result.Events).Quantity);
        Assert.Throws<InvalidOperationException>(() => service.ReportStep(result, 10, null, Now));
    }

    [Theory]
    [InlineData(PlanStepAction.List)]
    [InlineData(PlanStepAction.SellNow)]
    [InlineData(PlanStepAction.PlaceBuyOrder)]
    public void Unsupported_partial_action_is_not_settled_even_after_API_confirmation(PlanStepAction action)
    {
        var original = Start() with { Steps = [new("buy", action, 42, "Objet", 10, new Money(200), [], PlanStepState.Current)] };
        var result = service.ReportStep(original, 4, null, Now);
        var confirmed = service.ReconcileWithVerifiedState(result, new Money(5_000), new Dictionary<string, long> { ["2:42"] = 6 },
            Now.AddMinutes(1), [new("evidence", action == PlanStepAction.PlaceBuyOrder ? PlanEvidenceKind.BuyOrder :
                action == PlanStepAction.List ? PlanEvidenceKind.SellListing : PlanEvidenceKind.CompletedSell,
                42, 4, new Money(200), Now, Now.AddMinutes(1))]);
        Assert.Equal(PlanState.ReconciliationRequired, confirmed.State);
        Assert.Equal(PlanResidualReason.UnsupportedPartialCompletion, confirmed.ResidualReason);
        Assert.Equal(6, confirmed.Steps[0].Quantity - confirmed.Steps[0].ReportedQuantity);
        Assert.Equal(PlanShadowEventState.Confirmed, Assert.Single(confirmed.Events).State);
        Assert.NotEmpty(PlanOrchestrationService.OutstandingReservations(confirmed));
        if (action != PlanStepAction.PlaceBuyOrder)
        {
            var resources = PlanOrchestrationService.ProjectEffectiveResources(new Money(5_000), new Dictionary<string, long> { ["2:42"] = 10 }, result.Events);
            Assert.Equal(6, resources.Quantities["2:42"]);
            Assert.Equal(6, PlanOrchestrationService.OutstandingReservations(result).Single(value => value.Kind == PlanResourceKind.Inventory).Quantity);
        }
    }

    [Fact]
    public void Extra_dependency_or_resource_never_enters_automatic_resizing()
    {
        var original = Start();
        foreach (var plan in new[] {
            original with { Reservations = original.Reservations.Append(new(PlanResourceKind.Position, "position", 1, Money.Zero)).ToArray() },
            original with { Attention = PlanAttention.Passive },
            original with { Steps = [original.Steps[0], original.Steps[1] with { DependsOnStepIds = [] }] },
            original with { Steps = [original.Steps[0], original.Steps[1] with { ItemId = 43 }] },
        })
        {
            var result = service.ReportStep(plan, 4, null, Now);
            Assert.Equal(PlanState.ReconciliationRequired, result.State);
            Assert.Equal(10, result.Steps[1].Quantity);
        }
    }

    [Fact]
    public void More_expensive_actual_purchase_records_facts_but_does_not_bypass_cash_commitment()
    {
        var result = service.ReportStep(Start(), 4, new Money(300), Now);
        Assert.Equal(PlanResidualReason.ResourceRecheckRequired, result.ResidualReason);
        Assert.Equal(PlanState.ReconciliationRequired, result.State);
        Assert.Equal(-1_200, result.Events[0].Effects.Single(value => value.Kind == PlanResourceKind.Cash).Cash.Copper);
        Assert.Equal(10, result.Steps[1].Quantity);
    }

    [Fact]
    public void Undo_partial_report_restores_original_instructions_prices_and_reservations()
    {
        var original = Start();
        var result = service.UndoLastStep(service.ReportStep(original, 4, new Money(110), Now), Now.AddSeconds(1));
        Assert.Equal(original.Steps, result.Steps);
        Assert.Equal(original.Reservations, result.Reservations);
        Assert.Equal(PlanShadowEventState.Reversed, Assert.Single(result.Events).State);
        Assert.Equal(0, result.CurrentStepOrdinal);
        Assert.Equal(PlanResidualReason.None, result.ResidualReason);
        Assert.Equal(1_100, PlanOrchestrationService.OutstandingReservations(result).Sum(value => value.Cash.Copper));
        var again = service.ReportStep(result, 10, null, Now.AddSeconds(2));
        Assert.Equal(2, again.Events.Count);
        Assert.Equal(10, again.Events[1].Quantity);
        var confirmed = service.ReconcileWithVerifiedState(again, new Money(4_000), new Dictionary<string, long> { ["2:42"] = 10 },
            Now.AddMinutes(1), [new("buy-new", PlanEvidenceKind.CompletedBuy, 42, 10, new Money(100), Now.AddSeconds(2), Now.AddMinutes(1))]);
        Assert.Equal(PlanStepState.Confirmed, confirmed.Steps[0].State);
    }

    [Fact]
    public void Verified_partial_report_cannot_be_undone_and_history_remains_intact()
    {
        var reported = service.ReportStep(Start(), 4, null, Now);
        var confirmedPart = reported with { Events = [reported.Events[0] with { State = PlanShadowEventState.PartiallyConfirmed, VerifiedQuantity = 2 }] };
        var result = service.UndoLastStep(confirmedPart, Now);
        Assert.Equal(PlanState.ReconciliationRequired, result.State);
        Assert.Equal(confirmedPart.Events, result.Events);
        Assert.Equal(4, result.Steps[1].Quantity);
        Assert.Equal(PlanStepState.RecheckRequired, result.Steps[1].State);
        Assert.Equal(-1, result.CurrentStepOrdinal);
    }

    [Fact]
    public void Previously_acted_exit_is_preserved_and_requires_reconciliation()
    {
        var original = Start();
        var downstream = new PlanExecutionEvent("prior-exit", original.Id, "exit", 1, Now, 10, new Money(200), [],
            PlanShadowEventState.Confirmed, null, [], Action: PlanStepAction.List);
        original = original with { Steps = [original.Steps[0], original.Steps[1] with { State = PlanStepState.Confirmed }], Events = [downstream] };
        var result = service.ReportStep(original, 4, null, Now.AddSeconds(1));
        Assert.Equal(PlanResidualReason.DependentWorkAlreadyRecorded, result.ResidualReason);
        Assert.Equal(downstream, result.Events[0]);
        Assert.Equal(original.Steps[1], result.Steps[1]);
        Assert.Equal(2, result.Events.Count);
    }

    [Fact]
    public void API_confirming_four_of_full_ten_never_resizes_local_instruction()
    {
        var full = service.ReportStep(Start(), 10, null, Now);
        var partial = service.ReconcileWithVerifiedState(full, new Money(4_600), new Dictionary<string, long> { ["2:42"] = 4 },
            Now.AddMinutes(1), [new("buy-part", PlanEvidenceKind.CompletedBuy, 42, 4, new Money(100), Now, Now.AddMinutes(1))]);
        Assert.Equal(10, partial.Steps[1].Quantity);
        Assert.Equal(10, partial.Steps[0].ReportedQuantity);
        Assert.Equal(4, partial.Events[0].VerifiedQuantity);
        Assert.Equal(PlanResidualReason.None, partial.ResidualReason);
        var effective = PlanOrchestrationService.ProjectEffectiveResources(new Money(4_600), new Dictionary<string, long> { ["2:42"] = 4 }, partial.Events);
        Assert.Equal(10, effective.Quantities["2:42"]);
        Assert.Equal(4_000, effective.EffectiveCash.Copper);
    }

    [Fact]
    public void Reduced_continuation_refresh_compares_against_original_instruction_without_reexpanding_it()
    {
        var original = Start();
        var reduced = service.ReportStep(original, 4, null, Now);
        var candidate = new PlanCandidate("partial", 1, "partial", PlanAttention.Active, original.Steps,
            original.Reservations, Money.Zero, new Money(1_100), 0, 0, 1, 1, true, []);
        var refreshed = service.ApplyRefresh(reduced, candidate, true);
        Assert.Equal(PlanState.InProgress, refreshed.State);
        Assert.Equal(4, refreshed.Steps[1].Quantity);
        var changed = candidate with { Steps = [original.Steps[0], original.Steps[1] with { UnitPrice = new Money(500) }] };
        Assert.Equal(PlanState.RecheckRequired, service.ApplyRefresh(reduced, changed, true).State);
    }

    [Fact]
    public void Partial_continuation_cannot_exceed_the_cash_budget_admitted_at_start()
    {
        var result = service.ReportStep(Start() with { Reservations = [new(PlanResourceKind.Cash, "cash", 0, new Money(400))] }, 4, null, Now);
        Assert.Equal(PlanState.ReconciliationRequired, result.State);
        Assert.Equal(PlanResidualReason.ResourceRecheckRequired, result.ResidualReason);
        Assert.Equal(10, result.Steps[1].Quantity);
    }

    private PlanRecord Start(PlanStepAction exit = PlanStepAction.List) => service.Start(new("partial", 1, "partial", PlanAttention.Active,
        [new("buy", PlanStepAction.BuyNow, 42, "Objet", 10, new Money(100), [], PlanStepState.Pending),
         new("exit", exit, 42, "Objet", 10, new Money(200), ["buy"], PlanStepState.Pending)],
        [new(PlanResourceKind.Cash, "cash", 0, new Money(1_100))], Money.Zero, new Money(1_100), 0, 0, 1, 1, true, []),
        Now, new Money(5_000), new Dictionary<string, long>());
}
