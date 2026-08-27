using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.OrderBooks;

/// <summary>
/// An available quantity at one order-book unit price.
/// </summary>
public sealed record OrderBookLevel
{
    public OrderBookLevel(int quantity, Money unitPrice)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "An order-book level quantity must be positive.");
        }

        if (unitPrice.Copper < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "An order-book level unit price cannot be negative.");
        }

        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public int Quantity { get; }

    public Money UnitPrice { get; }
}
