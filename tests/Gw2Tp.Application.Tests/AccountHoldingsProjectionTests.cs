using Gw2Tp.Application.AccountEvidence;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.Plans;
using Gw2Tp.Domain.Finance;
using Gw2Tp.Testing;
using Xunit;
using static Gw2Tp.Testing.HoldingsEvidenceFixture;

namespace Gw2Tp.Application.Tests;

public sealed class AccountHoldingsProjectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly AccountHoldingsProjector projector = new();
    private AccountHoldingsProjection Project(AccountHoldingsSnapshot value, DateTimeOffset? at = null) =>
        projector.Project(value, at ?? Now, Generation, Incarnation);

    [Theory]
    [InlineData(6, 4, 6)]
    [InlineData(10, 10, 10)]
    [InlineData(150, 70, 150)]
    public void Independent_locations_are_observed_but_only_the_largest_is_admitted(int bank, int bag, int expected)
    {
        var snapshot = Snapshot(Now, [Item(10, bank), Item(10, bag, AccountHoldingsSource.CharacterInventory, A.ActorId)], categories: new Dictionary<int, HoldingsItemCategory> { [10] = HoldingsItemCategory.Commodity });
        var result = Project(snapshot);
        var item = Assert.Single(result.Items);
        Assert.Equal(bank + bag, item.ObservedQuantity);
        Assert.Equal(expected, item.Admission!.AdmissionQuantity);
        Assert.Equal(Math.Min(bank, bag), item.OmittedQuantity);
        Assert.Contains(HoldingsAdmissionReason.IndependentLocationsOmitted, item.Admission.Reasons);
        Assert.Equal(expected, result.Quantities["2:10"]);
        Assert.Equal(item.Admission.Observation.Location, Assert.Single(result.Items).Admission!.Observation.Location);
    }

    [Fact]
    public void Keep_quantity_is_applied_once_to_the_cap_and_delivery_never_increases_it()
    {
        var snapshot = Snapshot(Now, [Item(10, 150), Item(10, 150, AccountHoldingsSource.MaterialStorage)],
            categories: new Dictionary<int, HoldingsItemCategory> { [10] = HoldingsItemCategory.Commodity }, keep: new Dictionary<int, int> { [10] = 100 }, deliveryQuantity: 7);
        var result = Project(snapshot);
        Assert.Equal(300, Assert.Single(result.Items).ObservedQuantity);
        Assert.Equal(50, result.Quantities["2:10"]);
        Assert.Equal(50, result.TradeableQuantities["2:10"]);
        Assert.Equal(7, Assert.Single(result.UncollectedDelivery!.ObservedTotals).Quantity);
        Assert.Equal(250, result.UncollectedDelivery.CoinsCopper);
        Assert.Empty(Project(Snapshot(Now, deliveryQuantity: 7)).Quantities);
    }

    [Fact]
    public void Matching_equipment_partial_templates_and_unknown_category_fail_closed_but_known_commodity_survives()
    {
        var actor = Actor(A, Now);
        var reference = new EquipmentProtectionObservation(A.ActorId, 0, 10, "Helm", EquipmentObservedLocation.Equipped, [1], null, AccountItemBinding.AccountBound, null, [new(HoldingsComponentKind.Upgrade, 0, 11)]);
        actor = actor with { Equipment = actor.Equipment with { Value = new[] { reference } }, EquipmentTabs = actor.EquipmentTabs with { Completeness = EvidenceCompleteness.Partial } };
        var rows = new[] { Item(10, 1), Item(11, 1, slot: 1), Item(12, 1, slot: 2), Item(13, 3, slot: 3), Item(14, 1, slot: 4) };
        var snapshot = Snapshot(Now, rows, [actor], new Dictionary<int, HoldingsItemCategory> { [10] = HoldingsItemCategory.Equipment, [11] = HoldingsItemCategory.Commodity,
            [12] = HoldingsItemCategory.Equipment, [13] = HoldingsItemCategory.Commodity });
        var result = Project(snapshot);
        Assert.Equal(3, Assert.Single(result.Quantities).Value);
        Assert.Equal("2:13", Assert.Single(result.Quantities).Key);
        Assert.Equal(7, result.Items.Sum(item => item.ObservedQuantity)); // references are not physical units
        Assert.All(result.Items.Where(item => item.ItemId != 13), item => Assert.Null(item.Admission));
    }

    [Fact]
    public void Binding_and_actor_access_remain_attached_to_the_single_selected_location()
    {
        var row = Item(10, 7, binding: AccountItemBinding.CharacterBound, bound: B);
        var snapshot = Snapshot(Now, [row], [Actor(A, Now), Actor(B, Now)]);
        var result = Project(snapshot);
        var selected = Assert.Single(result.Items).Admission!;
        Assert.Equal(7, selected.AdmissionQuantity);
        Assert.Equal(0, selected.TradeableQuantity);
        Assert.False(result.CanAccess(selected, A.ActorId));
        Assert.True(result.CanAccess(selected, B.ActorId));
        var sale = Candidate(PlanStepAction.SellNow, 7);
        Assert.False(PlanHoldingsAdmission.Authorize(sale, result).IsHardEligible);
        Assert.False(PlanHoldingsAdmission.Authorize(Candidate(PlanStepAction.Craft, 7), result, A.ActorId, [Recipe()]).IsHardEligible);
    }

    [Fact]
    public void Per_source_clock_not_maximum_timestamp_controls_freshness_and_live_physical_is_always_partial()
    {
        var snapshot = Snapshot(Now, [Item(10, 4)]);
        snapshot = snapshot with { Capture = snapshot.Capture with { Bank = snapshot.Capture.Bank with { Fetch = new(Now.AddMinutes(-16), Now, null) } } };
        var result = Project(snapshot);
        Assert.Empty(result.Quantities);
        var physical = AccountHoldingsProjector.PhysicalEvidence(result);
        Assert.Equal(PlanEvidenceCompleteness.Partial, physical.Provenance.Completeness);
        Assert.Equal(Now.AddMinutes(-16), physical.Provenance.FetchedAtUtc);
        Assert.Null(physical.Provenance.UpstreamObservedAtUtc);
        Assert.Empty(projector.Project(snapshot, Now, Guid.NewGuid().ToString("N"), Incarnation).Quantities);
        Assert.Empty(Project(snapshot, Now.AddMinutes(16)).Quantities);
    }

    [Fact]
    public void A_location_move_pauses_the_committed_instruction_and_does_not_fabricate_accounting_events()
    {
        var original = Project(Snapshot(Now, [Item(10, 10)]));
        var orchestration = new PlanOrchestrationService();
        var authorized = PlanHoldingsAdmission.Authorize(Candidate(PlanStepAction.SellNow, 10), original);
        Assert.True(authorized.IsHardEligible);
        var plan = orchestration.Start(authorized, Now);
        var moved = Project(Snapshot(Now, [Item(10, 10, AccountHoldingsSource.MaterialStorage)]));
        var paused = PlanHoldingsAdmission.Revalidate(plan, moved);
        Assert.Equal(PlanState.RecheckRequired, paused.State);
        Assert.Equal("holdings_source_changed", paused.HoldingsEligibilityReason);
        Assert.Empty(paused.Events);
        Assert.Equal(original.Snapshot.Capture.Bank.Value!.Items[0].Location, Assert.Single(paused.HoldingsAuthority!.Commitments).Location);
        Assert.Equal(10, Assert.Single(PlanOrchestrationService.OutstandingReservations(paused)).Quantity);
    }

    [Fact]
    public void Provisional_buy_and_craft_output_do_not_admit_physical_stock_or_retire_on_capture_replay()
    {
        var orchestration = new PlanOrchestrationService();
        var buy = Candidate(PlanStepAction.BuyNow, 10) with { Requirements = [new(PlanResourceKind.Cash, "cash", 0, new Money(100))] };
        var plan = orchestration.ReportStep(orchestration.Start(buy, Now), 10, new Money(10), Now);
        Assert.Equal(10, PlanOrchestrationService.ProjectEffectiveResources(new Money(1000), new Dictionary<string, long>(), plan.Events).Quantities["2:10"]);
        var safe = AccountHoldingsProjector.AdmissibleResources(new Money(1000), new Dictionary<string, long>(), plan.Events);
        Assert.Empty(safe.Quantities);
        Assert.Equal(900, safe.EffectiveCash.Copper);
        var frame = new PlanEvidenceFrame(Scope, Now.AddMinutes(5), AccountHoldingsProjector.PhysicalEvidence(Project(Snapshot(Now, deliveryQuantity: 10))),
            new(new(null, null, null, PlanEvidenceAvailability.Unavailable, PlanEvidenceCompleteness.Unknown, new HashSet<string>()), Money.Zero),
            new(new(null, null, null, PlanEvidenceAvailability.Unavailable, PlanEvidenceCompleteness.Unknown, new HashSet<string>()), []),
            new(new(null, null, null, PlanEvidenceAvailability.Unavailable, PlanEvidenceCompleteness.Unknown, new HashSet<string>()), []));
        var once = orchestration.ReconcileWithVerifiedState(plan, Scope, frame);
        var twice = orchestration.ReconcileWithVerifiedState(once, Scope, frame);
        Assert.True(PlanRecordSemantics.AreEqual(once, twice));
        Assert.Equal(PlanShadowEventState.PendingConfirmation, Assert.Single(twice.Events).State);
    }

    [Fact]
    public void Actor_capability_recipe_or_location_loss_pauses_and_only_fresh_same_prerequisites_resume()
    {
        var original = Project(Snapshot(Now, [Item(10, 7)]));
        var plan = new PlanOrchestrationService().Start(PlanHoldingsAdmission.Authorize(Candidate(PlanStepAction.Craft, 7),
            original, A.ActorId, [Recipe()]), Now);
        var changed = Snapshot(Now.AddSeconds(1), [Item(10, 7)], [Actor(A, Now.AddSeconds(1), capabilities: [new("Artificer", 100, true)])]);
        var paused = PlanHoldingsAdmission.Revalidate(plan, Project(changed, Now.AddSeconds(1)));
        Assert.Equal("crafting_actor_unavailable", paused.HoldingsEligibilityReason);
        Assert.Equal(PlanStepState.RecheckRequired, Assert.Single(paused.Steps).State);
        var restored = PlanHoldingsAdmission.Revalidate(paused, Project(Snapshot(Now.AddSeconds(2), [Item(10, 7)]), Now.AddSeconds(2)));
        Assert.Null(restored.HoldingsEligibilityReason);
        Assert.Equal(PlanState.InProgress, restored.State);
        Assert.Equal(PlanStepState.Current, Assert.Single(restored.Steps).State);
        var unlocked = Snapshot(Now.AddSeconds(3), [Item(10, 7)]);
        unlocked = unlocked with { Capture = unlocked.Capture with { RecipeUnlocks = unlocked.Capture.RecipeUnlocks with { Value = Array.Empty<int>() } } };
        Assert.Equal("crafting_actor_unavailable", PlanHoldingsAdmission.Revalidate(restored, Project(unlocked, Now.AddSeconds(3))).HoldingsEligibilityReason);
    }

    [Fact]
    public void Bought_output_needs_new_physical_evidence_before_consumption_and_sale_proceeds_are_not_wallet_gold()
    {
        var orchestration = new PlanOrchestrationService();
        var proposal = Candidate(PlanStepAction.BuyNow, 1) with { Requirements = [new(PlanResourceKind.Cash, "cash", 0, new Money(10))],
            Steps = [new("buy", PlanStepAction.BuyNow, 10, "Objet", 1, new Money(10), [], PlanStepState.Pending),
                new("sell", PlanStepAction.SellNow, 10, "Objet", 1, new Money(100), ["buy"], PlanStepState.Pending)] };
        var start = orchestration.Start(PlanHoldingsAdmission.Authorize(proposal, Project(Snapshot(Now))), Now);
        var reported = orchestration.ReportStep(start, 1, new Money(10), Now);
        var paused = PlanHoldingsAdmission.Revalidate(reported, Project(Snapshot(Now)));
        Assert.Equal("holdings_inputs_unavailable", paused.HoldingsEligibilityReason);
        var resumed = PlanHoldingsAdmission.Revalidate(paused, Project(Snapshot(Now.AddSeconds(1), [Item(10, 1)]), Now.AddSeconds(1)));
        Assert.Null(resumed.HoldingsEligibilityReason);
        Assert.Equal(AccountHoldingsSource.Bank, Assert.Single(resumed.HoldingsAuthority!.Commitments).Location.Source);
        var sold = orchestration.ReportStep(resumed, 1, new Money(100), Now.AddSeconds(2));
        Assert.Equal(990, AccountHoldingsProjector.AdmissibleResources(new Money(1000), new Dictionary<string, long>(), sold.Events).EffectiveCash.Copper);
    }

    [Fact]
    public void Candidate_reauthorization_cannot_silently_swap_its_owned_location()
    {
        var original = PlanHoldingsAdmission.Authorize(Candidate(PlanStepAction.SellNow, 1), Project(Snapshot(Now, [Item(10, 10)])));
        var moved = Project(Snapshot(Now, [Item(10, 10, AccountHoldingsSource.MaterialStorage)]));
        var changed = PlanHoldingsAdmission.Authorize(original, moved);
        Assert.False(changed.IsHardEligible);
        Assert.Contains("holdings_source_changed", changed.ExclusionReasons);
    }

    private static PlanCandidate Candidate(PlanStepAction action, int quantity) => new("candidate", 1, "candidate", PlanAttention.Active,
        [new("step", action, 10, "Objet", quantity, new Money(10), [], PlanStepState.Pending,
            CraftEffects: action == PlanStepAction.Craft ? [new(PlanResourceKind.Inventory, "10", -quantity, Money.Zero)] : null, RecipeId: action == PlanStepAction.Craft ? 1 : null)],
        [new(PlanResourceKind.Inventory, "10", quantity, Money.Zero)], Money.Zero, Money.Zero, 8000, 0, 1, 1, true, []);
}
