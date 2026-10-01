using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.Plans;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.AccountEvidence;

/// <summary>One physical resource/actor authority for candidate, resume and consuming-step eligibility.</summary>
public static class PlanHoldingsAdmission
{
    public static PlanCandidate Authorize(PlanCandidate candidate, AccountHoldingsProjection? projection,
        string? actorId = null, IReadOnlyList<CraftingRecipe>? recipes = null)
    {
        PlanCandidate Reject(string reason) => candidate with { IsHardEligible = false, ExclusionReasons = candidate.ExclusionReasons.Append(reason).Distinct().ToArray() };
        if (projection?.IsCurrentGeneration != true) return Reject("holdings_evidence_unavailable");
        var commitments = new List<PlanHoldingsCommitment>();
        if (!PlanOrchestrationService.TryAggregateResourceDemands(candidate.Requirements, out var demands)) return Reject("holdings_invalid_demand");
        foreach (var demand in demands.Values.Where(value => value.Kind == PlanResourceKind.Inventory && value.Quantity > 0))
        {
            if (!int.TryParse(demand.ResourceId, out var id)) return Reject("holdings_invalid_demand");
            var selected = projection.Items.SingleOrDefault(item => item.ItemId == id)?.Admission;
            var sale = candidate.Steps.Any(step => step.ItemId == id && step.Action is PlanStepAction.List or PlanStepAction.Relist or PlanStepAction.SellNow);
            if (selected is null || demand.Quantity > (sale ? selected.TradeableQuantity : selected.AdmissionQuantity) || !projection.CanAccess(selected, actorId))
                return Reject("holdings_inputs_unavailable");
            commitments.Add(Commit(selected, demand.Quantity));
        }
        if (candidate.HoldingsAuthority is { } previous && previous.Commitments.Any(committed =>
            !commitments.Any(selected => selected.ItemId == committed.ItemId &&
                AccountHoldingsProjector.SameLocation(selected.Location, committed.Location) &&
                selected.Binding == committed.Binding && selected.BoundActorId == committed.BoundActorId)))
            return Reject("holdings_source_changed");
        var snapshot = projection.Snapshot;
        return candidate with { HoldingsAuthority = new(snapshot.Capture.AccountScope.AccountId, snapshot.StoreIncarnation,
            snapshot.Generation, commitments, actorId, recipes,
            projection.FreshCapture.Characters.SingleOrDefault(actor => actor.Actor.ActorId == actorId)?.Actor.DisplayName),
            Steps = candidate.Steps.Select(step => step with { CraftingActorId = step.Action == PlanStepAction.Craft ? actorId : null }).ToArray() };
    }

    public static PlanRecord Revalidate(PlanRecord plan, AccountHoldingsProjection? projection,
        IReadOnlyCollection<PlanExecutionEvent>? accountEvents = null,
        IReadOnlyCollection<PlanResourceRequirement>? otherReservations = null)
    {
        if (plan.State is PlanState.Invalid or PlanState.ExecutionComplete or PlanState.ReconciliationRequired) return plan;
        // Legacy durable plans cannot confer live physical/actor authority. No preservation migration is needed pre-0.1.
        if (plan.HoldingsAuthority is not { } authority) return Block(plan, "holdings_commitment_required");
        if (projection?.IsCurrentGeneration != true || authority.AccountScopeId != projection.Snapshot.Capture.AccountScope.AccountId)
            return Block(plan, "holdings_evidence_unavailable");
        var commitments = authority.Commitments.ToList();
        var outstanding = PlanOrchestrationService.OutstandingReservations(plan);
        foreach (var commitment in commitments)
        {
            // Already reported consumption no longer needs this physical coordinate.
            if (!outstanding.Any(value => value.Kind == PlanResourceKind.Inventory && value.ResourceId == commitment.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture))) continue;
            var selected = projection.Items.SingleOrDefault(item => item.ItemId == commitment.ItemId)?.Admission;
            if (selected is null || !Same(commitment, selected) || !projection.CanAccess(selected, authority.CraftingActorId))
                return Block(plan, "holdings_source_changed");
        }
        if (authority.CraftingActorId is { } actorId)
        {
            var actor = projection.FreshCapture.Characters.SingleOrDefault(actor => actor.Actor.ActorId == actorId);
            var remainingRecipes = (authority.Recipes ?? []).Where(recipe => plan.Steps.Any(step => step.RecipeId == recipe.RecipeId &&
                step.State is PlanStepState.Current or PlanStepState.Pending or PlanStepState.RecheckRequired)).ToArray();
            if (actor is null || remainingRecipes.Length == 0 && plan.Steps.Any(step => step.Action == PlanStepAction.Craft && step.State is PlanStepState.Current or PlanStepState.Pending or PlanStepState.RecheckRequired))
                return Block(plan, "crafting_actor_unavailable");
            if (remainingRecipes.Length > 0)
            {
                // Restrict selector to the committed real actor, without fabricating aggregate capabilities.
                var restricted = projection.FreshCapture with
                {
                    Roster = projection.FreshCapture.Roster with { Value = new[] { actor.Actor } }, Characters = [actor],
                };
                if (new CraftingActorSelector().Select(restricted, remainingRecipes).Failure != CraftingActorSelectionFailure.None)
                    return Block(plan, "crafting_actor_unavailable");
            }
        }
        var current = plan.CurrentStepOrdinal >= 0 && plan.CurrentStepOrdinal < plan.Steps.Count ? plan.Steps[plan.CurrentStepOrdinal] : null;
        if (current is null) return plan;
        var consumption = current.Action == PlanStepAction.Craft
            ? (current.CraftEffects ?? []).Where(value => value.Kind == PlanResourceKind.Inventory && value.Quantity < 0)
                .Select(value => value with { Quantity = checked(-value.Quantity) }).ToArray()
            : current.Action is PlanStepAction.List or PlanStepAction.Relist or PlanStepAction.SellNow
                ? new[] { new PlanResourceRequirement(PlanResourceKind.Inventory, current.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture), current.Quantity, Money.Zero) } : [];
        if (!PlanOrchestrationService.TryAggregateResourceDemands(consumption, out var required)) return Block(plan, "holdings_invalid_demand");
        var quantities = AccountHoldingsProjector.AdmissibleResources(Money.Zero, projection.Quantities, accountEvents ?? plan.Events).Quantities;
        var reserved = (otherReservations ?? []).Where(value => value.Kind == PlanResourceKind.Inventory).GroupBy(value => value.ResourceId)
            .ToDictionary(group => group.Key, group => checked(group.Sum(value => value.Quantity)), StringComparer.Ordinal);
        foreach (var demand in required.Values)
        {
            if (!int.TryParse(demand.ResourceId, out var itemId)) return Block(plan, "holdings_invalid_demand");
            var selected = projection.Items.SingleOrDefault(item => item.ItemId == itemId)?.Admission;
            var sale = current.Action is PlanStepAction.List or PlanStepAction.Relist or PlanStepAction.SellNow;
            if (selected is null || !projection.CanAccess(selected, authority.CraftingActorId) ||
                demand.Quantity > quantities.GetValueOrDefault($"{(int)PlanResourceKind.Inventory}:{itemId}") - reserved.GetValueOrDefault(demand.ResourceId) ||
                demand.Quantity > (sale ? selected.TradeableQuantity : selected.AdmissionQuantity))
                return Block(plan, "holdings_inputs_unavailable");
            var committed = commitments.SingleOrDefault(value => value.ItemId == itemId);
            if (committed is not null && !Same(committed, selected)) return Block(plan, "holdings_source_changed");
            if (committed is null) commitments.Add(Commit(selected, demand.Quantity));
        }
        var nextAuthority = authority with { Commitments = commitments, Generation = projection.Snapshot.Generation, StoreIncarnation = projection.Snapshot.StoreIncarnation };
        var result = plan with { HoldingsAuthority = nextAuthority };
        if (plan.HoldingsEligibilityReason is not null && plan.State == PlanState.RecheckRequired)
        {
            var steps = plan.Steps.ToArray();
            steps[plan.CurrentStepOrdinal] = current with { State = PlanStepState.Current };
            result = result with { State = PlanState.InProgress, Steps = steps, HoldingsEligibilityReason = null };
        }
        return result;
    }

    private static PlanHoldingsCommitment Commit(HoldingsAdmission value, long quantity) =>
        new(value.ItemId, value.Observation.Location, quantity, value.Observation.Binding, value.Observation.BoundActor?.ActorId);
    private static bool Same(PlanHoldingsCommitment commitment, HoldingsAdmission selected) =>
        AccountHoldingsProjector.SameLocation(commitment.Location, selected.Observation.Location) &&
        commitment.Binding == selected.Observation.Binding && commitment.BoundActorId == selected.Observation.BoundActor?.ActorId;
    private static PlanRecord Block(PlanRecord plan, string reason) => plan with
    {
        State = PlanState.RecheckRequired, HoldingsEligibilityReason = reason,
        Steps = plan.Steps.Select((step, index) => index == plan.CurrentStepOrdinal && step.State == PlanStepState.Current
            ? step with { State = PlanStepState.RecheckRequired } : step).ToArray(),
    };
}
