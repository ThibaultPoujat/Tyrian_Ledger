using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.PersonalTradingPost;

namespace Gw2Tp.Application.AccountEvidence;

/// <summary>Typed internal observations. Admission, persistence and safe presentation are separate downstream boundaries.</summary>
public interface IAccountHoldingsCollector
{
    Task<Gw2ApiResult<AccountHoldingsCapture>> CollectAsync(
        DateTimeOffset evaluatedAtUtc, CancellationToken cancellationToken = default);
}

public enum AccountHoldingsSource
{
    AccountIdentity,
    CharacterRoster,
    CharacterInventory,
    SharedInventory,
    Bank,
    MaterialStorage,
    TradingPostDelivery,
    RecipeUnlocks,
    CharacterCrafting,
    CharacterEquipment,
    CharacterEquipmentTabRoster,
    CharacterEquipmentTabs,
}

public enum EvidenceAvailability { Available, MissingPermission, Unavailable }
public enum EvidenceCompleteness { Unknown, Partial, Complete }

/// <summary>Local private name and deterministic account/name reference. Rename means a new actor, not identity proof.</summary>
public sealed record AccountActor(string ActorId, string DisplayName)
{
    public override string ToString() => $"AccountActor {{ ActorId = {ActorId} }}";
}

/// <summary>Local fetch interval includes queuing/retries. Null upstream time means no observation clock is established.</summary>
public sealed record EvidenceFetchProvenance(
    DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc, DateTimeOffset? UpstreamObservedAtUtc);

/// <summary>Includes empty positions and duplicate raw material rows. Failure leaves both counts unknown.</summary>
public sealed record EvidenceSourceCoverage(int? ExpectedLocationCount, int? ObservedLocationCount);

public sealed record AccountSourceEvidence<T>(
    AccountHoldingsSource Source,
    string? ActorId,
    EvidenceAvailability Availability,
    EvidenceCompleteness Completeness,
    EvidenceSourceCoverage Coverage,
    EvidenceFetchProvenance Fetch,
    T? Value,
    Gw2ApiErrorCategory? ErrorCategory);

/// <summary>Coordinates identify a location in one capture, never a globally stable item instance.</summary>
public sealed record HoldingsLocation(
    AccountHoldingsSource Source,
    string? ActorId,
    int? BagIndex,
    int? SlotIndex,
    IReadOnlyList<int> MaterialCategoryIds,
    IReadOnlyList<int> SourceRowIndices);

public enum HoldingsComponentKind { Upgrade, Infusion }
public sealed record HoldingsAttachedComponent(HoldingsComponentKind Kind, int Index, int ItemId);

public sealed record HoldingsItemObservation(
    HoldingsLocation Location,
    int ItemId,
    int Quantity,
    AccountItemBinding Binding,
    AccountActor? BoundActor,
    IReadOnlyList<HoldingsAttachedComponent> AttachedComponents);

/// <summary>A container reference, explicitly separate from loose item holdings.</summary>
public sealed record InstalledBagObservation(int BagIndex, int ItemId, int Size);

public sealed record HoldingsInventoryObservation(
    IReadOnlyList<HoldingsItemObservation> Items,
    IReadOnlyList<InstalledBagObservation> InstalledBags);

public sealed record DeliveryItemObservation(int RowIndex, int ItemId, int Quantity);
public sealed record ObservedDeliveryItemTotal(int ItemId, int Quantity);

/// <summary>Uncollected delivery observations. Coins are integer copper, never wallet cash or usable stock.</summary>
public sealed record HoldingsDeliveryObservation(
    long CoinsCopper,
    IReadOnlyList<DeliveryItemObservation> Items,
    IReadOnlyList<ObservedDeliveryItemTotal> ObservedTotals);

public sealed record ActorHoldingsEvidence(
    AccountActor Actor,
    AccountSourceEvidence<HoldingsInventoryObservation> Inventory,
    AccountSourceEvidence<IReadOnlyList<CraftingDisciplineCapability>> Crafting,
    AccountSourceEvidence<IReadOnlyList<EquipmentProtectionObservation>> Equipment,
    AccountSourceEvidence<IReadOnlyList<int>> EquipmentTabRoster,
    AccountSourceEvidence<IReadOnlyList<EquipmentTabObservation>> EquipmentTabs);

public sealed record CharacterHoldingsCoverage(
    EvidenceCompleteness Completeness, int? ExpectedActorCount, int SuccessfulActorCount);

/// <summary>Successful endpoint coverage does not establish coherent cross-source physical state.</summary>
public sealed record AccountHoldingsCapture(
    AccountScope AccountScope,
    Guid RefreshId,
    DateTimeOffset EvaluatedAtUtc,
    EvidenceFetchProvenance AccountIdentityFetch,
    AccountSourceEvidence<IReadOnlyList<AccountActor>> Roster,
    AccountSourceEvidence<HoldingsInventoryObservation> Bank,
    AccountSourceEvidence<HoldingsInventoryObservation> SharedInventory,
    AccountSourceEvidence<HoldingsInventoryObservation> MaterialStorage,
    AccountSourceEvidence<HoldingsDeliveryObservation> Delivery,
    IReadOnlyList<ActorHoldingsEvidence> Characters,
    CharacterHoldingsCoverage CharacterCoverage,
    AccountSourceEvidence<IReadOnlyList<int>> RecipeUnlocks);

public enum EquipmentObservedLocation
{
    Unknown, Equipped, Armory, EquippedFromLegendaryArmory, LegendaryArmory,
}

/// <summary>A protection reference, never a physical quantity or item-instance identifier.</summary>
public sealed record EquipmentProtectionObservation(
    string ActorId, int RowIndex, int ItemId, string? Slot,
    EquipmentObservedLocation Location, IReadOnlyList<int> TabIds, int? UnlockCount,
    AccountItemBinding Binding, AccountActor? BoundActor,
    IReadOnlyList<HoldingsAttachedComponent> AttachedComponents);

public sealed record EquipmentTabObservation(
    int TabId, bool IsActive, IReadOnlyList<EquipmentProtectionObservation> Equipment);
