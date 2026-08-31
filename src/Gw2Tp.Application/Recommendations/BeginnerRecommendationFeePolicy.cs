using Gw2Tp.Analytics.Finance;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Recommendations;

/// <summary>
/// The owner-selected M9 fee policy used for modeled beginner recommendations. The separate
/// whole-copper rounding behavior remains documented as externally unverified in VERIFY-013.
/// </summary>
public static class BeginnerRecommendationFeePolicy
{
    public const int ListingFeeBasisPoints = 500;
    public const int ExchangeFeeBasisPoints = 1_000;

    public static TransactionFeePolicy Create() => new(
        new FeeRule(ListingFeeBasisPoints, FeeRounding.Up, new Money(1)),
        new FeeRule(ExchangeFeeBasisPoints, FeeRounding.Up, new Money(1)));
}
