using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Plans;

/// <summary>Deterministic plan selection and reversible local-shadow transitions.</summary>
public sealed class PlanOrchestrationService : IPlanOrchestrationService
{
    public const int MaximumCandidates = 18;

    /// <summary>Applies only unconfirmed local events; confirmed evidence is already in the verified snapshot.</summary>
    public static PlanEffectiveResources ProjectEffectiveResources(
        Money verifiedCash,
        IReadOnlyDictionary<string, long> verifiedQuantities,
        IReadOnlyCollection<PlanExecutionEvent> events)
    {
        ArgumentNullException.ThrowIfNull(verifiedQuantities);
        ArgumentNullException.ThrowIfNull(events);
        var cash = verifiedCash;
        var quantities = new Dictionary<string, long>(verifiedQuantities, StringComparer.Ordinal);
        foreach (var effect in events.Where(value => value.State == PlanShadowEventState.PendingConfirmation)
                     .OrderBy(value => value.Sequence).SelectMany(value => value.Effects))
        {
            cash += effect.Cash;
            if (effect.Quantity != 0) quantities[Key(effect)] = checked(quantities.GetValueOrDefault(Key(effect)) + effect.Quantity);
        }
        return new PlanEffectiveResources(verifiedCash, cash, quantities);
    }

    public Task<PlanBundleSelection> SelectAsync(IReadOnlyList<PlanCandidate> candidates, Money availableCash, Money hardReserve, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (availableCash.Copper < 0 || hardReserve.Copper < 0 || hardReserve.Copper > availableCash.Copper) throw new ArgumentOutOfRangeException(nameof(availableCash));
        cancellationToken.ThrowIfCancellationRequested();
        var eligible = candidates.Where(IsExecutable).OrderBy(c => c.Id, StringComparer.Ordinal).Take(MaximumCandidates).ToArray();
        var deployable = availableCash - hardReserve;
        var best = new Selection([], Money.Zero, 0, new Dictionary<string, long>(StringComparer.Ordinal));
        Search(eligible, 0, deployable, [], Money.Zero, 0, new Dictionary<string, long>(StringComparer.Ordinal), ref best);
        var selectedIds = best.Plans.Select(plan => plan.Id).ToHashSet(StringComparer.Ordinal);
        return Task.FromResult(new PlanBundleSelection(best.Plans, best.Cash, best.Utility,
            candidates.Where(candidate => !selectedIds.Contains(candidate.Id)).Select(candidate => candidate.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            deployable - best.Cash));
    }

    public PlanRecord Start(PlanCandidate candidate, DateTimeOffset startedAtUtc)
    {
        if (!IsExecutable(candidate)) throw new ArgumentException("The candidate is not executable.", nameof(candidate));
        var steps = candidate.Steps.Select((step, index) => step with { State = index == 0 ? PlanStepState.Current : PlanStepState.Pending }).ToArray();
        var state = candidate.Attention == PlanAttention.Passive && steps.Length == 0 ? PlanState.Waiting : PlanState.InProgress;
        return new PlanRecord(candidate.Id, candidate.Version, candidate.SourceOpportunityId, candidate.Attention, state,
            PlanReconciliationState.None, RequireUtc(startedAtUtc), candidate.Requirements, candidate.ModeledProfit,
            0, steps, [], candidate.Utility, PlanHysteresisPolicy.Default);
    }

    public PlanRecord ReportStep(PlanRecord plan, int quantity, Money? unitPrice, DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.State is PlanState.ReconciliationRequired or PlanState.RecheckRequired or PlanState.Invalid) throw new InvalidOperationException("The plan cannot accept execution while paused.");
        if (plan.CurrentStepOrdinal < 0 || plan.CurrentStepOrdinal >= plan.Steps.Count) throw new InvalidOperationException("The plan has no executable current step.");
        var step = plan.Steps[plan.CurrentStepOrdinal];
        if (step.State != PlanStepState.Current || quantity <= 0) throw new InvalidOperationException("Only the current step can be reported.");
        var eventId = Guid.NewGuid().ToString("N");
        var effects = EffectsFor(step, quantity, unitPrice ?? step.UnitPrice);
        var dependencies = plan.Events.Where(e => e.State is PlanShadowEventState.PendingConfirmation or PlanShadowEventState.Confirmed).Select(e => e.Id).ToArray();
        var execution = new PlanExecutionEvent(eventId, plan.Id, step.Id, plan.Events.Count + 1, RequireUtc(occurredAtUtc), quantity,
            unitPrice ?? step.UnitPrice, effects, PlanShadowEventState.PendingConfirmation, null, dependencies);
        var steps = plan.Steps.ToArray();
        steps[plan.CurrentStepOrdinal] = step with { State = PlanStepState.AwaitingConfirmation };
        var next = plan.CurrentStepOrdinal + 1;
        if (next < steps.Length) steps[next] = steps[next] with { State = PlanStepState.Current };
        var planState = next < steps.Length ? PlanState.InProgress : PlanState.ExecutionComplete;
        return plan with { Steps = steps, Events = plan.Events.Append(execution).ToArray(), CurrentStepOrdinal = next, State = planState, ReconciliationState = PlanReconciliationState.AwaitingEvidence };
    }

    public PlanRecord UndoLastStep(PlanRecord plan, DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var last = plan.Events.LastOrDefault(e => e.State == PlanShadowEventState.PendingConfirmation);
        if (last is null) throw new InvalidOperationException("There is no unconfirmed execution to undo.");
        var invalidated = plan.Events.Where(e => e.DependsOnEventIds.Contains(last.Id, StringComparer.Ordinal) && e.State == PlanShadowEventState.PendingConfirmation).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var events = plan.Events.Select(e => e.Id == last.Id
                ? e with { State = PlanShadowEventState.Reversed }
                : invalidated.Contains(e.Id) ? e with { State = PlanShadowEventState.Invalidated } : e).ToArray();
        var ordinal = plan.Steps.Select((step, index) => (step, index)).Single(pair => pair.step.Id == last.StepId).index;
        var steps = plan.Steps.Select((step, index) => index >= ordinal ? step with { State = index == ordinal ? PlanStepState.Current : PlanStepState.Invalidated } : step).ToArray();
        return plan with { Events = events, Steps = steps, CurrentStepOrdinal = ordinal, State = PlanState.InProgress, ReconciliationState = PlanReconciliationState.None };
    }

    public PlanRecord Reconcile(PlanRecord plan, IReadOnlyCollection<string> confirmedEventIds, bool materiallyContradicted)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(confirmedEventIds);
        if (materiallyContradicted) return plan with { State = PlanState.ReconciliationRequired, ReconciliationState = PlanReconciliationState.Contradicted };
        var confirmed = confirmedEventIds.ToHashSet(StringComparer.Ordinal);
        var events = plan.Events.Select(e => confirmed.Contains(e.Id) && e.State == PlanShadowEventState.PendingConfirmation ? e with { State = PlanShadowEventState.Confirmed } : e).ToArray();
        var steps = plan.Steps.Select(step => events.Any(e => e.StepId == step.Id && e.State == PlanShadowEventState.Confirmed)
            ? step with { State = PlanStepState.Confirmed } : step).ToArray();
        return plan with { Events = events, Steps = steps, ReconciliationState = events.Any(e => e.State == PlanShadowEventState.PendingConfirmation) ? PlanReconciliationState.AwaitingEvidence : PlanReconciliationState.Compatible };
    }

    private static bool IsExecutable(PlanCandidate candidate) => candidate.IsHardEligible && candidate.Steps.Count > 0 &&
        candidate.Requirements.All(requirement => requirement.Quantity >= 0 && requirement.Cash.Copper >= 0) &&
        (candidate.Attention != PlanAttention.Active || candidate.Steps.Select((step, index) => (step, index)).All(pair => pair.step.Action != PlanStepAction.PlaceBuyOrder || pair.index == candidate.Steps.Count - 1));

    private static void Search(PlanCandidate[] candidates, int index, Money capacity, List<PlanCandidate> selected, Money cash, int utility, Dictionary<string, long> quantities, ref Selection best)
    {
        if (index == candidates.Length) { var current = new Selection(selected.ToArray(), cash, utility, quantities); if (current.IsBetterThan(best)) best = current; return; }
        Search(candidates, index + 1, capacity, selected, cash, utility, quantities, ref best);
        var candidate = candidates[index]; var candidateCash = candidate.Requirements.Aggregate(Money.Zero, (sum, requirement) => sum + requirement.Cash);
        if ((cash + candidateCash).Copper > capacity.Copper || candidate.Requirements.Any(requirement => requirement.Quantity > 0 && quantities.GetValueOrDefault(Key(requirement)) > 0)) return;
        var nextQuantities = new Dictionary<string, long>(quantities, StringComparer.Ordinal);
        foreach (var requirement in candidate.Requirements.Where(requirement => requirement.Quantity > 0)) nextQuantities[Key(requirement)] = checked(nextQuantities.GetValueOrDefault(Key(requirement)) + requirement.Quantity);
        selected.Add(candidate); Search(candidates, index + 1, capacity, selected, cash + candidateCash, checked(utility + (int)Math.Clamp(candidate.Utility, int.MinValue, int.MaxValue)), nextQuantities, ref best); selected.RemoveAt(selected.Count - 1);
    }

    private static IReadOnlyList<PlanResourceRequirement> EffectsFor(PlanStep step, int quantity, Money? unitPrice)
    {
        var cost = unitPrice is null ? Money.Zero : new Money(checked(unitPrice.Value.Copper * quantity));
        return step.Action switch
        {
            PlanStepAction.BuyNow => [new(PlanResourceKind.Cash, "cash", 0, -cost), new(PlanResourceKind.Inventory, step.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture), quantity, Money.Zero)],
            PlanStepAction.PlaceBuyOrder => [new(PlanResourceKind.Cash, "cash", 0, -cost)],
            PlanStepAction.SellNow => [new(PlanResourceKind.Inventory, step.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture), -quantity, Money.Zero), new(PlanResourceKind.Cash, "cash", 0, cost)],
            PlanStepAction.List or PlanStepAction.Relist => [new(PlanResourceKind.Inventory, step.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture), -quantity, Money.Zero)],
            _ => [],
        };
    }
    private static string Key(PlanResourceRequirement requirement) => $"{(int)requirement.Kind}:{requirement.ResourceId}";
    private static DateTimeOffset RequireUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero ? value : throw new ArgumentException("Timestamp must be UTC.", nameof(value));

    private sealed record Selection(IReadOnlyList<PlanCandidate> Plans, Money Cash, int Utility, IReadOnlyDictionary<string, long> Quantities)
    {
        public bool IsBetterThan(Selection other) => Utility > other.Utility || Utility == other.Utility && (Plans.Count > other.Plans.Count || Plans.Count == other.Plans.Count && string.CompareOrdinal(string.Join('|', Plans.Select(p => p.Id)), string.Join('|', other.Plans.Select(p => p.Id))) < 0);
    }
}
