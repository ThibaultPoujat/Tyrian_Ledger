using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.OrderBooks;

/// <summary>
/// An exact weighted average unit price, represented as total copper divided by quantity.
/// No whole-copper rounding or floating-point conversion is applied.
/// </summary>
public readonly record struct WeightedAverageExecutionPrice
{
    public WeightedAverageExecutionPrice(Money totalValue, int quantity)
    {
        if (totalValue.Copper < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalValue), "An execution total cannot be negative.");
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "An executed quantity must be positive.");
        }

        TotalValue = totalValue;
        Quantity = quantity;
    }

    public Money TotalValue { get; }

    public int Quantity { get; }
}
