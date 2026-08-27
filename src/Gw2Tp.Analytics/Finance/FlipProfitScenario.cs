using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.Finance;

/// <summary>
/// The result of a modeled completed flip for total acquisition and sale values.
/// </summary>
public sealed record FlipProfitScenario(
    Money AcquisitionCost,
    Money GrossSaleValue,
    Money ListingFee,
    Money ExchangeFee,
    Money NetSaleProceeds,
    Money NetProfit);
