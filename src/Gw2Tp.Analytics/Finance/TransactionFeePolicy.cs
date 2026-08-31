using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.Finance;

/// <summary>
/// Calculates separately configured listing and exchange fees. This policy intentionally
/// provides no default fee schedule; callers own the rates, rounding, and minimums they require.
/// </summary>
public sealed class TransactionFeePolicy
{
    public TransactionFeePolicy(FeeRule listingFeeRule, FeeRule exchangeFeeRule)
    {
        ListingFeeRule = listingFeeRule ?? throw new ArgumentNullException(nameof(listingFeeRule));
        ExchangeFeeRule = exchangeFeeRule ?? throw new ArgumentNullException(nameof(exchangeFeeRule));
    }

    public FeeRule ListingFeeRule { get; }

    public FeeRule ExchangeFeeRule { get; }

    /// <summary>
    /// Calculates each fee independently against the supplied non-negative gross sale value.
    /// A configured minimum applies only when the gross sale value is positive.
    /// </summary>
    public FeeBreakdown CalculateFees(Money grossSaleValue)
    {
        if (grossSaleValue.Copper < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(grossSaleValue),
                "A gross sale value cannot be negative.");
        }

        return new FeeBreakdown(
            CalculateFee(grossSaleValue, ListingFeeRule),
            CalculateFee(grossSaleValue, ExchangeFeeRule));
    }

    private static Money CalculateFee(Money grossSaleValue, FeeRule rule)
    {
        var wholeBasisPointBlocks = grossSaleValue.Copper / FeeRule.BasisPointsPerWhole;
        var remainderCopper = grossSaleValue.Copper % FeeRule.BasisPointsPerWhole;
        var wholeFee = checked(wholeBasisPointBlocks * rule.BasisPoints);
        var remainderProduct = remainderCopper * rule.BasisPoints;
        var remainderFee = remainderProduct / FeeRule.BasisPointsPerWhole;

        if (rule.Rounding is FeeRounding.Up && remainderProduct % FeeRule.BasisPointsPerWhole != 0)
        {
            remainderFee = checked(remainderFee + 1);
        }

        var calculatedFee = new Money(checked(wholeFee + remainderFee));
        return grossSaleValue.Copper > 0 && calculatedFee.Copper < rule.MinimumFee.Copper
            ? rule.MinimumFee
            : calculatedFee;
    }
}
