using Gw2Tp.Analytics.Finance;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Recommendations;

/// <summary>
/// Turns one complete, in-memory order-book input into a bounded, deterministic set of beginner
/// fast-flip recommendations. This type does not fetch, persist, or retain market data.
/// </summary>
public sealed class BeginnerRecommendationEngine
{
    private static readonly IReadOnlyList<BeginnerRecommendationAssumption> Assumptions = Array.AsReadOnly(
    [
        BeginnerRecommendationAssumption.CurrentOrderBookSnapshotOnly,
        BeginnerRecommendationAssumption.ManualInGameOrdersRequired,
        BeginnerRecommendationAssumption.NoExecutionSaleOrProfitGuarantee,
        BeginnerRecommendationAssumption.FeeRoundingPendingExternalVerification,
    ]);

    private readonly FlipProfitCalculator profitCalculator = new(BeginnerRecommendationFeePolicy.Create());

    /// <summary>
    /// Calculates recommendations from only the supplied current input. Invalid or unavailable
    /// individual market candidates are excluded. Duplicate item inputs are rejected to prevent
    /// input ordering from changing the independent recommendation set.
    /// </summary>
    public BeginnerRecommendationResult Calculate(BeginnerRecommendationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Candidates);

        var profilePolicy = BeginnerRecommendationPolicy.GetRiskProfilePolicy(request.RiskProfile);
        var spendCap = BeginnerRecommendationPolicy.CalculateSpendCap(request.Capital, request.RiskProfile);
        var validCandidates = request.Candidates.Where(HasValidMarketInput).ToArray();
        ValidateDistinctCandidateItems(validCandidates);

        var recommendations = new List<BeginnerRecommendation>();
        foreach (var candidate in validCandidates)
        {
            var recommendation = TryCreateRecommendation(candidate, spendCap, profilePolicy, request.ScanCompletedAtUtc);
            if (recommendation is not null)
            {
                recommendations.Add(recommendation);
            }
        }

        recommendations.Sort(BeginnerRecommendationComparer.Instance);
        var rankedRecommendations = recommendations
            .Take(BeginnerRecommendationPolicy.MaximumRecommendationCount)
            .Select((recommendation, index) => recommendation with { Rank = index + 1 })
            .ToArray();

        return new BeginnerRecommendationResult(
            request.Capital,
            request.RiskProfile,
            spendCap,
            request.ScanCompletedAtUtc.ToUniversalTime(),
            rankedRecommendations);
    }

    private BeginnerRecommendation? TryCreateRecommendation(
        BeginnerRecommendationCandidate? candidate,
        Money spendCap,
        BeginnerRiskProfilePolicy profilePolicy,
        DateTimeOffset scanCompletedAtUtc)
    {
        if (!HasValidMarketInput(candidate))
        {
            return null;
        }

        var item = candidate!.Item;
        var listing = candidate.Listing;
        var bestBuyerPrice = listing.Buys.Max(level => level.UnitPriceInCopper);
        var cheapestSellerPrice = listing.Sells.Min(level => level.UnitPriceInCopper);

        if (bestBuyerPrice == int.MaxValue || cheapestSellerPrice <= 1)
        {
            return null;
        }

        try
        {
            var buyUnitPrice = new Money(checked((long)bestBuyerPrice + 1));
            var saleUnitPrice = new Money(checked((long)cheapestSellerPrice - 1));
            var maximumQuantity = GetMaximumQuantity(item.NormalStackLimit, spendCap, buyUnitPrice);
            if (maximumQuantity == 0)
            {
                return null;
            }

            var metrics = FindLargestAffordableMetrics(maximumQuantity, spendCap, buyUnitPrice, saleUnitPrice);
            if (metrics is null ||
                metrics.ProfitScenario.NetProfit.Copper < profilePolicy.MinimumProfit.Copper ||
                !metrics.ModeledRoi.MeetsOrExceedsBasisPoints(profilePolicy.MinimumRoiBasisPoints))
            {
                return null;
            }

            var sellerQuantityAtOrBelowBuyPrice = CalculateSellerQuantityAtOrBelowBuyPrice(
                listing.Sells,
                buyUnitPrice);
            var routeEvidence = new BeginnerRecommendationRouteEvidence(
                sellerQuantityAtOrBelowBuyPrice,
                sellerQuantityAtOrBelowBuyPrice >= metrics.Quantity);
            var route = routeEvidence.CoversSelectedQuantity
                ? BeginnerRecommendationRoute.CanActNow
                : BeginnerRecommendationRoute.PlaceOrderAndWait;

            return new BeginnerRecommendation(
                Rank: 0,
                item.ItemId,
                item.Name,
                route,
                metrics.Quantity,
                buyUnitPrice,
                saleUnitPrice,
                metrics.BuyOrderReserve,
                metrics.ProfitScenario.GrossSaleValue,
                metrics.ProfitScenario.ListingFee,
                metrics.ProfitScenario.ExchangeFee,
                metrics.ProfitScenario.NetSaleProceeds,
                metrics.TotalCost,
                metrics.ProfitScenario.NetProfit,
                metrics.ModeledRoi,
                scanCompletedAtUtc.ToUniversalTime(),
                routeEvidence,
                Assumptions);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static bool HasValidMarketInput(BeginnerRecommendationCandidate? candidate)
    {
        if (candidate?.Item is null || candidate.Listing is null || candidate.Item.ItemId <= 0 ||
            candidate.Listing.ItemId != candidate.Item.ItemId || string.IsNullOrWhiteSpace(candidate.Item.Name) ||
            candidate.Item.NormalStackLimit != MarketItemStackPolicy.NormalStackLimit ||
            candidate.Listing.Buys is null || candidate.Listing.Sells is null ||
            candidate.Listing.Buys.Count == 0 || candidate.Listing.Sells.Count == 0)
        {
            return false;
        }

        return candidate.Listing.Buys.All(IsValidOrderLevel) && candidate.Listing.Sells.All(IsValidOrderLevel);
    }

    private static bool IsValidOrderLevel(MarketOrderLevel? level) =>
        level is not null && level.Quantity > 0 && level.UnitPriceInCopper > 0 && level.Listings > 0;

    private static int GetMaximumQuantity(int stackLimit, Money spendCap, Money buyUnitPrice)
    {
        if (spendCap.Copper <= 0 || buyUnitPrice.Copper <= 0)
        {
            return 0;
        }

        var quantityByBuyReserve = spendCap.Copper / buyUnitPrice.Copper;
        return (int)Math.Min(stackLimit, quantityByBuyReserve);
    }

    private RecommendationMetrics? FindLargestAffordableMetrics(
        int maximumQuantity,
        Money spendCap,
        Money buyUnitPrice,
        Money saleUnitPrice)
    {
        var lowerBound = 0;
        var upperBound = maximumQuantity;

        while (lowerBound < upperBound)
        {
            var candidateQuantity = lowerBound + ((upperBound - lowerBound + 1) / 2);
            var metrics = CalculateMetrics(candidateQuantity, buyUnitPrice, saleUnitPrice);
            if (metrics.TotalCost.Copper <= spendCap.Copper)
            {
                lowerBound = candidateQuantity;
            }
            else
            {
                upperBound = candidateQuantity - 1;
            }
        }

        return lowerBound == 0
            ? null
            : CalculateMetrics(lowerBound, buyUnitPrice, saleUnitPrice);
    }

    private RecommendationMetrics CalculateMetrics(int quantity, Money buyUnitPrice, Money saleUnitPrice)
    {
        var buyOrderReserve = new Money(checked(buyUnitPrice.Copper * quantity));
        var grossSale = new Money(checked(saleUnitPrice.Copper * quantity));
        var profitScenario = profitCalculator.Calculate(buyOrderReserve, grossSale);
        var totalCost = buyOrderReserve + profitScenario.ListingFee;

        return new RecommendationMetrics(
            quantity,
            buyOrderReserve,
            totalCost,
            profitScenario,
            new ExactRoi(profitScenario.NetProfit, totalCost));
    }

    private static long CalculateSellerQuantityAtOrBelowBuyPrice(
        IReadOnlyList<MarketOrderLevel> sellerLevels,
        Money buyUnitPrice)
    {
        var quantity = 0L;
        foreach (var level in sellerLevels)
        {
            if (level.UnitPriceInCopper <= buyUnitPrice.Copper)
            {
                quantity = checked(quantity + level.Quantity);
            }
        }

        return quantity;
    }

    private static void ValidateDistinctCandidateItems(IReadOnlyList<BeginnerRecommendationCandidate> candidates)
    {
        var itemIds = new HashSet<int>();
        foreach (var candidate in candidates)
        {
            if (candidate?.Item is not null && !itemIds.Add(candidate.Item.ItemId))
            {
                throw new ArgumentException(
                    $"The current market input contains duplicate item ID {candidate.Item.ItemId}.",
                    nameof(candidates));
            }
        }
    }

    private sealed record RecommendationMetrics(
        int Quantity,
        Money BuyOrderReserve,
        Money TotalCost,
        FlipProfitScenario ProfitScenario,
        ExactRoi ModeledRoi);

    private sealed class BeginnerRecommendationComparer : IComparer<BeginnerRecommendation>
    {
        public static BeginnerRecommendationComparer Instance { get; } = new();

        public int Compare(BeginnerRecommendation? left, BeginnerRecommendation? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return 1;
            }

            if (right is null)
            {
                return -1;
            }

            var profitComparison = right.ModeledProfit.Copper.CompareTo(left.ModeledProfit.Copper);
            if (profitComparison != 0)
            {
                return profitComparison;
            }

            var roiComparison = right.ModeledRoi.CompareTo(left.ModeledRoi);
            if (roiComparison != 0)
            {
                return roiComparison;
            }

            var costComparison = left.TotalCost.Copper.CompareTo(right.TotalCost.Copper);
            return costComparison != 0
                ? costComparison
                : left.ItemId.CompareTo(right.ItemId);
        }
    }
}
