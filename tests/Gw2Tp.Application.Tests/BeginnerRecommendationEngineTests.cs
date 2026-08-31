using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Recommendations;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class BeginnerRecommendationEngineTests
{
    private static readonly DateTimeOffset ScanTime = new(2026, 8, 31, 15, 30, 0, TimeSpan.FromHours(2));
    private readonly BeginnerRecommendationEngine engine = new();

    public static TheoryData<BeginnerRiskProfile, long, long, int, long, long, long> ProfileCases => new()
    {
        { BeginnerRiskProfile.Cautious, 22_000, 2_200, 2, 2_200, 1_400, 200 },
        { BeginnerRiskProfile.Balanced, 17_600, 4_400, 4, 4_400, 2_800, 400 },
        { BeginnerRiskProfile.Adventurous, 17_600, 8_800, 8, 8_800, 5_600, 800 },
    };

    [Theory]
    [MemberData(nameof(ProfileCases))]
    public void Calculates_profile_caps_quantities_and_integer_copper_breakdowns(
        BeginnerRiskProfile profile,
        long capitalCopper,
        long expectedSpendCapCopper,
        int expectedQuantity,
        long expectedTotalCostCopper,
        long expectedProfitCopper,
        long expectedListingFeeCopper)
    {
        var result = engine.Calculate(Request(capitalCopper, profile, StandardCandidate()));

        var recommendation = Assert.Single(result.Recommendations);
        Assert.Equal(new Money(expectedSpendCapCopper), result.SpendCap);
        Assert.Equal(expectedQuantity, recommendation.Quantity);
        Assert.Equal(new Money(1_000), recommendation.BuyUnitPrice);
        Assert.Equal(new Money(2_000), recommendation.SaleUnitPrice);
        Assert.Equal(new Money(expectedTotalCostCopper), recommendation.TotalCost);
        Assert.Equal(new Money(expectedProfitCopper), recommendation.ModeledProfit);
        Assert.Equal(new Money(expectedListingFeeCopper), recommendation.ListingFee);
        Assert.Equal(new Money(expectedListingFeeCopper * 2), recommendation.ExchangeFee);
        Assert.Equal(ScanTime.ToUniversalTime(), recommendation.ScanCompletedAtUtc);
        Assert.Equal(BeginnerRecommendationRoute.PlaceOrderAndWait, recommendation.Route);
        Assert.Contains(BeginnerRecommendationAssumption.FeeRoundingPendingExternalVerification, recommendation.Assumptions);
    }

    [Theory]
    [InlineData(22_350, 1)]
    [InlineData(22_360, 2)]
    public void Chooses_the_largest_whole_quantity_that_fits_the_full_up_front_spend_cap(
        long capitalCopper,
        int expectedQuantity)
    {
        var result = engine.Calculate(Request(
            capitalCopper,
            BeginnerRiskProfile.Cautious,
            Candidate(itemId: 1, bestBuyerPrice: 999, cheapestSellerPrice: 2_355)));

        Assert.Equal(expectedQuantity, Assert.Single(result.Recommendations).Quantity);
    }

    [Fact]
    public void Never_exceeds_the_fixed_normal_stack_limit()
    {
        var result = engine.Calculate(Request(3_000_000, BeginnerRiskProfile.Cautious, StandardCandidate()));

        var recommendation = Assert.Single(result.Recommendations);
        Assert.Equal(MarketItemStackPolicy.NormalStackLimit, recommendation.Quantity);
        Assert.Equal(275_000, recommendation.TotalCost.Copper);
    }

    [Fact]
    public void Enforces_the_minimum_profit_boundary_without_rounding_money()
    {
        var belowProfitFloor = Candidate(itemId: 1, bestBuyerPrice: 999, cheapestSellerPrice: 2_354);
        var atProfitFloor = Candidate(itemId: 2, bestBuyerPrice: 999, cheapestSellerPrice: 2_355);

        var belowResult = engine.Calculate(Request(11_180, BeginnerRiskProfile.Cautious, belowProfitFloor));
        var atResult = engine.Calculate(Request(11_180, BeginnerRiskProfile.Cautious, atProfitFloor));

        Assert.Empty(belowResult.Recommendations);
        Assert.Equal(new Money(1_000), Assert.Single(atResult.Recommendations).ModeledProfit);
    }

    [Fact]
    public void Enforces_the_exact_minimum_roi_boundary_against_full_up_front_cost()
    {
        var belowRoiFloor = Candidate(itemId: 1, bestBuyerPrice: 19_999, cheapestSellerPrice: 24_709);

        var result = engine.Calculate(Request(212_360, BeginnerRiskProfile.Cautious, belowRoiFloor));

        Assert.Empty(result.Recommendations);
    }

    [Fact]
    public void Compares_roi_thresholds_as_exact_integer_copper_ratios()
    {
        var atThreshold = new ExactRoi(new Money(50), new Money(1_000));
        var belowThreshold = new ExactRoi(new Money(49), new Money(1_000));

        Assert.True(atThreshold.MeetsOrExceedsBasisPoints(500));
        Assert.False(belowThreshold.MeetsOrExceedsBasisPoints(500));
        Assert.Equal(1_999, BeginnerRecommendationPolicy
            .CalculateSpendCap(new Money(19_999), BeginnerRiskProfile.Cautious)
            .Copper);
    }

    [Fact]
    public void Prescribes_one_copper_over_the_best_buyer_and_one_copper_under_the_cheapest_seller()
    {
        var result = engine.Calculate(Request(22_000, BeginnerRiskProfile.Cautious, StandardCandidate()));

        var recommendation = Assert.Single(result.Recommendations);
        Assert.Equal(new Money(1_000), recommendation.BuyUnitPrice);
        Assert.Equal(new Money(2_000), recommendation.SaleUnitPrice);
        Assert.Equal(0, recommendation.RouteEvidence.SellerQuantityAtOrBelowBuyPrice);
        Assert.False(recommendation.RouteEvidence.CoversSelectedQuantity);
    }

    [Fact]
    public void Rejects_invalid_unavailable_and_zero_quantity_market_inputs()
    {
        var unavailable = new BeginnerRecommendationCandidate(
            Metadata(1),
            new MarketListing(1, [], [Level(1, 2_001)]));
        var nonPositiveLevel = new BeginnerRecommendationCandidate(
            Metadata(2),
            new MarketListing(2, [new MarketOrderLevel(1, 0, 999)], [Level(1, 2_001)]));
        var nonPositiveSalePrice = Candidate(itemId: 3, bestBuyerPrice: 999, cheapestSellerPrice: 1);
        var overflowingOverbid = Candidate(itemId: 4, bestBuyerPrice: int.MaxValue, cheapestSellerPrice: 2_001);
        var wrongStackPolicy = new BeginnerRecommendationCandidate(
            new MarketItemMetadata(5, "Wrong stack", MarketItemStackPolicy.NormalStackLimit - 1),
            new MarketListing(5, [Level(100, 999)], [Level(100, 2_001)]));

        var invalidResult = engine.Calculate(Request(22_000, BeginnerRiskProfile.Cautious,
            unavailable,
            nonPositiveLevel,
            nonPositiveSalePrice,
            overflowingOverbid,
            wrongStackPolicy));
        var zeroCapitalResult = engine.Calculate(Request(0, BeginnerRiskProfile.Cautious, StandardCandidate()));

        Assert.Empty(invalidResult.Recommendations);
        Assert.Empty(zeroCapitalResult.Recommendations);
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Calculate(Request(
            22_000,
            (BeginnerRiskProfile)42,
            StandardCandidate())));
    }

    [Fact]
    public void Rejects_crossing_seller_depth_as_unprofitable_under_the_selected_price_policy()
    {
        var crossingDepth = Candidate(
            itemId: 1,
            bestBuyerPrice: 999,
            cheapestSellerPrice: 1_000,
            cheapestSellerQuantity: 10);

        var result = engine.Calculate(Request(22_000, BeginnerRiskProfile.Cautious, crossingDepth));

        Assert.Empty(result.Recommendations);
        Assert.Empty(result.CanActNow);
    }

    [Fact]
    public void Rejects_duplicate_item_inputs_to_keep_results_independent()
    {
        Assert.Throws<ArgumentException>(() => engine.Calculate(Request(
            22_000,
            BeginnerRiskProfile.Cautious,
            StandardCandidate(itemId: 1),
            StandardCandidate(itemId: 1))));
    }

    [Fact]
    public void Ranks_equal_recommendations_stably_by_item_id_and_limits_the_global_result_set()
    {
        var result = engine.Calculate(Request(
            22_000,
            BeginnerRiskProfile.Cautious,
            StandardCandidate(itemId: 6),
            StandardCandidate(itemId: 3),
            StandardCandidate(itemId: 1),
            StandardCandidate(itemId: 5),
            StandardCandidate(itemId: 2),
            StandardCandidate(itemId: 4)));

        Assert.Equal([1, 2, 3, 4, 5], result.Recommendations.Select(recommendation => recommendation.ItemId));
        Assert.Equal([1, 2, 3, 4, 5], result.Recommendations.Select(recommendation => recommendation.Rank));
    }

    [Fact]
    public void Ranks_modeled_profit_before_exact_roi()
    {
        var lowerRoiHigherProfit = Candidate(
            itemId: 1,
            bestBuyerPrice: 89_999,
            cheapestSellerPrice: 114_121);
        var higherRoiLowerProfit = Candidate(
            itemId: 2,
            bestBuyerPrice: 54_999,
            cheapestSellerPrice: 71_801);

        var result = engine.Calculate(Request(
            1_000_000,
            BeginnerRiskProfile.Cautious,
            higherRoiLowerProfit,
            lowerRoiHigherProfit));

        Assert.Equal([1, 2], result.Recommendations.Select(recommendation => recommendation.ItemId));
        Assert.True(result.Recommendations[0].ModeledProfit.Copper > result.Recommendations[1].ModeledProfit.Copper);
        Assert.True(result.Recommendations[0].ModeledRoi.CompareTo(result.Recommendations[1].ModeledRoi) < 0);
    }

    [Fact]
    public void Requires_only_in_memory_input_and_exposes_no_floating_point_money_contract()
    {
        var constructor = Assert.Single(typeof(BeginnerRecommendationEngine).GetConstructors());
        var result = engine.Calculate(Request(22_000, BeginnerRiskProfile.Cautious, StandardCandidate()));
        var recommendation = Assert.Single(result.Recommendations);
        var publicPropertyTypes = typeof(BeginnerRecommendation).GetProperties()
            .Select(property => property.PropertyType)
            .Append(typeof(ExactRoi));

        Assert.Empty(constructor.GetParameters());
        Assert.All(
            publicPropertyTypes,
            type => Assert.DoesNotContain(type, new[] { typeof(float), typeof(double), typeof(decimal) }));
        Assert.Equal(new Money(2_200), recommendation.TotalCost);
    }

    private static BeginnerRecommendationRequest Request(
        long capitalCopper,
        BeginnerRiskProfile profile,
        params BeginnerRecommendationCandidate[] candidates) => new(
        new Money(capitalCopper),
        profile,
        ScanTime,
        candidates);

    private static BeginnerRecommendationCandidate StandardCandidate(int itemId = 1) => Candidate(
        itemId,
        bestBuyerPrice: 999,
        cheapestSellerPrice: 2_001);

    private static BeginnerRecommendationCandidate Candidate(
        int itemId,
        int bestBuyerPrice,
        int cheapestSellerPrice,
        int cheapestSellerQuantity = 100) => new(
        Metadata(itemId),
        new MarketListing(
            itemId,
            [Level(100, bestBuyerPrice)],
            [Level(cheapestSellerQuantity, cheapestSellerPrice)]));

    private static MarketItemMetadata Metadata(int itemId) => new(
        itemId,
        $"Item {itemId}",
        MarketItemStackPolicy.NormalStackLimit);

    private static MarketOrderLevel Level(int quantity, int unitPriceInCopper) => new(
        Listings: 1,
        Quantity: quantity,
        UnitPriceInCopper: unitPriceInCopper);
}
