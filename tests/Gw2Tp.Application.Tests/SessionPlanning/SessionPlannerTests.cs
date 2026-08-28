using Gw2Tp.Analytics.FlipOpportunities;
using Gw2Tp.Application.Preferences;
using Gw2Tp.Application.SessionPlanning;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Application.Tests.SessionPlanning;

public sealed class SessionPlannerTests
{
    private static readonly FlipOpportunityScoringConfiguration ScoringConfiguration = new(
        targetNetProfit: new Money(100),
        targetReturnOnInvestmentBasisPoints: 1_000,
        acceptablePriceImpactBasisPoints: 1_000,
        weights: new OpportunityScoringWeights(1, 1, 1, 1, 1, 1),
        freshDataScoreBasisPoints: 10_000,
        staleDataScoreBasisPoints: 0,
        normalConfidenceRiskScoreBasisPoints: 10_000,
        reducedConfidenceRiskScoreBasisPoints: 0,
        twoLegFlipComplexityScoreBasisPoints: 10_000);

    private readonly SessionPlanner planner = new();

    [Fact]
    public void Applies_saved_constraints_and_returns_the_remaining_candidates_in_deterministic_order()
    {
        var preferences = UserSessionPreferences.Create(
            capitalLimitCopper: 1_000,
            minimumProfitCopper: 400,
            riskPreference: OpportunityRiskPreference.Normal,
            strategyPreference: OpportunityStrategyPreference.MarketFlip,
            allocationPercent: 50);
        SessionPlanCandidate[] candidates =
        [
            CreateCandidate(900_004, 7_500, 300, 600, FlipOpportunityConfidence.Normal, SessionEffortCategory.Medium),
            CreateCandidate(900_001, 8_000, 500, 500, FlipOpportunityConfidence.Normal, SessionEffortCategory.VeryLow),
            CreateCandidate(900_002, 9_000, 600, 900, FlipOpportunityConfidence.Normal, SessionEffortCategory.High),
            CreateCandidate(900_003, 9_500, 400, 700, FlipOpportunityConfidence.Reduced, SessionEffortCategory.Low),
            CreateCandidate(900_005, 8_500, 300, 300, FlipOpportunityConfidence.Normal, SessionEffortCategory.OngoingPatient),
        ];

        var shortlist = planner.CreateShortlist(candidates, preferences);

        Assert.Equal([900_001, 900_004], shortlist.Select(entry => entry.Candidate.Score.ItemId));
        Assert.Equal([1, 2], shortlist.Select(entry => entry.Rank));
    }

    [Fact]
    public void Returns_no_candidates_when_every_candidate_exceeds_the_allocated_capital()
    {
        var preferences = UserSessionPreferences.Create(
            capitalLimitCopper: 200,
            minimumProfitCopper: null,
            riskPreference: OpportunityRiskPreference.All,
            strategyPreference: OpportunityStrategyPreference.All,
            allocationPercent: 100);

        var shortlist = planner.CreateShortlist(
        [
            CreateCandidate(900_001, 8_000, 201, 500, FlipOpportunityConfidence.Normal, SessionEffortCategory.Low),
            CreateCandidate(900_002, 7_000, 300, 500, FlipOpportunityConfidence.Reduced, SessionEffortCategory.High),
        ],
        preferences);

        Assert.Empty(shortlist);
    }

    [Theory]
    [InlineData(OpportunityRiskPreference.Normal, 900_001)]
    [InlineData(OpportunityRiskPreference.Reduced, 900_002)]
    public void Filters_candidates_by_the_selected_risk_preference(
        OpportunityRiskPreference riskPreference,
        int expectedItemId)
    {
        var preferences = UserSessionPreferences.Create(
            capitalLimitCopper: null,
            minimumProfitCopper: null,
            riskPreference,
            strategyPreference: OpportunityStrategyPreference.All,
            allocationPercent: 100);

        var shortlist = planner.CreateShortlist(
        [
            CreateCandidate(900_001, 8_000, 100, 500, FlipOpportunityConfidence.Normal, SessionEffortCategory.Low),
            CreateCandidate(900_002, 7_000, 100, 500, FlipOpportunityConfidence.Reduced, SessionEffortCategory.High),
        ],
        preferences);

        var candidate = Assert.Single(shortlist);
        Assert.Equal(expectedItemId, candidate.Candidate.Score.ItemId);
    }

    [Theory]
    [InlineData(SessionEffortCategory.VeryLow, 900_001)]
    [InlineData(SessionEffortCategory.Low, 900_002)]
    [InlineData(SessionEffortCategory.Medium, 900_003)]
    [InlineData(SessionEffortCategory.High, 900_004)]
    [InlineData(SessionEffortCategory.OngoingPatient, 900_005)]
    public void Filters_each_supported_effort_category(
        SessionEffortCategory selectedEffortCategory,
        int expectedItemId)
    {
        var shortlist = planner.CreateShortlist(
        [
            CreateCandidate(900_001, 5_000, 100, 500, FlipOpportunityConfidence.Normal, SessionEffortCategory.VeryLow),
            CreateCandidate(900_002, 6_000, 100, 500, FlipOpportunityConfidence.Normal, SessionEffortCategory.Low),
            CreateCandidate(900_003, 7_000, 100, 500, FlipOpportunityConfidence.Normal, SessionEffortCategory.Medium),
            CreateCandidate(900_004, 8_000, 100, 500, FlipOpportunityConfidence.Normal, SessionEffortCategory.High),
            CreateCandidate(900_005, 9_000, 100, 500, FlipOpportunityConfidence.Normal, SessionEffortCategory.OngoingPatient),
        ],
        UserSessionPreferences.Default,
        selectedEffortCategory);

        var candidate = Assert.Single(shortlist);
        Assert.Equal(expectedItemId, candidate.Candidate.Score.ItemId);
    }

    [Fact]
    public void Uses_score_then_existing_score_tie_breakers_for_shortlist_order()
    {
        var shortlist = planner.CreateShortlist(
        [
            CreateCandidate(900_003, 8_000, 100, 500, FlipOpportunityConfidence.Normal, SessionEffortCategory.Low),
            CreateCandidate(900_001, 8_000, 100, 500, FlipOpportunityConfidence.Normal, SessionEffortCategory.Low),
            CreateCandidate(900_002, 9_000, 100, 500, FlipOpportunityConfidence.Normal, SessionEffortCategory.Low),
        ],
        UserSessionPreferences.Default);

        Assert.Equal([900_002, 900_001, 900_003], shortlist.Select(entry => entry.Candidate.Score.ItemId));
    }

    private static SessionPlanCandidate CreateCandidate(
        int itemId,
        int scoreBasisPoints,
        long capitalRequiredCopper,
        long netProfitCopper,
        FlipOpportunityConfidence confidence,
        SessionEffortCategory effortCategory)
    {
        var capitalRequired = new Money(capitalRequiredCopper);
        var netProfit = new Money(netProfitCopper);
        var score = new FlipOpportunityScore(
            itemId,
            requestedQuantity: 1,
            scoreBasisPoints,
            new OpportunityScoreExplanation(
                ScoringConfiguration,
                netProfit,
                capitalRequired,
                new ExactReturnOnInvestment(netProfit, capitalRequired),
                Money.Zero,
                isStale: false,
                confidence,
                transactionLegCount: 2,
                contributions: []));

        return new SessionPlanCandidate(
            score,
            OpportunityStrategyPreference.MarketFlip,
            effortCategory);
    }
}
