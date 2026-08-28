using Gw2Tp.Analytics.Crafting;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Analytics.OwnedItems;
using Gw2Tp.Application.AccountSnapshots;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Crafting;

/// <summary>
/// Maps minimized account snapshots and typed market listings into the pure
/// crafting analyzer input without retaining raw account payloads.
/// </summary>
public static class CraftingPathAnalysisRequestFactory
{
    public static CraftingPathAnalysisRequest Create(
        int targetItemId,
        int requestedQuantity,
        IReadOnlyList<CraftingRecipe> recipes,
        AccountOwnedItemsSnapshot ownedItemsSnapshot,
        AccountCraftingSnapshot? craftingSnapshot,
        IReadOnlyList<MarketListing> marketListings,
        CraftingSearchLimits searchLimits)
    {
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(ownedItemsSnapshot);
        ArgumentNullException.ThrowIfNull(marketListings);
        ArgumentNullException.ThrowIfNull(searchLimits);

        return new CraftingPathAnalysisRequest(
            targetItemId,
            requestedQuantity,
            recipes,
            MapMarketEvidence(marketListings),
            MapOwnedLots(ownedItemsSnapshot),
            MapCapabilities(craftingSnapshot),
            searchLimits);
    }

    private static IReadOnlyDictionary<int, OwnedItemMarketEvidence> MapMarketEvidence(
        IReadOnlyList<MarketListing> marketListings)
    {
        if (marketListings.Any(listing => listing is null))
        {
            throw new ArgumentException("Market listings cannot contain null values.", nameof(marketListings));
        }

        var evidence = new Dictionary<int, OwnedItemMarketEvidence>();
        foreach (var listing in marketListings)
        {
            if (!evidence.TryAdd(
                    listing.ItemId,
                    new OwnedItemMarketEvidence(
                        listing.Buys.Select(MapLevel).ToArray(),
                        listing.Sells.Select(MapLevel).ToArray())))
            {
                throw new ArgumentException("Market listings must have unique item IDs.", nameof(marketListings));
            }
        }

        return evidence;
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<OwnedItemLot>> MapOwnedLots(
        AccountOwnedItemsSnapshot ownedItemsSnapshot)
    {
        var quantities = new Dictionary<int, (int Eligible, int Bound)>();
        foreach (var bankItem in ownedItemsSnapshot.BankItems)
        {
            if (bankItem is null)
            {
                throw new ArgumentException("Bank items cannot contain null values.", nameof(ownedItemsSnapshot));
            }

            AddQuantity(
                quantities,
                bankItem.ItemId,
                string.IsNullOrWhiteSpace(bankItem.Binding),
                bankItem.Count);
        }

        foreach (var material in ownedItemsSnapshot.Materials)
        {
            if (material is null)
            {
                throw new ArgumentException("Materials cannot contain null values.", nameof(ownedItemsSnapshot));
            }

            AddQuantity(quantities, material.ItemId, isEligible: true, material.Count);
        }

        return quantities.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<OwnedItemLot>)CreateLots(entry.Value.Eligible, entry.Value.Bound));
    }

    private static CraftingAccountCapabilities MapCapabilities(AccountCraftingSnapshot? craftingSnapshot)
    {
        if (craftingSnapshot is null)
        {
            return new CraftingAccountCapabilities(
                hasVerifiedUnlockedRecipes: false,
                unlockedRecipeIds: [],
                hasVerifiedDisciplines: false,
                disciplines: []);
        }

        var disciplines = craftingSnapshot.Characters
            .SelectMany(character => character.Disciplines)
            .Select(discipline => new CraftingDisciplineCapability(
                discipline.Discipline,
                discipline.Rating,
                discipline.IsActive))
            .ToArray();
        return new CraftingAccountCapabilities(
            hasVerifiedUnlockedRecipes: true,
            craftingSnapshot.UnlockedRecipeIds,
            hasVerifiedDisciplines: true,
            disciplines);
    }

    private static OrderBookLevel MapLevel(MarketOrderLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);
        return new OrderBookLevel(level.Quantity, new Money(level.UnitPriceInCopper));
    }

    private static void AddQuantity(
        IDictionary<int, (int Eligible, int Bound)> quantities,
        int itemId,
        bool isEligible,
        int quantity)
    {
        if (itemId <= 0 || quantity < 0)
        {
            throw new ArgumentException("Account item IDs must be positive and quantities cannot be negative.");
        }

        if (quantity == 0)
        {
            return;
        }

        quantities.TryGetValue(itemId, out var current);
        quantities[itemId] = isEligible
            ? (checked(current.Eligible + quantity), current.Bound)
            : (current.Eligible, checked(current.Bound + quantity));
    }

    private static IReadOnlyList<OwnedItemLot> CreateLots(int eligibleQuantity, int boundQuantity)
    {
        var lots = new List<OwnedItemLot>();
        if (eligibleQuantity > 0)
        {
            lots.Add(new OwnedItemLot(eligibleQuantity));
        }

        if (boundQuantity > 0)
        {
            lots.Add(new OwnedItemLot(boundQuantity, OwnedItemRestriction.Bound));
        }

        return Array.AsReadOnly(lots.ToArray());
    }
}
