using Gw2Tp.Analytics.Finance;
using Gw2Tp.Domain.MarketData;

namespace Gw2Tp.Analytics.FlipOpportunities;

/// <summary>
/// Immutable input assumptions retained with an analysis so callers can explain the modeled scenario.
/// </summary>
public sealed record FlipOpportunityScenario(
    int ItemId,
    int RequestedQuantity,
    DateTimeOffset AnalyzedAtUtc,
    FeeRule ListingFeeRule,
    FeeRule ExchangeFeeRule,
    DataFreshness? Freshness,
    bool IsPartialData,
    FlipOpportunityConstraints Constraints);
