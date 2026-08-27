namespace Gw2Tp.Analytics.FlipOpportunities;

/// <summary>
/// Stable, ordered reason codes explaining data quality, scenario viability, and constraint outcomes.
/// </summary>
public enum FlipAnalysisReason
{
    MissingOrderBook = 0,
    MissingFreshnessMetadata = 1,
    PartialMarketData = 2,
    StaleMarketData = 3,
    InsufficientAcquisitionDepth = 4,
    InsufficientLiquidationDepth = 5,
    UndefinedReturnOnInvestment = 6,
    BelowMinimumNetProfit = 7,
    ExceedsMaximumCapital = 8,
}
