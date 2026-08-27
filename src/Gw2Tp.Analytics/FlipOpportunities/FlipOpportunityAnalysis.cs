using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.FlipOpportunities;

/// <summary>
/// The complete, explainable result of analyzing one requested flip quantity.
/// </summary>
public sealed record FlipOpportunityAnalysis
{
    public FlipOpportunityAnalysis(
        FlipOpportunityScenario scenario,
        FlipOpportunityUsability usability,
        FlipOpportunityConfidence confidence,
        bool meetsFinancialConstraints,
        IReadOnlyList<FlipAnalysisReason> reasons,
        OrderBookExecutionScenario? acquisitionExecution,
        OrderBookExecutionScenario? liquidationExecution,
        FlipLiquidityMetrics? liquidity,
        FlipProfitScenario? profit,
        Money? capitalRequired,
        ExactReturnOnInvestment? returnOnInvestment)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(reasons);

        Scenario = scenario;
        Usability = usability;
        Confidence = confidence;
        MeetsFinancialConstraints = meetsFinancialConstraints;
        Reasons = Array.AsReadOnly(reasons.ToArray());
        AcquisitionExecution = acquisitionExecution;
        LiquidationExecution = liquidationExecution;
        Liquidity = liquidity;
        Profit = profit;
        CapitalRequired = capitalRequired;
        ReturnOnInvestment = returnOnInvestment;
    }

    public FlipOpportunityScenario Scenario { get; }

    public FlipOpportunityUsability Usability { get; }

    public FlipOpportunityConfidence Confidence { get; }

    public bool MeetsFinancialConstraints { get; }

    public bool IsEligible => Usability == FlipOpportunityUsability.Usable && MeetsFinancialConstraints;

    /// <summary>
    /// Reason codes are emitted in <see cref="FlipAnalysisReason"/> declaration order.
    /// </summary>
    public IReadOnlyList<FlipAnalysisReason> Reasons { get; }

    public OrderBookExecutionScenario? AcquisitionExecution { get; }

    public OrderBookExecutionScenario? LiquidationExecution { get; }

    public FlipLiquidityMetrics? Liquidity { get; }

    public FlipProfitScenario? Profit { get; }

    public Money? CapitalRequired { get; }

    public ExactReturnOnInvestment? ReturnOnInvestment { get; }
}
