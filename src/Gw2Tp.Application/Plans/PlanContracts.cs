using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Plans;

public enum PlanAttention { Passive = 1, Active }
public enum PlanState { Proposed = 1, InProgress, Waiting, RecheckRequired, ReconciliationRequired, ExecutionComplete, Invalid }
public enum PlanStepAction { BuyNow = 1, PlaceBuyOrder, CancelBuyOrder, List, Relist, SellNow, Craft }
public enum PlanStepState { Pending = 1, Current, LocallyReported, AwaitingConfirmation, Confirmed, PartiallyConfirmed, RecheckRequired, Invalidated }
public enum PlanShadowEventState { PendingConfirmation = 1, Confirmed, PartiallyConfirmed, Reversed, Invalidated }
public enum PlanReconciliationState { None = 1, AwaitingEvidence, Compatible, Contradicted }
public enum PlanResourceKind { Cash = 1, Inventory, OpenOrderExposure, Position, ExpectedIncoming }
public enum PlanEvidenceKind { BuyOrder = 1, SellListing, CompletedBuy, CompletedSell }

/// <summary>Versioned, typed resource demand. Money is always exact copper.</summary>
public sealed record PlanResourceRequirement(PlanResourceKind Kind, string ResourceId, long Quantity, Money Cash);

public sealed record PlanStep(
    string Id, PlanStepAction Action, int ItemId, string ItemName, int Quantity,
    Money? UnitPrice, IReadOnlyList<string> DependsOnStepIds, PlanStepState State,
    string? ExternalIdentity = null, DateTimeOffset? IssuedAtUtc = null,
    IReadOnlyList<PlanResourceRequirement>? CraftEffects = null);

public sealed record PlanCandidate(
    string Id, int Version, string SourceOpportunityId, PlanAttention Attention,
    IReadOnlyList<PlanStep> Steps, IReadOnlyList<PlanResourceRequirement> Requirements,
    Money ModeledProfit, Money CommittedCapital, int ConfidenceBasisPoints, int UrgencyBasisPoints,
    int ExpectedInteractionSeconds, long Utility, bool IsHardEligible, IReadOnlyList<string> ExclusionReasons);

public sealed record PlanBundleSelection(
    IReadOnlyList<PlanCandidate> Plans, Money ReservedCash, int Utility,
    IReadOnlyList<string> ExcludedCandidateIds, Money IntentionallyFreeCash);

public sealed record PlanHysteresisPolicy(int Version, int MaterialImprovementBasisPoints)
{
    public static PlanHysteresisPolicy Default { get; } = new(1, 1_000);
}

public sealed record PlanExecutionEvent(
    string Id, string PlanId, string StepId, long Sequence, DateTimeOffset OccurredAtUtc,
    int Quantity, Money? UnitPrice, IReadOnlyList<PlanResourceRequirement> Effects,
    PlanShadowEventState State, string? ReversesEventId, IReadOnlyList<string> DependsOnEventIds,
    DateTimeOffset? ExpectedObservableUntilUtc = null, int? VerifiedQuantity = null,
    Money? VerifiedUnitPrice = null, PlanEvidenceKind? ExpectedEvidenceKind = null,
    DateTimeOffset? IssuedAtUtc = null, string? LastRelevantEvidenceFingerprint = null,
    IReadOnlyList<string>? VerifiedEvidenceIds = null, PlanStepAction? Action = null,
    DateTimeOffset? FirstNegativeEvidenceCapturedAtUtc = null, int NegativeEvidenceCaptureCount = 0);

public sealed record PlanRecord(
    string Id, int Version, string SourceOpportunityId, PlanAttention Attention,
    PlanState State, PlanReconciliationState ReconciliationState, DateTimeOffset StartedAtUtc,
    IReadOnlyList<PlanResourceRequirement> Reservations, Money ModeledProfit,
    int CurrentStepOrdinal, IReadOnlyList<PlanStep> Steps, IReadOnlyList<PlanExecutionEvent> Events,
    long BaselineUtility, PlanHysteresisPolicy HysteresisPolicy,
    Money? BaselineVerifiedCash = null,
    IReadOnlyDictionary<string, long>? BaselineVerifiedQuantities = null,
    int ConsecutiveContradictionCount = 0,
    DateTimeOffset? LastObservedAtUtc = null,
    DateTimeOffset? LastEvidenceCapturedAtUtc = null,
    string? LastEvidenceFingerprint = null,
    long Revision = 0,
    bool IsCancelled = false,
    DateTimeOffset? CancellationReconciliationExpiresAtUtc = null,
    bool IsReconciliationOnly = false);

public sealed record PlanVerifiedEvidence(
    string Identity, PlanEvidenceKind Kind, int ItemId, int Quantity, Money UnitPrice,
    DateTimeOffset CreatedAtUtc, DateTimeOffset ObservedAtUtc,
    string? ExternalIdentity = null);

public sealed class PlanConcurrencyException : InvalidOperationException
{
    public PlanConcurrencyException() : base("The plan changed while it was being updated.") { }
}

public enum PlanStartResult { Started = 1, AlreadyStarted, ResourcesUnavailable }

public sealed record PlanEffectiveResources(Money VerifiedCash, Money EffectiveCash,
    IReadOnlyDictionary<string, long> Quantities);

public interface IPlanRepository
{
    Task<IReadOnlyList<PlanRecord>> GetStartedAsync(long accountProfileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlanRecord>> GetReconciliationCandidatesAsync(long accountProfileId, CancellationToken cancellationToken = default);
    Task<PlanStartResult> TryStartAsync(long accountProfileId, PlanRecord plan, Money verifiedCash,
        Money hardReserve, IReadOnlyDictionary<string, long> verifiedQuantities,
        CancellationToken cancellationToken = default);
    Task SaveAsync(long accountProfileId, PlanRecord plan, CancellationToken cancellationToken = default);
}

public interface IPlanOrchestrationService
{
    Task<PlanBundleSelection> SelectAsync(IReadOnlyList<PlanCandidate> candidates, Money availableCash, Money hardReserve,
        CancellationToken cancellationToken = default, IReadOnlyDictionary<string, long>? availableQuantities = null);
    PlanRecord Start(PlanCandidate candidate, DateTimeOffset startedAtUtc, Money? verifiedCash = null,
        IReadOnlyDictionary<string, long>? verifiedQuantities = null);
    PlanRecord ReportStep(PlanRecord plan, int quantity, Money? unitPrice, DateTimeOffset occurredAtUtc);
    PlanRecord CancelUnperformedStep(PlanRecord plan);
    PlanRecord UndoLastStep(PlanRecord plan, DateTimeOffset occurredAtUtc);
    PlanRecord ReconcileWithVerifiedState(PlanRecord plan, Money verifiedCash,
        IReadOnlyDictionary<string, long> verifiedQuantities, DateTimeOffset observedAtUtc,
        IReadOnlyCollection<PlanVerifiedEvidence>? evidence = null,
        DateTimeOffset? evidenceCapturedAtUtc = null,
        IReadOnlySet<PlanEvidenceKind>? completeEvidenceKinds = null);
    PlanRecord ApplyRefresh(PlanRecord plan, PlanCandidate? currentCandidate, bool evidenceReady);
    PlanRecord Reconcile(PlanRecord plan, IReadOnlyCollection<string> confirmedEventIds, bool materiallyContradicted);
}
