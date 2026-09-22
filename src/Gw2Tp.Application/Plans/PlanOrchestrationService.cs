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
        foreach (var execution in events.Where(value => value.State is PlanShadowEventState.PendingConfirmation or PlanShadowEventState.PartiallyConfirmed)
                     .OrderBy(value => value.Sequence))
        {
            foreach (var effect in ResidualEffects(execution))
            {
                if (effect.Kind == PlanResourceKind.Cash) cash += effect.Cash;
                else if (effect.Quantity != 0) quantities[Key(effect)] = checked(quantities.GetValueOrDefault(Key(effect)) + effect.Quantity);
            }
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
        requirements.AddRange(plan.Reservations.Where(value => value.Kind is not (PlanResourceKind.Cash or PlanResourceKind.Inventory) &&
            !represented.Contains(Key(value)) && IsGenericReservationOutstanding(plan, value)));
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
        var softBuffer = OpportunityReserve(deployable, eligible);
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
        var steps = candidate.Steps.Select((step, index) => step with { State = index == 0 ? PlanStepState.Current : PlanStepState.Pending,
            IssuedAtUtc = index == 0 ? RequireUtc(startedAtUtc) : null }).ToArray();
        var state = candidate.Attention == PlanAttention.Passive && steps.Length == 0 ? PlanState.Waiting : PlanState.InProgress;
        return new PlanRecord(Guid.NewGuid().ToString("N"), candidate.Version, candidate.SourceOpportunityId, candidate.Attention, state,
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
            plan.Events.Where(e => e.State is PlanShadowEventState.PendingConfirmation or PlanShadowEventState.PartiallyConfirmed or PlanShadowEventState.Confirmed).Select(e => e.Id).ToArray(),
            occurred.Add(DefaultObservationWindow), null, null, ExpectedEvidenceFor(step), step.IssuedAtUtc ?? plan.StartedAtUtc, Action: step.Action);
        var steps = plan.Steps.ToArray();
        steps[plan.CurrentStepOrdinal] = step with { Quantity = quantity, UnitPrice = effectiveUnitPrice,
            State = ExpectedEvidenceFor(step) is null ? PlanStepState.LocallyReported : PlanStepState.AwaitingConfirmation };
        var next = plan.CurrentStepOrdinal + 1;
        if (next < steps.Length) steps[next] = steps[next] with { State = PlanStepState.Current, IssuedAtUtc = occurred };
        return plan with { Steps = steps, Events = plan.Events.Append(execution).ToArray(), CurrentStepOrdinal = next,
            State = next < steps.Length ? PlanState.InProgress : step.Action == PlanStepAction.PlaceBuyOrder ? PlanState.Waiting : PlanState.ExecutionComplete,
            ReconciliationState = PlanReconciliationState.AwaitingEvidence };
    }

    public PlanRecord CancelUnperformedStep(PlanRecord plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.CurrentStepOrdinal < 0 || plan.CurrentStepOrdinal >= plan.Steps.Count || plan.Steps[plan.CurrentStepOrdinal].State != PlanStepState.Current)
            throw new InvalidOperationException("Only the current unperformed step can be cancelled.");
        var steps = plan.Steps.Select((step, index) => index >= plan.CurrentStepOrdinal ? step with { State = PlanStepState.Invalidated } : step).ToArray();
        var hasUnresolvedPriorExecution = plan.Events.Any(value => value.State is PlanShadowEventState.PendingConfirmation or PlanShadowEventState.PartiallyConfirmed);
        return plan with
        {
            Steps = steps,
            CurrentStepOrdinal = -1,
            State = hasUnresolvedPriorExecution ? PlanState.ReconciliationRequired : PlanState.Invalid,
            ReconciliationState = hasUnresolvedPriorExecution ? PlanReconciliationState.AwaitingEvidence : PlanReconciliationState.None,
            IsCancelled = true,
        };
    }

    public PlanRecord UndoLastStep(PlanRecord plan, DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var last = plan.Events.LastOrDefault(e => e.State is PlanShadowEventState.PendingConfirmation or PlanShadowEventState.PartiallyConfirmed);
        if (last is null) throw new InvalidOperationException("There is no unconfirmed execution to undo.");
        if (plan.Events.Any(e => e.DependsOnEventIds.Contains(last.Id, StringComparer.Ordinal) && e.State is PlanShadowEventState.Confirmed or PlanShadowEventState.PartiallyConfirmed))
            return plan with { State = PlanState.ReconciliationRequired, ReconciliationState = PlanReconciliationState.Contradicted };
        var invalidated = plan.Events.Where(e => e.DependsOnEventIds.Contains(last.Id, StringComparer.Ordinal) && e.State == PlanShadowEventState.PendingConfirmation).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var events = plan.Events.Select(e => e.Id == last.Id ? e with { State = PlanShadowEventState.Reversed } : invalidated.Contains(e.Id) ? e with { State = PlanShadowEventState.Invalidated } : e).ToArray();
        var ordinal = plan.Steps.Select((step, index) => (step, index)).Single(pair => pair.step.Id == last.StepId).index;
        var steps = plan.Steps.Select((step, index) => index >= ordinal ? step with { State = index == ordinal ? PlanStepState.Current : PlanStepState.Invalidated } : step).ToArray();
        return plan with { Events = events, Steps = steps, CurrentStepOrdinal = ordinal, State = PlanState.InProgress, ReconciliationState = PlanReconciliationState.None };
    }

    /// <summary>Applies verified account evidence to pending events without double-counting confirmed effects.</summary>
    public PlanRecord ReconcileWithVerifiedState(PlanRecord plan, Money verifiedCash, IReadOnlyDictionary<string, long> verifiedQuantities, DateTimeOffset observedAtUtc,
        IReadOnlyCollection<PlanVerifiedEvidence>? evidence = null, DateTimeOffset? evidenceCapturedAtUtc = null,
        IReadOnlySet<PlanEvidenceKind>? completeEvidenceKinds = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(verifiedQuantities);
        var observed = RequireUtc(observedAtUtc);
        var baselineCash = plan.BaselineVerifiedCash ?? verifiedCash;
        var baselineQuantities = plan.BaselineVerifiedQuantities ?? verifiedQuantities;
        var expectedCash = baselineCash;
        var expectedQuantities = new Dictionary<string, long>(baselineQuantities, StringComparer.Ordinal);
        var events = plan.Events.ToArray();
        var freshCapture = evidenceCapturedAtUtc is { } captured && (plan.LastEvidenceCapturedAtUtc is null || captured > plan.LastEvidenceCapturedAtUtc.Value);
        var evidenceSet = evidence ?? [];
        var fingerprint = EvidenceFingerprint(evidenceSet);
        var contradictions = 0;
        var blockedByPending = false;
        foreach (var execution in plan.Events.OrderBy(value => value.Sequence))
        {
            if (execution.State is PlanShadowEventState.Reversed or PlanShadowEventState.Invalidated) continue;
            var index = Array.FindIndex(events, value => value.Id == execution.Id);
            if (execution.State == PlanShadowEventState.PendingConfirmation || execution.State == PlanShadowEventState.PartiallyConfirmed)
            {
                var claimedEvidenceIds = events.Where(value => value.Id != execution.Id)
                    .SelectMany(value => value.VerifiedEvidenceIds ?? []).ToHashSet(StringComparer.Ordinal);
                var availableEvidence = evidenceSet.Where(value => !claimedEvidenceIds.Contains(value.Identity)).ToArray();
                var relevantFingerprint = RelevantEvidenceFingerprint(plan, execution, availableEvidence);
                var canUseNegativeEvidence = freshCapture && HasCompleteRelevantObservation(execution, completeEvidenceKinds);
                if (!blockedByPending && IsConfirmedCancellation(plan, execution, availableEvidence, freshCapture, completeEvidenceKinds))
                {
                    events[index] = execution with
                    {
                        State = PlanShadowEventState.Confirmed,
                        VerifiedQuantity = execution.Quantity,
                        VerifiedUnitPrice = execution.UnitPrice,
                        LastRelevantEvidenceFingerprint = freshCapture ? relevantFingerprint : execution.LastRelevantEvidenceFingerprint,
                        VerifiedEvidenceIds = [],
                    };
                }
                else if (!blockedByPending && TryMatchEvidence(plan, execution, availableEvidence, out var observedQuantity, out var observedPrice, out var verifiedEvidenceIds))
                {
                    var cumulativeQuantity = Math.Max(execution.VerifiedQuantity.GetValueOrDefault(), observedQuantity);
                    var nextState = cumulativeQuantity >= execution.Quantity ? PlanShadowEventState.Confirmed : PlanShadowEventState.PartiallyConfirmed;
                    events[index] = execution with { State = nextState, VerifiedQuantity = cumulativeQuantity, VerifiedUnitPrice = observedPrice,
                        LastRelevantEvidenceFingerprint = freshCapture ? relevantFingerprint : execution.LastRelevantEvidenceFingerprint,
                        VerifiedEvidenceIds = verifiedEvidenceIds };
                    if (nextState == PlanShadowEventState.PartiallyConfirmed) blockedByPending = true;
                }
                else if (execution.ExpectedEvidenceKind is not null)
                {
                    blockedByPending = true;
                    if (canUseNegativeEvidence && !string.Equals(relevantFingerprint, execution.LastRelevantEvidenceFingerprint, StringComparison.Ordinal) &&
                        execution.ExpectedObservableUntilUtc is { } deadline && observed > deadline)
                    {
                        contradictions++;
                    }
                    if (freshCapture) events[index] = execution with { LastRelevantEvidenceFingerprint = relevantFingerprint };
                }
            }
            var effective = events[index].State switch
            {
                PlanShadowEventState.Confirmed => EffectiveEffects(events[index]),
                PlanShadowEventState.PartiallyConfirmed => ResidualEffects(events[index]),
                PlanShadowEventState.PendingConfirmation => ResidualEffects(events[index]),
                _ => [],
            };
            foreach (var effect in effective) Apply(expectedQuantities, ref expectedCash, effect);
        }
        var stillAwaitingEvidence = events.Any(e => e.State is PlanShadowEventState.PendingConfirmation or PlanShadowEventState.PartiallyConfirmed);
        var contradictionCount = !stillAwaitingEvidence ? 0 : contradictions == 0 ? plan.ConsecutiveContradictionCount : plan.ConsecutiveContradictionCount + 1;
        var state = plan.State;
        var reconciliation = stillAwaitingEvidence ? PlanReconciliationState.AwaitingEvidence : PlanReconciliationState.Compatible;
        if (plan.IsCancelled && !stillAwaitingEvidence) { state = PlanState.Invalid; reconciliation = PlanReconciliationState.Compatible; }
        else if (contradictionCount >= 2) { state = PlanState.ReconciliationRequired; reconciliation = PlanReconciliationState.Contradicted; }
        else if (contradictions > 0) state = PlanState.RecheckRequired;
        var updatedSteps = plan.Steps.Select(step =>
        {
            var eventForStep = events.FirstOrDefault(e => e.StepId == step.Id);
            return eventForStep?.State switch
            {
                PlanShadowEventState.Confirmed => step with { State = PlanStepState.Confirmed },
                PlanShadowEventState.PartiallyConfirmed => step with { State = PlanStepState.PartiallyConfirmed },
                _ => step,
            };
        }).ToArray();
        return plan with { Events = events, Steps = updatedSteps, State = state, ReconciliationState = reconciliation, BaselineVerifiedCash = baselineCash,
            BaselineVerifiedQuantities = new Dictionary<string, long>(baselineQuantities, StringComparer.Ordinal), ConsecutiveContradictionCount = contradictionCount, LastObservedAtUtc = observed,
            LastEvidenceCapturedAtUtc = freshCapture ? evidenceCapturedAtUtc : plan.LastEvidenceCapturedAtUtc, LastEvidenceFingerprint = freshCapture ? fingerprint : plan.LastEvidenceFingerprint };
    }

    /// <summary>Freezes the current step and marks it for recheck only on material evidence loss.</summary>
    public PlanRecord ApplyRefresh(PlanRecord plan, PlanCandidate? currentCandidate, bool evidenceReady)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!evidenceReady || plan.State is PlanState.ReconciliationRequired or PlanState.Invalid or PlanState.ExecutionComplete) return plan;
        var current = plan.Steps.FirstOrDefault(step => step.State == PlanStepState.Current);
        var replacement = currentCandidate?.Steps.FirstOrDefault(step => step.Id.EndsWith($":{plan.CurrentStepOrdinal + 1}", StringComparison.Ordinal));
        var materiallyChanged = current is not null && (replacement is null || replacement.Action != current.Action || replacement.ItemId != current.ItemId || MateriallyDifferent(replacement.Quantity, current.Quantity) || MateriallyDifferent(replacement.UnitPrice?.Copper, current.UnitPrice?.Copper));
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

    private static Money OpportunityReserve(Money deployable, PlanCandidate[] eligible)
    {
        if (deployable.Copper <= 0 || eligible.Length == 0) return Money.Zero;
        var largestOpportunity = eligible.Max(candidate => Math.Min(deployable.Copper, Math.Max(0, candidate.CommittedCapital.Copper)));
        var minimumLiquidity = deployable.Copper / 20;
        return new Money(Math.Min(deployable.Copper, Math.Max(minimumLiquidity, largestOpportunity / 4)));
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

    private static bool TryMatchEvidence(PlanRecord plan, PlanExecutionEvent execution,
        IReadOnlyCollection<PlanVerifiedEvidence> evidence, out int quantity, out Money unitPrice, out IReadOnlyList<string> verifiedEvidenceIds)
    {
        quantity = 0;
        unitPrice = Money.Zero;
        verifiedEvidenceIds = [];
        if (execution.ExpectedEvidenceKind is not { } expectedKind || execution.Action == PlanStepAction.CancelBuyOrder) return false;
        var step = plan.Steps.FirstOrDefault(value => value.Id == execution.StepId);
        if (step is null) return false;
        var issuedAt = execution.IssuedAtUtc ?? step.IssuedAtUtc ?? plan.StartedAtUtc;
        var matching = evidence.Where(value => IsCompatibleEvidenceKind(expectedKind, value.Kind) && value.ItemId == step.ItemId && value.Quantity > 0 &&
            value.CreatedAtUtc >= issuedAt && value.ObservedAtUtc >= execution.OccurredAtUtc &&
            (step.ExternalIdentity is null || string.Equals(step.ExternalIdentity, value.ExternalIdentity ?? value.Identity, StringComparison.Ordinal)) &&
            (execution.UnitPrice is null || value.UnitPrice == execution.UnitPrice.Value))
            .OrderByDescending(value => value.CreatedAtUtc).ThenBy(value => value.Identity, StringComparer.Ordinal).ToArray();
        if (matching.Length == 0) return false;
        quantity = Math.Min(execution.Quantity, matching.Sum(value => value.Quantity));
        unitPrice = matching[0].UnitPrice;
        verifiedEvidenceIds = matching.Select(value => value.Identity).Distinct(StringComparer.Ordinal).ToArray();
        return quantity > 0;
    }

    private static bool IsCompatibleEvidenceKind(PlanEvidenceKind expected, PlanEvidenceKind actual) => expected switch
    {
        PlanEvidenceKind.BuyOrder => actual is PlanEvidenceKind.BuyOrder or PlanEvidenceKind.CompletedBuy,
        PlanEvidenceKind.SellListing => actual is PlanEvidenceKind.SellListing or PlanEvidenceKind.CompletedSell,
        _ => expected == actual,
    };

    private static bool HasCompleteRelevantObservation(PlanExecutionEvent execution, IReadOnlySet<PlanEvidenceKind>? completeKinds)
    {
        if (execution.ExpectedEvidenceKind is not { } expected || completeKinds is null) return false;
        if (execution.Action == PlanStepAction.CancelBuyOrder) return completeKinds.Contains(PlanEvidenceKind.BuyOrder);
        return expected switch
        {
            PlanEvidenceKind.BuyOrder => completeKinds.Contains(PlanEvidenceKind.BuyOrder) && completeKinds.Contains(PlanEvidenceKind.CompletedBuy),
            PlanEvidenceKind.SellListing => completeKinds.Contains(PlanEvidenceKind.SellListing) && completeKinds.Contains(PlanEvidenceKind.CompletedSell),
            _ => completeKinds.Contains(expected),
        };
    }

    private static string RelevantEvidenceFingerprint(PlanRecord plan, PlanExecutionEvent execution, IEnumerable<PlanVerifiedEvidence> evidence)
    {
        if (execution.ExpectedEvidenceKind is not { } expected) return string.Empty;
        var step = plan.Steps.FirstOrDefault(value => value.Id == execution.StepId);
        if (step is null) return string.Empty;
        var issuedAt = execution.IssuedAtUtc ?? step.IssuedAtUtc ?? plan.StartedAtUtc;
        if (execution.Action == PlanStepAction.CancelBuyOrder)
        {
            return EvidenceFingerprint(evidence.Where(value => value.Kind == PlanEvidenceKind.BuyOrder && value.ItemId == step.ItemId &&
                value.CreatedAtUtc >= issuedAt && value.ObservedAtUtc >= execution.OccurredAtUtc &&
                string.Equals(step.ExternalIdentity, value.ExternalIdentity ?? value.Identity, StringComparison.Ordinal)));
        }
        return EvidenceFingerprint(evidence.Where(value => IsCompatibleEvidenceKind(expected, value.Kind) && value.ItemId == step.ItemId &&
            value.CreatedAtUtc >= issuedAt && value.ObservedAtUtc >= execution.OccurredAtUtc &&
            (step.ExternalIdentity is null || string.Equals(step.ExternalIdentity, value.ExternalIdentity ?? value.Identity, StringComparison.Ordinal))));
    }

    private static PlanEvidenceKind? ExpectedEvidenceFor(PlanStep step) => step.Action switch
    {
        PlanStepAction.PlaceBuyOrder => PlanEvidenceKind.BuyOrder,
        PlanStepAction.CancelBuyOrder => PlanEvidenceKind.BuyOrder,
        PlanStepAction.List or PlanStepAction.Relist => PlanEvidenceKind.SellListing,
        PlanStepAction.BuyNow => PlanEvidenceKind.CompletedBuy,
        PlanStepAction.SellNow => PlanEvidenceKind.CompletedSell,
        _ => null,
    };

    private static bool MateriallyDifferent(long? replacement, long? current)
    {
        if (replacement is null || current is null) return replacement != current;
        var basisPoints = Math.Abs(replacement.Value - current.Value) * 10_000m / Math.Max(Math.Abs(current.Value), 1);
        return basisPoints >= PlanHysteresisPolicy.Default.MaterialImprovementBasisPoints;
    }

    private static string EvidenceFingerprint(IEnumerable<PlanVerifiedEvidence> evidence) => string.Join('|', evidence
        .OrderBy(value => value.Kind).ThenBy(value => value.Identity, StringComparer.Ordinal)
        .Select(value => $"{value.Kind}:{value.Identity}:{value.ItemId}:{value.Quantity}:{value.UnitPrice.Copper}:{value.CreatedAtUtc.UtcTicks}"));

    private static bool IsConfirmedCancellation(PlanRecord plan, PlanExecutionEvent execution,
        IReadOnlyCollection<PlanVerifiedEvidence> evidence, bool freshCapture, IReadOnlySet<PlanEvidenceKind>? completeKinds)
    {
        if (execution.Action != PlanStepAction.CancelBuyOrder || !freshCapture || !HasCompleteRelevantObservation(execution, completeKinds)) return false;
        var step = plan.Steps.FirstOrDefault(value => value.Id == execution.StepId);
        if (string.IsNullOrWhiteSpace(step?.ExternalIdentity)) return false;
        return !evidence.Any(value => value.Kind == PlanEvidenceKind.BuyOrder && value.ItemId == step.ItemId &&
            value.ObservedAtUtc >= execution.OccurredAtUtc &&
            string.Equals(step.ExternalIdentity, value.ExternalIdentity ?? value.Identity, StringComparison.Ordinal));
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
        return ScaleEffects(execution, verifiedQuantity);
    }

    private static IEnumerable<PlanResourceRequirement> ResidualEffects(PlanExecutionEvent execution)
    {
        if (execution.State == PlanShadowEventState.Confirmed) return [];
        var observed = execution.State == PlanShadowEventState.PartiallyConfirmed ? execution.VerifiedQuantity.GetValueOrDefault() : 0;
        var remaining = Math.Max(0, execution.Quantity - observed);
        if (remaining == execution.Quantity) return execution.Effects;
        return ScaleEffects(execution, remaining);
    }

    private static IEnumerable<PlanResourceRequirement> ScaleEffects(PlanExecutionEvent execution, int quantity)
    {
        if (execution.Quantity <= 0) return execution.Effects;
        if (execution.Action is PlanStepAction.List or PlanStepAction.Relist && execution.UnitPrice is { } listingPrice)
        {
            var fee = Gw2TradingPostFeePolicy.Create().CalculateFees(new Money(checked(listingPrice.Copper * quantity))).ListingFee;
            return execution.Effects.Select(effect => effect.Kind == PlanResourceKind.Cash
                ? effect with { Cash = -fee }
                : effect with { Quantity = effect.Quantity == 0 ? 0 : checked(effect.Quantity * quantity / execution.Quantity) });
        }
        if (execution.Action == PlanStepAction.SellNow && execution.UnitPrice is { } salePrice)
        {
            var gross = new Money(checked(salePrice.Copper * quantity));
            var fees = Gw2TradingPostFeePolicy.Create().CalculateFees(gross);
            return execution.Effects.Select(effect => effect.Kind == PlanResourceKind.Cash
                ? effect with { Cash = gross - fees.ListingFee - fees.ExchangeFee }
                : effect with { Quantity = effect.Quantity == 0 ? 0 : checked(effect.Quantity * quantity / execution.Quantity) });
        }
        return execution.Effects.Select(effect =>
        {
            var scaledQuantity = effect.Quantity == 0 ? 0 : checked(effect.Quantity * quantity / execution.Quantity);
            var scaledCash = effect.Cash.Copper == 0 ? effect.Cash : new Money(checked(effect.Cash.Copper * quantity / execution.Quantity));
            return effect with { Quantity = scaledQuantity, Cash = scaledCash };
        });
    }

    private static bool IsGenericReservationOutstanding(PlanRecord plan, PlanResourceRequirement requirement) => requirement.Kind switch
    {
        PlanResourceKind.ExpectedIncoming => plan.Steps.Any(step => step.Action == PlanStepAction.PlaceBuyOrder &&
            string.Equals(step.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture), requirement.ResourceId, StringComparison.Ordinal) &&
            step.State is not PlanStepState.Confirmed),
        PlanResourceKind.OpenOrderExposure => plan.Steps.Any(step => step.Action == PlanStepAction.CancelBuyOrder &&
            string.Equals(step.ExternalIdentity, requirement.ResourceId, StringComparison.Ordinal) &&
            step.State is PlanStepState.Pending or PlanStepState.Current or PlanStepState.AwaitingConfirmation or PlanStepState.PartiallyConfirmed),
        _ => true,
    };

    private static string Key(PlanResourceRequirement requirement) => $"{(int)requirement.Kind}:{requirement.ResourceId}";
    private static DateTimeOffset RequireUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero ? value : throw new ArgumentException("Timestamp must be UTC.", nameof(value));

    private sealed record Selection(IReadOnlyList<PlanCandidate> Plans, Money Cash, int Utility, IReadOnlyDictionary<string, long> Quantities)
    {
        public bool IsBetterThan(Selection other) => Utility > other.Utility || Utility == other.Utility && (Plans.Count > other.Plans.Count || Plans.Count == other.Plans.Count && string.CompareOrdinal(string.Join('|', Plans.Select(p => p.Id)), string.Join('|', other.Plans.Select(p => p.Id))) < 0);
    }
}
