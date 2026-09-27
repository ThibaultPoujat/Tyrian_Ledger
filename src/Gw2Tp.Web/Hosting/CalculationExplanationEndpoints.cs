using System.Globalization;
using Gw2Tp.Application.Crafting;
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
                capitalState = recommendations.Portfolio is null
                    ? recommendations.EvidenceError == "buy_sizing_unavailable" ? "purchase_sizing_unavailable" : "unknown"
                    : "known",
                capital = recommendations.Portfolio is null ? null : new
                {
                    availableCash = MoneyResponse.From(recommendations.Portfolio.AvailableCash),
                    totalBankroll = MoneyResponse.From(recommendations.Portfolio.TotalBankroll),
                    cashReserve = MoneyResponse.From(recommendations.Portfolio.CashReserve),
                    reserveStatus = recommendations.Portfolio.ReserveStatus.ToString(),
                    reserveShortfall = MoneyResponse.From(recommendations.Portfolio.CashReserveShortfall),
                    remainingCashAfterSizing = MoneyResponse.From(recommendations.Portfolio.RemainingCashAfterSizing),
                    evidenceState = "known",
                    exposures = (recommendations.Portfolio.ExistingExposures ?? []).Select(exposure => new
                    {
                        kind = exposure.Kind.ToString(),
                        exposure.ItemId,
                        exposure.Strategy,
                        exposure.Category,
                        capital = MoneyResponse.From(exposure.CapitalAtRisk),
                    }).ToArray(),
                },
                sources = new
                {
                    account = Source(recommendations.LastSuccessfulSyncAtUtc, recommendations.AccountEvidenceExpiresAtUtc),
                    orders = Source(recommendations.CurrentOrdersObservedAtUtc, recommendations.AccountEvidenceExpiresAtUtc),
                    market = Source(recommendations.ScannerObservedAtUtc, recommendations.AccountEvidenceExpiresAtUtc),
                    crafting = new { state = CraftingSourceState(decision.Crafting), observedAtUtc = decision.Crafting?.AccountEvidenceCapturedAtUtc, expiresAtUtc = (DateTimeOffset?)null },
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
            craftingCandidates = (decision.Crafting?.Opportunities ?? []).Select(opportunity => Crafting(opportunity, trace)).ToArray(),
            crafting = decision.Crafting is null ? new { state = "unavailable", evidenceFailureCode = (string?)null, summaryExclusions = Array.Empty<string>(), truncationReasons = Array.Empty<string>() } : new
            {
                state = decision.Crafting.State.ToString(),
                evidenceFailureCode = decision.Crafting.EvidenceFailureCode,
                summaryExclusions = decision.Crafting.SummaryExclusions.Select(value => value.ToString()).ToArray(),
                truncationReasons = decision.Crafting.TruncationReasons.Select(value => value.ToString()).ToArray(),
            },
            selection = new
            {
                generatedCandidates = trace.GeneratedCandidates,
                hardEligibleCandidates = trace.HardEligibleCandidates,
                resourceEligibleCandidates = trace.ResourceEligibleCandidates,
                selectedCandidates = trace.SelectedCandidates,
                trace.PortfolioSizingUnavailable,
                trace.UnavailableReason,
                resources = trace.Resources is null ? null : new
                {
                    effectiveCash = MoneyResponse.From(trace.Resources.EffectiveCash),
                    reservedCash = MoneyResponse.From(trace.Resources.ReservedCash),
                    availableCash = MoneyResponse.From(trace.Resources.AvailableCash),
                    hardReserve = MoneyResponse.From(trace.Resources.HardReserve),
                    reservations = trace.Resources.Reservations.Select(Resource).ToArray(),
                    availableVerifiedInventory = trace.Resources.AvailableQuantities
                        .Where(pair => pair.Key.StartsWith($"{(int)PlanResourceKind.Inventory}:", StringComparison.Ordinal))
                        .Select(pair => new { itemId = int.TryParse(pair.Key.AsSpan(2), out var itemId) ? itemId : (int?)null, quantity = pair.Value }).ToArray(),
                },
            },
        };
    }

    private static object Action(PrimaryRecommendationRecord action, PlanDecisionSelectionTrace trace)
    {
        // Observations such as REVIEW, WAIT, and SKIP intentionally have no
        // executable Plan candidate.  They still need an explanation, but
        // must not be force-converted through the action-only plan mapper.
        var candidateId = PlanEndpointService.IsActionable(action)
            ? PlanEndpointService.ToCandidate(action).Id
            : null;
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
            executableSignal = candidateId is not null && trace.ExecutableSignalCandidateIds.Contains(candidateId),
            excludedFromSelection = candidateId is not null && trace.ExcludedCandidateIds.Contains(candidateId, StringComparer.Ordinal),
            selection = candidateId is null ? null : CandidateTrace(candidateId, trace),
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
        // Only craft candidates have a deliberately public-safe ID. Plan and
        // signal projections link by their already-public plan/action fields.
        id = candidate.Id.StartsWith("craft:", StringComparison.Ordinal) ? candidate.Id : null,
        candidate.IsHardEligible,
        candidate.Utility,
        modeledProfit = MoneyResponse.From(candidate.ModeledProfit),
        committedCapital = MoneyResponse.From(candidate.CommittedCapital),
        selected = CandidateIsSelected(candidate.Id, trace),
        excludedFromSelection = trace.ExcludedCandidateIds.Contains(candidate.Id, StringComparer.Ordinal),
        selection = CandidateTrace(candidate.Id, trace),
        exclusions = candidate.ExclusionReasons,
        requirements = candidate.Requirements.Select(requirement => new
        {
            kind = requirement.Kind.ToString(),
            requirement.Quantity,
            cash = MoneyResponse.From(requirement.Cash),
        }).ToArray(),
    };

    private static object Resource(PlanResourceRequirement requirement) => new
    {
        kind = requirement.Kind.ToString(),
        itemId = requirement.Kind == PlanResourceKind.Inventory && int.TryParse(requirement.ResourceId, out var itemId) ? itemId : (int?)null,
        requirement.Quantity,
        cash = MoneyResponse.From(requirement.Cash),
    };

    private static object? CandidateTrace(string candidateId, PlanDecisionSelectionTrace trace) => trace.Candidates?
        .FirstOrDefault(candidate => candidate.CandidateId == candidateId) is { } detail
            ? new { detail.Stage, detail.Reason } : null;

    private static bool CandidateIsSelected(string candidateId, PlanDecisionSelectionTrace trace) =>
        trace.ExecutableSignalCandidateIds.Contains(candidateId) || trace.Candidates?
            .Any(candidate => candidate.CandidateId == candidateId && candidate.Stage == "selected") == true;

    private static object Crafting(CraftingOpportunity opportunity, PlanDecisionSelectionTrace trace) => new
    {
        opportunity.Id,
        outputItemId = opportunity.Recipe.OutputItemId,
        opportunity.OutputName,
        state = opportunity.Economics.State.ToString(),
        isActionable = opportunity.IsActionable,
        exclusions = opportunity.Exclusions.Select(value => value.ToString()).ToArray(),
        candidate = opportunity.Candidate is null ? null : Candidate(opportunity.Candidate, trace),
        economics = new
        {
            economicInputCost = FromOptional(opportunity.Economics.EconomicInputCost),
            outputSale = opportunity.Economics.OutputSale is null ? null : new
            {
                grossSaleValue = MoneyResponse.From(opportunity.Economics.OutputSale.GrossSaleValue),
                listingFee = MoneyResponse.From(opportunity.Economics.OutputSale.ListingFee),
                exchangeFee = MoneyResponse.From(opportunity.Economics.OutputSale.ExchangeFee),
                netSaleProceeds = MoneyResponse.From(opportunity.Economics.OutputSale.NetSaleProceeds),
            },
            netProfit = FromOptional(opportunity.Economics.NetProfit),
            totalCost = FromOptional(opportunity.Economics.TotalCost),
            roi = opportunity.Economics.ModeledRoi is { } roi ? new
            {
                profit = MoneyResponse.From(roi.Profit),
                totalCost = MoneyResponse.From(roi.TotalCost),
            } : null,
            uncertainties = opportunity.Economics.Uncertainties.Select(value => value.ToString()).ToArray(),
            ingredients = opportunity.Economics.Ingredients.Select(ingredient => new
            {
                ingredient.ItemId,
                ingredient.RequiredQuantity,
                ingredient.OwnedTradableQuantity,
                ingredient.BoundQuantity,
                ingredient.UnknownQuantity,
                ingredient.PurchasedQuantity,
                strategy = ingredient.Strategy.ToString(),
                ownedOpportunityCost = FromOptional(ingredient.OwnedOpportunityCost),
                ownedLiquidation = Evidence(ingredient.OwnedLiquidationEvidence),
                purchasedAcquisitionCost = FromOptional(ingredient.PurchasedAcquisitionCost),
                acquisition = ingredient.Acquisition is null ? null : new
                {
                    strategy = ingredient.Acquisition.Strategy.ToString(),
                    ingredient.Acquisition.Quantity,
                    totalCost = MoneyResponse.From(ingredient.Acquisition.TotalCost),
                    execution = Evidence(ingredient.Acquisition.ExecutionEvidence),
                },
                acquisitionAlternatives = (ingredient.AcquisitionAlternatives ?? []).Select(alternative => new
                {
                    strategy = alternative.Strategy.ToString(),
                    alternative.Quantity,
                    totalCost = FromOptional(alternative.TotalCost ?? alternative.ExecutionEvidence?.TotalValue),
                    execution = Evidence(alternative.ExecutionEvidence),
                }).ToArray(),
                economicInputCost = FromOptional(ingredient.EconomicInputCost),
                uncertainties = ingredient.Uncertainties.Select(value => value.ToString()).ToArray(),
            }).ToArray(),
        },
    };

    private static object? FromOptional(Money? money) => money is { } value ? MoneyResponse.From(value) : null;

    private static object? Evidence(CraftingExecutionEvidence? evidence) => evidence is null ? null : new
    {
        evidence.RequestedQuantity,
        evidence.FilledQuantity,
        isFullyFilled = evidence.IsFullyFilled,
        totalValue = MoneyResponse.From(evidence.TotalValue),
    };

    private static string CraftingSourceState(CraftingPlannerResult? crafting) =>
        crafting?.AccountEvidenceCapturedAtUtc is not null ? "known" : "unknown";

    private static object Theory() => new
    {
        positionSizing = new
        {
            version = PositionSizingPolicy.CurrentVersion,
            basisPointsPerWhole = PositionSizingPolicy.BasisPointsPerWhole,
            reserveRounding = "up",
            capRounding = "down",
            purchaseOnly = true,
            highLiquidityItemCapBasisPoints = PositionSizingPolicy.Default.HighLiquidityItemCapBasisPoints,
            mediumLiquidityItemCapBasisPoints = PositionSizingPolicy.Default.MediumLiquidityItemCapBasisPoints,
            lowLiquidityItemCapBasisPoints = PositionSizingPolicy.Default.LowLiquidityItemCapBasisPoints,
            strategyCapBasisPoints = PositionSizingPolicy.Default.StrategyCapBasisPoints,
            categoryCapBasisPoints = PositionSizingPolicy.Default.CategoryCapBasisPoints,
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
