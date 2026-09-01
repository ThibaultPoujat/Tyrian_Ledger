using System.Numerics;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Recommendations;

/// <summary>
/// The only risk profiles supported by the M9 beginner recommendation experience.
/// </summary>
public enum BeginnerRiskProfile
{
    Cautious,
    Balanced,
    Adventurous,
}

/// <summary>
/// Fixed financial constraints for one supported beginner risk profile.
/// </summary>
public sealed record BeginnerRiskProfilePolicy(
    int SpendCapBasisPoints,
    int MinimumRoiBasisPoints,
    Money MinimumProfit);

/// <summary>
/// M9's fixed beginner recommendation rules.
/// </summary>
public static class BeginnerRecommendationPolicy
{
    public const int MaximumRecommendationCount = 5;
    public const int MinimumAggregateSideQuantity = 10;
    public const int MaximumPlannedPriceSpreadMultiple = 2;
    public const int MinimumDetailedSideListings = 3;
    public const int MinimumDetailedSideQuantity = 10;

    public static BeginnerRiskProfilePolicy GetRiskProfilePolicy(BeginnerRiskProfile profile) => profile switch
    {
        BeginnerRiskProfile.Cautious => new(1_000, 500, new Money(1_000)),
        BeginnerRiskProfile.Balanced => new(2_500, 800, new Money(2_500)),
        BeginnerRiskProfile.Adventurous => new(5_000, 1_200, new Money(5_000)),
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "The beginner risk profile is not supported."),
    };

    public static Money CalculateSpendCap(Money capital, BeginnerRiskProfile profile)
    {
        if (capital.Copper < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capital), "Capital cannot be negative.");
        }

        var policy = GetRiskProfilePolicy(profile);
        var wholeBasisPointBlocks = capital.Copper / FeeBasisPointsPerWhole;
        var remainderCopper = capital.Copper % FeeBasisPointsPerWhole;
        var wholeCap = checked(wholeBasisPointBlocks * policy.SpendCapBasisPoints);
        var remainderCap = checked(remainderCopper * policy.SpendCapBasisPoints / FeeBasisPointsPerWhole);

        return new Money(checked(wholeCap + remainderCap));
    }

    private const int FeeBasisPointsPerWhole = 10_000;
}

/// <summary>
/// The practical action state derived from current seller depth at the prescribed buy price.
/// </summary>
public enum BeginnerRecommendationRoute
{
    CanActNow,
    PlaceOrderAndWait,
}

/// <summary>
/// Structured assumptions that callers can explain without treating a modeled recommendation
/// as a guarantee or an automated instruction.
/// </summary>
public enum BeginnerRecommendationAssumption
{
    CurrentOrderBookSnapshotOnly,
    CurrentOrderBookDepthAndSpreadGuard,
    ManualInGameOrdersRequired,
    NoExecutionSaleOrProfitGuarantee,
    FeeRoundingPendingExternalVerification,
}

/// <summary>
/// One public-market item supplied to the pure recommendation engine after detailed market
/// reads have completed. The contained types are application contracts, not external DTOs.
/// </summary>
public sealed record BeginnerRecommendationCandidate(
    MarketItemMetadata Item,
    MarketListing Listing);

/// <summary>
/// Complete in-memory input for one deterministic recommendation calculation.
/// </summary>
public sealed record BeginnerRecommendationRequest(
    Money Capital,
    BeginnerRiskProfile RiskProfile,
    DateTimeOffset ScanCompletedAtUtc,
    IReadOnlyList<BeginnerRecommendationCandidate> Candidates);

/// <summary>
/// Exact modeled ROI represented as a signed profit numerator and positive full-up-front-cost
/// denominator. No decimal or floating-point representation is used.
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

        return new BigInteger(Profit.Copper) * FeeBasisPointsPerWhole >=
            new BigInteger(TotalCost.Copper) * basisPoints;
    }

    public int CompareTo(ExactRoi other) =>
        (new BigInteger(Profit.Copper) * other.TotalCost.Copper).CompareTo(
            new BigInteger(other.Profit.Copper) * TotalCost.Copper);

    private const int FeeBasisPointsPerWhole = 10_000;
}

/// <summary>
/// Current order-book evidence used to explain the selected action route.
/// </summary>
public sealed record BeginnerRecommendationRouteEvidence(
    long SellerQuantityAtOrBelowBuyPrice,
    bool CoversSelectedQuantity);

/// <summary>
/// One transparent, non-guaranteed recommendation calculated entirely in integer copper.
/// </summary>
public sealed record BeginnerRecommendation(
    int Rank,
    int ItemId,
    string ItemName,
    BeginnerRecommendationRoute Route,
    int Quantity,
    Money BuyUnitPrice,
    Money SaleUnitPrice,
    Money BuyOrderReserve,
    Money GrossSale,
    Money ListingFee,
    Money ExchangeFee,
    Money NetSaleProceeds,
    Money TotalCost,
    Money ModeledProfit,
    ExactRoi ModeledRoi,
    DateTimeOffset ScanCompletedAtUtc,
    BeginnerRecommendationRouteEvidence RouteEvidence,
    IReadOnlyList<BeginnerRecommendationAssumption> Assumptions);

/// <summary>
/// The ordered, bounded result set for one complete market input.
/// </summary>
public sealed record BeginnerRecommendationResult(
    Money Capital,
    BeginnerRiskProfile RiskProfile,
    Money SpendCap,
    DateTimeOffset ScanCompletedAtUtc,
    IReadOnlyList<BeginnerRecommendation> Recommendations)
{
    public IReadOnlyList<BeginnerRecommendation> CanActNow => Recommendations
        .Where(recommendation => recommendation.Route == BeginnerRecommendationRoute.CanActNow)
        .ToArray();

    public IReadOnlyList<BeginnerRecommendation> PlaceOrderAndWait => Recommendations
        .Where(recommendation => recommendation.Route == BeginnerRecommendationRoute.PlaceOrderAndWait)
        .ToArray();
}
