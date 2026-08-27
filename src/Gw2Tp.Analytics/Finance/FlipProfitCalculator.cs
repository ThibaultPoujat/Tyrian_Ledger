using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.Finance;

/// <summary>
/// Calculates a completed flip scenario using a caller-supplied transaction-fee policy.
/// </summary>
public sealed class FlipProfitCalculator
{
    private readonly TransactionFeePolicy feePolicy;

    public FlipProfitCalculator(TransactionFeePolicy feePolicy)
    {
        this.feePolicy = feePolicy ?? throw new ArgumentNullException(nameof(feePolicy));
    }

    /// <summary>
    /// Models a completed sale: listing and exchange fees are calculated independently from
    /// the gross sale value, net proceeds subtract both fees, and net profit subtracts the
    /// acquisition cost. Unsold or cancelled listings are outside this scenario.
    /// </summary>
    public FlipProfitScenario Calculate(Money acquisitionCost, Money grossSaleValue)
    {
        if (acquisitionCost.Copper < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(acquisitionCost),
                "An acquisition cost cannot be negative.");
        }

        var fees = feePolicy.CalculateFees(grossSaleValue);
        var netSaleProceeds = grossSaleValue - fees.ListingFee - fees.ExchangeFee;
        var netProfit = netSaleProceeds - acquisitionCost;

        return new FlipProfitScenario(
            acquisitionCost,
            grossSaleValue,
            fees.ListingFee,
            fees.ExchangeFee,
            netSaleProceeds,
            netProfit);
    }
}
