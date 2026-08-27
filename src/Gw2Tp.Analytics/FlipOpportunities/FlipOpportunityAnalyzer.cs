using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.FlipOpportunities;

/// <summary>
/// Deterministically models acquiring and immediately liquidating one item quantity against a supplied order book.
/// Results are scenarios, not a guarantee that live orders will execute or remain available.
/// </summary>
public sealed class FlipOpportunityAnalyzer
{
    private readonly TransactionFeePolicy feePolicy;
    private readonly FlipProfitCalculator profitCalculator;
    private readonly OrderBookExecutionSimulator orderBookExecutionSimulator = new();
    private readonly StaleDataPolicy staleDataPolicy;

    public FlipOpportunityAnalyzer(
        TransactionFeePolicy feePolicy,
        StaleDataPolicy? staleDataPolicy = null)
    {
        this.feePolicy = feePolicy ?? throw new ArgumentNullException(nameof(feePolicy));
        profitCalculator = new FlipProfitCalculator(feePolicy);
        this.staleDataPolicy = staleDataPolicy ?? new StaleDataPolicy();
    }

    public FlipOpportunityAnalysis Analyze(FlipOpportunityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scenario = new FlipOpportunityScenario(
            request.ItemId,
            request.RequestedQuantity,
            request.AnalyzedAtUtc,
            feePolicy.ListingFeeRule,
            feePolicy.ExchangeFeeRule,
            request.OrderBook?.Freshness,
            request.OrderBook?.IsPartialData ?? false,
            request.Constraints);

        if (request.OrderBook is null)
        {
            return CreateUnusableResult(scenario, FlipAnalysisReason.MissingOrderBook);
        }

        var orderBook = request.OrderBook;
        if (orderBook.IsPartialData)
        {
            return CreateUnusableResult(scenario, FlipAnalysisReason.PartialMarketData);
        }

        if (orderBook.Freshness is null)
        {
            return CreateUnusableResult(scenario, FlipAnalysisReason.MissingFreshnessMetadata);
        }

        var reasons = new List<FlipAnalysisReason>();
        var isStale = request.AnalyzedAtUtc >= orderBook.Freshness.ExpiresAtUtc;
        if (isStale)
        {
            reasons.Add(FlipAnalysisReason.StaleMarketData);
        }

        var acquisitionExecution = orderBookExecutionSimulator.SimulateAcquisition(
            orderBook.SellLevels,
            request.RequestedQuantity);
        var liquidationExecution = orderBookExecutionSimulator.SimulateLiquidation(
            orderBook.BuyLevels,
            request.RequestedQuantity);
        var liquidity = new FlipLiquidityMetrics(
            request.RequestedQuantity,
            acquisitionExecution.FilledQuantity,
            liquidationExecution.FilledQuantity,
            acquisitionExecution.PriceImpact,
            liquidationExecution.PriceImpact);

        if (!acquisitionExecution.IsFullyFilled)
        {
            reasons.Add(FlipAnalysisReason.InsufficientAcquisitionDepth);
        }

        if (!liquidationExecution.IsFullyFilled)
        {
            reasons.Add(FlipAnalysisReason.InsufficientLiquidationDepth);
        }

        if (!acquisitionExecution.IsFullyFilled || !liquidationExecution.IsFullyFilled)
        {
            return new FlipOpportunityAnalysis(
                scenario,
                FlipOpportunityUsability.Unusable,
                GetConfidence(isStale),
                meetsFinancialConstraints: false,
                reasons,
                acquisitionExecution,
                liquidationExecution,
                liquidity,
                profit: null,
                capitalRequired: null,
                returnOnInvestment: null);
        }

        var profit = profitCalculator.Calculate(acquisitionExecution.TotalValue, liquidationExecution.TotalValue);
        var capitalRequired = profit.AcquisitionCost + profit.ListingFee;

        if (capitalRequired.Copper == 0)
        {
            reasons.Add(FlipAnalysisReason.UndefinedReturnOnInvestment);
            return new FlipOpportunityAnalysis(
                scenario,
                FlipOpportunityUsability.Unusable,
                GetConfidence(isStale),
                meetsFinancialConstraints: false,
                reasons,
                acquisitionExecution,
                liquidationExecution,
                liquidity,
                profit,
                capitalRequired,
                returnOnInvestment: null);
        }

        var returnOnInvestment = new ExactReturnOnInvestment(profit.NetProfit, capitalRequired);
        var meetsMinimumProfit = profit.NetProfit.Copper >= request.Constraints.MinimumNetProfit.Copper;
        var meetsCapitalLimit = request.Constraints.MaximumCapitalRequired is not { } maximumCapitalRequired ||
            capitalRequired.Copper <= maximumCapitalRequired.Copper;

        if (!meetsMinimumProfit)
        {
            reasons.Add(FlipAnalysisReason.BelowMinimumNetProfit);
        }

        if (!meetsCapitalLimit)
        {
            reasons.Add(FlipAnalysisReason.ExceedsMaximumCapital);
        }

        var usability = isStale && staleDataPolicy.StaleDataHandling == StaleDataHandling.Unusable
            ? FlipOpportunityUsability.Unusable
            : FlipOpportunityUsability.Usable;

        return new FlipOpportunityAnalysis(
            scenario,
            usability,
            GetConfidence(isStale),
            meetsMinimumProfit && meetsCapitalLimit,
            reasons,
            acquisitionExecution,
            liquidationExecution,
            liquidity,
            profit,
            capitalRequired,
            returnOnInvestment);
    }

    private static FlipOpportunityAnalysis CreateUnusableResult(
        FlipOpportunityScenario scenario,
        FlipAnalysisReason reason) =>
        new(
            scenario,
            FlipOpportunityUsability.Unusable,
            FlipOpportunityConfidence.Normal,
            meetsFinancialConstraints: false,
            [reason],
            acquisitionExecution: null,
            liquidationExecution: null,
            liquidity: null,
            profit: null,
            capitalRequired: null,
            returnOnInvestment: null);

    private static FlipOpportunityConfidence GetConfidence(bool isStale) =>
        isStale ? FlipOpportunityConfidence.Reduced : FlipOpportunityConfidence.Normal;
}
