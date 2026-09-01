using System.Globalization;
using System.Text.Json;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketSnapshots;
using Gw2Tp.Application.Recommendations;
using Gw2Tp.Domain.Finance;
using Gw2Tp.Testing;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class BrowserRecommendationGoldenVectorTests
{
    [Fact]
    public async Task Shared_browser_vectors_match_the_authoritative_csharp_recommendation_policy()
    {
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var fixtureLoader = new JsonFixtureLoader(fixtureRoot);
        using var json = await fixtureLoader.LoadAsync("recommendations/browser-recommendation-golden-v1.json");
        var vectors = json.RootElement.Deserialize<GoldenVectorDocument>(new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(vectors);
        Assert.Equal(1, vectors.FormatVersion);

        var feePolicy = BeginnerRecommendationFeePolicy.Create();
        foreach (var vector in vectors.FeeVectors)
        {
            var fees = feePolicy.CalculateFees(new Money(ParseCopper(vector.GrossSaleCopper)));
            Assert.Equal(vector.ListingFeeCopper, fees.ListingFee.Copper.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(vector.ExchangeFeeCopper, fees.ExchangeFee.Copper.ToString(CultureInfo.InvariantCulture));
        }

        var engine = new BeginnerRecommendationEngine();
        foreach (var vector in vectors.RecommendationVectors)
        {
            vector.Snapshot.Validate();
            var riskProfile = Enum.Parse<BeginnerRiskProfile>(vector.RiskProfile, ignoreCase: true);
            var generatedAtUtc = DateTimeOffset.Parse(
                vector.Snapshot.GeneratedAtUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            var request = new BeginnerRecommendationRequest(
                new Money(ParseCopper(vector.CapitalCopper)),
                riskProfile,
                generatedAtUtc,
                vector.Snapshot.Candidates.Select(candidate => ToRecommendationCandidate(
                    candidate,
                    vector.Snapshot.Compatibility.NormalStackLimit)).ToArray());

            var result = engine.Calculate(request);
            Assert.Equal(vector.CapitalCopper, result.Capital.Copper.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(riskProfile, result.RiskProfile);
            Assert.Equal(vector.Expected.SpendCapCopper, result.SpendCap.Copper.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(vector.Expected.Recommendations.Count, result.Recommendations.Count);
            Assert.Equal(generatedAtUtc.ToUniversalTime(), result.ScanCompletedAtUtc);
            Assert.All(result.Recommendations, recommendation =>
                Assert.Equal(generatedAtUtc.ToUniversalTime(), recommendation.ScanCompletedAtUtc));

            for (var index = 0; index < vector.Expected.Recommendations.Count; index++)
            {
                AssertRecommendation(vector.Expected.Recommendations[index], result.Recommendations[index]);
            }
        }
    }

    private static BeginnerRecommendationCandidate ToRecommendationCandidate(
        MarketSnapshotCandidate candidate,
        int normalStackLimit) => new(
        new MarketItemMetadata(candidate.ItemId, candidate.ItemName, normalStackLimit),
        new MarketListing(
            candidate.ItemId,
            candidate.Buys.Select(level => new MarketOrderLevel(
                level.ListingCount,
                level.Quantity,
                level.UnitPriceInCopper)).ToArray(),
            candidate.Sells.Select(level => new MarketOrderLevel(
                level.ListingCount,
                level.Quantity,
                level.UnitPriceInCopper)).ToArray()));

    private static void AssertRecommendation(GoldenRecommendation expected, BeginnerRecommendation actual)
    {
        Assert.Equal(expected.Rank, actual.Rank);
        Assert.Equal(expected.ItemId, actual.ItemId);
        Assert.Equal(expected.ItemName, actual.ItemName);
        Assert.Equal(expected.Route, ToFixtureValue(actual.Route));
        Assert.Equal(expected.Quantity, actual.Quantity.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(expected.BuyUnitPriceCopper, actual.BuyUnitPrice.Copper.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(expected.SaleUnitPriceCopper, actual.SaleUnitPrice.Copper.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(expected.BuyOrderReserveCopper, actual.BuyOrderReserve.Copper.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(expected.GrossSaleCopper, actual.GrossSale.Copper.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(expected.ListingFeeCopper, actual.ListingFee.Copper.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(expected.ExchangeFeeCopper, actual.ExchangeFee.Copper.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(expected.NetSaleProceedsCopper, actual.NetSaleProceeds.Copper.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(expected.TotalCostCopper, actual.TotalCost.Copper.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(expected.ModeledProfitCopper, actual.ModeledProfit.Copper.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(expected.ModeledProfitCopper, actual.ModeledRoi.Profit.Copper.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(expected.TotalCostCopper, actual.ModeledRoi.TotalCost.Copper.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(
            expected.SellerQuantityAtOrBelowBuyPrice,
            actual.RouteEvidence.SellerQuantityAtOrBelowBuyPrice.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(expected.CoversSelectedQuantity, actual.RouteEvidence.CoversSelectedQuantity);
        Assert.Equal(
            [
                "current-order-book-snapshot-only",
                "current-order-book-depth-and-spread-guard",
                "manual-in-game-orders-required",
                "no-execution-sale-or-profit-guarantee",
                "fee-rounding-pending-external-verification",
            ],
            actual.Assumptions.Select(ToFixtureValue));
    }

    private static long ParseCopper(string value) => long.Parse(value, CultureInfo.InvariantCulture);

    private static string ToFixtureValue(BeginnerRecommendationRoute route) => route switch
    {
        BeginnerRecommendationRoute.CanActNow => "can-act-now",
        BeginnerRecommendationRoute.PlaceOrderAndWait => "place-order-and-wait",
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, "The recommendation route is not supported."),
    };

    private static string ToFixtureValue(BeginnerRecommendationAssumption assumption) => assumption switch
    {
        BeginnerRecommendationAssumption.CurrentOrderBookSnapshotOnly => "current-order-book-snapshot-only",
        BeginnerRecommendationAssumption.CurrentOrderBookDepthAndSpreadGuard => "current-order-book-depth-and-spread-guard",
        BeginnerRecommendationAssumption.ManualInGameOrdersRequired => "manual-in-game-orders-required",
        BeginnerRecommendationAssumption.NoExecutionSaleOrProfitGuarantee => "no-execution-sale-or-profit-guarantee",
        BeginnerRecommendationAssumption.FeeRoundingPendingExternalVerification => "fee-rounding-pending-external-verification",
        _ => throw new ArgumentOutOfRangeException(nameof(assumption), assumption, "The recommendation assumption is not supported."),
    };

    private sealed record GoldenVectorDocument(
        int FormatVersion,
        IReadOnlyList<GoldenFeeVector> FeeVectors,
        IReadOnlyList<GoldenRecommendationVector> RecommendationVectors);

    private sealed record GoldenFeeVector(
        string Name,
        string GrossSaleCopper,
        string ListingFeeCopper,
        string ExchangeFeeCopper);

    private sealed record GoldenRecommendationVector(
        string Name,
        string CapitalCopper,
        string RiskProfile,
        MarketSnapshotDocument Snapshot,
        GoldenRecommendationExpected Expected);

    private sealed record GoldenRecommendationExpected(
        string SpendCapCopper,
        IReadOnlyList<GoldenRecommendation> Recommendations);

    private sealed record GoldenRecommendation(
        int Rank,
        int ItemId,
        string ItemName,
        string Route,
        string Quantity,
        string BuyUnitPriceCopper,
        string SaleUnitPriceCopper,
        string BuyOrderReserveCopper,
        string GrossSaleCopper,
        string ListingFeeCopper,
        string ExchangeFeeCopper,
        string NetSaleProceedsCopper,
        string TotalCostCopper,
        string ModeledProfitCopper,
        string SellerQuantityAtOrBelowBuyPrice,
        bool CoversSelectedQuantity);
}
