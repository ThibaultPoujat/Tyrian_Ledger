using Gw2Tp.Application.Persistence;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Application.Plans;
using Gw2Tp.Application.Recommendations;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Web.Hosting;

internal static class PlanEndpoints
{
    public static void MapPlanEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/plans", async (PlanEndpointService service, CancellationToken cancellationToken) =>
            Results.Json(await service.GetAsync(cancellationToken).ConfigureAwait(false)));
        endpoints.MapPost("/api/plans/{planId}/start", (string planId, PlanEndpointService service, CancellationToken cancellationToken) =>
            service.StartAsync(planId, cancellationToken));
        endpoints.MapPost("/api/plans/{planId}/complete", (string planId, PlanStepCompletion request, PlanEndpointService service, CancellationToken cancellationToken) =>
            service.CompleteAsync(planId, request, cancellationToken));
        endpoints.MapPost("/api/plans/{planId}/undo", (string planId, PlanEndpointService service, CancellationToken cancellationToken) =>
            service.UndoAsync(planId, cancellationToken));
    }
}

internal sealed record PlanStepCompletion(int Quantity, string? UnitPriceCopper);

internal sealed class PlanEndpointService(
    IPrimaryRecommendationService recommendations,
    IAccountPortfolioGateway accountPortfolio,
    IPersonalTradingPostRepository profiles,
    IPlanRepository repository,
    IPlanOrchestrationService orchestration)
{
    public async Task<object> GetAsync(CancellationToken cancellationToken)
    {
        var context = await BuildContextAsync(cancellationToken).ConfigureAwait(false);
        if (context is null) return new { state = "unavailable", proposals = Array.Empty<object>(), plans = Array.Empty<object>() };
        var plans = await repository.GetStartedAsync(context.Profile.Id, cancellationToken).ConfigureAwait(false);
        var reservations = plans.SelectMany(plan => plan.Reservations).ToArray();
        var reservedCash = reservations.Aggregate(Money.Zero, (total, requirement) => total + requirement.Cash);
        var reservedResourceKeys = reservations.Where(requirement => requirement.Quantity > 0)
            .Select(ResourceKey).ToHashSet(StringComparer.Ordinal);
        var candidates = context.Candidates.Where(candidate => !candidate.Requirements
            .Where(requirement => requirement.Quantity > 0).Any(requirement => reservedResourceKeys.Contains(ResourceKey(requirement)))).ToArray();
        var availableAfterReservations = context.Recommendations.Portfolio!.AvailableCash - reservedCash;
        var selection = availableAfterReservations.Copper < context.Recommendations.Portfolio.CashReserve.Copper
            ? new PlanBundleSelection([], Money.Zero, 0, candidates.Select(candidate => candidate.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray(), Money.Zero)
            : await orchestration.SelectAsync(candidates, availableAfterReservations,
                context.Recommendations.Portfolio.CashReserve, cancellationToken).ConfigureAwait(false);
        return new { state = "ready", proposals = selection.Plans.Select(ToResponse), excludedCandidateIds = selection.ExcludedCandidateIds,
            intentionallyFreeCash = selection.IntentionallyFreeCash, plans = plans.Select(ToResponse) };
    }

    public async Task<IResult> StartAsync(string planId, CancellationToken cancellationToken)
    {
        var context = await RequireContextAsync(cancellationToken).ConfigureAwait(false);
        var candidate = context.Candidates.SingleOrDefault(candidate => string.Equals(candidate.Id, planId, StringComparison.Ordinal));
        if (candidate is null) return Results.NotFound(new { error = "plan_candidate_not_found" });
        var active = await repository.GetStartedAsync(context.Profile.Id, cancellationToken).ConfigureAwait(false);
        var activeRequirements = active.SelectMany(plan => plan.Reservations).ToArray();
        var reservedCash = activeRequirements.Aggregate(Money.Zero, (total, requirement) => total + requirement.Cash);
        var candidateCash = candidate.Requirements.Aggregate(Money.Zero, (total, requirement) => total + requirement.Cash);
        var activeResourceKeys = activeRequirements.Where(requirement => requirement.Quantity > 0)
            .Select(ResourceKey).ToHashSet(StringComparer.Ordinal);
        var conflictsWithReservation = candidate.Requirements.Where(requirement => requirement.Quantity > 0)
            .Any(requirement => activeResourceKeys.Contains(ResourceKey(requirement)));
        var deployable = context.Recommendations.Portfolio!.AvailableCash - context.Recommendations.Portfolio.CashReserve;
        if (conflictsWithReservation || (reservedCash + candidateCash).Copper > deployable.Copper)
            return Results.Conflict(new { error = "plan_resources_unavailable" });
        var plan = orchestration.Start(candidate, DateTimeOffset.UtcNow);
        await repository.SaveAsync(context.Profile.Id, plan, cancellationToken).ConfigureAwait(false);
        return Results.Json(new { state = "started", plan = ToResponse(plan) });
    }

    public async Task<IResult> CompleteAsync(string planId, PlanStepCompletion request, CancellationToken cancellationToken)
    {
        var context = await RequireContextAsync(cancellationToken).ConfigureAwait(false);
        var plan = await FindAsync(context.Profile.Id, planId, cancellationToken).ConfigureAwait(false);
        if (plan is null) return Results.NotFound(new { error = "plan_not_found" });
        Money? price = null;
        if (request.UnitPriceCopper is not null && (!long.TryParse(request.UnitPriceCopper, out var copper) || copper < 0)) return Results.BadRequest(new { error = "invalid_unit_price" });
        if (request.UnitPriceCopper is not null) price = new Money(long.Parse(request.UnitPriceCopper, System.Globalization.CultureInfo.InvariantCulture));
        var updated = orchestration.ReportStep(plan, request.Quantity, price, DateTimeOffset.UtcNow);
        await repository.SaveAsync(context.Profile.Id, updated, cancellationToken).ConfigureAwait(false);
        return Results.Json(new { state = "reported", plan = ToResponse(updated) });
    }

    public async Task<IResult> UndoAsync(string planId, CancellationToken cancellationToken)
    {
        var context = await RequireContextAsync(cancellationToken).ConfigureAwait(false);
        var plan = await FindAsync(context.Profile.Id, planId, cancellationToken).ConfigureAwait(false);
        if (plan is null) return Results.NotFound(new { error = "plan_not_found" });
        var updated = orchestration.UndoLastStep(plan, DateTimeOffset.UtcNow);
        await repository.SaveAsync(context.Profile.Id, updated, cancellationToken).ConfigureAwait(false);
        return Results.Json(new { state = "undone", plan = ToResponse(updated) });
    }

    private async Task<Context?> BuildContextAsync(CancellationToken cancellationToken)
    {
        var result = await recommendations.GetAsync(cancellationToken).ConfigureAwait(false);
        if (result.State != PrimaryRecommendationState.Ready || result.Portfolio is null) return null;
        var account = await accountPortfolio.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!account.IsSuccess || account.Value is null) return null;
        var profile = await profiles.FindAccountProfileAsync(account.Value.AccountScope.AccountId, cancellationToken).ConfigureAwait(false);
        return profile is null ? null : new Context(profile, result, result.Actions.Where(IsActionable).Select(ToCandidate).ToArray());
    }

    private async Task<Context> RequireContextAsync(CancellationToken cancellationToken) =>
        await BuildContextAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Plan evidence is unavailable.");

    private async Task<PlanRecord?> FindAsync(long profileId, string planId, CancellationToken cancellationToken) =>
        (await repository.GetStartedAsync(profileId, cancellationToken).ConfigureAwait(false)).SingleOrDefault(plan => plan.Id == planId);

    private static bool IsActionable(PrimaryRecommendationRecord record) => record.Action is PrimaryRecommendationAction.Buy or PrimaryRecommendationAction.BuySmall or PrimaryRecommendationAction.UpdateBid or PrimaryRecommendationAction.CancelBid or PrimaryRecommendationAction.List or PrimaryRecommendationAction.Reduce or PrimaryRecommendationAction.SellPartial or PrimaryRecommendationAction.Sell;
    private static string ResourceKey(PlanResourceRequirement requirement) => $"{(int)requirement.Kind}:{requirement.ResourceId}";

    private static PlanCandidate ToCandidate(PrimaryRecommendationRecord record)
    {
        var action = record.Action switch
        {
            PrimaryRecommendationAction.Buy or PrimaryRecommendationAction.BuySmall => PlanStepAction.BuyNow,
            PrimaryRecommendationAction.UpdateBid => PlanStepAction.PlaceBuyOrder,
            PrimaryRecommendationAction.CancelBid => PlanStepAction.CancelBuyOrder,
            PrimaryRecommendationAction.List => PlanStepAction.List,
            PrimaryRecommendationAction.Reduce or PrimaryRecommendationAction.SellPartial or PrimaryRecommendationAction.Sell => PlanStepAction.SellNow,
            _ => throw new ArgumentOutOfRangeException(nameof(record)),
        };
        var attention = action is PlanStepAction.PlaceBuyOrder or PlanStepAction.List ? PlanAttention.Passive : PlanAttention.Active;
        var price = record.Prices.PlannedBid ?? record.Prices.PlannedListPrice ?? record.Prices.MaximumBid ?? record.Prices.CurrentOrderUnitPrice;
        var requirements = new List<PlanResourceRequirement>();
        if (action is PlanStepAction.BuyNow or PlanStepAction.PlaceBuyOrder) requirements.Add(new(PlanResourceKind.Cash, "cash", 0, record.Capital));
        if (action is PlanStepAction.List or PlanStepAction.SellNow) requirements.Add(new(PlanResourceKind.Inventory, record.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture), record.Quantity, Money.Zero));
        var id = $"recommendation:{record.Action}:{record.Source}:{record.OrderId ?? record.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var step = new PlanStep($"{id}:1", action, record.ItemId, record.ItemName, record.Quantity, price, [], PlanStepState.Pending);
        return new PlanCandidate(id, 1, id, attention, [step], requirements, record.Economics?.NetProfit ?? Money.Zero,
            record.Capital, (int)Math.Round((record.Score?.TotalPoints ?? 0m) * 100m, MidpointRounding.AwayFromZero),
            record.Action is PrimaryRecommendationAction.CancelBid ? 10_000 : 0, attention == PlanAttention.Passive ? 120 : 600,
            checked((long)Math.Round((record.Score?.TotalPoints ?? 0m) * 100m, MidpointRounding.AwayFromZero)), true, []);
    }

    private static object ToResponse(PlanCandidate plan) => new { id = plan.Id, attention = plan.Attention.ToString(), modeledProfit = plan.ModeledProfit, committedCapital = plan.CommittedCapital, interactionSeconds = plan.ExpectedInteractionSeconds, steps = plan.Steps.Select(ToResponse) };
    private static object ToResponse(PlanRecord plan) => new { id = plan.Id, attention = plan.Attention.ToString(), state = plan.State.ToString(), reconciliationState = plan.ReconciliationState.ToString(), modeledProfit = plan.ModeledProfit, currentStepOrdinal = plan.CurrentStepOrdinal, steps = plan.Steps.Select(ToResponse), hasUndoableEvent = plan.Events.Any(e => e.State == PlanShadowEventState.PendingConfirmation) };
    private static object ToResponse(PlanStep step) => new { id = step.Id, action = step.Action.ToString(), itemName = step.ItemName, quantity = step.Quantity, unitPrice = step.UnitPrice, state = step.State.ToString() };

    private sealed record Context(AccountProfile Profile, PrimaryRecommendationResult Recommendations, IReadOnlyList<PlanCandidate> Candidates);
}
