using System.Numerics;
using Gw2Tp.Analytics.Finance;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Finance;

/// <summary>
/// The canonical Guild Wars 2 Trading Post fee model used by application features.
/// The rates, minimums, and non-refundable listing fee have external documentation,
/// but fractional-copper rounding remains a provisional modeling assumption while
/// VERIFY-013 is open.
/// </summary>
public static class Gw2TradingPostFeePolicy
{
    public const int PolicyVersion = 1;
    public const int ListingFeeBasisPoints = 500;
    public const int ExchangeFeeBasisPoints = 1_000;
    public const long MinimumPositiveFeeCopper = 1;
    public const bool IsListingFeeRefundable = false;
    public const bool IsFractionalCopperRoundingExternallyVerified = false;

    /// <summary>
    /// Returns the capital economically committed to a completed-sale scenario:
    /// acquisition basis plus the non-refundable listing fee. This is the one
    /// ROI denominator policy shared by scanner, accounting, and recommendations.
    /// </summary>
    public static Money CalculateFullUpFrontCost(Money acquisitionCost, Money listingFee) => acquisitionCost + listingFee;

    public static ExactRoi CalculateExactRoi(Money profit, Money acquisitionCost, Money listingFee) =>
        new(profit, CalculateFullUpFrontCost(acquisitionCost, listingFee));

    public static ExactRoi? TryCalculateExactRoi(Money profit, Money acquisitionCost, Money listingFee)
    {
        var totalCost = CalculateFullUpFrontCost(acquisitionCost, listingFee);
        return totalCost.Copper > 0 ? new ExactRoi(profit, totalCost) : null;
    }

    /// <summary>
    /// Finds the lowest integer unit list price whose completed sale of the
    /// supplied quantity returns at least the requested net proceeds. This
    /// deliberately searches the small exact rounding interval instead of
    /// assuming net proceeds are monotonic at every copper: independent
    /// round-up can make an adjacent gross price return less net copper.
    /// </summary>
    public static Money? TryCalculateBreakEvenUnitPrice(Money requiredNetProceeds, int quantity)
    {
        if (requiredNetProceeds.Copper < 0) throw new ArgumentOutOfRangeException(nameof(requiredNetProceeds));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));

        var retainedBasisPoints = FeeRule.BasisPointsPerWhole - ListingFeeBasisPoints - ExchangeFeeBasisPoints;
        var denominator = new BigInteger(retainedBasisPoints) * quantity;
        var minimumCandidate = CeilingDivide(
            new BigInteger(requiredNetProceeds.Copper) * FeeRule.BasisPointsPerWhole,
            denominator);
        var sufficientCandidate = CeilingDivide(
            (new BigInteger(requiredNetProceeds.Copper) + FeeComponentCount) * FeeRule.BasisPointsPerWhole,
            denominator);
        var maximumUnitPrice = new BigInteger(long.MaxValue / quantity);
        if (minimumCandidate > maximumUnitPrice)
        {
            return null;
        }

        var feePolicy = Create();
        var maximumGrossSale = new Money((long)maximumUnitPrice * quantity);
        var maximumFees = feePolicy.CalculateFees(maximumGrossSale);
        if ((maximumGrossSale - maximumFees.ListingFee - maximumFees.ExchangeFee).Copper < requiredNetProceeds.Copper)
        {
            return null;
        }

        var finalCandidate = BigInteger.Min(sufficientCandidate, maximumUnitPrice);
        for (var candidate = (long)minimumCandidate; ; candidate++)
        {
            var grossSale = new Money(checked(candidate * (long)quantity));
            var fees = feePolicy.CalculateFees(grossSale);
            if ((grossSale - fees.ListingFee - fees.ExchangeFee).Copper >= requiredNetProceeds.Copper)
            {
                return new Money(candidate);
            }

            if (candidate == (long)finalCandidate)
            {
                throw new InvalidOperationException("The bounded break-even search did not find its guaranteed sufficient price.");
            }
        }
    }

    /// <summary>
    /// Creates the canonical modeled policy. Listing and exchange fees are calculated
    /// independently against the total gross sale value and each fractional fee is rounded up.
    /// </summary>
    public static TransactionFeePolicy Create() => new(
        new FeeRule(
            ListingFeeBasisPoints,
            FeeRounding.Up,
            new Money(MinimumPositiveFeeCopper)),
        new FeeRule(
            ExchangeFeeBasisPoints,
            FeeRounding.Up,
            new Money(MinimumPositiveFeeCopper)));

    private static BigInteger CeilingDivide(BigInteger numerator, BigInteger denominator) =>
        (numerator + denominator - BigInteger.One) / denominator;

    private const int FeeComponentCount = 2;
}
