using Gw2Tp.Analytics.Crafting;
using Gw2Tp.Application.AccountSnapshots;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
using Xunit;

namespace Gw2Tp.Application.Tests.Crafting;

public sealed class CraftingPathAnalysisRequestFactoryTests
{
    [Fact]
    public void Maps_minimized_snapshots_and_listings_without_losing_bound_stock()
    {
        var ownedItems = new AccountOwnedItemsSnapshot(
            "profile-alpha",
            [
                new AccountBankItem(1, 2, Binding: null),
                new AccountBankItem(1, 3, "Account"),
                new AccountBankItem(2, 7, Binding: null),
            ],
            [
                new AccountMaterial(1, 5, 4),
                new AccountMaterial(3, 5, 9),
            ]);
        var crafting = new AccountCraftingSnapshot(
            "profile-alpha",
            [100],
            [
                new AccountCharacterCrafting(
                    "Lira",
                    [
                        new AccountCraftingDiscipline("Artificer", 500, true),
                        new AccountCraftingDiscipline("Chef", 200, false),
                    ]),
            ]);

        var request = CraftingPathAnalysisRequestFactory.Create(
            targetItemId: 10,
            requestedQuantity: 1,
            recipes: [new CraftingRecipe(100, 10, 1, [new CraftingIngredient(1, 5)], CraftingRecipeAvailability.RequiresAccountUnlock, [])],
            ownedItems,
            crafting,
            marketListings:
            [
                Listing(1, buy: 90, sell: 100),
                Listing(10, buy: 1_000, sell: 1_100),
            ],
            new CraftingSearchLimits(3, 10));

        Assert.Equal(
            [
                new Gw2Tp.Analytics.OwnedItems.OwnedItemLot(6),
                new Gw2Tp.Analytics.OwnedItems.OwnedItemLot(3, Gw2Tp.Analytics.OwnedItems.OwnedItemRestriction.Bound),
            ],
            request.OwnedLotsByItem[1]);
        Assert.True(request.AccountCapabilities.HasVerifiedUnlockedRecipes);
        Assert.Equal([100], request.AccountCapabilities.UnlockedRecipeIds);
        Assert.True(request.AccountCapabilities.HasVerifiedDisciplines);
        Assert.Equal(
            [
                ("Artificer", 500, true),
                ("Chef", 200, false),
            ],
            request.AccountCapabilities.Disciplines.Select(discipline =>
                (discipline.Discipline, discipline.Rating, discipline.IsActive)));
        Assert.Equal(90, Assert.Single(request.MarketEvidenceByItem[1].BuyLevels).UnitPrice.Copper);
        Assert.Equal(1_100, Assert.Single(request.MarketEvidenceByItem[10].SellLevels).UnitPrice.Copper);
    }

    [Fact]
    public void Uses_explicit_unknown_capabilities_when_no_crafting_snapshot_is_supplied()
    {
        var request = CraftingPathAnalysisRequestFactory.Create(
            targetItemId: 10,
            requestedQuantity: 1,
            recipes: [],
            new AccountOwnedItemsSnapshot("profile-alpha", [], []),
            craftingSnapshot: null,
            marketListings: [Listing(10, buy: 100, sell: 110)],
            new CraftingSearchLimits(3, 10));

        Assert.False(request.AccountCapabilities.HasVerifiedUnlockedRecipes);
        Assert.False(request.AccountCapabilities.HasVerifiedDisciplines);
        Assert.Empty(request.AccountCapabilities.UnlockedRecipeIds);
        Assert.Empty(request.AccountCapabilities.Disciplines);
    }

    [Fact]
    public void Rejects_duplicate_market_listing_item_ids()
    {
        Assert.Throws<ArgumentException>(() => CraftingPathAnalysisRequestFactory.Create(
            targetItemId: 10,
            requestedQuantity: 1,
            recipes: [],
            new AccountOwnedItemsSnapshot("profile-alpha", [], []),
            craftingSnapshot: null,
            marketListings: [Listing(10, buy: 100, sell: 110), Listing(10, buy: 90, sell: 120)],
            new CraftingSearchLimits(3, 10)));
    }

    [Fact]
    public void Rejects_null_market_order_book_sides_and_levels_with_argument_errors()
    {
        var emptyLevels = Array.Empty<MarketOrderLevel>();

        Assert.Throws<ArgumentException>(() => CreateRequest([new MarketListing(10, null!, emptyLevels)]));
        Assert.Throws<ArgumentException>(() => CreateRequest([new MarketListing(10, emptyLevels, null!)]));
        Assert.Throws<ArgumentException>(() => CreateRequest(
            [new MarketListing(10, new MarketOrderLevel[] { null! }, emptyLevels)]));
    }

    private static CraftingPathAnalysisRequest CreateRequest(IReadOnlyList<MarketListing> marketListings) =>
        CraftingPathAnalysisRequestFactory.Create(
            targetItemId: 10,
            requestedQuantity: 1,
            recipes: [],
            new AccountOwnedItemsSnapshot("profile-alpha", [], []),
            craftingSnapshot: null,
            marketListings,
            new CraftingSearchLimits(3, 10));

    private static MarketListing Listing(int itemId, int buy, int sell) =>
        new(
            itemId,
            [new MarketOrderLevel(1, 20, buy)],
            [new MarketOrderLevel(1, 20, sell)]);
}
