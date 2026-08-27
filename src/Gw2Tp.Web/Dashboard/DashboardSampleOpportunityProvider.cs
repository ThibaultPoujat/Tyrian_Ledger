using System.Numerics;
using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.FlipOpportunities;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Application.Preferences;
using Gw2Tp.Application.Time;
using Gw2Tp.Domain.Finance;
using Gw2Tp.Domain.MarketData;

namespace Gw2Tp.Web.Dashboard;

/// <summary>
/// Creates a local, deterministic data set solely for the first dashboard UI.
/// It does not read from the Guild Wars 2 API or present its values as live data.
/// </summary>
internal sealed class DashboardSampleOpportunityProvider
{
    private static readonly TransactionFeePolicy SampleFeePolicy = new(
        new FeeRule(0, FeeRounding.Down),
        new FeeRule(0, FeeRounding.Down));

    private static readonly FlipOpportunityScoringConfiguration SampleScoringConfiguration = new(
        targetNetProfit: new Money(700),
        targetReturnOnInvestmentBasisPoints: 8_000,
        acceptablePriceImpactBasisPoints: 2_000,
        weights: new OpportunityScoringWeights(
            netProfit: 4,
            capitalEfficiency: 3,
            liquidity: 1,
            freshness: 1,
            risk: 1,
            complexity: 1),
        freshDataScoreBasisPoints: FlipOpportunityScoringConfiguration.MaximumScoreBasisPoints,
        staleDataScoreBasisPoints: 0,
        normalConfidenceRiskScoreBasisPoints: FlipOpportunityScoringConfiguration.MaximumScoreBasisPoints,
        reducedConfidenceRiskScoreBasisPoints: 0,
        twoLegFlipComplexityScoreBasisPoints: FlipOpportunityScoringConfiguration.MaximumScoreBasisPoints);

    private readonly IClock clock;

    public DashboardSampleOpportunityProvider(IClock clock)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public DashboardOpportunitiesResponse GetDashboard(UserSessionPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var analyzedAtUtc = clock.UtcNow;
        var analyzer = new FlipOpportunityAnalyzer(SampleFeePolicy);
        var scorer = new FlipOpportunityScorer(SampleScoringConfiguration);
        var candidates = CreateCandidates(analyzedAtUtc);
        var analysesByItemId = candidates
            .Select(candidate => (candidate, analysis: analyzer.Analyze(candidate.Request)))
            .ToDictionary(result => result.candidate.ItemId, result => result);
        var rankedScores = scorer
            .Rank(analysesByItemId.Values.Select(result => result.analysis))
            .OrderByDescending(score => score.ScoreBasisPoints)
            .ThenBy(score => score.ItemId);

        var opportunities = rankedScores
            .Select(score => CreateResponse(score, rank: 0, analysesByItemId[score.ItemId]))
            .Where(opportunity => MatchesPreferences(opportunity, preferences))
            .Select((opportunity, index) => opportunity with { Rank = index + 1 })
            .ToArray();

        return new DashboardOpportunitiesResponse(
            IsSampleData: true,
            SourceDescription: "Deterministic local sample data. No live market scan was performed.",
            GeneratedAtUtc: analyzedAtUtc,
            Opportunities: Array.AsReadOnly(opportunities));
    }

    private static bool MatchesPreferences(
        DashboardOpportunityResponse opportunity,
        UserSessionPreferences preferences)
    {
        var perOpportunityCapitalLimit = preferences.GetPerOpportunityCapitalLimitCopper();

        return (perOpportunityCapitalLimit is null ||
                opportunity.CapitalRequiredCopper <= perOpportunityCapitalLimit)
            && (preferences.MinimumProfitCopper is null ||
                opportunity.ModeledNetProfitCopper >= preferences.MinimumProfitCopper)
            && MatchesRiskPreference(opportunity.Confidence, preferences.RiskPreference)
            && MatchesStrategyPreference(opportunity.Strategy, preferences.StrategyPreference);
    }

    private static bool MatchesRiskPreference(
        string confidence,
        OpportunityRiskPreference preference) => preference switch
    {
        OpportunityRiskPreference.All => true,
        OpportunityRiskPreference.Normal => confidence == "normal",
        OpportunityRiskPreference.Reduced => confidence == "reduced",
        _ => throw new ArgumentOutOfRangeException(nameof(preference), preference, "The risk preference is not supported."),
    };

    private static bool MatchesStrategyPreference(
        string strategy,
        OpportunityStrategyPreference preference) => preference switch
    {
        OpportunityStrategyPreference.All => true,
        OpportunityStrategyPreference.MarketFlip => strategy == "market-flip",
        _ => throw new ArgumentOutOfRangeException(nameof(preference), preference, "The strategy preference is not supported."),
    };

    private static IReadOnlyList<SampleCandidate> CreateCandidates(DateTimeOffset analyzedAtUtc) =>
    [
        new SampleCandidate(
            900_001,
            "Sample market flip #900001",
            CreateRequest(
                900_001,
                requestedQuantity: 5,
                buyLevels: [Level(5, 160)],
                sellLevels: [Level(5, 100)],
                freshness: CurrentFreshness(analyzedAtUtc),
                analyzedAtUtc)),
        new SampleCandidate(
            900_002,
            "Sample market flip #900002",
            CreateRequest(
                900_002,
                requestedQuantity: 10,
                buyLevels: [Level(10, 140)],
                sellLevels: [Level(10, 100)],
                freshness: CurrentFreshness(analyzedAtUtc),
                analyzedAtUtc)),
        new SampleCandidate(
            900_003,
            "Sample market flip #900003",
            CreateRequest(
                900_003,
                requestedQuantity: 4,
                buyLevels: [Level(4, 350)],
                sellLevels: [Level(4, 200)],
                freshness: StaleFreshness(analyzedAtUtc),
                analyzedAtUtc)),
        new SampleCandidate(
            900_004,
            "Sample market flip #900004",
            CreateRequest(
                900_004,
                requestedQuantity: 5,
                buyLevels: [Level(5, 300)],
                sellLevels: [Level(2, 100), Level(3, 200)],
                freshness: CurrentFreshness(analyzedAtUtc),
                analyzedAtUtc)),
    ];

    private static DashboardOpportunityResponse CreateResponse(
        FlipOpportunityScore score,
        int rank,
        (SampleCandidate candidate, FlipOpportunityAnalysis analysis) result)
    {
        var explanation = score.Explanation;
        var capturedAtUtc = result.analysis.Scenario.Freshness?.CapturedAtUtc
            ?? throw new InvalidOperationException("Dashboard sample opportunities must include freshness metadata.");

        return new DashboardOpportunityResponse(
            ItemId: score.ItemId,
            Label: result.candidate.Label,
            Strategy: "market-flip",
            Rank: rank,
            ScoreBasisPoints: score.ScoreBasisPoints,
            CapitalRequiredCopper: explanation.CapitalRequired.Copper,
            ModeledNetProfitCopper: explanation.NetProfit.Copper,
            ReturnOnInvestmentBasisPoints: ToBasisPoints(explanation.ReturnOnInvestment),
            LiquidityPriceImpactCopper: explanation.TotalPriceImpact.Copper,
            Confidence: ToResponseValue(explanation.Confidence),
            Freshness: explanation.IsStale ? "stale" : "current",
            CapturedAtUtc: capturedAtUtc,
            Detail: CreateDetailResponse(result.analysis));
    }

    private static DashboardOpportunityDetailResponse CreateDetailResponse(FlipOpportunityAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var acquisition = analysis.AcquisitionExecution
            ?? throw new InvalidOperationException("Dashboard sample opportunities must include acquisition execution.");
        var liquidation = analysis.LiquidationExecution
            ?? throw new InvalidOperationException("Dashboard sample opportunities must include liquidation execution.");
        var liquidity = analysis.Liquidity
            ?? throw new InvalidOperationException("Dashboard sample opportunities must include liquidity metrics.");
        var profit = analysis.Profit
            ?? throw new InvalidOperationException("Dashboard sample opportunities must include modeled profit.");
        var capitalRequired = analysis.CapitalRequired
            ?? throw new InvalidOperationException("Dashboard sample opportunities must include capital requirements.");
        var returnOnInvestment = analysis.ReturnOnInvestment
            ?? throw new InvalidOperationException("Dashboard sample opportunities must include ROI.");
        var freshness = analysis.Scenario.Freshness
            ?? throw new InvalidOperationException("Dashboard sample opportunities must include freshness metadata.");

        return new DashboardOpportunityDetailResponse(
            RequestedQuantity: analysis.Scenario.RequestedQuantity,
            AnalyzedAtUtc: analysis.Scenario.AnalyzedAtUtc,
            Acquisition: CreateExecutionResponse(acquisition),
            Exit: CreateExecutionResponse(liquidation),
            Fees: new DashboardFeeResponse(
                ListingBasisPoints: analysis.Scenario.ListingFeeRule.BasisPoints,
                ListingRounding: ToResponseValue(analysis.Scenario.ListingFeeRule.Rounding),
                ListingFeeCopper: profit.ListingFee.Copper,
                ExchangeBasisPoints: analysis.Scenario.ExchangeFeeRule.BasisPoints,
                ExchangeRounding: ToResponseValue(analysis.Scenario.ExchangeFeeRule.Rounding),
                ExchangeFeeCopper: profit.ExchangeFee.Copper),
            Financials: new DashboardFinancialResponse(
                AcquisitionCostCopper: profit.AcquisitionCost.Copper,
                GrossSaleValueCopper: profit.GrossSaleValue.Copper,
                NetSaleProceedsCopper: profit.NetSaleProceeds.Copper,
                CapitalRequiredCopper: capitalRequired.Copper,
                ModeledNetProfitCopper: profit.NetProfit.Copper,
                ReturnOnInvestmentBasisPoints: ToBasisPoints(returnOnInvestment)),
            Liquidity: new DashboardLiquidityResponse(
                AcquisitionFilledQuantity: liquidity.AcquisitionFilledQuantity,
                LiquidationFilledQuantity: liquidity.LiquidationFilledQuantity,
                IsFullyAcquirable: liquidity.IsFullyAcquirable,
                IsFullyLiquidatable: liquidity.IsFullyLiquidatable,
                AcquisitionPriceImpactCopper: liquidity.AcquisitionPriceImpact.Copper,
                LiquidationPriceImpactCopper: liquidity.LiquidationPriceImpact.Copper,
                TotalPriceImpactCopper: liquidity.TotalPriceImpact.Copper),
            Freshness: analysis.Scenario.AnalyzedAtUtc >= freshness.ExpiresAtUtc ? "stale" : "current",
            CapturedAtUtc: freshness.CapturedAtUtc,
            ExpiresAtUtc: freshness.ExpiresAtUtc,
            Confidence: ToResponseValue(analysis.Confidence));
    }

    private static DashboardExecutionResponse CreateExecutionResponse(OrderBookExecutionScenario execution) =>
        new(
            RequestedQuantity: execution.RequestedQuantity,
            FilledQuantity: execution.FilledQuantity,
            IsFullyFilled: execution.IsFullyFilled,
            TotalValueCopper: execution.TotalValue.Copper,
            PriceImpactCopper: execution.PriceImpact.Copper);

    private static FlipOpportunityRequest CreateRequest(
        int itemId,
        int requestedQuantity,
        IReadOnlyList<OrderBookLevel> buyLevels,
        IReadOnlyList<OrderBookLevel> sellLevels,
        DataFreshness freshness,
        DateTimeOffset analyzedAtUtc) =>
        new(
            itemId,
            requestedQuantity,
            new FlipOpportunityOrderBook(buyLevels, sellLevels, freshness, isPartialData: false),
            analyzedAtUtc,
            new FlipOpportunityConstraints(Money.Zero));

    private static DataFreshness CurrentFreshness(DateTimeOffset analyzedAtUtc) =>
        new(analyzedAtUtc.AddMinutes(-1), analyzedAtUtc.AddMinutes(4));

    private static DataFreshness StaleFreshness(DateTimeOffset analyzedAtUtc) =>
        new(analyzedAtUtc.AddMinutes(-30), analyzedAtUtc.AddMinutes(-1));

    private static OrderBookLevel Level(int quantity, long copper) =>
        new(quantity, new Money(copper));

    private static long ToBasisPoints(ExactReturnOnInvestment returnOnInvestment) =>
        (long)(new BigInteger(returnOnInvestment.NetProfit.Copper) * FeeRule.BasisPointsPerWhole /
            returnOnInvestment.CapitalRequired.Copper);

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

    private sealed record SampleCandidate(int ItemId, string Label, FlipOpportunityRequest Request);
}

internal sealed record DashboardOpportunitiesResponse(
    bool IsSampleData,
    string SourceDescription,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<DashboardOpportunityResponse> Opportunities);

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
