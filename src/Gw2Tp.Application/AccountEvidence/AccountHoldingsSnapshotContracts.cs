using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Application.Plans;

namespace Gw2Tp.Application.AccountEvidence;

/// <summary>Normalized private evidence, not an upstream DTO or a coherent physical snapshot.</summary>
public sealed record AccountHoldingsSnapshot(
    AccountHoldingsCapture Capture, string StoreIncarnation, string Generation,
    IReadOnlyDictionary<int, HoldingsItemCategory> Categories,
    AccountHoldingsProtectionRules Rules, EquipmentProtectionFloor ProtectionFloor,
    IReadOnlyList<HoldingsItemObservation> StaleObservations);

public interface IAccountHoldingsSnapshotRepository
{
    /// <summary>Atomic monotone replacement; merges protective floors and failed-source stale observations.</summary>
    Task<bool> ReplaceAsync(AccountHoldingsSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<AccountHoldingsSnapshot?> GetLatestAsync(AccountScope scope, CancellationToken cancellationToken = default);
}

public interface IAccountHoldingsSnapshotService
{
    Task<Gw2ApiResult<AccountHoldingsSnapshot>> RefreshAsync(CancellationToken cancellationToken = default);
    Task<AccountHoldingsSnapshot?> GetLatestAsync(AccountScope scope, CancellationToken cancellationToken = default);
    Task<AccountHoldingsProjection?> GetProjectionAsync(AccountScope scope, DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken = default);
}

public interface IAccountHoldingsRulesProvider
{
    AccountHoldingsProtectionRules Get(AccountScope scope);
}

/// <summary>Explicit local policy seam. Durable settings/override UI are outside P02C.</summary>
public sealed class DefaultAccountHoldingsRulesProvider : IAccountHoldingsRulesProvider
{
    public AccountHoldingsProtectionRules Get(AccountScope scope) => new(scope, new HashSet<int>(), new Dictionary<int, int>());
}

public enum HoldingsAdmissionReason
{
    IndependentLocationsOmitted, ProtectionOrUnknownEvidence, StaleGeneration,
    InaccessibleActor, RetainedQuantity, DeliveryUncollected,
}

public sealed record HoldingsAdmission(int ItemId, HoldingsItemObservation Observation,
    int AdmissionQuantity, int TradeableQuantity, IReadOnlyList<HoldingsAdmissionReason> Reasons);

public sealed record HoldingsItemProjection(int ItemId, long ObservedQuantity, long ProtectedOrUnknownQuantity,
    long OmittedQuantity, HoldingsAdmission? Admission);

public sealed record AccountHoldingsProjection(AccountHoldingsSnapshot Snapshot,
    AccountHoldingsCapture FreshCapture, IReadOnlyList<ProtectedHoldingsObservation> Rows,
    IReadOnlyList<HoldingsItemProjection> Items, bool IsCurrentGeneration)
{
    public IReadOnlyDictionary<string, long> Quantities => Items.Where(item => item.Admission is { AdmissionQuantity: > 0 })
        .ToDictionary(item => $"{(int)PlanResourceKind.Inventory}:{item.ItemId}", item => (long)item.Admission!.AdmissionQuantity, StringComparer.Ordinal);
    public IReadOnlyDictionary<string, long> TradeableQuantities => Items.Where(item => item.Admission is { TradeableQuantity: > 0 })
        .ToDictionary(item => $"{(int)PlanResourceKind.Inventory}:{item.ItemId}", item => (long)item.Admission!.TradeableQuantity, StringComparer.Ordinal);
    public HoldingsDeliveryObservation? UncollectedDelivery => Snapshot.Capture.Delivery.Value;

    public bool CanAccess(HoldingsAdmission admission, string? actorId) => admission.Observation.Binding switch
    {
        AccountItemBinding.CharacterBound => actorId is not null && admission.Observation.BoundActor?.ActorId == actorId &&
            (admission.Observation.Location.Source != AccountHoldingsSource.CharacterInventory || admission.Observation.Location.ActorId == actorId),
        AccountItemBinding.Unspecified or AccountItemBinding.AccountBound =>
            actorId is null || admission.Observation.Location.Source != AccountHoldingsSource.CharacterInventory || admission.Observation.Location.ActorId == actorId,
        _ => false,
    };
}

/// <summary>Location coordinate and actor are committed separately from acquisition/cost provenance.</summary>
public sealed record PlanHoldingsCommitment(int ItemId, HoldingsLocation Location, long Quantity,
    AccountItemBinding Binding, string? BoundActorId);

public sealed record PlanHoldingsAuthority(string AccountScopeId, string StoreIncarnation, string Generation,
    IReadOnlyList<PlanHoldingsCommitment> Commitments, string? CraftingActorId = null,
    IReadOnlyList<CraftingRecipe>? Recipes = null, string? CraftingActorName = null);
