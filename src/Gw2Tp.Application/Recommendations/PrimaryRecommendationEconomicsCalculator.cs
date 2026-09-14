using System.Globalization;
using System.Numerics;
using Gw2Tp.Analytics.Finance;
using Gw2Tp.Application.Finance;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Recommendations;

/// <summary>Applies canonical fees once to complete suggested-quantity totals.</summary>
public sealed class PrimaryRecommendationEconomicsCalculator
{
    private readonly FlipProfitCalculator calculator = new(Gw2TradingPostFeePolicy.Create());

    public PrimaryRecommendationEconomics CalculateUnitPrices(Money unitAcquisitionPrice, Money unitSalePrice, int quantity)
    {
        if (unitAcquisitionPrice.Copper < 0 || unitSalePrice.Copper < 0 || quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }
        return CalculateTotals(
            new Money(checked(unitAcquisitionPrice.Copper * quantity)),
            new Money(checked(unitSalePrice.Copper * quantity)));
    }

    public PrimaryRecommendationEconomics CalculateTotals(Money acquisitionCost, Money grossSaleValue)
    {
        var scenario = calculator.Calculate(acquisitionCost, grossSaleValue);
        var totalCost = Gw2TradingPostFeePolicy.CalculateFullUpFrontCost(acquisitionCost, scenario.ListingFee);
        return new PrimaryRecommendationEconomics(
            acquisitionCost, grossSaleValue, scenario.ListingFee, scenario.ExchangeFee,
            scenario.NetSaleProceeds, scenario.NetProfit, totalCost, FormatRoi(scenario.NetProfit, totalCost));
    }

    private static string FormatRoi(Money profit, Money totalCost)
    {
        if (totalCost.Copper <= 0) return "Unavailable";
        var scaled = new BigInteger(profit.Copper) * 10_000;
        var denominator = new BigInteger(totalCost.Copper);
        var quotient = BigInteger.DivRem(BigInteger.Abs(scaled), denominator, out var remainder);
        if (remainder * 2 >= denominator) quotient++;
        var sign = scaled.Sign < 0 ? "-" : string.Empty;
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{quotient / 100}.{quotient % 100:D2}%");
    }
}
