using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.FlipOpportunities;

/// <summary>
/// A transparent liquidity proxy based on target fill completeness and order-book price impact.
/// It is not an execution probability or a composite score.
/// </summary>
public sealed record FlipLiquidityMetrics
{
    public FlipLiquidityMetrics(
        int requestedQuantity,
        int acquisitionFilledQuantity,
        int liquidationFilledQuantity,
        Money acquisitionPriceImpact,
        Money liquidationPriceImpact)
    {
        if (requestedQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedQuantity),
                "A requested quantity must be positive.");
        }

        ValidateFilledQuantity(acquisitionFilledQuantity, requestedQuantity, nameof(acquisitionFilledQuantity));
        ValidateFilledQuantity(liquidationFilledQuantity, requestedQuantity, nameof(liquidationFilledQuantity));

        if (acquisitionPriceImpact.Copper < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(acquisitionPriceImpact),
                "Acquisition price impact cannot be negative.");
        }

        if (liquidationPriceImpact.Copper < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(liquidationPriceImpact),
                "Liquidation price impact cannot be negative.");
        }

        RequestedQuantity = requestedQuantity;
        AcquisitionFilledQuantity = acquisitionFilledQuantity;
        LiquidationFilledQuantity = liquidationFilledQuantity;
        AcquisitionPriceImpact = acquisitionPriceImpact;
        LiquidationPriceImpact = liquidationPriceImpact;
        TotalPriceImpact = acquisitionPriceImpact + liquidationPriceImpact;
    }

    public int RequestedQuantity { get; }

    public int AcquisitionFilledQuantity { get; }

    public int LiquidationFilledQuantity { get; }

    public bool IsFullyAcquirable => AcquisitionFilledQuantity == RequestedQuantity;

    public bool IsFullyLiquidatable => LiquidationFilledQuantity == RequestedQuantity;

    public Money AcquisitionPriceImpact { get; }

    public Money LiquidationPriceImpact { get; }

    public Money TotalPriceImpact { get; }

    private static void ValidateFilledQuantity(int filledQuantity, int requestedQuantity, string parameterName)
    {
        if (filledQuantity < 0 || filledQuantity > requestedQuantity)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "A filled quantity must be between zero and the requested quantity.");
        }
    }
}
