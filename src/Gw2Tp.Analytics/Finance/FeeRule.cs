using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.Finance;

/// <summary>
/// A configurable percentage fee expressed in basis points, rounded to whole copper, and
/// optionally subject to a minimum positive-transaction fee.
/// </summary>
public sealed record FeeRule
{
    public const int BasisPointsPerWhole = 10_000;

    public FeeRule(int basisPoints, FeeRounding rounding, Money minimumFee = default)
    {
        if (basisPoints is < 0 or > BasisPointsPerWhole)
        {
            throw new ArgumentOutOfRangeException(
                nameof(basisPoints),
                basisPoints,
                $"The fee rate must be between 0 and {BasisPointsPerWhole} basis points.");
        }

        if (rounding is not FeeRounding.Down and not FeeRounding.Up)
        {
            throw new ArgumentOutOfRangeException(nameof(rounding), rounding, "The fee rounding mode is not supported.");
        }

        if (minimumFee.Copper < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumFee), "A minimum fee cannot be negative.");
        }

        BasisPoints = basisPoints;
        Rounding = rounding;
        MinimumFee = minimumFee;
    }

    public int BasisPoints { get; }

    public FeeRounding Rounding { get; }

    /// <summary>
    /// The minimum fee applied when the transaction value is positive. Zero preserves the
    /// legacy no-minimum behavior.
    /// </summary>
    public Money MinimumFee { get; }
}
