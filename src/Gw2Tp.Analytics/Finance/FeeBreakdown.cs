using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.Finance;

/// <summary>
/// The independently calculated transaction fees for a gross sale value.
/// </summary>
public readonly record struct FeeBreakdown(Money ListingFee, Money ExchangeFee);
