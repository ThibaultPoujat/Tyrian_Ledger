using Gw2Tp.Application.Finance;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Plans;

/// <summary>Deterministic plan selection and reversible local-shadow transitions.</summary>
public sealed class PlanOrchestrationService : IPlanOrchestrationService
{
    public const int MaximumCandidates = 18;
    public static readonly TimeSpan DefaultObservationWindow = TimeSpan.FromMinutes(15);

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
            if (effect.Kind == PlanResourceKind.Cash) cash += effect.Cash;
            else if (effect.Quantity != 0) quantities[Key(effect)] = checked(quantities.GetValueOrDefault(Key(effect)) + effect.Quantity);
        }
        return new PlanEffectiveResources(verifiedCash, cash, quantities);
    }

    /// <summary>Returns only resources still needed by not-yet-reported steps.</summary>
    public static IReadOnlyList<PlanResourceRequirement> OutstandingReservations(PlanRecord plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.State is PlanState.ExecutionComplete or PlanState.Invalid) return [];
        var requirements = new List<PlanResourceRequirement>();
        foreach (var step in plan.Steps.Where(step => step.State is PlanStepState.Pending or PlanStepState.Current))
        {
            var quantity = Math.Max(0, step.Quantity);
            if (quantity == 0) continue;
            var itemId = step.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            switch (step.Action)
            {
                case PlanStepAction.BuyNow:
                case PlanStepAction.PlaceBuyOrder:
                    if (step.UnitPrice is { } buyPrice)
                        requirements.Add(new(PlanResourceKind.Cash, "cash", 0, new Money(checked(buyPrice.Copper * quantity))));
                    break;
                case PlanStepAction.List:
                case PlanStepAction.Relist:
                case PlanStepAction.SellNow:
                    requirements.Add(new(PlanResourceKind.Inventory, itemId, quantity, Money.Zero));
                    if (step.Action is PlanStepAction.List or PlanStepAction.Relist && step.UnitPrice is { } listPrice)
                    {
                        var gross = new Money(checked(listPrice.Copper * quantity));
                        requirements.Add(new(PlanResourceKind.Cash, "cash", 0, Gw2TradingPostFeePolicy.Create().CalculateFees(gross).ListingFee));
                    }
                    break;
            }
        }
        var represented = requirements.Select(Key).ToHashSet(StringComparer.Ordinal);
        requirements.AddRange(plan.Reservations.Where(value => value.Kind is not (PlanResourceKind.Cash or PlanResourceKind.Inventory) && !represented.Contains(Key(value))));
        return requirements;
    }

    public Task<PlanBundleSelection> SelectAsync(
        IReadOnlyList<PlanCandidate> candidates,
        Money availableCash,
        Money hardReserve,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, long>? availableQuantities = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (availableCash.Copper < 0 || hardReserve.Copper < 0 || hardReserve.Copper > availableCash.Copper) throw new ArgumentOutOfRangeException(nameof(availableCash));
        cancellationToken.ThrowIfCancellationRequested();
        var eligible = candidates.Where(IsExecutable)
            .OrderByDescending(candidate => candidate.Utility)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .Take(MaximumCandidates).ToArray();
        var deployable = availableCash - hardReserve;
        var capacities = BuildCapacities(eligible, availableQuantities);
        var softBuffer = new Money(deployable.Copper / 10);
        var best = SelectWithin(eligible, deployable - softBuffer, capacities, cancellationToken);
        if (best.Plans.Count == 0 && eligible.Length > 0) best = SelectWithin(eligible, deployable, capacities, cancellationToken);
        var selectedIds = best.Plans.Select(plan => plan.Id).ToHashSet(StringComparer.Ordinal);
        return Task.FromResult(new PlanBundleSelection(best.Plans, best.Cash, best.Utility,
            candidates.Where(candidate => !selectedIds.Contains(candidate.Id)).Select(candidate => candidate.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            deployable - best.Cash));
    }

    public PlanRecord Start(PlanCandidate candidate, DateTimeOffset startedAtUtc, Money? verifiedCash = null,
        IReadOnlyDictionary<string, long>? verifiedQuantities = null)
    {
        if (!IsExecutable(candidate)) throw new ArgumentException("The candidate is not executable.", nameof(candidate));
        var steps = candidate.Steps.Select((step, index) => step with { State = index == 0 ? PlanStepState.Current : PlanStepState.Pending }).ToArray();
        var state = candidate.Attention == PlanAttention.Passive && steps.Length == 0 ? PlanState.Waiting : PlanState.InProgress;
        return new PlanRecord(candidate.Id, candidate.Version, candidate.SourceOpportunityId, candidate.Attention, state,
            PlanReconciliationState.None, RequireUtc(startedAtUtc), candidate.Requirements, candidate.ModeledProfit, 0, steps, [], candidate.Utility,
            PlanHysteresisPolicy.Default, verifiedCash,
            verifiedQuantities is null ? null : new Dictionary<string, long>(verifiedQuantities, StringComparer.Ordinal), 0, startedAtUtc);
    }

    public PlanRecord ReportStep(PlanRecord plan, int quantity, Money? unitPrice, DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.State is PlanState.ReconciliationRequired or PlanState.RecheckRequired or PlanState.Invalid) throw new InvalidOperationException("The plan cannot accept execution while paused.");
        if (plan.CurrentStepOrdinal < 0 || plan.CurrentStepOrdinal >= plan.Steps.Count) throw new InvalidOperationException("The plan has no executable current step.");
        var step = plan.Steps[plan.CurrentStepOrdinal];
        if (step.State != PlanStepState.Current || quantity <= 0) throw new InvalidOperationException("Only the current step can be reported with a positive quantity.");
        var occurred = RequireUtc(occurredAtUtc);
        var effectiveUnitPrice = unitPrice ?? step.UnitPrice;
        var execution = new PlanExecutionEvent(Guid.NewGuid().ToString("N"), plan.Id, step.Id, plan.Events.Count + 1, occurred, quantity,
            effectiveUnitPrice, EffectsFor(step, quantity, effectiveUnitPrice), PlanShadowEventState.PendingConfirmation, null,
            plan.Events.Where(e => e.State is PlanShadowEventState.PendingConfirmation or PlanShadowEventState.Confirmed).Select(e => e.Id).ToArray(),
            occurred.Add(DefaultObservationWindow));
        var steps = plan.Steps.ToArray();
        steps[plan.CurrentStepOrdinal] = step with { Quantity = quantity, UnitPrice = effectiveUnitPrice, State = PlanStepState.AwaitingConfirmation };
        var next = plan.CurrentStepOrdinal + 1;
        if (next < steps.Length) steps[next] = steps[next] with { State = PlanStepState.Current };
        return plan with { Steps = steps, Events = plan.Events.Append(execution).ToArray(), CurrentStepOrdinal = next,
            State = next < steps.Length ? PlanState.InProgress : PlanState.ExecutionComplete, ReconciliationState = PlanReconciliationState.AwaitingEvidence };
    }

    public PlanRecord UndoLastStep(PlanRecord plan, DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var last = plan.Events.LastOrDefault(e => e.State == PlanShadowEventState.PendingConfirmation);
        if (last is null) throw new InvalidOperationException("There is no unconfirmed execution to undo.");
        var invalidated = plan.Events.Where(e => e.DependsOnEventIds.Contains(last.Id, StringComparer.Ordinal) && e.State == PlanShadowEventState.PendingConfirmation).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var events = plan.Events.Select(e => e.Id == last.Id ? e with { State = PlanShadowEventState.Reversed } : invalidated.Contains(e.Id) ? e with { State = PlanShadowEventState.Invalidated } : e).ToArray();
        var ordinal = plan.Steps.Select((step, index) => (step, index)).Single(pair => pair.step.Id == last.StepId).index;
        var steps = plan.Steps.Select((step, index) => index >= ordinal ? step with { State = index == ordinal ? PlanStepState.Current : PlanStepState.Invalidated } : step).ToArray();
        return plan with { Events = events, Steps = steps, CurrentStepOrdinal = ordinal, State = PlanState.InProgress, ReconciliationState = PlanReconciliationState.None };
    }

    /// <summary>Applies verified account evidence to pending events without double-counting confirmed effects.</summary>
    public PlanRecord ReconcileWithVerifiedState(PlanRecord plan, Money verifiedCash, IReadOnlyDictionary<string, long> verifiedQuantities, DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(verifiedQuantities);
        var observed = RequireUtc(observedAtUtc);
        var baselineCash = plan.BaselineVerifiedCash ?? verifiedCash;
        var baselineQuantities = plan.BaselineVerifiedQuantities ?? verifiedQuantities;
        var expectedCash = baselineCash;
        var expectedQuantities = new Dictionary<string, long>(baselineQuantities, StringComparer.Ordinal);
        var events = plan.Events.ToArray();
        var contradictions = 0;
        foreach (var execution in plan.Events.OrderBy(value => value.Sequence))
        {
            var priorQuantities = new Dictionary<string, long>(expectedQuantities, StringComparer.Ordinal);
            if (execution.State is not (PlanShadowEventState.PendingConfirmation or PlanShadowEventState.Confirmed)) continue;
            foreach (var effect in EffectiveEffects(execution)) Apply(expectedQuantities, ref expectedCash, effect);
            if (execution.State != PlanShadowEventState.PendingConfirmation) continue;
            if (MatchesExactly(expectedCash, expectedQuantities, verifiedCash, verifiedQuantities, execution.Effects) || MatchesCompatibleQuantity(execution, priorQuantities, verifiedQuantities))
            {
                var index = Array.FindIndex(events, value => value.Id == execution.Id);
                events[index] = execution with { State = PlanShadowEventState.Confirmed, VerifiedQuantity = VerifiedQuantity(execution, baselineQuantities, verifiedQuantities), VerifiedUnitPrice = execution.UnitPrice };
            }
            else if (execution.ExpectedObservableUntilUtc is { } deadline && observed > deadline) contradictions++;
        }
        var contradictionCount = contradictions == 0 ? 0 : plan.ConsecutiveContradictionCount + 1;
        var state = plan.State;
        var reconciliation = events.Any(e => e.State == PlanShadowEventState.PendingConfirmation) ? PlanReconciliationState.AwaitingEvidence : PlanReconciliationState.Compatible;
        if (contradictionCount >= 2) { state = PlanState.ReconciliationRequired; reconciliation = PlanReconciliationState.Contradicted; }
        else if (contradictions > 0) state = PlanState.RecheckRequired;
        return plan with { Events = events, State = state, ReconciliationState = reconciliation, BaselineVerifiedCash = baselineCash,
            BaselineVerifiedQuantities = new Dictionary<string, long>(baselineQuantities, StringComparer.Ordinal), ConsecutiveContradictionCount = contradictionCount, LastObservedAtUtc = observed };
    }

    /// <summary>Freezes the current step and marks it for recheck only on material evidence loss.</summary>
    public PlanRecord ApplyRefresh(PlanRecord plan, PlanCandidate? currentCandidate, bool evidenceReady)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!evidenceReady || plan.State is PlanState.ReconciliationRequired or PlanState.Invalid or PlanState.ExecutionComplete) return plan;
        var current = plan.Steps.FirstOrDefault(step => step.State == PlanStepState.Current);
        var replacement = currentCandidate?.Steps.FirstOrDefault(step => step.Id.EndsWith($":{plan.CurrentStepOrdinal + 1}", StringComparison.Ordinal));
        var materiallyChanged = current is not null && (replacement is null || replacement.Action != current.Action || replacement.ItemId != current.ItemId || replacement.Quantity != current.Quantity || replacement.UnitPrice?.Copper != current.UnitPrice?.Copper);
        var improvementThreshold = Math.Max(1, Math.Abs(plan.BaselineUtility) * plan.HysteresisPolicy.MaterialImprovementBasisPoints / 10_000);
        var materiallyImproved = currentCandidate is not null && currentCandidate.Utility >= plan.BaselineUtility + improvementThreshold;
        return materiallyChanged || materiallyImproved
            ? plan with { State = PlanState.RecheckRequired, ReconciliationState = PlanReconciliationState.AwaitingEvidence }
            : plan;
    }

    public PlanRecord Reconcile(PlanRecord plan, IReadOnlyCollection<string> confirmedEventIds, bool materiallyContradicted)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(confirmedEventIds);
        if (materiallyContradicted) return plan with { State = PlanState.ReconciliationRequired, ReconciliationState = PlanReconciliationState.Contradicted };
        var confirmed = confirmedEventIds.ToHashSet(StringComparer.Ordinal);
        var events = plan.Events.Select(e => confirmed.Contains(e.Id) && e.State == PlanShadowEventState.PendingConfirmation ? e with { State = PlanShadowEventState.Confirmed } : e).ToArray();
        var steps = plan.Steps.Select(step => events.Any(e => e.StepId == step.Id && e.State == PlanShadowEventState.Confirmed) ? step with { State = PlanStepState.Confirmed } : step).ToArray();
        return plan with { Events = events, Steps = steps, ReconciliationState = events.Any(e => e.State == PlanShadowEventState.PendingConfirmation) ? PlanReconciliationState.AwaitingEvidence : PlanReconciliationState.Compatible };
    }

    private static bool IsExecutable(PlanCandidate candidate) => candidate.IsHardEligible && (candidate.Steps.Count > 0 || candidate.Attention == PlanAttention.Passive) && candidate.Requirements.All(requirement => requirement.Quantity >= 0 && requirement.Cash.Copper >= 0) &&
        (candidate.Attention != PlanAttention.Active || candidate.Steps.Select((step, index) => (step, index)).All(pair => pair.step.Action != PlanStepAction.PlaceBuyOrder || pair.index == candidate.Steps.Count - 1));

    private static Selection SelectWithin(PlanCandidate[] candidates, Money capacity, IReadOnlyDictionary<string, long> quantities, CancellationToken cancellationToken)
    {
        var best = new Selection([], Money.Zero, 0, new Dictionary<string, long>(StringComparer.Ordinal));
        Search(candidates, 0, capacity, [], Money.Zero, 0, new Dictionary<string, long>(StringComparer.Ordinal), quantities, ref best, cancellationToken);
        return best;
    }

    private static void Search(PlanCandidate[] candidates, int index, Money capacity, List<PlanCandidate> selected, Money cash, int utility, Dictionary<string, long> quantities, IReadOnlyDictionary<string, long> capacities, ref Selection best, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (index == candidates.Length)
        {
            var current = new Selection(selected.ToArray(), cash, utility, quantities);
            if (current.IsBetterThan(best)) best = current;
            return;
        }
        Search(candidates, index + 1, capacity, selected, cash, utility, quantities, capacities, ref best, cancellationToken);
        var candidate = candidates[index];
        var candidateCash = candidate.Requirements.Aggregate(Money.Zero, (sum, requirement) => sum + requirement.Cash);
        if ((cash + candidateCash).Copper > capacity.Copper || candidate.Requirements.Any(requirement => requirement.Quantity > 0 && checked(quantities.GetValueOrDefault(Key(requirement)) + requirement.Quantity) > capacities.GetValueOrDefault(Key(requirement), long.MaxValue))) return;
        var nextQuantities = new Dictionary<string, long>(quantities, StringComparer.Ordinal);
        foreach (var requirement in candidate.Requirements.Where(requirement => requirement.Quantity > 0)) nextQuantities[Key(requirement)] = checked(nextQuantities.GetValueOrDefault(Key(requirement)) + requirement.Quantity);
        selected.Add(candidate);
        Search(candidates, index + 1, capacity, selected, cash + candidateCash, checked(utility + (int)Math.Clamp(candidate.Utility, int.MinValue, int.MaxValue)), nextQuantities, capacities, ref best, cancellationToken);
        selected.RemoveAt(selected.Count - 1);
    }

    private static IReadOnlyDictionary<string, long> BuildCapacities(PlanCandidate[] candidates, IReadOnlyDictionary<string, long>? supplied)
    {
        var capacities = supplied is null ? new Dictionary<string, long>(StringComparer.Ordinal) : new Dictionary<string, long>(supplied, StringComparer.Ordinal);
        foreach (var requirement in candidates.SelectMany(candidate => candidate.Requirements).Where(value => value.Quantity > 0)) if (!capacities.ContainsKey(Key(requirement))) capacities[Key(requirement)] = requirement.Quantity;
        return capacities;
    }

    private static IReadOnlyList<PlanResourceRequirement> EffectsFor(PlanStep step, int quantity, Money? unitPrice)
    {
        var gross = unitPrice is null ? Money.Zero : new Money(checked(unitPrice.Value.Copper * quantity));
        var fees = Gw2TradingPostFeePolicy.Create().CalculateFees(gross);
        var item = step.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return step.Action switch
        {
            PlanStepAction.BuyNow => [new(PlanResourceKind.Cash, "cash", 0, -gross), new(PlanResourceKind.Inventory, item, quantity, Money.Zero)],
            PlanStepAction.PlaceBuyOrder => [new(PlanResourceKind.Cash, "cash", 0, -gross)],
            PlanStepAction.SellNow => [new(PlanResourceKind.Inventory, item, -quantity, Money.Zero), new(PlanResourceKind.Cash, "cash", 0, gross - fees.ListingFee - fees.ExchangeFee)],
            PlanStepAction.List or PlanStepAction.Relist => [new(PlanResourceKind.Inventory, item, -quantity, Money.Zero), new(PlanResourceKind.Cash, "cash", 0, -fees.ListingFee)],
            _ => [],
        };
    }

    private static bool MatchesExactly(Money expectedCash, IReadOnlyDictionary<string, long> expectedQuantities, Money actualCash, IReadOnlyDictionary<string, long> actualQuantities, IReadOnlyList<PlanResourceRequirement> effects)
    {
        if (effects.Any(effect => effect.Kind == PlanResourceKind.Cash) && expectedCash.Copper != actualCash.Copper) return false;
        return effects.Where(effect => effect.Quantity != 0).All(effect => actualQuantities.TryGetValue(Key(effect), out var actual) && actual == expectedQuantities.GetValueOrDefault(Key(effect)));
    }

    private static bool MatchesCompatibleQuantity(PlanExecutionEvent execution, IReadOnlyDictionary<string, long> baseline, IReadOnlyDictionary<string, long> actual)
    {
        var quantityEffects = execution.Effects.Where(effect => effect.Quantity != 0).ToArray();
        if (quantityEffects.Length == 0) return false;
        return quantityEffects.All(effect => actual.TryGetValue(Key(effect), out var observed) && (effect.Quantity > 0 ? observed - baseline.GetValueOrDefault(Key(effect)) > 0 && observed - baseline.GetValueOrDefault(Key(effect)) <= effect.Quantity : observed - baseline.GetValueOrDefault(Key(effect)) < 0 && observed - baseline.GetValueOrDefault(Key(effect)) >= effect.Quantity));
    }

    private static int? VerifiedQuantity(PlanExecutionEvent execution, IReadOnlyDictionary<string, long> baseline, IReadOnlyDictionary<string, long> actual)
    {
        var effect = execution.Effects.FirstOrDefault(value => value.Quantity != 0);
        if (effect is null || !actual.TryGetValue(Key(effect), out var observed)) return null;
        var delta = observed - baseline.GetValueOrDefault(Key(effect));
        return delta == 0 ? null : (int)Math.Clamp(Math.Abs(delta), 0, int.MaxValue);
    }

    private static void Apply(Dictionary<string, long> quantities, ref Money cash, PlanResourceRequirement effect)
    {
        if (effect.Kind == PlanResourceKind.Cash) cash += effect.Cash;
        else if (effect.Quantity != 0) quantities[Key(effect)] = checked(quantities.GetValueOrDefault(Key(effect)) + effect.Quantity);
    }

    private static IEnumerable<PlanResourceRequirement> EffectiveEffects(PlanExecutionEvent execution)
    {
        if (execution.State != PlanShadowEventState.Confirmed || execution.VerifiedQuantity is not { } verifiedQuantity || execution.Quantity <= 0)
        {
            return execution.Effects;
        }
        return execution.Effects.Select(effect =>
        {
            if (effect.Quantity == 0) return effect;
            var sign = effect.Quantity < 0 ? -1 : 1;
            var quantity = checked(sign * verifiedQuantity);
            var cash = effect.Cash;
            if (cash.Copper != 0) cash = new Money(checked(cash.Copper * verifiedQuantity / execution.Quantity));
            return effect with { Quantity = quantity, Cash = cash };
        });
    }

    private static string Key(PlanResourceRequirement requirement) => $"{(int)requirement.Kind}:{requirement.ResourceId}";
    private static DateTimeOffset RequireUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero ? value : throw new ArgumentException("Timestamp must be UTC.", nameof(value));

    private sealed record Selection(IReadOnlyList<PlanCandidate> Plans, Money Cash, int Utility, IReadOnlyDictionary<string, long> Quantities)
    {
        public bool IsBetterThan(Selection other) => Utility > other.Utility || Utility == other.Utility && (Plans.Count > other.Plans.Count || Plans.Count == other.Plans.Count && string.CompareOrdinal(string.Join('|', Plans.Select(p => p.Id)), string.Join('|', other.Plans.Select(p => p.Id))) < 0);
    }
}
