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
    public const int ListingFeeBasisPoints = 500;
    public const int ExchangeFeeBasisPoints = 1_000;
    public const long MinimumPositiveFeeCopper = 1;
    public const bool IsListingFeeRefundable = false;
    public const bool IsFractionalCopperRoundingExternallyVerified = false;

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
}
