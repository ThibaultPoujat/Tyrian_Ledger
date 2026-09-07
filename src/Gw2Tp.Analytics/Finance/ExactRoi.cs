using System.Numerics;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.Finance;

/// <summary>
/// Exact return on investment represented as a signed profit numerator and a
/// positive cost denominator. This type intentionally exposes no floating-point
/// approximation so financial callers retain exact integer-copper semantics.
/// </summary>
public readonly record struct ExactRoi
{
    public ExactRoi(Money profit, Money totalCost)
    {
        if (totalCost.Copper <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCost), "ROI requires a positive total cost.");
        }

        Profit = profit;
        TotalCost = totalCost;
    }

    public Money Profit { get; }

    public Money TotalCost { get; }

    public bool MeetsOrExceedsBasisPoints(int basisPoints)
    {
        if (basisPoints < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(basisPoints));
        }

        return new BigInteger(Profit.Copper) * BasisPointsPerWhole >=
            new BigInteger(TotalCost.Copper) * basisPoints;
    }

    public int CompareTo(ExactRoi other) =>
        (new BigInteger(Profit.Copper) * other.TotalCost.Copper).CompareTo(
            new BigInteger(other.Profit.Copper) * TotalCost.Copper);

    private const int BasisPointsPerWhole = 10_000;
}
