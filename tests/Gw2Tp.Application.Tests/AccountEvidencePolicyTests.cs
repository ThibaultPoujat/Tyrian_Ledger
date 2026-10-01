using System.Text.Json;
using Gw2Tp.Application.AccountEvidence;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.PersonalTradingPost;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class AccountEvidencePolicyTests
{
    private static readonly AccountScope Scope = new("synthetic-policy-account");
    private static readonly AccountActor A = new("opaque-a", "Synthetic A");
    private static readonly AccountActor B = new("opaque-b", "Synthetic B");
    private readonly AccountHoldingsProtectionPolicy protections = new();
    private readonly CraftingActorSelector selector = new();

    [Fact]
    public void Inactive_500_and_active_100_do_not_make_an_active_500_actor()
    {
        var capture = Capture(Actor(A, [new("Artificer", 500, false)]), Actor(B, [new("Artificer", 100, true)]));
        var result = selector.Select(capture, [Recipe(1, "Artificer", 400)]);
        Assert.Equal(CraftingActorSelectionFailure.CapabilityUnavailable, result.Failure);
        Assert.Empty(result.Assignments);
    }

    [Fact]
    public void All_real_actors_are_retained_and_stable_ties_use_opaque_id()
    {
        var capture = Capture(Actor(B), Actor(A));
        var result = selector.Select(capture, [Recipe(1)]);
        Assert.Equal(CraftingActorSelectionFailure.None, result.Failure);
        Assert.Equal(A, Assert.Single(result.Assignments).Actor);
        Assert.Equal(2, capture.Characters.Count);
        Assert.Contains(result.Prerequisites, value => value.Kind == CraftingActorAccessKind.AdmissionRequired);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(selector.Select(capture, [Recipe(1)])));
    }

    [Fact]
    public void Two_actor_chain_is_explicitly_unsupported_but_one_actual_actor_can_cover_a_chain()
    {
        var capture = Capture(Actor(A), Actor(B, [new("Weaponsmith", 500, true)]));
        var chain = new[] { Recipe(1), Recipe(2, "Weaponsmith") };
        Assert.Equal(CraftingActorSelectionFailure.UnsupportedChain, selector.Select(capture, chain).Failure);
        capture = Capture(Actor(A, [new("Artificer", 500, true), new("Weaponsmith", 500, true)]), Actor(B));
        var selected = selector.Select(capture, chain);
        Assert.Equal([1, 2], selected.Assignments.Select(assignment => assignment.RecipeId));
        Assert.All(selected.Assignments, assignment => Assert.Equal(A, assignment.Actor));
    }

    [Fact]
    public void Recipe_and_actor_coverage_are_independent_and_unknown_is_never_craftable()
    {
        var capture = Capture(Actor(A));
        Assert.Equal(CraftingActorSelectionFailure.RecipeEvidenceUnavailable, selector.Select(capture with
            { RecipeUnlocks = capture.RecipeUnlocks with { Completeness = EvidenceCompleteness.Partial } }, [Recipe(1)]).Failure);
        Assert.Equal(CraftingActorSelectionFailure.RecipeLocked, selector.Select(capture, [Recipe(3)]).Failure);
        Assert.Equal(CraftingActorSelectionFailure.ActorEvidenceUnavailable, selector.Select(capture with
            { Roster = capture.Roster with { Value = null } }, [Recipe(1)]).Failure);
        var partialActor = Actor(A) with { Crafting = Actor(A).Crafting with { Completeness = EvidenceCompleteness.Partial } };
        Assert.Equal(CraftingActorSelectionFailure.CapabilityUnavailable, selector.Select(Capture(partialActor), [Recipe(1)]).Failure);
    }

    [Fact]
    public void Inventory_access_and_transfer_requirements_are_retained_without_admission()
    {
        var bank = Item(10, 1);
        var material = Item(11, 1, AccountHoldingsSource.MaterialStorage);
        var shared = Item(12, 1, AccountHoldingsSource.SharedInventory);
        var other = Item(13, 1, AccountHoldingsSource.CharacterInventory, B.ActorId);
        var capture = WithItems(Capture(Actor(A), Actor(B)), bank, material, shared, other);
        var result = selector.Select(capture, [Recipe(1)], [bank, material, shared, other]);
        Assert.Equal(CraftingActorSelectionFailure.None, result.Failure);
        Assert.Equal(A, Assert.Single(result.Assignments).Actor);
        Assert.Contains(result.Prerequisites, value => value.Kind == CraftingActorAccessKind.Bank && value.ItemId == 10);
        Assert.Contains(result.Prerequisites, value => value.Kind == CraftingActorAccessKind.MaterialStorage && value.ItemId == 11);
        Assert.Contains(result.Prerequisites, value => value.Kind == CraftingActorAccessKind.SharedInventory && value.ItemId == 12);
        Assert.Contains(result.Prerequisites, value => value.Kind == CraftingActorAccessKind.TransferFromActor && value.FromActorId == B.ActorId);
    }

    [Fact]
    public void Bound_to_other_actor_is_rejected_and_bound_inputs_can_select_a_later_real_actor()
    {
        var item = Item(10, 1) with { Binding = AccountItemBinding.CharacterBound, BoundActor = B };
        var capture = WithItems(Capture(Actor(A), Actor(B, [])), item);
        Assert.Equal(CraftingActorSelectionFailure.BoundToOtherActor, selector.Select(capture, [Recipe(1)], [item]).Failure);
        capture = WithItems(Capture(Actor(A), Actor(B)), item);
        Assert.Equal(B, Assert.Single(selector.Select(capture, [Recipe(1)], [item]).Assignments).Actor);
    }

    [Fact]
    public void Missing_or_malformed_input_facts_cannot_become_access_prerequisites()
    {
        var item = Item(10, 1);
        var capture = Capture(Actor(A));
        Assert.Equal(CraftingActorSelectionFailure.InputEvidenceUnavailable, selector.Select(capture, [Recipe(1)], [item]).Failure);
        item = item with { Binding = AccountItemBinding.OtherBound };
        capture = WithItems(capture, item);
        Assert.Equal(CraftingActorSelectionFailure.InputEvidenceUnavailable, selector.Select(capture, [Recipe(1)], [item]).Failure);
        capture = capture with { Bank = capture.Bank with { Completeness = EvidenceCompleteness.Partial } };
        Assert.Equal(CraftingActorSelectionFailure.InputEvidenceUnavailable, selector.Select(capture, [Recipe(1)], [item]).Failure);
    }

    [Theory]
    [InlineData(EquipmentObservedLocation.Equipped)]
    [InlineData(EquipmentObservedLocation.Armory)]
    [InlineData(EquipmentObservedLocation.EquippedFromLegendaryArmory)]
    [InlineData(EquipmentObservedLocation.LegendaryArmory)]
    [InlineData(EquipmentObservedLocation.Unknown)]
    public void Every_equipment_location_and_attached_component_protects_all_matching_bag_copies(EquipmentObservedLocation location)
    {
        var reference = Equipment(A, 10, location) with { AttachedComponents = [new(HoldingsComponentKind.Infusion, 0, 11)] };
        var actor = Actor(A) with { Equipment = Evidence(AccountHoldingsSource.CharacterEquipment, (IReadOnlyList<EquipmentProtectionObservation>)[reference], A.ActorId) };
        var capture = WithItems(Capture(actor), Item(10, 2), Item(10, 1, AccountHoldingsSource.CharacterInventory, A.ActorId), Item(11, 1));
        var result = protections.Evaluate(capture, Rules(), Categories((10, HoldingsItemCategory.Equipment), (11, HoldingsItemCategory.Commodity)));
        Assert.Equal(3, result.Rows.Count); // references contribute no physical inventory rows/units
        Assert.All(result.Rows, row =>
        {
            Assert.Equal(0, row.ConsumableAllowance);
            Assert.Equal(0, row.SaleableAllowance);
            Assert.Contains(HoldingsProtectionReason.EquipmentReferenceAmbiguous, row.Reasons);
        });
    }

    [Fact]
    public void Inactive_templates_protect_without_an_active_equipment_reference()
    {
        var actor = Actor(A) with { EquipmentTabRoster = Evidence(AccountHoldingsSource.CharacterEquipmentTabRoster, (IReadOnlyList<int>)[1, 2], A.ActorId),
            EquipmentTabs = Evidence(AccountHoldingsSource.CharacterEquipmentTabs, (IReadOnlyList<EquipmentTabObservation>)
                [new(1, true, []), new(2, false, [Equipment(A, 10, EquipmentObservedLocation.Armory)])], A.ActorId) };
        var result = protections.Evaluate(WithItems(Capture(actor), Item(10, 2)), Rules(), Categories((10, HoldingsItemCategory.Equipment)));
        Assert.True(result.HasCompleteEquipmentCoverage);
        Assert.Equal(0, Assert.Single(result.Rows).ConsumableAllowance);
    }

    [Fact]
    public void One_missing_actors_tabs_blocks_equipment_but_a_known_commodity_remains_independently_eligible()
    {
        var b = Actor(B) with { EquipmentTabs = Actor(B).EquipmentTabs with { Availability = EvidenceAvailability.MissingPermission, Value = null } };
        var capture = WithItems(Capture(Actor(A), b), Item(10, 1), Item(11, 5));
        var result = protections.Evaluate(capture, Rules(), Categories((10, HoldingsItemCategory.Equipment), (11, HoldingsItemCategory.Commodity)));
        Assert.False(result.HasCompleteEquipmentCoverage);
        Assert.Equal(0, result.Rows[0].ConsumableAllowance);
        Assert.Contains(HoldingsProtectionReason.EquipmentCoverageIncomplete, result.Rows[0].Reasons);
        Assert.Equal(5, result.Rows[1].ConsumableAllowance);
    }

    [Fact]
    public void Unknown_category_binding_and_actor_mismatch_fail_closed()
    {
        var rows = new[] { Item(10, 1), Item(11, 1) with { Binding = AccountItemBinding.OtherBound },
            Item(12, 1) with { Binding = AccountItemBinding.CharacterBound, BoundActor = B } };
        var capture = WithItems(Capture(Actor(A)), rows);
        var result = protections.Evaluate(capture, Rules(), Categories((11, HoldingsItemCategory.Commodity), (12, HoldingsItemCategory.Commodity)), A);
        Assert.All(result.Rows, row => Assert.Equal(0, row.ConsumableAllowance));
        Assert.Contains(HoldingsProtectionReason.UnknownCategory, result.Rows[0].Reasons);
        Assert.Contains(HoldingsProtectionReason.UnknownBinding, result.Rows[1].Reasons);
        Assert.Contains(HoldingsProtectionReason.ActorMismatch, result.Rows[2].Reasons);
    }

    [Fact]
    public void Known_binding_is_retained_and_does_not_make_a_bound_copy_saleable()
    {
        var item = Item(10, 2) with { Binding = AccountItemBinding.CharacterBound, BoundActor = A };
        var capture = WithItems(Capture(Actor(A)), item);
        var result = protections.Evaluate(capture, Rules(), Categories((10, HoldingsItemCategory.Commodity)), A);
        Assert.Equal(2, Assert.Single(result.Rows).ConsumableAllowance);
        Assert.Equal(0, Assert.Single(result.Rows).SaleableAllowance);
        Assert.Equal(0, Assert.Single(protections.Evaluate(capture, Rules(), Categories((10, HoldingsItemCategory.Commodity))).Rows).ConsumableAllowance);
    }

    [Fact]
    public void Account_reserve_100_is_applied_once_to_150_units_across_locations()
    {
        var capture = WithItems(Capture(Actor(A)), Item(10, 70), Item(10, 80, AccountHoldingsSource.MaterialStorage));
        var result = protections.Evaluate(capture, Rules(reserve: 100), Categories((10, HoldingsItemCategory.Commodity)));
        Assert.Equal([0, 50], result.Rows.Select(row => row.ConsumableAllowance));
        Assert.Equal(50, result.Rows.Sum(row => row.SaleableAllowance));
        Assert.All(result.Rows, row => Assert.Contains(HoldingsProtectionReason.KeepQuantity, row.Reasons));
        var fullyProtected = protections.Evaluate(capture, Rules(protect: true), Categories((10, HoldingsItemCategory.Commodity)));
        Assert.All(fullyProtected.Rows, row => Assert.Equal(0, row.ConsumableAllowance));
        Assert.All(fullyProtected.Rows, row => Assert.Contains(HoldingsProtectionReason.UserProtected, row.Reasons));
    }

    [Fact]
    public void Partial_later_reads_retain_the_account_floor_but_not_stale_actor_capabilities()
    {
        var firstActor = Actor(A) with { Equipment = Evidence(AccountHoldingsSource.CharacterEquipment,
            (IReadOnlyList<EquipmentProtectionObservation>)[Equipment(A, 10) with { AttachedComponents = [new(HoldingsComponentKind.Upgrade, 0, 11)] }], A.ActorId) };
        var first = protections.Evaluate(Capture(firstActor), Rules(), new Dictionary<int, HoldingsItemCategory>());
        var renamed = new AccountActor("opaque-renamed", "Renamed");
        var laterActor = Actor(renamed, []) with { EquipmentTabs = Actor(renamed).EquipmentTabs with { Completeness = EvidenceCompleteness.Partial, Value = null } };
        var later = WithItems(Capture(laterActor), Item(10, 1), Item(11, 1));
        var result = protections.Evaluate(later, Rules(), Categories((10, HoldingsItemCategory.Equipment), (11, HoldingsItemCategory.Commodity)), previousFloor: first.Floor);
        Assert.All(result.Rows, row => Assert.Equal(0, row.ConsumableAllowance));
        Assert.Contains(11, result.Floor.ItemIds);
        Assert.Equal(CraftingActorSelectionFailure.CapabilityUnavailable, selector.Select(later, [Recipe(1)]).Failure);
        Assert.Equal(CraftingActorSelectionFailure.CapabilityUnavailable, selector.Select(Capture(), [Recipe(1)]).Failure);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(protections.Evaluate(later, Rules(),
            Categories((10, HoldingsItemCategory.Equipment), (11, HoldingsItemCategory.Commodity)), previousFloor: first.Floor)));
    }

    [Fact]
    public void Account_mismatch_invalid_reserve_and_unknown_source_cannot_clear_protections()
    {
        var capture = WithItems(Capture(Actor(A)), Item(10, 2));
        Assert.Throws<ArgumentException>(() => protections.Evaluate(capture, Rules() with { AccountScope = new("other") }, Categories()));
        Assert.Throws<ArgumentException>(() => protections.Evaluate(capture, Rules(reserve: -1), Categories()));
        Assert.Throws<ArgumentException>(() => protections.Evaluate(capture, Rules(), Categories(), previousFloor: new(new("other"), new HashSet<int>())));
        capture = capture with { Bank = capture.Bank with { Completeness = EvidenceCompleteness.Partial } };
        Assert.Equal(0, Assert.Single(protections.Evaluate(capture, Rules(), Categories((10, HoldingsItemCategory.Commodity))).Rows).ConsumableAllowance);
    }

    private static AccountHoldingsProtectionRules Rules(bool protect = false, int reserve = 0) => new(Scope,
        protect ? new HashSet<int> { 10 } : new HashSet<int>(), new Dictionary<int, int> { [10] = reserve });
    private static Dictionary<int, HoldingsItemCategory> Categories(params (int Id, HoldingsItemCategory Category)[] values) => values.ToDictionary(value => value.Id, value => value.Category);
    private static CraftingRecipe Recipe(int id, string discipline = "Artificer", int rating = 400) => new(id, 100, 1, [discipline], rating, [], [new("Item", 10, 1)]);
    private static EquipmentProtectionObservation Equipment(AccountActor actor, int id, EquipmentObservedLocation location = EquipmentObservedLocation.Equipped) =>
        new(actor.ActorId, 0, id, "Coat", location, [1], null, AccountItemBinding.AccountBound, null, []);
    private static HoldingsItemObservation Item(int id, int count, AccountHoldingsSource source = AccountHoldingsSource.Bank, string? actor = null) =>
        new(new(source, actor, source == AccountHoldingsSource.CharacterInventory ? 0 : null,
            source == AccountHoldingsSource.MaterialStorage ? null : id, [], [id]), id, count, AccountItemBinding.Unspecified, null, []);
    private static readonly EvidenceFetchProvenance Fetch = new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
    private static AccountSourceEvidence<T> Evidence<T>(AccountHoldingsSource source, T value, string? actor = null) =>
        new(source, actor, EvidenceAvailability.Available, EvidenceCompleteness.Complete, new(0, 0), Fetch, value, null);
    private static ActorHoldingsEvidence Actor(AccountActor actor, IReadOnlyList<CraftingDisciplineCapability>? capabilities = null) => new(actor,
        Evidence(AccountHoldingsSource.CharacterInventory, new HoldingsInventoryObservation([], []), actor.ActorId),
        Evidence(AccountHoldingsSource.CharacterCrafting, capabilities ?? [new("Artificer", 500, true)], actor.ActorId),
        Evidence(AccountHoldingsSource.CharacterEquipment, (IReadOnlyList<EquipmentProtectionObservation>)[], actor.ActorId),
        Evidence(AccountHoldingsSource.CharacterEquipmentTabRoster, (IReadOnlyList<int>)[1], actor.ActorId),
        Evidence(AccountHoldingsSource.CharacterEquipmentTabs, (IReadOnlyList<EquipmentTabObservation>)[new(1, true, [])], actor.ActorId));
    private static AccountHoldingsCapture Capture(params ActorHoldingsEvidence[] actors) => new(Scope, Guid.Empty, DateTimeOffset.UnixEpoch, Fetch,
        Evidence(AccountHoldingsSource.CharacterRoster, (IReadOnlyList<AccountActor>)actors.Select(actor => actor.Actor).ToArray()),
        Evidence(AccountHoldingsSource.Bank, new HoldingsInventoryObservation([], [])),
        Evidence(AccountHoldingsSource.SharedInventory, new HoldingsInventoryObservation([], [])),
        Evidence(AccountHoldingsSource.MaterialStorage, new HoldingsInventoryObservation([], [])),
        Evidence(AccountHoldingsSource.TradingPostDelivery, new HoldingsDeliveryObservation(0, [], [])), actors,
        new(EvidenceCompleteness.Complete, actors.Length, actors.Length), Evidence(AccountHoldingsSource.RecipeUnlocks, (IReadOnlyList<int>)[1, 2]));
    private static AccountHoldingsCapture WithItems(AccountHoldingsCapture capture, params HoldingsItemObservation[] items)
    {
        HoldingsInventoryObservation Inventory(AccountHoldingsSource source, string? actor = null) => new(items.Where(item => item.Location.Source == source && item.Location.ActorId == actor).ToArray(), []);
        return capture with
        {
            Bank = capture.Bank with { Value = Inventory(AccountHoldingsSource.Bank) },
            SharedInventory = capture.SharedInventory with { Value = Inventory(AccountHoldingsSource.SharedInventory) },
            MaterialStorage = capture.MaterialStorage with { Value = Inventory(AccountHoldingsSource.MaterialStorage) },
            Characters = capture.Characters.Select(actor => actor with { Inventory = actor.Inventory with { Value = Inventory(AccountHoldingsSource.CharacterInventory, actor.Actor.ActorId) } }).ToArray(),
        };
    }
}
