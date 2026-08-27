using System.Numerics;
using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.FlipOpportunities;
using Gw2Tp.Analytics.OrderBooks;
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

    public DashboardOpportunitiesResponse GetDashboard()
    {
        var analyzedAtUtc = clock.UtcNow;
        var analyzer = new FlipOpportunityAnalyzer(SampleFeePolicy);
        var scorer = new FlipOpportunityScorer(SampleScoringConfiguration);
        var candidates = CreateCandidates(analyzedAtUtc);
        var analysesByItemId = candidates
            .Select(candidate => (candidate, analysis: analyzer.Analyze(candidate.Request)))
            .ToDictionary(result => result.candidate.ItemId, result => result);
        var rankedScores = scorer.Rank(analysesByItemId.Values.Select(result => result.analysis));

        var opportunities = rankedScores
            .Select((score, index) => CreateResponse(score, index + 1, analysesByItemId[score.ItemId]))
            .ToArray();

        return new DashboardOpportunitiesResponse(
            IsSampleData: true,
            SourceDescription: "Deterministic local sample data. No live market scan was performed.",
            GeneratedAtUtc: analyzedAtUtc,
            Opportunities: Array.AsReadOnly(opportunities));
    }

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
            CapturedAtUtc: capturedAtUtc);
    }

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
    DateTimeOffset CapturedAtUtc);
