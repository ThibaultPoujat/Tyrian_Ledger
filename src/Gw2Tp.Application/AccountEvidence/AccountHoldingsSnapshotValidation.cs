using Gw2Tp.Application.Crafting;

namespace Gw2Tp.Application.AccountEvidence;

/// <summary>Shared typed validation at persistence, read and restore boundaries.</summary>
public static class AccountHoldingsSnapshotValidation
{
    public static void Validate(AccountHoldingsSnapshot snapshot)
    {
        void Require(bool valid) { if (!valid) throw new ArgumentException("Invalid normalized holdings evidence."); }
        void Fetch(EvidenceFetchProvenance value) => Require(value.StartedAtUtc.Offset == TimeSpan.Zero &&
            value.CompletedAtUtc.Offset == TimeSpan.Zero && value.StartedAtUtc <= value.CompletedAtUtc &&
            (value.UpstreamObservedAtUtc is null || value.UpstreamObservedAtUtc.Value.Offset == TimeSpan.Zero));
        void Actor(AccountActor actor) => Require(!string.IsNullOrWhiteSpace(actor.ActorId) && !string.IsNullOrWhiteSpace(actor.DisplayName));
        void Components(IReadOnlyList<HoldingsAttachedComponent> values) => Require(values.All(value =>
            Enum.IsDefined(value.Kind) && value.Index >= 0 && value.ItemId > 0) &&
            values.Select(value => (value.Kind, value.Index)).Distinct().Count() == values.Count);
        void Item(HoldingsItemObservation row)
        {
            Require(row.ItemId > 0 && row.Quantity >= 0 && Enum.IsDefined(row.Binding) && AccountEvidencePolicyFacts.ValidLocation(row) &&
                row.Location.SourceRowIndices.All(index => index >= 0) && row.Location.MaterialCategoryIds.All(id => id > 0));
            if (row.BoundActor is not null) Actor(row.BoundActor);
            Components(row.AttachedComponents);
        }
        void Equipment(EquipmentProtectionObservation row, string actorId)
        {
            Require(row.ActorId == actorId && row.RowIndex >= 0 && row.ItemId > 0 && Enum.IsDefined(row.Location) &&
                Enum.IsDefined(row.Binding) && row.TabIds.All(id => id > 0) && row.UnlockCount is null or >= 0);
            if (row.BoundActor is not null) Actor(row.BoundActor);
            Components(row.AttachedComponents);
        }
        void Source<T>(AccountSourceEvidence<T> value, AccountHoldingsSource expected, string? actorId = null)
        {
            Require(value.Source == expected && value.ActorId == actorId && Enum.IsDefined(value.Availability) && Enum.IsDefined(value.Completeness) &&
                (value.ErrorCategory is null || Enum.IsDefined(value.ErrorCategory.Value)) &&
                value.Coverage.ExpectedLocationCount is null or >= 0 && value.Coverage.ObservedLocationCount is null or >= 0);
            Require(value.Completeness != EvidenceCompleteness.Complete ||
                (value.Availability == EvidenceAvailability.Available && value.Value is not null && value.ErrorCategory is null));
            Fetch(value.Fetch);
        }
        void Inventory(AccountSourceEvidence<HoldingsInventoryObservation> source, AccountHoldingsSource expected, string? actorId = null)
        {
            Source(source, expected, actorId);
            if (source.Value is null) return;
            foreach (var row in source.Value.Items) { Item(row); Require(row.Location.Source == expected && row.Location.ActorId == actorId); }
            Require(source.Value.Items.Select(row => (row.Location.BagIndex, row.Location.SlotIndex, row.Location.SourceRowIndices.FirstOrDefault()))
                .Distinct().Count() == source.Value.Items.Count);
            Require(source.Value.InstalledBags.All(bag => bag.BagIndex >= 0 && bag.ItemId > 0 && bag.Size >= 0));
        }
        var capture = snapshot.Capture;
        Require(!string.IsNullOrWhiteSpace(capture.AccountScope.AccountId) && capture.RefreshId != Guid.Empty && capture.EvaluatedAtUtc.Offset == TimeSpan.Zero &&
            Guid.TryParse(snapshot.Generation, out _) && Guid.TryParse(snapshot.StoreIncarnation, out _));
        Fetch(capture.AccountIdentityFetch);
        Source(capture.Roster, AccountHoldingsSource.CharacterRoster);
        foreach (var actor in capture.Roster.Value ?? []) Actor(actor);
        Require((capture.Roster.Value ?? []).Select(actor => actor.ActorId).Distinct(StringComparer.Ordinal).Count() == (capture.Roster.Value?.Count ?? 0));
        Inventory(capture.Bank, AccountHoldingsSource.Bank); Inventory(capture.SharedInventory, AccountHoldingsSource.SharedInventory);
        Inventory(capture.MaterialStorage, AccountHoldingsSource.MaterialStorage);
        Source(capture.RecipeUnlocks, AccountHoldingsSource.RecipeUnlocks);
        Require((capture.RecipeUnlocks.Value ?? []).All(id => id > 0));
        Source(capture.Delivery, AccountHoldingsSource.TradingPostDelivery);
        if (capture.Delivery.Value is { } delivery)
        {
            Require(delivery.CoinsCopper >= 0 && delivery.Items.All(row => row.ItemId > 0 && row.Quantity > 0 && row.RowIndex >= 0));
            var totals = delivery.Items.GroupBy(row => row.ItemId).ToDictionary(group => group.Key, group => checked(group.Sum(row => row.Quantity)));
            Require(delivery.ObservedTotals.Count == totals.Count && delivery.ObservedTotals.All(row => row.Quantity == totals.GetValueOrDefault(row.ItemId)));
        }
        Require(capture.Characters.Select(actor => actor.Actor.ActorId).Distinct(StringComparer.Ordinal).Count() == capture.Characters.Count &&
            Enum.IsDefined(capture.CharacterCoverage.Completeness) && capture.CharacterCoverage.SuccessfulActorCount >= 0 &&
            capture.CharacterCoverage.ExpectedActorCount is null or >= 0);
        foreach (var actor in capture.Characters)
        {
            Actor(actor.Actor);
            var id = actor.Actor.ActorId;
            Inventory(actor.Inventory, AccountHoldingsSource.CharacterInventory, id);
            Source(actor.Crafting, AccountHoldingsSource.CharacterCrafting, id);
            Require((actor.Crafting.Value ?? []).All(value => !string.IsNullOrWhiteSpace(value.Discipline) && value.Rating >= 0));
            Source(actor.Equipment, AccountHoldingsSource.CharacterEquipment, id);
            Source(actor.EquipmentTabRoster, AccountHoldingsSource.CharacterEquipmentTabRoster, id);
            Source(actor.EquipmentTabs, AccountHoldingsSource.CharacterEquipmentTabs, id);
            Require((actor.EquipmentTabRoster.Value ?? []).All(tab => tab > 0));
            foreach (var row in actor.Equipment.Value ?? []) Equipment(row, id);
            foreach (var tab in actor.EquipmentTabs.Value ?? [])
            {
                Require(tab.TabId > 0);
                foreach (var row in tab.Equipment) Equipment(row, id);
            }
        }
        Require(snapshot.Rules.AccountScope == capture.AccountScope && snapshot.ProtectionFloor.AccountScope == capture.AccountScope &&
            snapshot.ProtectionFloor.ItemIds.All(id => id > 0) && snapshot.Categories.All(pair => pair.Key > 0 && Enum.IsDefined(pair.Value)));
        _ = new AccountHoldingsProtectionPolicy().Evaluate(capture, snapshot.Rules, snapshot.Categories, previousFloor: snapshot.ProtectionFloor);
        foreach (var row in snapshot.StaleObservations) Item(row);
    }
}
