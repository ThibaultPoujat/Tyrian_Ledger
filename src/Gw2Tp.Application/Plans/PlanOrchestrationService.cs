using Gw2Tp.Application.Finance;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Plans;

/// <summary>Deterministic plan selection and reversible local-shadow transitions.</summary>
public sealed class PlanOrchestrationService : IPlanOrchestrationService
{
    public const int MaximumCandidates = 18;
    public static readonly TimeSpan DefaultObservationWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan CancellationReconciliationRetentionWindow = TimeSpan.FromMinutes(30);

    public static bool IsCancellationReconciliationRetained(PlanRecord plan, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var now = RequireUtc(nowUtc);
        return plan.State == PlanState.Invalid && plan.IsCancelled && plan.CancellationReconciliationExpiresAtUtc is { } expiresAt &&
            now <= RequireUtc(expiresAt) &&
            plan.Events.Any(value => value.Action == PlanStepAction.CancelBuyOrder && value.State == PlanShadowEventState.Confirmed);
    }

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
        // Dependent resources are produced by earlier outstanding steps. Only
        // the residual need is reserved before a plan starts.
        var produced = new Dictionary<string, long>(StringComparer.Ordinal);
        void Consume(PlanResourceRequirement requirement)
        {
            var key = Key(requirement);
            var covered = Math.Min(produced.GetValueOrDefault(key), requirement.Quantity);
            if (covered > 0) produced[key] -= covered;
            var residual = requirement.Quantity - covered;
            if (residual > 0) requirements.Add(requirement with { Quantity = residual });
        }
        void Produce(PlanResourceRequirement effect)
        {
            if (effect.Kind == PlanResourceKind.Inventory && effect.Quantity > 0)
                produced[Key(effect)] = checked(produced.GetValueOrDefault(Key(effect)) + effect.Quantity);
        }
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
                    Produce(new(PlanResourceKind.Inventory, itemId, quantity, Money.Zero));
                    break;
                case PlanStepAction.List:
                case PlanStepAction.Relist:
                case PlanStepAction.SellNow:
                    Consume(new(PlanResourceKind.Inventory, itemId, quantity, Money.Zero));
                    if (step.Action is PlanStepAction.List or PlanStepAction.Relist && step.UnitPrice is { } listPrice)
                    {
                        var gross = new Money(checked(listPrice.Copper * quantity));
                        requirements.Add(new(PlanResourceKind.Cash, "cash", 0, Gw2TradingPostFeePolicy.Create().CalculateFees(gross).ListingFee));
                    }
                    break;
                case PlanStepAction.Craft:
                    foreach (var effect in step.CraftEffects ?? [])
                    {
                        if (effect.Kind == PlanResourceKind.Inventory && effect.Quantity < 0)
                            Consume(effect with { Quantity = -effect.Quantity, Cash = Money.Zero });
                    }
                    foreach (var effect in step.CraftEffects ?? [])
                    {
                        Produce(effect);
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
        if (step.State != PlanStepState.Current || quantity <= 0 || step.Action == PlanStepAction.Craft && quantity != step.Quantity) throw new InvalidOperationException("Only the exact current manual craft can be reported with a positive quantity.");
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
            ConsecutiveContradictionCount = 0,
            ReconciliationReason = !hasUnresolvedPriorExecution || plan.ReconciliationState == PlanReconciliationState.Contradicted
                ? PlanReconciliationReason.None : plan.ReconciliationReason,
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
        return plan with { Events = events, Steps = steps, CurrentStepOrdinal = ordinal, State = PlanState.InProgress,
            ReconciliationState = PlanReconciliationState.None, IsCancelled = false,
            ConsecutiveContradictionCount = 0,
            ReconciliationReason = PlanReconciliationReason.None };
    }

    /// <summary>Applies account-scoped evidence without inferring completeness across source boundaries.</summary>
    public PlanRecord ReconcileWithVerifiedState(PlanRecord plan, AccountScope trustedAccountScope, PlanEvidenceFrame evidenceFrame,
        IReadOnlySet<string>? accountClaimedEvidenceIds = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(trustedAccountScope);
        ArgumentNullException.ThrowIfNull(evidenceFrame);
        ArgumentNullException.ThrowIfNull(evidenceFrame.AccountScope);
        ArgumentNullException.ThrowIfNull(evidenceFrame.PhysicalInventory);
        ArgumentNullException.ThrowIfNull(evidenceFrame.Cash);
        ArgumentNullException.ThrowIfNull(evidenceFrame.CurrentOrders);
        ArgumentNullException.ThrowIfNull(evidenceFrame.CompletedTransactions);
        var evaluatedAt = RequireUtc(evidenceFrame.EvaluationTimeUtc);
        if (string.IsNullOrWhiteSpace(trustedAccountScope.AccountId) ||
            !string.Equals(trustedAccountScope.AccountId, evidenceFrame.AccountScope.AccountId, StringComparison.Ordinal)) return plan;
        ValidateProvenance(evidenceFrame.PhysicalInventory.Provenance);
        ValidateProvenance(evidenceFrame.Cash.Provenance);
        ValidateProvenance(evidenceFrame.CurrentOrders.Provenance);
        ValidateProvenance(evidenceFrame.CompletedTransactions.Provenance);

        var physical = evidenceFrame.PhysicalInventory.Provenance.Availability == PlanEvidenceAvailability.Available
            ? evidenceFrame.PhysicalInventory.Value ?? new Dictionary<string, long>(StringComparer.Ordinal)
            : new Dictionary<string, long>(StringComparer.Ordinal);
        Money? sourceCash = evidenceFrame.Cash.Provenance.Availability == PlanEvidenceAvailability.Available &&
            evidenceFrame.Cash.Provenance.Completeness == PlanEvidenceCompleteness.Complete &&
            !string.IsNullOrWhiteSpace(evidenceFrame.Cash.Provenance.CaptureId) && evidenceFrame.Cash.Provenance.FetchedAtUtc is not null
            ? evidenceFrame.Cash.Value : null;
        var verifiedCash = sourceCash ?? plan.BaselineVerifiedCash ?? Money.Zero;
        var evidenceSet = (SourceValue(evidenceFrame.CurrentOrders) ?? [])
            .Concat(SourceValue(evidenceFrame.CompletedTransactions) ?? []).ToArray();
        var baselineCash = plan.BaselineVerifiedCash ?? sourceCash;
        var baselineQuantities = plan.BaselineVerifiedQuantities is null
            ? new Dictionary<string, long>(StringComparer.Ordinal)
            : new Dictionary<string, long>(plan.BaselineVerifiedQuantities, StringComparer.Ordinal);
        var expectedCash = baselineCash ?? Money.Zero;
        var expectedQuantities = new Dictionary<string, long>(baselineQuantities, StringComparer.Ordinal);
        var events = plan.Events.ToArray();
        var tpCapture = CompleteTradingPostCapture(evidenceFrame);
        var freshCapture = tpCapture is not null && IsNewCapture(tpCapture, plan.LastEvidenceCaptureId, plan.LastEvidenceCapturedAtUtc);
        var physicalCapture = CompletePhysicalInventoryCapture(evidenceFrame.PhysicalInventory);
        var freshPhysicalCapture = physicalCapture is not null && IsNewPhysicalCapture(physicalCapture,
            plan.LastPhysicalInventoryCaptureId, plan.LastPhysicalInventoryFetchedAtUtc, plan.LastPhysicalInventoryObservedAtUtc);
        var fingerprint = EvidenceFingerprint(evidenceSet);
        var contradictions = 0;
        var observedReason = PlanReconciliationReason.None;
        var blockedByPending = false;
        var cancellationEvidenceConflict = false;
        foreach (var execution in plan.Events.OrderBy(value => value.Sequence))
        {
            if (execution.State is PlanShadowEventState.Reversed or PlanShadowEventState.Invalidated) continue;
            var index = Array.FindIndex(events, value => value.Id == execution.Id);
            if (execution.State == PlanShadowEventState.Confirmed && HasExactCancellationFillEvidence(plan, execution, evidenceSet))
            {
                cancellationEvidenceConflict = true;
                blockedByPending = true;
            }
            if (execution.State is PlanShadowEventState.PendingConfirmation or PlanShadowEventState.PartiallyConfirmed)
            {
                var ownEvidenceIds = (execution.VerifiedEvidenceIds ?? []).ToHashSet(StringComparer.Ordinal);
                var claimedEvidenceIds = accountClaimedEvidenceIds is null
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : accountClaimedEvidenceIds.Where(value => !ownEvidenceIds.Contains(value)).ToHashSet(StringComparer.Ordinal);
                claimedEvidenceIds.UnionWith(events.Where(value => value.Id != execution.Id)
                    .SelectMany(value => value.VerifiedEvidenceIds ?? []));
                var availableEvidence = evidenceSet.Where(value => !claimedEvidenceIds.Contains(value.Identity)).ToArray();
                var relevantFingerprint = RelevantEvidenceFingerprint(plan, execution, availableEvidence);
                var negativeCapture = execution.Action == PlanStepAction.Craft
                    ? CompleteInventoryCapture(evidenceFrame.PhysicalInventory, execution)
                    : CompleteTradingPostCapture(evidenceFrame);
                var canUseNegativeEvidence = negativeCapture is not null && IsNewEventNegativeCapture(execution, negativeCapture) &&
                    (execution.Action == PlanStepAction.Craft ? freshPhysicalCapture : freshCapture);
                if (!blockedByPending && HasCancellationFillEvidence(plan, execution, availableEvidence))
                {
                    cancellationEvidenceConflict = true;
                    observedReason = PlanReconciliationReason.CancellationLateFill;
                    blockedByPending = true;
                    if (canUseNegativeEvidence) events[index] = execution with { LastRelevantEvidenceFingerprint = relevantFingerprint };
                }
                else if (!blockedByPending && IsCancellationAbsence(plan, execution, availableEvidence, canUseNegativeEvidence))
                {
                    var capturedAt = RequireUtc(negativeCapture!.FetchedAtUtc!.Value);
                    var firstNegativeCapture = execution.FirstNegativeEvidenceCapturedAtUtc ?? capturedAt;
                    var negativeCaptureCount = checked(execution.NegativeEvidenceCaptureCount + 1);
                    var nextEvent = execution with
                    {
                        State = negativeCaptureCount >= 2 && capturedAt - firstNegativeCapture >= DefaultObservationWindow
                            ? PlanShadowEventState.Confirmed : execution.State,
                        VerifiedQuantity = negativeCaptureCount >= 2 && capturedAt - firstNegativeCapture >= DefaultObservationWindow
                            ? execution.Quantity : execution.VerifiedQuantity,
                        VerifiedUnitPrice = negativeCaptureCount >= 2 && capturedAt - firstNegativeCapture >= DefaultObservationWindow
                            ? execution.UnitPrice : execution.VerifiedUnitPrice,
                        LastRelevantEvidenceFingerprint = relevantFingerprint,
                        VerifiedEvidenceIds = [],
                        FirstNegativeEvidenceCapturedAtUtc = firstNegativeCapture,
                        NegativeEvidenceCaptureCount = negativeCaptureCount,
                        LastNegativeEvidenceCaptureId = negativeCapture.CaptureId,
                        LastNegativeEvidenceCapturedAtUtc = capturedAt,
                    };
                    events[index] = nextEvent;
                }
                else if (!cancellationEvidenceConflict &&
                    (!blockedByPending || execution.Action is PlanStepAction.List or PlanStepAction.Relist &&
                        plan.Events.Any(value => value.Sequence < execution.Sequence && value.Action == PlanStepAction.Craft &&
                            value.State is PlanShadowEventState.PendingConfirmation or PlanShadowEventState.PartiallyConfirmed)) &&
                    TryMatchEvidence(plan, execution, availableEvidence, out var observedQuantity, out var observedPrice, out var verifiedEvidenceIds))
                {
                    var cumulativeQuantity = Math.Max(execution.VerifiedQuantity.GetValueOrDefault(), observedQuantity);
                    var nextState = cumulativeQuantity >= execution.Quantity ? PlanShadowEventState.Confirmed : PlanShadowEventState.PartiallyConfirmed;
                    events[index] = execution with { State = nextState, VerifiedQuantity = cumulativeQuantity, VerifiedUnitPrice = observedPrice,
                        LastRelevantEvidenceFingerprint = freshCapture ? relevantFingerprint : execution.LastRelevantEvidenceFingerprint,
                        VerifiedEvidenceIds = (execution.VerifiedEvidenceIds ?? []).Concat(verifiedEvidenceIds).Distinct(StringComparer.Ordinal).ToArray() };
                    if (nextState == PlanShadowEventState.PartiallyConfirmed) blockedByPending = true;
                }
                else if (!blockedByPending && execution.Action == PlanStepAction.Craft)
                {
                    var inventorySource = evidenceFrame.PhysicalInventory;
                    var craftContradicted = false;
                    if (HasInventoryCoverage(inventorySource, execution) &&
                        TryMatchCraftInventory(execution, expectedQuantities, physical, inventorySource.Provenance.CoverageKeys, out craftContradicted))
                    {
                        events[index] = execution with { State = PlanShadowEventState.Confirmed, VerifiedQuantity = execution.Quantity,
                            VerifiedUnitPrice = execution.UnitPrice, VerifiedEvidenceIds = [] };
                    }
                    else if (HasInventoryCoverage(inventorySource, execution) && craftContradicted)
                    {
                        blockedByPending = true;
                        if (canUseNegativeEvidence)
                        {
                            var capturedAt = RequireUtc(negativeCapture!.UpstreamObservedAtUtc!.Value);
                            var firstNegativeCapture = execution.FirstNegativeEvidenceCapturedAtUtc ?? capturedAt;
                            var negativeCaptureCount = checked(execution.NegativeEvidenceCaptureCount + 1);
                            events[index] = execution with { FirstNegativeEvidenceCapturedAtUtc = firstNegativeCapture,
                                NegativeEvidenceCaptureCount = negativeCaptureCount, LastNegativeEvidenceCaptureId = negativeCapture.CaptureId,
                                LastNegativeEvidenceCapturedAtUtc = capturedAt };
                            if (negativeCaptureCount >= 2 && capturedAt - firstNegativeCapture >= DefaultObservationWindow)
                            {
                                contradictions += 2;
                                observedReason = PlanReconciliationReason.CraftInventoryMismatch;
                            }
                        }
                    }
                }
                else if (execution.ExpectedEvidenceKind is not null)
                {
                    blockedByPending = true;
                    var persistentCancellation = execution.Action == PlanStepAction.CancelBuyOrder && IsCancellationStillVisible(plan, execution, availableEvidence);
                    if (canUseNegativeEvidence && execution.ExpectedObservableUntilUtc is { } deadline && evaluatedAt > deadline &&
                        (persistentCancellation || !string.Equals(relevantFingerprint, execution.LastRelevantEvidenceFingerprint, StringComparison.Ordinal)))
                    {
                        contradictions++;
                        observedReason = persistentCancellation
                            ? PlanReconciliationReason.CancellationStillVisible
                            : PlanReconciliationReason.TradingPostEvidenceMismatch;
                    }
                    if (canUseNegativeEvidence)
                    {
                        events[index] = execution with
                        {
                            LastRelevantEvidenceFingerprint = relevantFingerprint,
                            FirstNegativeEvidenceCapturedAtUtc = persistentCancellation ? null : execution.FirstNegativeEvidenceCapturedAtUtc,
                            NegativeEvidenceCaptureCount = persistentCancellation ? 0 : execution.NegativeEvidenceCaptureCount,
                            LastNegativeEvidenceCaptureId = negativeCapture!.CaptureId,
                            LastNegativeEvidenceCapturedAtUtc = negativeCapture.FetchedAtUtc,
                        };
                    }
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
        if (!events.Any(value => (value.State is PlanShadowEventState.PendingConfirmation or PlanShadowEventState.PartiallyConfirmed) && value.Action != PlanStepAction.Craft) &&
            CraftShadowProjectionMatchesVerified(events, expectedQuantities, physical, evidenceFrame.PhysicalInventory))
        {
            events = events.Select(value => value.State == PlanShadowEventState.PendingConfirmation && value.Action == PlanStepAction.Craft
                ? value with { State = PlanShadowEventState.Confirmed, VerifiedQuantity = value.Quantity, VerifiedUnitPrice = value.UnitPrice, VerifiedEvidenceIds = [] }
                : value).ToArray();
        }
        var stillAwaitingEvidence = events.Any(e => e.State is PlanShadowEventState.PendingConfirmation or PlanShadowEventState.PartiallyConfirmed);
        var contradictionCount = !stillAwaitingEvidence ? 0 : contradictions == 0 ? plan.ConsecutiveContradictionCount : checked(plan.ConsecutiveContradictionCount + contradictions);
        var state = plan.State;
        var reconciliation = stillAwaitingEvidence ? PlanReconciliationState.AwaitingEvidence : PlanReconciliationState.Compatible;
        var isReconciliationOnly = plan.IsReconciliationOnly || (plan.IsCancelled && cancellationEvidenceConflict);
        if (cancellationEvidenceConflict || plan.IsReconciliationOnly) { state = PlanState.ReconciliationRequired; reconciliation = PlanReconciliationState.Contradicted; }
        else if (plan.IsCancelled && !stillAwaitingEvidence) { state = PlanState.Invalid; reconciliation = PlanReconciliationState.Compatible; }
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
        if (!stillAwaitingEvidence && state == PlanState.RecheckRequired) state = CompatibleLifecycleState(plan, updatedSteps);
        var reconciliationReason = cancellationEvidenceConflict
            ? PlanReconciliationReason.CancellationLateFill
            : observedReason != PlanReconciliationReason.None
                ? observedReason
                : plan.ReconciliationReason;
        if (!stillAwaitingEvidence && reconciliation != PlanReconciliationState.Contradicted)
            reconciliationReason = PlanReconciliationReason.None;
        else if (plan.ReconciliationState == PlanReconciliationState.Contradicted &&
            reconciliation != PlanReconciliationState.Contradicted && observedReason == PlanReconciliationReason.None)
            reconciliationReason = PlanReconciliationReason.None;
        var cancellationRetentionExpiresAt = plan.CancellationReconciliationExpiresAtUtc;
        if (state == PlanState.Invalid && plan.IsCancelled && cancellationRetentionExpiresAt is null &&
            events.Any(value => value.Action == PlanStepAction.CancelBuyOrder && value.State == PlanShadowEventState.Confirmed))
            cancellationRetentionExpiresAt = RequireUtc(tpCapture?.FetchedAtUtc ?? evaluatedAt) + CancellationReconciliationRetentionWindow;
        var updated = plan with { Events = events, Steps = updatedSteps, State = state, ReconciliationState = reconciliation, BaselineVerifiedCash = baselineCash,
            BaselineVerifiedQuantities = plan.BaselineVerifiedQuantities is null ? null : new Dictionary<string, long>(baselineQuantities, StringComparer.Ordinal), ConsecutiveContradictionCount = contradictionCount,
            LastObservedAtUtc = plan.LastObservedAtUtc,
            LastEvidenceCapturedAtUtc = freshCapture ? tpCapture!.FetchedAtUtc : plan.LastEvidenceCapturedAtUtc,
            LastEvidenceCaptureId = freshCapture ? tpCapture!.CaptureId : plan.LastEvidenceCaptureId,
            LastEvidenceFingerprint = freshCapture ? fingerprint : plan.LastEvidenceFingerprint,
            LastPhysicalInventoryCaptureId = freshPhysicalCapture ? physicalCapture!.CaptureId : plan.LastPhysicalInventoryCaptureId,
            LastPhysicalInventoryFetchedAtUtc = freshPhysicalCapture ? physicalCapture!.FetchedAtUtc : plan.LastPhysicalInventoryFetchedAtUtc,
            LastPhysicalInventoryObservedAtUtc = freshPhysicalCapture ? physicalCapture!.UpstreamObservedAtUtc : plan.LastPhysicalInventoryObservedAtUtc,
            CancellationReconciliationExpiresAtUtc = cancellationRetentionExpiresAt, IsReconciliationOnly = isReconciliationOnly,
            ReconciliationReason = reconciliationReason };
        return PlanRecordSemantics.AreEqual(plan, updated) ? plan : updated with { LastObservedAtUtc = evaluatedAt };
    }

    /// <summary>Reconciles an account's eligible plans in stable order while sharing durable evidence claims.</summary>
    public IReadOnlyList<PlanRecord> ReconcileAccountPlans(IReadOnlyList<PlanRecord> accountPlans,
        AccountScope trustedAccountScope, PlanEvidenceFrame evidenceFrame, DateTimeOffset evaluationTimeUtc,
        Func<PlanRecord, PlanRecord>? afterReconcile = null)
    {
        ArgumentNullException.ThrowIfNull(accountPlans);
        ArgumentNullException.ThrowIfNull(trustedAccountScope);
        ArgumentNullException.ThrowIfNull(evidenceFrame);
        var evaluationTime = RequireUtc(evaluationTimeUtc);
        var claimedEvidenceIds = accountPlans.SelectMany(plan => plan.Events)
            .SelectMany(execution => execution.VerifiedEvidenceIds ?? [])
            .ToHashSet(StringComparer.Ordinal);
        var candidates = accountPlans
            .Where(plan => plan.State is PlanState.InProgress or PlanState.Waiting or PlanState.RecheckRequired or
                PlanState.ReconciliationRequired or PlanState.ExecutionComplete ||
                IsCancellationReconciliationRetained(plan, evaluationTime))
            .OrderBy(plan => plan.StartedAtUtc)
            .ThenBy(plan => plan.Id, StringComparer.Ordinal);
        var updated = new List<PlanRecord>();
        foreach (var plan in candidates)
        {
            var reconciled = ReconcileWithVerifiedState(plan, trustedAccountScope, evidenceFrame, claimedEvidenceIds);
            if (afterReconcile is not null) reconciled = afterReconcile(reconciled);
            updated.Add(reconciled);
            claimedEvidenceIds.UnionWith(reconciled.Events.SelectMany(value => value.VerifiedEvidenceIds ?? []));
        }
        return updated;
    }

    // Kept internal for existing deterministic application tests; production callers use the typed frame.
    internal PlanRecord ReconcileWithVerifiedState(PlanRecord plan, Money verifiedCash,
        IReadOnlyDictionary<string, long> verifiedQuantities, DateTimeOffset observedAtUtc,
        IReadOnlyCollection<PlanVerifiedEvidence>? evidence = null, DateTimeOffset? evidenceCapturedAtUtc = null,
        IReadOnlySet<PlanEvidenceKind>? completeEvidenceKinds = null)
    {
        var at = RequireUtc(evidenceCapturedAtUtc ?? observedAtUtc);
        var scope = new AccountScope("synthetic-test-account");
        var completeQuantities = new Dictionary<string, long>(verifiedQuantities, StringComparer.Ordinal);
        var fullCoverage = plan.Events.SelectMany(value => value.Effects)
            .Where(effect => effect.Kind == PlanResourceKind.Inventory && effect.Quantity != 0)
            .Select(Key).Concat(verifiedQuantities.Keys).ToHashSet(StringComparer.Ordinal);
        foreach (var key in fullCoverage) completeQuantities.TryAdd(key, 0);
        var inventory = new PlanEvidenceSource<IReadOnlyDictionary<string, long>>(
            new($"test-inventory:{at.UtcTicks}", at, at, PlanEvidenceAvailability.Available, PlanEvidenceCompleteness.Complete, fullCoverage), completeQuantities);
        var cash = new PlanEvidenceSource<Money>(new($"test-cash:{at.UtcTicks}", at, at, PlanEvidenceAvailability.Available, PlanEvidenceCompleteness.Complete, new HashSet<string>(StringComparer.Ordinal)), verifiedCash);
        var evidenceRows = evidence ?? [];
        var tpAvailability = PlanEvidenceAvailability.Available;
        var tpCompleteness = completeEvidenceKinds is null ? PlanEvidenceCompleteness.Partial : PlanEvidenceCompleteness.Complete;
        var tpId = $"test-trading-post:{at.UtcTicks}";
        var current = new PlanEvidenceSource<IReadOnlyList<PlanVerifiedEvidence>>(
            new(tpId, at, at, tpAvailability, tpCompleteness, new HashSet<string>(new[] { "buy_orders", "sell_listings" }, StringComparer.Ordinal)),
            evidenceRows.Where(value => value.Kind is PlanEvidenceKind.BuyOrder or PlanEvidenceKind.SellListing).ToArray());
        var completed = new PlanEvidenceSource<IReadOnlyList<PlanVerifiedEvidence>>(
            new(tpId, at, at, tpAvailability, tpCompleteness, new HashSet<string>(new[] { "completed_buys", "completed_sells" }, StringComparer.Ordinal)),
            evidenceRows.Where(value => value.Kind is PlanEvidenceKind.CompletedBuy or PlanEvidenceKind.CompletedSell).ToArray());
        var seeded = plan with
        {
            BaselineVerifiedCash = plan.BaselineVerifiedCash ?? verifiedCash,
            BaselineVerifiedQuantities = SeedSyntheticBaseline(plan, completeQuantities),
        };
        return ReconcileWithVerifiedState(seeded, scope, new PlanEvidenceFrame(scope, RequireUtc(observedAtUtc), inventory, cash, current, completed));
    }

    private static IReadOnlyDictionary<string, long> SeedSyntheticBaseline(PlanRecord plan, IReadOnlyDictionary<string, long> quantities)
    {
        var baseline = plan.BaselineVerifiedQuantities is null
            ? new Dictionary<string, long>(quantities, StringComparer.Ordinal)
            : new Dictionary<string, long>(plan.BaselineVerifiedQuantities, StringComparer.Ordinal);
        foreach (var key in plan.Events.SelectMany(value => value.Effects).Where(effect => effect.Kind == PlanResourceKind.Inventory && effect.Quantity != 0).Select(Key))
            baseline.TryAdd(key, 0);
        return baseline;
    }

    /// <summary>Freezes the current step and marks it for recheck only on material evidence loss.</summary>
    public PlanRecord ApplyRefresh(PlanRecord plan, PlanCandidate? currentCandidate, bool evidenceReady)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!evidenceReady || plan.State is PlanState.ReconciliationRequired or PlanState.Invalid or PlanState.ExecutionComplete) return plan;
        var current = plan.Steps.FirstOrDefault(step => step.State == PlanStepState.Current);
        var replacement = currentCandidate?.Steps.FirstOrDefault(step => string.Equals(step.Id, current?.Id, StringComparison.Ordinal));
        var materiallyChanged = current is not null && (replacement is null || replacement.Action != current.Action || replacement.ItemId != current.ItemId || MateriallyDifferent(replacement.Quantity, current.Quantity) || MateriallyDifferent(replacement.UnitPrice?.Copper, current.UnitPrice?.Copper));
        var improvementThreshold = Math.Max(1, Math.Abs(plan.BaselineUtility) * plan.HysteresisPolicy.MaterialImprovementBasisPoints / 10_000);
        var materiallyImproved = currentCandidate is not null && currentCandidate.Utility >= plan.BaselineUtility + improvementThreshold;
        return materiallyChanged || materiallyImproved
            ? plan with { State = PlanState.RecheckRequired, ReconciliationState = PlanReconciliationState.AwaitingEvidence,
                ReconciliationReason = PlanReconciliationReason.OpportunityChanged }
            : plan;
    }

    public PlanRecord Reconcile(PlanRecord plan, IReadOnlyCollection<string> confirmedEventIds, bool materiallyContradicted)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(confirmedEventIds);
        if (materiallyContradicted) return plan with { State = PlanState.ReconciliationRequired,
            ReconciliationState = PlanReconciliationState.Contradicted,
            ReconciliationReason = PlanReconciliationReason.TradingPostEvidenceMismatch };
        var confirmed = confirmedEventIds.ToHashSet(StringComparer.Ordinal);
        var events = plan.Events.Select(e => confirmed.Contains(e.Id) && e.State == PlanShadowEventState.PendingConfirmation ? e with { State = PlanShadowEventState.Confirmed } : e).ToArray();
        var steps = plan.Steps.Select(step => events.Any(e => e.StepId == step.Id && e.State == PlanShadowEventState.Confirmed) ? step with { State = PlanStepState.Confirmed } : step).ToArray();
        var nextReconciliation = events.Any(e => e.State is PlanShadowEventState.PendingConfirmation or PlanShadowEventState.PartiallyConfirmed)
            ? PlanReconciliationState.AwaitingEvidence : PlanReconciliationState.Compatible;
        return plan with { Events = events, Steps = steps, ReconciliationState = nextReconciliation,
            ConsecutiveContradictionCount = plan.ReconciliationState == PlanReconciliationState.Contradicted
                ? 0 : plan.ConsecutiveContradictionCount,
            ReconciliationReason = nextReconciliation == PlanReconciliationState.Compatible ||
                plan.ReconciliationState == PlanReconciliationState.Contradicted
                ? PlanReconciliationReason.None : plan.ReconciliationReason };
    }

    public static bool IsExecutable(PlanCandidate candidate) => candidate.IsHardEligible && (candidate.Steps.Count > 0 || candidate.Attention == PlanAttention.Passive) &&
        (candidate.Attention != PlanAttention.Active || candidate.Steps.Select((step, index) => (step, index)).All(pair => pair.step.Action != PlanStepAction.PlaceBuyOrder || pair.index == candidate.Steps.Count - 1)) &&
        TryAggregateResourceDemands(candidate.Requirements, out _);

    /// <summary>
    /// Aggregates equivalent requirements with checked arithmetic. Cash is keyed
    /// by the canonical cash identity and uses only its amount; non-cash
    /// resources use only their quantity. The other field is intentionally not
    /// treated as a second demand dimension.
    /// </summary>
    public static bool TryAggregateResourceDemands(
        IEnumerable<PlanResourceRequirement> requirements,
        out IReadOnlyDictionary<string, PlanResourceDemand> aggregated)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        var result = new Dictionary<string, PlanResourceDemand>(StringComparer.Ordinal);
        try
        {
            foreach (var requirement in requirements)
            {
                var key = ResourceKey(requirement);
                if (requirement.Kind == PlanResourceKind.Cash)
                {
                    if (requirement.Cash.Copper < 0) { aggregated = result; return false; }
                    var current = result.GetValueOrDefault(key);
                    var amount = checked((current?.Cash.Copper ?? 0) + requirement.Cash.Copper);
                    result[key] = new PlanResourceDemand(PlanResourceKind.Cash, "cash", 0, new Money(amount));
                }
                else
                {
                    if (requirement.Quantity < 0) { aggregated = result; return false; }
                    var current = result.GetValueOrDefault(key);
                    var quantity = checked((current?.Quantity ?? 0) + requirement.Quantity);
                    result[key] = new PlanResourceDemand(requirement.Kind, requirement.ResourceId, quantity, Money.Zero);
                }
            }
        }
        catch (OverflowException)
        {
            aggregated = result;
            return false;
        }

        aggregated = result;
        return true;
    }

    /// <summary>Returns the canonical kind-plus-identity key used for resource accounting.</summary>
    public static string ResourceKey(PlanResourceRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        var identity = requirement.Kind == PlanResourceKind.Cash ? "cash" : requirement.ResourceId;
        return $"{(int)requirement.Kind}:{identity}";
    }

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
        var added = false;
        try
        {
            if (!TryAggregateResourceDemands(candidate.Requirements, out var candidateDemands)) return;
            var candidateCash = CashDemand(candidateDemands);
            if ((cash + candidateCash).Copper > capacity.Copper) return;
            foreach (var demand in candidateDemands)
            {
                if (demand.Value.Kind == PlanResourceKind.Cash || demand.Value.Quantity <= 0) continue;
                if (!capacities.TryGetValue(demand.Key, out var resourceCapacity))
                {
                    if (demand.Value.Kind == PlanResourceKind.Inventory) return;
                    resourceCapacity = demand.Value.Quantity;
                }
                if (resourceCapacity < 0 || checked(quantities.GetValueOrDefault(demand.Key) + demand.Value.Quantity) > resourceCapacity) return;
            }
            var nextQuantities = new Dictionary<string, long>(quantities, StringComparer.Ordinal);
            foreach (var demand in candidateDemands.Where(demand => demand.Value.Kind != PlanResourceKind.Cash && demand.Value.Quantity > 0))
                nextQuantities[demand.Key] = checked(nextQuantities.GetValueOrDefault(demand.Key) + demand.Value.Quantity);
            selected.Add(candidate);
            added = true;
            Search(candidates, index + 1, capacity, selected, cash + candidateCash, checked(utility + (int)Math.Clamp(candidate.Utility, int.MinValue, int.MaxValue)), nextQuantities, capacities, ref best, cancellationToken);
        }
        catch (OverflowException)
        {
            return;
        }
        finally
        {
            if (added) selected.RemoveAt(selected.Count - 1);
        }
    }

    private static IReadOnlyDictionary<string, long> BuildCapacities(PlanCandidate[] candidates, IReadOnlyDictionary<string, long>? supplied)
    {
        var capacities = supplied is null ? new Dictionary<string, long>(StringComparer.Ordinal) : new Dictionary<string, long>(supplied, StringComparer.Ordinal);
        var fallbackCapacities = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (!TryAggregateResourceDemands(candidate.Requirements, out var demands)) continue;
            foreach (var demand in demands.Values.Where(value => value.Kind != PlanResourceKind.Cash && value.Quantity > 0))
            {
                var key = ResourceKey(new PlanResourceRequirement(demand.Kind, demand.ResourceId, demand.Quantity, demand.Cash));
                if (capacities.ContainsKey(key) || (supplied is not null && demand.Kind == PlanResourceKind.Inventory)) continue;
                fallbackCapacities[key] = Math.Max(fallbackCapacities.GetValueOrDefault(key), demand.Quantity);
            }
        }
        foreach (var fallback in fallbackCapacities) capacities[fallback.Key] = fallback.Value;
        return capacities;
    }

    private static Money CashDemand(IReadOnlyDictionary<string, PlanResourceDemand> demands) =>
        demands.Values.SingleOrDefault(value => value.Kind == PlanResourceKind.Cash)?.Cash ?? Money.Zero;

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
            PlanStepAction.Craft => step.CraftEffects ?? [],
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
            value.CreatedAtUtc >= issuedAt && value.CapturedAtUtc >= execution.OccurredAtUtc &&
            (step.ExternalIdentity is null || string.Equals(step.ExternalIdentity, value.ExternalIdentity ?? value.Identity, StringComparison.Ordinal)) &&
            (execution.UnitPrice is null || value.UnitPrice == execution.UnitPrice.Value))
            .OrderByDescending(value => value.CreatedAtUtc).ThenBy(value => value.Identity, StringComparer.Ordinal).ToArray();
        if (matching.Length == 0) return false;
        var ownedEvidenceIds = (execution.VerifiedEvidenceIds ?? []).ToHashSet(StringComparer.Ordinal);
        var ownedEvidence = matching.Where(value => ownedEvidenceIds.Contains(value.Identity)).ToArray();
        long supportedQuantity = Math.Max(execution.VerifiedQuantity.GetValueOrDefault(),
            Math.Min((long)execution.Quantity, ownedEvidence.Sum(value => (long)value.Quantity)));
        var newlyClaimedEvidence = SelectMinimumSupportingEvidence(
            matching.Where(value => !ownedEvidenceIds.Contains(value.Identity)).ToArray(),
            execution.Quantity - supportedQuantity);
        supportedQuantity = Math.Min((long)execution.Quantity,
            supportedQuantity + newlyClaimedEvidence.Sum(value => (long)value.Quantity));
        var priceEvidence = ownedEvidence.FirstOrDefault() ?? newlyClaimedEvidence.FirstOrDefault();
        if (priceEvidence is null || supportedQuantity <= 0) return false;
        quantity = (int)supportedQuantity;
        unitPrice = priceEvidence.UnitPrice;
        verifiedEvidenceIds = newlyClaimedEvidence.Select(value => value.Identity).Distinct(StringComparer.Ordinal).ToArray();
        return true;
    }

    private static IReadOnlyList<PlanVerifiedEvidence> SelectMinimumSupportingEvidence(
        IReadOnlyList<PlanVerifiedEvidence> evidence, long remainingQuantity)
    {
        if (remainingQuantity <= 0 || evidence.Count == 0) return [];
        var stable = evidence.OrderByDescending(value => value.CreatedAtUtc)
            .ThenBy(value => value.Identity, StringComparer.Ordinal).ToArray();
        var byQuantity = stable.OrderByDescending(value => value.Quantity)
            .ThenByDescending(value => value.CreatedAtUtc)
            .ThenBy(value => value.Identity, StringComparer.Ordinal).ToArray();
        long availableQuantity = 0;
        var minimumClaimCount = 0;
        foreach (var candidate in byQuantity)
        {
            availableQuantity += candidate.Quantity;
            minimumClaimCount++;
            if (availableQuantity >= remainingQuantity) break;
        }
        if (availableQuantity < remainingQuantity) return stable;

        var upperBound = byQuantity.Take(minimumClaimCount).Sum(value => (long)value.Quantity);
        var states = Enumerable.Range(0, minimumClaimCount + 1)
            .Select(_ => new Dictionary<long, int[]>()).ToArray();
        states[0][0] = [];
        for (var candidateIndex = 0; candidateIndex < stable.Length; candidateIndex++)
        {
            var maximumCount = Math.Min(minimumClaimCount, candidateIndex + 1);
            for (var count = maximumCount; count >= 1; count--)
            {
                foreach (var state in states[count - 1])
                {
                    var sum = state.Key + stable[candidateIndex].Quantity;
                    if (sum > upperBound) continue;
                    var selection = state.Value.Append(candidateIndex).ToArray();
                    if (!states[count].TryGetValue(sum, out var existing) || IsLexicographicallyEarlier(selection, existing))
                        states[count][sum] = selection;
                }
            }
        }

        var selectedSum = states[minimumClaimCount].Keys.Where(sum => sum >= remainingQuantity).Min();
        return states[minimumClaimCount][selectedSum].Select(index => stable[index]).ToArray();
    }

    private static bool IsLexicographicallyEarlier(IReadOnlyList<int> candidate, IReadOnlyList<int> existing)
    {
        for (var index = 0; index < candidate.Count; index++)
        {
            if (candidate[index] == existing[index]) continue;
            return candidate[index] < existing[index];
        }
        return false;
    }

    private static bool IsCompatibleEvidenceKind(PlanEvidenceKind expected, PlanEvidenceKind actual) => expected switch
    {
        PlanEvidenceKind.BuyOrder => actual is PlanEvidenceKind.BuyOrder or PlanEvidenceKind.CompletedBuy,
        PlanEvidenceKind.SellListing => actual is PlanEvidenceKind.SellListing or PlanEvidenceKind.CompletedSell,
        _ => expected == actual,
    };

    private static string RelevantEvidenceFingerprint(PlanRecord plan, PlanExecutionEvent execution, IEnumerable<PlanVerifiedEvidence> evidence)
    {
        if (execution.ExpectedEvidenceKind is not { } expected) return string.Empty;
        var step = plan.Steps.FirstOrDefault(value => value.Id == execution.StepId);
        if (step is null) return string.Empty;
        var issuedAt = execution.IssuedAtUtc ?? step.IssuedAtUtc ?? plan.StartedAtUtc;
        if (execution.Action == PlanStepAction.CancelBuyOrder)
        {
            return EvidenceFingerprint(evidence.Where(value => value.Kind == PlanEvidenceKind.BuyOrder && value.ItemId == step.ItemId &&
                value.CapturedAtUtc >= execution.OccurredAtUtc &&
                string.Equals(step.ExternalIdentity, value.ExternalIdentity ?? value.Identity, StringComparison.Ordinal)));
        }
        return EvidenceFingerprint(evidence.Where(value => IsCompatibleEvidenceKind(expected, value.Kind) && value.ItemId == step.ItemId &&
            value.CreatedAtUtc >= issuedAt && value.CapturedAtUtc >= execution.OccurredAtUtc &&
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

    private static bool TryMatchCraftInventory(PlanExecutionEvent execution,
        IReadOnlyDictionary<string, long> expectedBefore, IReadOnlyDictionary<string, long> observed,
        IReadOnlySet<string> coverageKeys, out bool contradicted)
    {
        contradicted = false;
        var effects = execution.Effects.Where(effect => effect.Kind == PlanResourceKind.Inventory && effect.Quantity != 0).ToArray();
        if (effects.Length == 0 || effects.Any(effect => !coverageKeys.Contains(Key(effect)) || !expectedBefore.ContainsKey(Key(effect)))) return false;
        var matches = true;
        foreach (var effect in effects)
        {
            var key = Key(effect);
            var before = expectedBefore[key];
            var target = checked(before + effect.Quantity);
            var actual = observed.GetValueOrDefault(key);
            if (actual == target) continue;
            matches = false;
            contradicted = true;
        }
        return matches;
    }

    private static bool CraftShadowProjectionMatchesVerified(IReadOnlyCollection<PlanExecutionEvent> events,
        IReadOnlyDictionary<string, long> expected, IReadOnlyDictionary<string, long> observed,
        PlanEvidenceSource<IReadOnlyDictionary<string, long>> physicalInventory)
    {
        var craftEvents = events.Where(value => value.Action == PlanStepAction.Craft && value.State == PlanShadowEventState.PendingConfirmation).ToArray();
        var keys = craftEvents
            .SelectMany(value => value.Effects).Where(effect => effect.Kind == PlanResourceKind.Inventory && effect.Quantity != 0)
            .Select(Key).Distinct(StringComparer.Ordinal).ToArray();
        var provenance = physicalInventory.Provenance;
        return keys.Length > 0 && provenance.Availability == PlanEvidenceAvailability.Available &&
            provenance.Completeness == PlanEvidenceCompleteness.Complete && !string.IsNullOrWhiteSpace(provenance.CaptureId) &&
            provenance.UpstreamObservedAtUtc is { } sourceObservedAt && craftEvents.All(value => sourceObservedAt >= value.OccurredAtUtc) &&
            keys.All(key => provenance.CoverageKeys.Contains(key) && expected.ContainsKey(key) && expected.GetValueOrDefault(key) == observed.GetValueOrDefault(key));
    }

    private static T? SourceValue<T>(PlanEvidenceSource<T> source) =>
        source.Provenance.Availability == PlanEvidenceAvailability.Available &&
        !string.IsNullOrWhiteSpace(source.Provenance.CaptureId) && source.Provenance.FetchedAtUtc is not null
            ? source.Value : default;

    private static void ValidateProvenance(PlanEvidenceProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        if (provenance.FetchedAtUtc is { } fetched) _ = RequireUtc(fetched);
        if (provenance.UpstreamObservedAtUtc is { } observed) _ = RequireUtc(observed);
    }

    private static PlanEvidenceProvenance? CompleteTradingPostCapture(PlanEvidenceFrame frame)
    {
        var current = frame.CurrentOrders.Provenance;
        var completed = frame.CompletedTransactions.Provenance;
        if (current.Availability != PlanEvidenceAvailability.Available || completed.Availability != PlanEvidenceAvailability.Available ||
            current.Completeness != PlanEvidenceCompleteness.Complete || completed.Completeness != PlanEvidenceCompleteness.Complete ||
            string.IsNullOrWhiteSpace(current.CaptureId) || !string.Equals(current.CaptureId, completed.CaptureId, StringComparison.Ordinal) ||
            current.FetchedAtUtc is not { } fetched || completed.FetchedAtUtc != fetched ||
            !current.CoverageKeys.IsSupersetOf(new[] { "buy_orders", "sell_listings" }) ||
            !completed.CoverageKeys.IsSupersetOf(new[] { "completed_buys", "completed_sells" }) ||
            frame.CurrentOrders.Value is null || frame.CompletedTransactions.Value is null) return null;
        return current;
    }

    private static PlanEvidenceProvenance? CompleteInventoryCapture(
        PlanEvidenceSource<IReadOnlyDictionary<string, long>> source, PlanExecutionEvent execution) =>
        HasInventoryCoverage(source, execution) ? source.Provenance : null;

    private static PlanEvidenceProvenance? CompletePhysicalInventoryCapture(
        PlanEvidenceSource<IReadOnlyDictionary<string, long>> source)
    {
        var provenance = source.Provenance;
        return source.Value is not null && provenance.Availability == PlanEvidenceAvailability.Available &&
            provenance.Completeness == PlanEvidenceCompleteness.Complete && !string.IsNullOrWhiteSpace(provenance.CaptureId) &&
            provenance.FetchedAtUtc is not null && provenance.UpstreamObservedAtUtc is not null ? provenance : null;
    }

    private static bool HasInventoryCoverage(PlanEvidenceSource<IReadOnlyDictionary<string, long>> source, PlanExecutionEvent execution)
    {
        var provenance = source.Provenance;
        var requiredKeys = execution.Effects.Where(effect => effect.Kind == PlanResourceKind.Inventory && effect.Quantity != 0)
            .Select(Key).Distinct(StringComparer.Ordinal).ToArray();
        return requiredKeys.Length > 0 && source.Value is not null && provenance.Availability == PlanEvidenceAvailability.Available &&
            provenance.Completeness == PlanEvidenceCompleteness.Complete && !string.IsNullOrWhiteSpace(provenance.CaptureId) &&
            provenance.UpstreamObservedAtUtc is { } observedAt && observedAt >= execution.OccurredAtUtc &&
            requiredKeys.All(provenance.CoverageKeys.Contains);
    }

    private static bool IsNewCapture(PlanEvidenceProvenance provenance, string? previousCaptureId, DateTimeOffset? previousCapturedAtUtc) =>
        !string.IsNullOrWhiteSpace(provenance.CaptureId) && provenance.FetchedAtUtc is { } fetchedAt &&
        !string.Equals(provenance.CaptureId, previousCaptureId, StringComparison.Ordinal) &&
        (previousCapturedAtUtc is null || fetchedAt > previousCapturedAtUtc.Value);

    private static bool IsNewPhysicalCapture(PlanEvidenceProvenance provenance, string? previousCaptureId,
        DateTimeOffset? previousFetchedAtUtc, DateTimeOffset? previousObservedAtUtc) =>
        !string.IsNullOrWhiteSpace(provenance.CaptureId) && provenance.FetchedAtUtc is { } fetchedAt &&
        provenance.UpstreamObservedAtUtc is { } observedAt && !string.Equals(provenance.CaptureId, previousCaptureId, StringComparison.Ordinal) &&
        (previousFetchedAtUtc is null || fetchedAt > previousFetchedAtUtc.Value) &&
        (previousObservedAtUtc is null || observedAt > previousObservedAtUtc.Value);

    private static bool IsNewEventNegativeCapture(PlanExecutionEvent execution, PlanEvidenceProvenance provenance)
    {
        var capturedAt = execution.Action == PlanStepAction.Craft ? provenance.UpstreamObservedAtUtc : provenance.FetchedAtUtc;
        return !string.IsNullOrWhiteSpace(provenance.CaptureId) && capturedAt is { } at &&
            at >= execution.OccurredAtUtc &&
            !string.Equals(provenance.CaptureId, execution.LastNegativeEvidenceCaptureId, StringComparison.Ordinal) &&
            (execution.LastNegativeEvidenceCapturedAtUtc is null || at > execution.LastNegativeEvidenceCapturedAtUtc.Value);
    }

    private static bool MateriallyDifferent(long? replacement, long? current)
    {
        if (replacement is null || current is null) return replacement != current;
        var basisPoints = Math.Abs(replacement.Value - current.Value) * 10_000m / Math.Max(Math.Abs(current.Value), 1);
        return basisPoints >= PlanHysteresisPolicy.Default.MaterialImprovementBasisPoints;
    }

    private static string EvidenceFingerprint(IEnumerable<PlanVerifiedEvidence> evidence) => string.Join('|', evidence
        .OrderBy(value => value.Kind).ThenBy(value => value.Identity, StringComparer.Ordinal)
        .Select(value => $"{value.Kind}:{value.Identity}:{value.ItemId}:{value.Quantity}:{value.UnitPrice.Copper}:{value.CreatedAtUtc.UtcTicks}"));

    private static bool IsCancellationAbsence(PlanRecord plan, PlanExecutionEvent execution,
        IReadOnlyCollection<PlanVerifiedEvidence> evidence, bool freshCapture)
    {
        if (execution.Action != PlanStepAction.CancelBuyOrder || !freshCapture) return false;
        var step = plan.Steps.FirstOrDefault(value => value.Id == execution.StepId);
        if (string.IsNullOrWhiteSpace(step?.ExternalIdentity)) return false;
        return !evidence.Any(value => value.Kind == PlanEvidenceKind.BuyOrder && value.ItemId == step.ItemId &&
            value.CapturedAtUtc >= execution.OccurredAtUtc &&
            string.Equals(step.ExternalIdentity, value.ExternalIdentity ?? value.Identity, StringComparison.Ordinal));
    }

    private static bool HasCancellationFillEvidence(PlanRecord plan, PlanExecutionEvent execution, IReadOnlyCollection<PlanVerifiedEvidence> evidence)
    {
        if (execution.Action != PlanStepAction.CancelBuyOrder) return false;
        var step = plan.Steps.FirstOrDefault(value => value.Id == execution.StepId);
        if (step is null) return false;
        var issuedAt = execution.IssuedAtUtc ?? step.IssuedAtUtc ?? plan.StartedAtUtc;
        return evidence.Any(value => value.Kind == PlanEvidenceKind.CompletedBuy && value.ItemId == step.ItemId &&
            value.CapturedAtUtc >= execution.OccurredAtUtc &&
            (string.Equals(step.ExternalIdentity, value.ExternalIdentity ?? value.Identity, StringComparison.Ordinal) ||
             value.CreatedAtUtc >= issuedAt && (execution.UnitPrice is not { } price || value.UnitPrice == price)));
    }

    private static bool HasExactCancellationFillEvidence(PlanRecord plan, PlanExecutionEvent execution, IReadOnlyCollection<PlanVerifiedEvidence> evidence)
    {
        if (execution.Action != PlanStepAction.CancelBuyOrder) return false;
        var step = plan.Steps.FirstOrDefault(value => value.Id == execution.StepId);
        return !string.IsNullOrWhiteSpace(step?.ExternalIdentity) && evidence.Any(value => value.Kind == PlanEvidenceKind.CompletedBuy && value.ItemId == step.ItemId &&
            string.Equals(step.ExternalIdentity, value.ExternalIdentity ?? value.Identity, StringComparison.Ordinal));
    }

    private static bool IsCancellationStillVisible(PlanRecord plan, PlanExecutionEvent execution, IReadOnlyCollection<PlanVerifiedEvidence> evidence)
    {
        if (execution.Action != PlanStepAction.CancelBuyOrder) return false;
        var step = plan.Steps.FirstOrDefault(value => value.Id == execution.StepId);
        return !string.IsNullOrWhiteSpace(step?.ExternalIdentity) && evidence.Any(value => value.Kind == PlanEvidenceKind.BuyOrder && value.ItemId == step.ItemId &&
            value.CapturedAtUtc >= execution.OccurredAtUtc &&
            string.Equals(step.ExternalIdentity, value.ExternalIdentity ?? value.Identity, StringComparison.Ordinal));
    }

    private static PlanState CompatibleLifecycleState(PlanRecord plan, IReadOnlyList<PlanStep>? updatedSteps)
    {
        var steps = updatedSteps ?? plan.Steps;
        if (plan.CurrentStepOrdinal >= 0 && plan.CurrentStepOrdinal < steps.Count && steps[plan.CurrentStepOrdinal].State == PlanStepState.Current)
            return PlanState.InProgress;
        return steps.LastOrDefault(step => step.State == PlanStepState.Confirmed)?.Action == PlanStepAction.PlaceBuyOrder
            ? PlanState.Waiting
            : PlanState.ExecutionComplete;
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

    private static string Key(PlanResourceRequirement requirement) => ResourceKey(requirement);
    private static DateTimeOffset RequireUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero ? value : throw new ArgumentException("Timestamp must be UTC.", nameof(value));

    private sealed record Selection(IReadOnlyList<PlanCandidate> Plans, Money Cash, int Utility, IReadOnlyDictionary<string, long> Quantities)
    {
        public bool IsBetterThan(Selection other) => Utility > other.Utility || Utility == other.Utility && (Plans.Count > other.Plans.Count || Plans.Count == other.Plans.Count && string.CompareOrdinal(string.Join('|', Plans.Select(p => p.Id)), string.Join('|', other.Plans.Select(p => p.Id))) < 0);
    }
}
