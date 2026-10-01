using Gw2Tp.Application.AccountEvidence;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.PersonalTradingPost;

namespace Gw2Tp.Testing;

/// <summary>Synthetic independent boundary vectors; no owner/account credentials or real actor names.</summary>
public static class HoldingsEvidenceFixture
{
    public static readonly AccountScope Scope = new("synthetic-holdings");
    public static readonly AccountActor A = new("opaque-a", "Personnage A de test");
    public static readonly AccountActor B = new("opaque-b", "Personnage B de test");
    public const string Generation = "11111111111111111111111111111111";
    public const string Incarnation = "22222222222222222222222222222222";

    public static AccountSourceEvidence<T> Source<T>(AccountHoldingsSource source, T value, DateTimeOffset at, string? actorId = null) =>
        new(source, actorId, EvidenceAvailability.Available, EvidenceCompleteness.Complete, new(0, 0), new(at, at, null), value, null);

    public static HoldingsItemObservation Item(int id, int count, AccountHoldingsSource source = AccountHoldingsSource.Bank,
        string? actorId = null, int slot = 0, AccountItemBinding binding = AccountItemBinding.Unspecified, AccountActor? bound = null) =>
        new(new(source, actorId, source == AccountHoldingsSource.CharacterInventory ? 0 : null,
            source == AccountHoldingsSource.MaterialStorage ? null : slot,
            source == AccountHoldingsSource.MaterialStorage ? [1] : [], [slot]), id, count, binding, bound, []);

    public static ActorHoldingsEvidence Actor(AccountActor actor, DateTimeOffset at, IReadOnlyList<HoldingsItemObservation>? items = null,
        IReadOnlyList<CraftingDisciplineCapability>? capabilities = null) => new(actor,
            Source(AccountHoldingsSource.CharacterInventory, new HoldingsInventoryObservation(items ?? [], []), at, actor.ActorId),
            Source(AccountHoldingsSource.CharacterCrafting, capabilities ?? (IReadOnlyList<CraftingDisciplineCapability>)[new("Artificer", 500, true)], at, actor.ActorId),
            Source(AccountHoldingsSource.CharacterEquipment, (IReadOnlyList<EquipmentProtectionObservation>)[], at, actor.ActorId),
            Source(AccountHoldingsSource.CharacterEquipmentTabRoster, (IReadOnlyList<int>)[1], at, actor.ActorId),
            Source(AccountHoldingsSource.CharacterEquipmentTabs, (IReadOnlyList<EquipmentTabObservation>)[new(1, true, [])], at, actor.ActorId));

    public static AccountHoldingsSnapshot Snapshot(DateTimeOffset at, IReadOnlyList<HoldingsItemObservation>? rows = null,
        IReadOnlyList<ActorHoldingsEvidence>? actors = null, IReadOnlyDictionary<int, HoldingsItemCategory>? categories = null,
        IReadOnlyDictionary<int, int>? keep = null, IReadOnlySet<int>? floor = null, int deliveryQuantity = 0)
    {
        rows ??= [];
        actors ??= [Actor(A, at, rows.Where(row => row.Location.ActorId == A.ActorId).ToArray())];
        HoldingsInventoryObservation Inventory(AccountHoldingsSource source) => new(rows.Where(row => row.Location.Source == source).ToArray(), []);
        var capture = new AccountHoldingsCapture(Scope, Guid.NewGuid(), at, new(at, at, null),
            Source(AccountHoldingsSource.CharacterRoster, (IReadOnlyList<AccountActor>)actors.Select(actor => actor.Actor).ToArray(), at),
            Source(AccountHoldingsSource.Bank, Inventory(AccountHoldingsSource.Bank), at),
            Source(AccountHoldingsSource.SharedInventory, Inventory(AccountHoldingsSource.SharedInventory), at),
            Source(AccountHoldingsSource.MaterialStorage, Inventory(AccountHoldingsSource.MaterialStorage), at),
            Source(AccountHoldingsSource.TradingPostDelivery, new HoldingsDeliveryObservation(250,
                deliveryQuantity > 0 ? [new(0, 10, deliveryQuantity)] : [], deliveryQuantity > 0 ? [new(10, deliveryQuantity)] : []), at),
            actors, new(EvidenceCompleteness.Complete, actors.Count, actors.Count),
            Source(AccountHoldingsSource.RecipeUnlocks, (IReadOnlyList<int>)[1, 2], at));
        return new(capture, Incarnation, Generation, categories ?? rows.Select(row => row.ItemId).Distinct().ToDictionary(id => id, _ => HoldingsItemCategory.Commodity),
            new(Scope, new HashSet<int>(), keep ?? new Dictionary<int, int>()), new(Scope, floor ?? new HashSet<int>()), []);
    }

    public static CraftingRecipe Recipe(int id = 1, string discipline = "Artificer", int input = 10, int output = 100) =>
        new(id, output, 1, [discipline], 400, [], [new("Item", input, 1)]);
}
