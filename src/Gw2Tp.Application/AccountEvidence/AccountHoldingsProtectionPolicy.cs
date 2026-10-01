using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.PersonalTradingPost;

namespace Gw2Tp.Application.AccountEvidence;

public enum HoldingsItemCategory { Unknown, Equipment, Commodity }
public enum HoldingsProtectionReason
{
    SourceIncomplete, UnknownLocation, UnknownCategory, UnknownBinding, ActorMismatch,
    ActorAccessRequired, EquipmentCoverageIncomplete, EquipmentReferenceAmbiguous,
    UserProtected, KeepQuantity,
}

public sealed record AccountHoldingsProtectionRules(
    AccountScope AccountScope, IReadOnlySet<int> ProtectedItemIds,
    IReadOnlyDictionary<int, int> MinimumRetainedQuantities);

/// <summary>Caller-owned invocation floor only. Persistence and reset/epoch rules belong to P02C/P03A.</summary>
public sealed record EquipmentProtectionFloor(AccountScope AccountScope, IReadOnlySet<int> ItemIds);

/// <summary>Protection allowances over observations, NOT admission, physical aggregation or tradeability proof.</summary>
public sealed record ProtectedHoldingsObservation(
    HoldingsItemObservation Observation, int ConsumableAllowance, int SaleableAllowance,
    IReadOnlyList<HoldingsProtectionReason> Reasons);
public sealed record AccountHoldingsProtectionResult(
    bool HasCompleteEquipmentCoverage, IReadOnlyList<ProtectedHoldingsObservation> Rows,
    EquipmentProtectionFloor Floor);

/// <summary>Pure account-wide protection policy. No clock, HTTP, persistence or consuming instructions.</summary>
public sealed class AccountHoldingsProtectionPolicy
{
    public AccountHoldingsProtectionResult Evaluate(
        AccountHoldingsCapture capture, AccountHoldingsProtectionRules rules,
        IReadOnlyDictionary<int, HoldingsItemCategory> categories,
        AccountActor? consumingActor = null, EquipmentProtectionFloor? previousFloor = null)
    {
        if (rules.AccountScope != capture.AccountScope ||
            (previousFloor is not null && previousFloor.AccountScope != capture.AccountScope))
            throw new ArgumentException("Protection inputs must belong to the captured account.");
        if (rules.ProtectedItemIds.Any(id => id <= 0) || rules.MinimumRetainedQuantities.Any(pair => pair.Key <= 0 || pair.Value < 0))
            throw new ArgumentException("Protection rules require positive item IDs and nonnegative reserves.");

        var currentActors = AccountEvidencePolicyFacts.CurrentActors(capture);
        var floor = new HashSet<int>(previousFloor?.ItemIds ?? new HashSet<int>());
        foreach (var actor in capture.Characters)
        {
            // Partial values still protect. Missing reads never erase previously observed IDs.
            var references = (actor.Equipment.Value ?? []).Concat(
                (actor.EquipmentTabs.Value ?? []).SelectMany(tab => tab.Equipment));
            foreach (var row in references)
            {
                floor.Add(row.ItemId);
                foreach (var component in row.AttachedComponents) floor.Add(component.ItemId);
            }
        }
        var completeEquipment = currentActors is not null && capture.Characters.All(actor =>
            AccountEvidencePolicyFacts.Complete(actor.Equipment, AccountHoldingsSource.CharacterEquipment, actor.Actor.ActorId) &&
            AccountEvidencePolicyFacts.Complete(actor.EquipmentTabRoster, AccountHoldingsSource.CharacterEquipmentTabRoster, actor.Actor.ActorId) &&
            AccountEvidencePolicyFacts.Complete(actor.EquipmentTabs, AccountHoldingsSource.CharacterEquipmentTabs, actor.Actor.ActorId) &&
            actor.EquipmentTabs.Value!.Select(tab => tab.TabId).ToHashSet().SetEquals(actor.EquipmentTabRoster.Value!) &&
            actor.EquipmentTabs.Value!.Count(tab => tab.IsActive) == 1 &&
            actor.Equipment.Value!.Concat(actor.EquipmentTabs.Value!.SelectMany(tab => tab.Equipment)).All(row =>
                row.ActorId == actor.Actor.ActorId && row.Location != EquipmentObservedLocation.Unknown &&
                row.Binding != AccountItemBinding.OtherBound));

        var sources = AccountEvidencePolicyFacts.InventorySources(capture).ToArray();
        var observations = sources.SelectMany(source => (source.Value?.Items ?? []).Select(row => (source, row)))
            .OrderBy(value => value.row.ItemId).ThenBy(value => value.row.Location.Source)
            .ThenBy(value => value.row.Location.ActorId, StringComparer.Ordinal)
            .ThenBy(value => value.row.Location.BagIndex).ThenBy(value => value.row.Location.SlotIndex)
            .ThenBy(value => value.row.Location.SourceRowIndices.FirstOrDefault()).ToArray();
        var remainingReserve = new Dictionary<int, int>(rules.MinimumRetainedQuantities);
        var results = new List<ProtectedHoldingsObservation>();
        foreach (var (source, row) in observations)
        {
            var reasons = new HashSet<HoldingsProtectionReason>();
            var location = row.Location;
            if (!AccountEvidencePolicyFacts.Complete(source, source.Source, source.ActorId)) reasons.Add(HoldingsProtectionReason.SourceIncomplete);
            if (!AccountEvidencePolicyFacts.ValidLocation(row) || location.Source != source.Source || location.ActorId != source.ActorId)
                reasons.Add(HoldingsProtectionReason.UnknownLocation);
            if (row.ItemId <= 0 || row.Quantity < 0) reasons.Add(HoldingsProtectionReason.SourceIncomplete);
            var category = categories.GetValueOrDefault(row.ItemId);
            if (category is not (HoldingsItemCategory.Commodity or HoldingsItemCategory.Equipment)) reasons.Add(HoldingsProtectionReason.UnknownCategory);
            if (category == HoldingsItemCategory.Equipment && !completeEquipment) reasons.Add(HoldingsProtectionReason.EquipmentCoverageIncomplete);
            if (floor.Contains(row.ItemId)) reasons.Add(HoldingsProtectionReason.EquipmentReferenceAmbiguous);
            if (rules.ProtectedItemIds.Contains(row.ItemId)) reasons.Add(HoldingsProtectionReason.UserProtected);
            if (row.Binding is not (AccountItemBinding.Unspecified or AccountItemBinding.AccountBound or AccountItemBinding.CharacterBound) ||
                (row.Binding != AccountItemBinding.CharacterBound && row.BoundActor is not null)) reasons.Add(HoldingsProtectionReason.UnknownBinding);
            if (location.Source == AccountHoldingsSource.CharacterInventory &&
                (currentActors is null || !currentActors.ContainsKey(location.ActorId!))) reasons.Add(HoldingsProtectionReason.ActorMismatch);
            if (row.Binding == AccountItemBinding.CharacterBound)
            {
                if (row.BoundActor is null || currentActors is null ||
                    !currentActors.TryGetValue(row.BoundActor.ActorId, out var bound) || bound != row.BoundActor)
                    reasons.Add(HoldingsProtectionReason.ActorMismatch);
                if (consumingActor is null) reasons.Add(HoldingsProtectionReason.ActorAccessRequired);
                else if (consumingActor != row.BoundActor) reasons.Add(HoldingsProtectionReason.ActorMismatch);
            }
            if (consumingActor is not null && (currentActors is null ||
                !currentActors.TryGetValue(consumingActor.ActorId, out var current) || current != consumingActor))
                reasons.Add(HoldingsProtectionReason.ActorMismatch);

            var allowed = reasons.Count == 0 ? row.Quantity : 0;
            var retained = Math.Min(allowed, remainingReserve.GetValueOrDefault(row.ItemId));
            if (retained > 0)
            {
                allowed -= retained;
                remainingReserve[row.ItemId] -= retained;
                reasons.Add(HoldingsProtectionReason.KeepQuantity);
            }
            results.Add(new(row, allowed, row.Binding == AccountItemBinding.Unspecified ? allowed : 0, reasons.Order().ToArray()));
        }
        return new(completeEquipment, results.ToArray(), new(capture.AccountScope, floor));
    }
}

internal static class AccountEvidencePolicyFacts
{
    internal static bool Complete<T>(AccountSourceEvidence<T> source, AccountHoldingsSource expected, string? actorId = null) =>
        source.Source == expected && source.ActorId == actorId && source.Availability == EvidenceAvailability.Available &&
        source.Completeness == EvidenceCompleteness.Complete && source.Value is not null && source.ErrorCategory is null;

    internal static Dictionary<string, AccountActor>? CurrentActors(AccountHoldingsCapture capture)
    {
        if (!Complete(capture.Roster, AccountHoldingsSource.CharacterRoster)) return null;
        var roster = capture.Roster.Value!;
        if (roster.Any(actor => string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.DisplayName)) ||
            roster.Select(actor => actor.ActorId).Distinct(StringComparer.Ordinal).Count() != roster.Count ||
            capture.Characters.Count != roster.Count ||
            capture.Characters.Select(actor => actor.Actor.ActorId).Distinct(StringComparer.Ordinal).Count() != roster.Count ||
            capture.Characters.Any(actor => !roster.Contains(actor.Actor))) return null;
        return roster.ToDictionary(actor => actor.ActorId, StringComparer.Ordinal);
    }

    internal static IEnumerable<AccountSourceEvidence<HoldingsInventoryObservation>> InventorySources(AccountHoldingsCapture capture) =>
        new[] { capture.Bank, capture.SharedInventory, capture.MaterialStorage }.Concat(capture.Characters.Select(actor => actor.Inventory));

    internal static bool ValidLocation(HoldingsItemObservation row) => row.Location.Source switch
    {
        AccountHoldingsSource.CharacterInventory => row.Location.ActorId is not null && row.Location.BagIndex is >= 0 && row.Location.SlotIndex is >= 0,
        AccountHoldingsSource.Bank or AccountHoldingsSource.SharedInventory => row.Location.ActorId is null && row.Location.BagIndex is null && row.Location.SlotIndex is >= 0,
        AccountHoldingsSource.MaterialStorage => row.Location.ActorId is null && row.Location.BagIndex is null && row.Location.SlotIndex is null && row.Location.SourceRowIndices.Count > 0,
        _ => false,
    };
}
