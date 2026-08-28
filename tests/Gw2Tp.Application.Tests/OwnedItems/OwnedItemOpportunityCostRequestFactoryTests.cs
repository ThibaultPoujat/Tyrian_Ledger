using Gw2Tp.Analytics.OwnedItems;
using Gw2Tp.Application.AccountSnapshots;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.OwnedItems;
using Xunit;

namespace Gw2Tp.Application.Tests.OwnedItems;

public sealed class OwnedItemOpportunityCostRequestFactoryTests
{
    [Fact]
    public void Aggregates_materials_and_unbound_bank_stock_and_flags_bound_bank_stock()
    {
        var snapshot = new AccountOwnedItemsSnapshot(
            "profile-alpha",
            [
                new AccountBankItem(900_001, 2, Binding: null),
                new AccountBankItem(900_001, 3, "Account"),
                new AccountBankItem(900_002, 9, Binding: null),
            ],
            [
                new AccountMaterial(900_001, 5, 4),
                new AccountMaterial(900_002, 5, 7),
            ]);
        var listing = new MarketListing(
            900_001,
            [new MarketOrderLevel(1, 10, 90)],
            [new MarketOrderLevel(1, 10, 100)]);

        var request = OwnedItemOpportunityCostRequestFactory.Create(
            snapshot,
            listing,
            requiredQuantity: 5,
            OwnedItemValuationRoute.ImmediateLiquidation);

        Assert.Equal(900_001, request.ItemId);
        Assert.Equal(5, request.RequiredQuantity);
        Assert.Equal(
            [
                new OwnedItemLot(6),
                new OwnedItemLot(3, OwnedItemRestriction.Bound),
            ],
            request.OwnedLots);
        Assert.Equal(90, Assert.Single(request.MarketEvidence.BuyLevels).UnitPrice.Copper);
        Assert.Equal(100, Assert.Single(request.MarketEvidence.SellLevels).UnitPrice.Copper);
    }
}
