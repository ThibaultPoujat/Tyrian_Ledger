namespace Gw2Tp.Analytics.Finance;

/// <summary>
/// A configurable percentage fee expressed in basis points and rounded to whole copper.
/// </summary>
public sealed record FeeRule
{
    public const int BasisPointsPerWhole = 10_000;

    public FeeRule(int basisPoints, FeeRounding rounding)
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

        BasisPoints = basisPoints;
        Rounding = rounding;
    }

    public int BasisPoints { get; }

    public FeeRounding Rounding { get; }
}
