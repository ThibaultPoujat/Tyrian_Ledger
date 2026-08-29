using Gw2Tp.Analytics.Finance;
using System.Numerics;
using Gw2Tp.Analytics.FlipOpportunities;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Application.MarketScanning;
using Gw2Tp.Application.Preferences;
using Gw2Tp.Domain.Finance;
using Microsoft.Extensions.Options;

namespace Gw2Tp.Web.Dashboard;

internal sealed class DashboardOpportunityProvider
{
    private readonly MarketFlipScanService marketFlipScanService;
    private readonly DashboardScoringOptions scoringOptions;

    public DashboardOpportunityProvider(
        MarketFlipScanService marketFlipScanService,
        IOptions<DashboardScoringOptions> scoringOptions)
    {
        this.marketFlipScanService = marketFlipScanService ?? throw new ArgumentNullException(nameof(marketFlipScanService));
        this.scoringOptions = scoringOptions?.Value ?? throw new ArgumentNullException(nameof(scoringOptions));
    }

    public async Task<DashboardOpportunitiesResponse> GetDashboardAsync(
        UserSessionPreferences preferences,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var scan = await marketFlipScanService.ScanAsync(
            preferences,
            scoringOptions.CreateConfiguration(),
            cancellationToken);
        return DashboardOpportunitiesResponse.From(scan);
    }
}

/// <summary>
/// Local, configurable ranking policy. The documented defaults preserve the former dashboard
/// scoring profile; they are heuristic application policy, not financial truth.
/// </summary>
internal sealed class DashboardScoringOptions
{
    public const string ConfigurationSectionName = "MarketDashboard:Scoring";

    public long TargetNetProfitCopper { get; set; } = 700;

    public int TargetReturnOnInvestmentBasisPoints { get; set; } = 8_000;

    public int AcceptablePriceImpactBasisPoints { get; set; } = 2_000;

    public int NetProfitWeight { get; set; } = 4;

    public int CapitalEfficiencyWeight { get; set; } = 3;

    public int LiquidityWeight { get; set; } = 1;

    public int FreshnessWeight { get; set; } = 1;

    public int RiskWeight { get; set; } = 1;

    public int ComplexityWeight { get; set; } = 1;

    public int FreshDataScoreBasisPoints { get; set; } = FlipOpportunityScoringConfiguration.MaximumScoreBasisPoints;

    public int StaleDataScoreBasisPoints { get; set; }

    public int NormalConfidenceRiskScoreBasisPoints { get; set; } = FlipOpportunityScoringConfiguration.MaximumScoreBasisPoints;

    public int ReducedConfidenceRiskScoreBasisPoints { get; set; }

    public int TwoLegFlipComplexityScoreBasisPoints { get; set; } = FlipOpportunityScoringConfiguration.MaximumScoreBasisPoints;

    public FlipOpportunityScoringConfiguration CreateConfiguration() => new(
        new Money(TargetNetProfitCopper),
        TargetReturnOnInvestmentBasisPoints,
        AcceptablePriceImpactBasisPoints,
        new OpportunityScoringWeights(
            NetProfitWeight,
            CapitalEfficiencyWeight,
            LiquidityWeight,
            FreshnessWeight,
            RiskWeight,
            ComplexityWeight),
        FreshDataScoreBasisPoints,
        StaleDataScoreBasisPoints,
        NormalConfidenceRiskScoreBasisPoints,
        ReducedConfidenceRiskScoreBasisPoints,
        TwoLegFlipComplexityScoreBasisPoints);

    public bool TryValidate(out string validationError)
    {
        try
        {
            _ = CreateConfiguration();
            validationError = string.Empty;
            return true;
        }
        catch (ArgumentException exception)
        {
            validationError = $"{ConfigurationSectionName} is invalid: {exception.Message}";
            return false;
        }
    }
}

internal sealed class DashboardScoringOptionsValidator : IValidateOptions<DashboardScoringOptions>
{
    public ValidateOptionsResult Validate(string? name, DashboardScoringOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.TryValidate(out var validationError)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(validationError);
    }
}

internal sealed record DashboardOpportunitiesResponse(
    string Status,
    string SourceDescription,
    DateTimeOffset GeneratedAtUtc,
    int TrackedItemCount,
    IReadOnlyList<DashboardScreenedCandidateResponse> ScreenedCandidates,
    IReadOnlyList<DashboardOpportunityResponse> Opportunities)
{
    internal static DashboardOpportunitiesResponse From(MarketFlipScanResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);

        return new DashboardOpportunitiesResponse(
            ToResponseValue(scan.Status),
            DescribeSource(scan),
            scan.ScannedAtUtc,
            scan.TrackedItemCount,
            scan.ScreenedCandidates.Select(candidate => new DashboardScreenedCandidateResponse(
                candidate.ItemId,
                candidate.BestBidCopper,
                candidate.BestAskCopper)).ToArray(),
            scan.Opportunities.Select((opportunity, index) => CreateResponse(opportunity, index + 1)).ToArray());
    }

    private static DashboardOpportunityResponse CreateResponse(MarketFlipScanOpportunity opportunity, int rank)
    {
        var analysis = opportunity.Analysis;
        var score = opportunity.Score;
        var explanation = score.Explanation;
        var acquisition = analysis.AcquisitionExecution
            ?? throw new InvalidOperationException("Ranked scan opportunities must include acquisition execution.");
        var liquidation = analysis.LiquidationExecution
            ?? throw new InvalidOperationException("Ranked scan opportunities must include liquidation execution.");
        var liquidity = analysis.Liquidity
            ?? throw new InvalidOperationException("Ranked scan opportunities must include liquidity metrics.");
        var profit = analysis.Profit
            ?? throw new InvalidOperationException("Ranked scan opportunities must include profit details.");
        var capitalRequired = analysis.CapitalRequired
            ?? throw new InvalidOperationException("Ranked scan opportunities must include capital requirements.");
        var returnOnInvestment = analysis.ReturnOnInvestment
            ?? throw new InvalidOperationException("Ranked scan opportunities must include return on investment.");
        var freshness = analysis.Scenario.Freshness
            ?? throw new InvalidOperationException("Ranked scan opportunities must include freshness metadata.");

        return new DashboardOpportunityResponse(
            analysis.Scenario.ItemId,
            $"Tracked market item #{analysis.Scenario.ItemId}",
            "market-flip",
            rank,
            score.ScoreBasisPoints,
            capitalRequired.Copper,
            profit.NetProfit.Copper,
            ToBasisPoints(returnOnInvestment),
            liquidity.TotalPriceImpact.Copper,
            ToResponseValue(analysis.Confidence),
            analysis.Reasons.Contains(FlipAnalysisReason.StaleMarketData) ? "stale" : "current",
            freshness.CapturedAtUtc,
            new DashboardOpportunityDetailResponse(
                analysis.Scenario.RequestedQuantity,
                analysis.Scenario.AnalyzedAtUtc,
                CreateExecutionResponse(acquisition),
                CreateExecutionResponse(liquidation),
                new DashboardFeeResponse(
                    analysis.Scenario.ListingFeeRule.BasisPoints,
                    ToResponseValue(analysis.Scenario.ListingFeeRule.Rounding),
                    profit.ListingFee.Copper,
                    analysis.Scenario.ExchangeFeeRule.BasisPoints,
                    ToResponseValue(analysis.Scenario.ExchangeFeeRule.Rounding),
                    profit.ExchangeFee.Copper),
                new DashboardFinancialResponse(
                    profit.AcquisitionCost.Copper,
                    profit.GrossSaleValue.Copper,
                    profit.NetSaleProceeds.Copper,
                    capitalRequired.Copper,
                    profit.NetProfit.Copper,
                    ToBasisPoints(returnOnInvestment)),
                new DashboardLiquidityResponse(
                    liquidity.AcquisitionFilledQuantity,
                    liquidity.LiquidationFilledQuantity,
                    liquidity.IsFullyAcquirable,
                    liquidity.IsFullyLiquidatable,
                    liquidity.AcquisitionPriceImpact.Copper,
                    liquidity.LiquidationPriceImpact.Copper,
                    liquidity.TotalPriceImpact.Copper),
                analysis.Reasons.Contains(FlipAnalysisReason.StaleMarketData) ? "stale" : "current",
                freshness.CapturedAtUtc,
                freshness.ExpiresAtUtc,
                ToResponseValue(analysis.Confidence)));
    }

    private static DashboardExecutionResponse CreateExecutionResponse(OrderBookExecutionScenario execution) => new(
        execution.RequestedQuantity,
        execution.FilledQuantity,
        execution.IsFullyFilled,
        execution.TotalValue.Copper,
        execution.PriceImpact.Copper);

    private static long ToBasisPoints(ExactReturnOnInvestment returnOnInvestment) =>
        (long)(new BigInteger(returnOnInvestment.NetProfit.Copper) * FeeRule.BasisPointsPerWhole /
            returnOnInvestment.CapitalRequired.Copper);

    private static string DescribeSource(MarketFlipScanResult scan) => scan.Status switch
    {
        MarketFlipScanStatus.NoTrackedItems => "No locally tracked market items are available to scan.",
        MarketFlipScanStatus.FeeConfigurationRequired =>
            "Aggregate prices screened locally tracked items. Configure both fee rules before detailed listings and modeled profit are requested.",
        MarketFlipScanStatus.Complete =>
            "Read-only live market scan of locally tracked items. Results are modeled scenarios, not orders, execution predictions, or profit guarantees.",
        MarketFlipScanStatus.Unavailable =>
            "Live market data was unavailable or incomplete. No ranked opportunity is shown.",
        _ => throw new ArgumentOutOfRangeException(nameof(scan), scan.Status, "The market scan status is not supported."),
    };

    private static string ToResponseValue(MarketFlipScanStatus status) => status switch
    {
        MarketFlipScanStatus.NoTrackedItems => "no-tracked-items",
        MarketFlipScanStatus.FeeConfigurationRequired => "fee-configuration-required",
        MarketFlipScanStatus.Complete => "complete",
        MarketFlipScanStatus.Unavailable => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "The market scan status is not supported."),
    };

    private static string ToResponseValue(FlipOpportunityConfidence confidence) => confidence switch
    {
        FlipOpportunityConfidence.Normal => "normal",
        FlipOpportunityConfidence.Reduced => "reduced",
        _ => throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "The opportunity confidence is not supported."),
    };

    private static string ToResponseValue(FeeRounding rounding) => rounding switch
    {
        FeeRounding.Down => "down",
        FeeRounding.Up => "up",
        _ => throw new ArgumentOutOfRangeException(nameof(rounding), rounding, "The fee rounding mode is not supported."),
    };
}

internal sealed record DashboardScreenedCandidateResponse(int ItemId, int BestBidCopper, int BestAskCopper);

internal sealed record DashboardOpportunityResponse(
    int ItemId,
    string Label,
    string Strategy,
    int Rank,
    int ScoreBasisPoints,
    long CapitalRequiredCopper,
    long ModeledNetProfitCopper,
    long ReturnOnInvestmentBasisPoints,
    long LiquidityPriceImpactCopper,
    string Confidence,
    string Freshness,
    DateTimeOffset CapturedAtUtc,
    DashboardOpportunityDetailResponse Detail);

internal sealed record DashboardOpportunityDetailResponse(
    int RequestedQuantity,
    DateTimeOffset AnalyzedAtUtc,
    DashboardExecutionResponse Acquisition,
    DashboardExecutionResponse Exit,
    DashboardFeeResponse Fees,
    DashboardFinancialResponse Financials,
    DashboardLiquidityResponse Liquidity,
    string Freshness,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Confidence);

internal sealed record DashboardExecutionResponse(
    int RequestedQuantity,
    int FilledQuantity,
    bool IsFullyFilled,
    long TotalValueCopper,
    long PriceImpactCopper);

internal sealed record DashboardFeeResponse(
    int ListingBasisPoints,
    string ListingRounding,
    long ListingFeeCopper,
    int ExchangeBasisPoints,
    string ExchangeRounding,
    long ExchangeFeeCopper);

internal sealed record DashboardFinancialResponse(
    long AcquisitionCostCopper,
    long GrossSaleValueCopper,
    long NetSaleProceedsCopper,
    long CapitalRequiredCopper,
    long ModeledNetProfitCopper,
    long ReturnOnInvestmentBasisPoints);

internal sealed record DashboardLiquidityResponse(
    int AcquisitionFilledQuantity,
    int LiquidationFilledQuantity,
    bool IsFullyAcquirable,
    bool IsFullyLiquidatable,
    long AcquisitionPriceImpactCopper,
    long LiquidationPriceImpactCopper,
    long TotalPriceImpactCopper);
