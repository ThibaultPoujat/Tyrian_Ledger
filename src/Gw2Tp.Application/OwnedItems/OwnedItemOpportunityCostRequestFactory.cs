using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Analytics.OwnedItems;
using Gw2Tp.Application.AccountSnapshots;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.OwnedItems;

/// <summary>
/// Converts minimized account and typed market snapshots into the pure analytics
/// request. It reports binding data without retaining raw account payloads.
/// </summary>
public static class OwnedItemOpportunityCostRequestFactory
{
    public static OwnedItemOpportunityCostRequest Create(
        AccountOwnedItemsSnapshot ownedItemsSnapshot,
        MarketListing marketListing,
        int requiredQuantity,
        OwnedItemValuationRoute valuationRoute)
    {
        ArgumentNullException.ThrowIfNull(ownedItemsSnapshot);
        ArgumentNullException.ThrowIfNull(marketListing);

        var unrestrictedQuantity = 0;
        var boundQuantity = 0;
        foreach (var bankItem in ownedItemsSnapshot.BankItems.Where(item => item.ItemId == marketListing.ItemId))
        {
            if (string.IsNullOrWhiteSpace(bankItem.Binding))
            {
                unrestrictedQuantity = checked(unrestrictedQuantity + bankItem.Count);
            }
            else
            {
                boundQuantity = checked(boundQuantity + bankItem.Count);
            }
        }

        foreach (var material in ownedItemsSnapshot.Materials.Where(item => item.ItemId == marketListing.ItemId))
        {
            unrestrictedQuantity = checked(unrestrictedQuantity + material.Count);
        }

        var lots = new List<OwnedItemLot>();
        if (unrestrictedQuantity > 0)
        {
            lots.Add(new OwnedItemLot(unrestrictedQuantity));
        }

        if (boundQuantity > 0)
        {
            lots.Add(new OwnedItemLot(boundQuantity, OwnedItemRestriction.Bound));
        }

        return new OwnedItemOpportunityCostRequest(
            marketListing.ItemId,
            requiredQuantity,
            lots,
            new OwnedItemMarketEvidence(
                marketListing.Buys.Select(MapLevel).ToArray(),
                marketListing.Sells.Select(MapLevel).ToArray()),
            valuationRoute);
    }

    private static OrderBookLevel MapLevel(MarketOrderLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);
        return new OrderBookLevel(level.Quantity, new Money(level.UnitPriceInCopper));
    }
}
