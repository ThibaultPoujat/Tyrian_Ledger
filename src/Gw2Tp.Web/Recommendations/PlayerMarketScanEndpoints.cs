using Gw2Tp.Application.Recommendations;
using Gw2Tp.Application.Scans;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Web.Recommendations;

internal static class PlayerMarketScanEndpoints
{
    private const string ScanRoute = "/api/recommendations/scan";
    private const long MaximumSafeIntegerCopper = 9_007_199_254_740_991;

    public static IEndpointRouteBuilder MapPlayerMarketScanEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(ScanRoute, (
            StartPlayerMarketScanRequest request,
            IPlayerMarketScanLifecycle lifecycle) =>
        {
            if (!request.TryCreate(out var scanRequest, out var errors))
            {
                return Results.ValidationProblem(errors);
            }

            if (!lifecycle.TryStart(scanRequest, out var startedSnapshot))
            {
                return Results.Conflict(PlayerMarketScanResponse.From(startedSnapshot));
            }

            return Results.Accepted(ScanRoute, PlayerMarketScanResponse.From(startedSnapshot));
        });
        endpoints.MapGet(ScanRoute, (IPlayerMarketScanLifecycle lifecycle) =>
            Results.Ok(PlayerMarketScanResponse.From(lifecycle.GetSnapshot())));
        endpoints.MapDelete(ScanRoute, async (IPlayerMarketScanLifecycle lifecycle) =>
        {
            var cancellation = await lifecycle.CancelAsync();
            return cancellation.HadActiveScan
                ? Results.Ok(PlayerMarketScanResponse.From(cancellation.Snapshot))
                : Results.Conflict(PlayerMarketScanResponse.From(cancellation.Snapshot));
        });

        return endpoints;
    }

    private sealed record StartPlayerMarketScanRequest(long CapitalCopper, string? RiskProfile)
    {
        public bool TryCreate(
            out PlayerMarketScanRequest request,
            out Dictionary<string, string[]> errors)
        {
            errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (CapitalCopper is < 0 or > MaximumSafeIntegerCopper)
            {
                errors["capitalCopper"] = ["Capital must be a non-negative JavaScript-safe integer number of copper."];
            }

            var riskProfile = RiskProfile switch
            {
                "cautious" => BeginnerRiskProfile.Cautious,
                "balanced" => BeginnerRiskProfile.Balanced,
                "adventurous" => BeginnerRiskProfile.Adventurous,
                _ => (BeginnerRiskProfile?)null,
            };
            if (riskProfile is null)
            {
                errors["riskProfile"] = ["Risk profile must be cautious, balanced, or adventurous."];
            }

            if (errors.Count > 0 || riskProfile is null)
            {
                request = null!;
                return false;
            }

            request = new PlayerMarketScanRequest(new Money(CapitalCopper), riskProfile.Value);
            return true;
        }
    }

    private sealed record PlayerMarketScanResponse(
        string State,
        PlayerMarketScanProgressResponse? Progress,
        bool IsRetryable,
        CompletedPlayerMarketScanResponse? Result)
    {
        public static PlayerMarketScanResponse From(PlayerMarketScanSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            return new PlayerMarketScanResponse(
                ToResponseValue(snapshot.State),
                snapshot.Progress is { } progress
                    ? new PlayerMarketScanProgressResponse(
                        ToResponseValue(progress.Stage),
                        progress.FinalistCount)
                    : null,
                snapshot.IsRetryable,
                snapshot.Result is { } result
                    ? CompletedPlayerMarketScanResponse.From(result)
                    : null);
        }

        private static string ToResponseValue(PlayerMarketScanState state) => state switch
        {
            PlayerMarketScanState.Idle => "idle",
            PlayerMarketScanState.Running => "running",
            PlayerMarketScanState.Complete => "complete",
            PlayerMarketScanState.Cancelled => "cancelled",
            PlayerMarketScanState.RateLimited => "rate-limited",
            PlayerMarketScanState.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "The scan state is not supported."),
        };

        private static string ToResponseValue(PlayerMarketScanStage stage) => stage switch
        {
            PlayerMarketScanStage.DiscoveringPriceItemIds => "discovering-price-item-ids",
            PlayerMarketScanStage.DiscoveringAggregatePrices => "discovering-aggregate-prices",
            PlayerMarketScanStage.ScreeningCandidates => "screening-candidates",
            PlayerMarketScanStage.ReadingFinalistListings => "reading-finalist-listings",
            PlayerMarketScanStage.ReadingFinalistMetadata => "reading-finalist-metadata",
            PlayerMarketScanStage.CalculatingRecommendations => "calculating-recommendations",
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "The scan stage is not supported."),
        };
    }

    private sealed record PlayerMarketScanProgressResponse(string Stage, int? FinalistCount);

    private sealed record CompletedPlayerMarketScanResponse(
        long CapitalCopper,
        string RiskProfile,
        long SpendCapCopper,
        DateTimeOffset ScanCompletedAtUtc,
        IReadOnlyList<BeginnerRecommendationResponse> CanActNow,
        IReadOnlyList<BeginnerRecommendationResponse> PlaceOrderAndWait)
    {
        public static CompletedPlayerMarketScanResponse From(BeginnerRecommendationResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            return new CompletedPlayerMarketScanResponse(
                result.Capital.Copper,
                ToResponseValue(result.RiskProfile),
                result.SpendCap.Copper,
                result.ScanCompletedAtUtc,
                result.CanActNow.Select(BeginnerRecommendationResponse.From).ToArray(),
                result.PlaceOrderAndWait.Select(BeginnerRecommendationResponse.From).ToArray());
        }

        private static string ToResponseValue(BeginnerRiskProfile profile) => profile switch
        {
            BeginnerRiskProfile.Cautious => "cautious",
            BeginnerRiskProfile.Balanced => "balanced",
            BeginnerRiskProfile.Adventurous => "adventurous",
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "The risk profile is not supported."),
        };
    }

    private sealed record BeginnerRecommendationResponse(
        int Rank,
        int ItemId,
        string ItemName,
        string Route,
        int Quantity,
        long BuyUnitPriceCopper,
        long SaleUnitPriceCopper,
        long BuyOrderReserveCopper,
        long GrossSaleCopper,
        long ListingFeeCopper,
        long ExchangeFeeCopper,
        long NetSaleProceedsCopper,
        long TotalCostCopper,
        long ModeledProfitCopper,
        ExactRoiResponse ModeledRoi,
        DateTimeOffset ScanCompletedAtUtc,
        BeginnerRecommendationRouteEvidenceResponse RouteEvidence,
        IReadOnlyList<string> Assumptions)
    {
        public static BeginnerRecommendationResponse From(BeginnerRecommendation recommendation)
        {
            ArgumentNullException.ThrowIfNull(recommendation);

            return new BeginnerRecommendationResponse(
                recommendation.Rank,
                recommendation.ItemId,
                recommendation.ItemName,
                ToResponseValue(recommendation.Route),
                recommendation.Quantity,
                recommendation.BuyUnitPrice.Copper,
                recommendation.SaleUnitPrice.Copper,
                recommendation.BuyOrderReserve.Copper,
                recommendation.GrossSale.Copper,
                recommendation.ListingFee.Copper,
                recommendation.ExchangeFee.Copper,
                recommendation.NetSaleProceeds.Copper,
                recommendation.TotalCost.Copper,
                recommendation.ModeledProfit.Copper,
                new ExactRoiResponse(
                    recommendation.ModeledRoi.Profit.Copper,
                    recommendation.ModeledRoi.TotalCost.Copper),
                recommendation.ScanCompletedAtUtc,
                new BeginnerRecommendationRouteEvidenceResponse(
                    recommendation.RouteEvidence.SellerQuantityAtOrBelowBuyPrice,
                    recommendation.RouteEvidence.CoversSelectedQuantity),
                recommendation.Assumptions.Select(ToResponseValue).ToArray());
        }

        private static string ToResponseValue(BeginnerRecommendationRoute route) => route switch
        {
            BeginnerRecommendationRoute.CanActNow => "can-act-now",
            BeginnerRecommendationRoute.PlaceOrderAndWait => "place-order-and-wait",
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, "The recommendation route is not supported."),
        };

        private static string ToResponseValue(BeginnerRecommendationAssumption assumption) => assumption switch
        {
            BeginnerRecommendationAssumption.CurrentOrderBookSnapshotOnly => "current-order-book-snapshot-only",
            BeginnerRecommendationAssumption.CurrentOrderBookDepthAndSpreadGuard => "current-order-book-depth-and-spread-guard",
            BeginnerRecommendationAssumption.ManualInGameOrdersRequired => "manual-in-game-orders-required",
            BeginnerRecommendationAssumption.NoExecutionSaleOrProfitGuarantee => "no-execution-sale-or-profit-guarantee",
            BeginnerRecommendationAssumption.FeeRoundingPendingExternalVerification => "fee-rounding-pending-external-verification",
            _ => throw new ArgumentOutOfRangeException(nameof(assumption), assumption, "The recommendation assumption is not supported."),
        };
    }

    private sealed record ExactRoiResponse(long ProfitCopper, long TotalCostCopper);

    private sealed record BeginnerRecommendationRouteEvidenceResponse(
        long SellerQuantityAtOrBelowBuyPrice,
        bool CoversSelectedQuantity);
}
