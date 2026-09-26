using System.Globalization;
using Gw2Tp.Application.Finance;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Application.Plans;
using Gw2Tp.Application.Recommendations;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Web.Hosting;

/// <summary>
/// A deliberately small, read-only rendering projection for calculation
/// explanations. It only reads the account-scoped decision-loop projection;
/// it never starts a scan, refresh, market-history request, or plan selection.
/// </summary>
internal static class CalculationExplanationEndpoints
{
    internal static IEndpointRouteBuilder MapCalculationExplanationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/calculation-explanations", async (
            HttpContext context,
            IPersonalTradingPostGateway personalTradingPost,
            PlanDecisionProjectionStore decisions,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var scope = await personalTradingPost.GetAccountScopeAsync(cancellationToken).ConfigureAwait(false);
            if (!scope.IsSuccess || scope.Value is null || !decisions.TryGet(scope.Value.AccountId, out var decision) || decision is null)
            {
                return Results.Json(Unavailable("decision_projection_unavailable"));
            }

            return Results.Json(ToResponse(decision));
        }).WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));

        return endpoints;
    }

    private static object Unavailable(string reason) => new
    {
        version = 1,
        state = "unavailable",
        reason,
        theory = Theory(),
        current = (object?)null,
        actions = Array.Empty<object>(),
        selection = new { generatedCandidates = 0, hardEligibleCandidates = 0, resourceEligibleCandidates = 0, selectedCandidates = 0, unavailableReason = reason },
    };

    internal static object ToResponse(PlanDecisionSnapshot decision)
    {
        var recommendations = decision.Recommendations!;
        var trace = decision.SelectionTrace ?? PlanDecisionSelectionTrace.Unavailable(
            decision.Candidates.Count, "selection_trace_unavailable");
        return new
        {
            version = 1,
            state = "ready",
            theory = Theory(),
            current = new
            {
                generatedAtUtc = recommendations.GeneratedAtUtc,
                cachedAtUtc = decision.CachedAtUtc,
                expiresAtUtc = recommendations.AccountEvidenceExpiresAtUtc,
                policy = new
                {
                    actionVersion = recommendations.Policies.ActionPolicyVersion,
                    scoreVersion = recommendations.Policies.ScorePolicyVersion,
                    positionSizingVersion = recommendations.Policies.PositionSizingPolicyVersion,
                    feeVersion = recommendations.Policies.FeePolicyVersion,
                    fifoVersion = recommendations.Policies.FifoPolicyVersion,
                    cashReserveBasisPoints = recommendations.Policies.CashReserveBasisPoints,
                    minimumProfitCopper = recommendations.Policies.MinimumProfitInCopper.ToString(CultureInfo.InvariantCulture),
                    minimumRoiBasisPoints = recommendations.Policies.MinimumRoiBasisPoints,
                },
                capital = recommendations.Portfolio is null ? null : new
                {
                    availableCash = MoneyResponse.From(recommendations.Portfolio.AvailableCash),
                    totalBankroll = MoneyResponse.From(recommendations.Portfolio.TotalBankroll),
                    cashReserve = MoneyResponse.From(recommendations.Portfolio.CashReserve),
                    reserveStatus = recommendations.Portfolio.ReserveStatus.ToString(),
                    reserveShortfall = MoneyResponse.From(recommendations.Portfolio.CashReserveShortfall),
                    remainingCashAfterSizing = MoneyResponse.From(recommendations.Portfolio.RemainingCashAfterSizing),
                    evidenceState = "known",
                },
                sources = new
                {
                    account = Source(recommendations.LastSuccessfulSyncAtUtc, recommendations.AccountEvidenceExpiresAtUtc),
                    orders = Source(recommendations.CurrentOrdersObservedAtUtc, recommendations.AccountEvidenceExpiresAtUtc),
                    market = Source(recommendations.ScannerObservedAtUtc, recommendations.AccountEvidenceExpiresAtUtc),
                    crafting = new { state = "separate_snapshot", observedAtUtc = (DateTimeOffset?)null, expiresAtUtc = (DateTimeOffset?)null },
                },
            },
            actions = recommendations.Actions.Select(action => Action(action, trace)).ToArray(),
            plans = decision.Plans.Select(plan => new
            {
                plan.Id,
                state = plan.State.ToString(),
                plan.CurrentStepOrdinal,
                candidate = decision.Candidates.FirstOrDefault(candidate => candidate.SourceOpportunityId == plan.SourceOpportunityId) is { } candidate
                    ? Candidate(candidate, trace) : null,
            }).ToArray(),
            craftingCandidates = decision.Candidates.Where(candidate => candidate.Id.StartsWith("craft:", StringComparison.Ordinal))
                .Select(candidate => Candidate(candidate, trace)).ToArray(),
            selection = new
            {
                generatedCandidates = trace.GeneratedCandidates,
                hardEligibleCandidates = trace.HardEligibleCandidates,
                resourceEligibleCandidates = trace.ResourceEligibleCandidates,
                selectedCandidates = trace.SelectedCandidates,
                trace.PortfolioSizingUnavailable,
                trace.UnavailableReason,
            },
        };
    }

    private static object Action(PrimaryRecommendationRecord action, PlanDecisionSelectionTrace trace)
    {
        var candidateId = PlanEndpointService.ToCandidate(action).Id;
        // Deliberately do not return the candidate ID: buy-order candidate IDs
        // contain an internal order identity that the explanation UI does not
        // need. This stable display key contains only the public action/item
        // semantics already shown on the Signal card.
        var explanationId = $"{action.Source}:{action.Action}:{action.ItemId.ToString(CultureInfo.InvariantCulture)}";
        return new
        {
            id = explanationId,
            action = action.Action.ToString(),
            action.ItemId,
            action.ItemName,
            action.Quantity,
            executableSignal = trace.ExecutableSignalCandidateIds.Contains(candidateId),
            excludedFromSelection = trace.ExcludedCandidateIds.Contains(candidateId, StringComparer.Ordinal),
            capital = MoneyResponse.From(action.Capital),
            economics = action.Economics is null ? null : new
            {
                acquisitionCost = MoneyResponse.From(action.Economics.AcquisitionCost),
                grossSaleValue = MoneyResponse.From(action.Economics.GrossSaleValue),
                listingFee = MoneyResponse.From(action.Economics.ListingFee),
                exchangeFee = MoneyResponse.From(action.Economics.ExchangeFee),
                netSaleProceeds = MoneyResponse.From(action.Economics.NetSaleProceeds),
                netProfit = MoneyResponse.From(action.Economics.NetProfit),
                totalCost = MoneyResponse.From(action.Economics.TotalCost),
                action.Economics.RoiDisplayPercent,
                acquisitionBasisState = action.Reasons.Any(reason => reason.Code == PrimaryRecommendationReasonCode.UnknownCostBasis) ? "unknown" : "known",
            },
            constraints = action.PortfolioConstraints.Select(constraint => new
            {
                name = constraint.Name.ToString(),
                capitalCapacity = MoneyResponse.From(constraint.CapitalCapacity),
                constraint.QuantityCapacity,
                constraint.IsBinding,
            }).ToArray(),
            // Display copy is deliberately not copied from the English domain
            // diagnostic message. The browser receives structured semantics only.
            reasons = action.Reasons.Select(reason => new { code = reason.Code.ToString() }).ToArray(),
            evidence = new
            {
                historyObservedAtUtc = action.History?.LastObservedAtUtc,
                historyState = action.History is null ? "unknown" : action.History.Windows.All(window => window.IsAvailable) ? "known" : "partial",
                liquidityState = action.Liquidity is null ? "unknown" : "known",
            },
        };
    }

    private static object Candidate(PlanCandidate candidate, PlanDecisionSelectionTrace trace) => new
    {
        candidate.Id,
        candidate.IsHardEligible,
        candidate.Utility,
        modeledProfit = MoneyResponse.From(candidate.ModeledProfit),
        committedCapital = MoneyResponse.From(candidate.CommittedCapital),
        selected = trace.ExecutableSignalCandidateIds.Contains(candidate.Id),
        excludedFromSelection = trace.ExcludedCandidateIds.Contains(candidate.Id, StringComparer.Ordinal),
        exclusions = candidate.ExclusionReasons,
        requirements = candidate.Requirements.Select(requirement => new
        {
            kind = requirement.Kind.ToString(),
            requirement.Quantity,
            cash = MoneyResponse.From(requirement.Cash),
        }).ToArray(),
    };

    private static object Theory() => new
    {
        positionSizing = new
        {
            version = PositionSizingPolicy.CurrentVersion,
            basisPointsPerWhole = PositionSizingPolicy.BasisPointsPerWhole,
            reserveRounding = "up",
            capRounding = "down",
            purchaseOnly = true,
        },
        fees = new
        {
            version = Gw2TradingPostFeePolicy.PolicyVersion,
            listingFeeBasisPoints = Gw2TradingPostFeePolicy.ListingFeeBasisPoints,
            exchangeFeeBasisPoints = Gw2TradingPostFeePolicy.ExchangeFeeBasisPoints,
            minimumPositiveFeeCopper = Gw2TradingPostFeePolicy.MinimumPositiveFeeCopper.ToString(CultureInfo.InvariantCulture),
            rounding = "up_independently",
            fractionalCopperRoundingExternallyVerified = Gw2TradingPostFeePolicy.IsFractionalCopperRoundingExternallyVerified,
            verification = "VERIFY-013",
        },
        crafting = new
        {
            missingInputPolicy = "unknown_never_zero",
            listingIdentifierPolicy = "VERIFY-004",
        },
    };

    private static object Source(DateTimeOffset? observedAtUtc, DateTimeOffset? expiresAtUtc) => new
    {
        state = observedAtUtc is null ? "unknown" : "known",
        observedAtUtc,
        expiresAtUtc,
    };

    private sealed record MoneyResponse(string Copper)
    {
        internal static MoneyResponse From(Money money) => new(money.Copper.ToString(CultureInfo.InvariantCulture));
    }
}
