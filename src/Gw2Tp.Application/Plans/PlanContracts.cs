using Gw2Tp.Domain.Finance;
using Gw2Tp.Application.PersonalTradingPost;
using System.Text.Json;

namespace Gw2Tp.Application.Plans;

public enum PlanAttention { Passive = 1, Active }
public enum PlanState { Proposed = 1, InProgress, Waiting, RecheckRequired, ReconciliationRequired, ExecutionComplete, Invalid }
public enum PlanStepAction { BuyNow = 1, PlaceBuyOrder, CancelBuyOrder, List, Relist, SellNow, Craft }
public enum PlanStepState { Pending = 1, Current, LocallyReported, AwaitingConfirmation, Confirmed, PartiallyConfirmed, RecheckRequired, Invalidated }
public enum PlanShadowEventState { PendingConfirmation = 1, Confirmed, PartiallyConfirmed, Reversed, Invalidated }
public enum PlanReconciliationState { None = 1, AwaitingEvidence, Compatible, Contradicted }
public enum PlanReconciliationReason { None = 0, CraftInventoryMismatch, TradingPostEvidenceMismatch, CancellationStillVisible, CancellationLateFill, OpportunityChanged }
public enum PlanResourceKind { Cash = 1, Inventory, OpenOrderExposure, Position, ExpectedIncoming }
public enum PlanEvidenceKind { BuyOrder = 1, SellListing, CompletedBuy, CompletedSell }
public enum PlanEvidenceAvailability { Unknown = 0, Available, Unavailable }
public enum PlanEvidenceCompleteness { Unknown = 0, Partial, Complete }

/// <summary>Versioned, typed resource demand. Money is always exact copper.</summary>
public sealed record PlanResourceRequirement(PlanResourceKind Kind, string ResourceId, long Quantity, Money Cash);

/// <summary>A checked, canonical aggregate of equivalent resource requirements.</summary>
public sealed record PlanResourceDemand(PlanResourceKind Kind, string ResourceId, long Quantity, Money Cash);

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
    DateTimeOffset? FirstNegativeEvidenceCapturedAtUtc = null, int NegativeEvidenceCaptureCount = 0,
    string? LastNegativeEvidenceCaptureId = null, DateTimeOffset? LastNegativeEvidenceCapturedAtUtc = null);

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
    bool IsReconciliationOnly = false,
    string? LastEvidenceCaptureId = null,
    string? LastPhysicalInventoryCaptureId = null,
    DateTimeOffset? LastPhysicalInventoryFetchedAtUtc = null,
    DateTimeOffset? LastPhysicalInventoryObservedAtUtc = null,
    PlanReconciliationReason ReconciliationReason = PlanReconciliationReason.None);

public enum PlanCompletionOperation { ReportPerformed = 1, NotPerformed }

/// <summary>A logical user command bound to one displayed execution step and durable revision.</summary>
public sealed record PlanCompletionCommand(
    string PlanId,
    string StepId,
    long ExpectedRevision,
    string CommandId,
    PlanCompletionOperation Operation,
    int Quantity,
    Money? UnitPrice);

/// <summary>Durable identity and committed result of one step-completion command.</summary>
public sealed record PlanCompletionReceipt(
    string PlanId,
    string CommandId,
    string StepId,
    long ExpectedRevision,
    PlanCompletionOperation Operation,
    int Quantity,
    Money? UnitPrice,
    long CommittedRevision,
    string? EventId,
    DateTimeOffset CreatedAtUtc);

public enum PlanCompletionStatus { Applied = 1, AlreadyApplied, Conflict, NotFound, Invalid }

public sealed record PlanCompletionResult(PlanCompletionStatus Status, PlanCompletionReceipt? Receipt = null);

public sealed record PlanVerifiedEvidence(
    string Identity, PlanEvidenceKind Kind, int ItemId, int Quantity, Money UnitPrice,
    DateTimeOffset CreatedAtUtc, DateTimeOffset CapturedAtUtc,
    string? ExternalIdentity = null);

/// <summary>Source-local capture facts; fetch time and upstream observation time remain distinct.</summary>
public sealed record PlanEvidenceProvenance(
    string? CaptureId,
    DateTimeOffset? FetchedAtUtc,
    DateTimeOffset? UpstreamObservedAtUtc,
    PlanEvidenceAvailability Availability,
    PlanEvidenceCompleteness Completeness,
    IReadOnlySet<string> CoverageKeys);

public sealed record PlanEvidenceSource<T>(PlanEvidenceProvenance Provenance, T? Value);

/// <summary>A trusted, account-scoped reconciliation input with independent source provenance.</summary>
public sealed record PlanEvidenceFrame(
    AccountScope AccountScope,
    DateTimeOffset EvaluationTimeUtc,
    PlanEvidenceSource<IReadOnlyDictionary<string, long>> PhysicalInventory,
    PlanEvidenceSource<Money> Cash,
    PlanEvidenceSource<IReadOnlyList<PlanVerifiedEvidence>> CurrentOrders,
    PlanEvidenceSource<IReadOnlyList<PlanVerifiedEvidence>> CompletedTransactions);

/// <summary>Structural comparison for durable plans containing record collections and dictionaries.</summary>
public static class PlanRecordSemantics
{
    public static bool AreEqual(PlanRecord left, PlanRecord right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        using var leftJson = JsonDocument.Parse(JsonSerializer.Serialize(left));
        using var rightJson = JsonDocument.Parse(JsonSerializer.Serialize(right));
        return Equal(leftJson.RootElement, rightJson.RootElement);
    }

    private static bool Equal(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind) return false;
        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
                var leftProperties = left.EnumerateObject().OrderBy(value => value.Name, StringComparer.Ordinal).ToArray();
                var rightProperties = right.EnumerateObject().OrderBy(value => value.Name, StringComparer.Ordinal).ToArray();
                return leftProperties.Length == rightProperties.Length && leftProperties.Zip(rightProperties)
                    .All(pair => pair.First.Name == pair.Second.Name && Equal(pair.First.Value, pair.Second.Value));
            case JsonValueKind.Array:
                var leftItems = left.EnumerateArray().ToArray();
                var rightItems = right.EnumerateArray().ToArray();
                return leftItems.Length == rightItems.Length && leftItems.Zip(rightItems).All(pair => Equal(pair.First, pair.Second));
            case JsonValueKind.String:
                return left.GetString() == right.GetString();
            case JsonValueKind.Number:
                return left.GetRawText() == right.GetRawText();
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return true;
            default:
                return left.GetRawText() == right.GetRawText();
        }
    }
}

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
    /// <summary>
    /// Applies one deterministic account-wide reconciliation view and commits all resulting plan revisions atomically.
    /// The callback receives every stored plan so durable evidence identities remain claimed after plans become terminal.
    /// </summary>
    Task<IReadOnlyList<PlanRecord>> ApplyAccountReconciliationAsync(long accountProfileId,
        Func<IReadOnlyList<PlanRecord>, IReadOnlyList<PlanRecord>> reconcile,
        CancellationToken cancellationToken = default);
    Task<PlanStartResult> TryStartAsync(long accountProfileId, PlanRecord plan, Money verifiedCash,
        Money hardReserve, IReadOnlyDictionary<string, long> verifiedQuantities,
        CancellationToken cancellationToken = default);
    Task<PlanCompletionResult> CompleteStepAsync(long accountProfileId, PlanCompletionCommand command,
        Func<PlanRecord, PlanRecord> transition, CancellationToken cancellationToken = default);
    Task SaveAsync(long accountProfileId, PlanRecord plan, CancellationToken cancellationToken = default);
}

public interface IPlanCompletionCommandService
{
    Task<PlanCompletionResult> CompleteAsync(long accountProfileId, PlanCompletionCommand command,
        CancellationToken cancellationToken = default);
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
    PlanRecord ReconcileWithVerifiedState(PlanRecord plan, AccountScope trustedAccountScope, PlanEvidenceFrame evidenceFrame,
        IReadOnlySet<string>? accountClaimedEvidenceIds = null);
    IReadOnlyList<PlanRecord> ReconcileAccountPlans(IReadOnlyList<PlanRecord> accountPlans,
        AccountScope trustedAccountScope, PlanEvidenceFrame evidenceFrame, DateTimeOffset evaluationTimeUtc,
        Func<PlanRecord, PlanRecord>? afterReconcile = null);
    PlanRecord ApplyRefresh(PlanRecord plan, PlanCandidate? currentCandidate, bool evidenceReady);
    PlanRecord Reconcile(PlanRecord plan, IReadOnlyCollection<string> confirmedEventIds, bool materiallyContradicted);
}
