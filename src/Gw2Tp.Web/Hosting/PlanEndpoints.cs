using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.Finance;
using Gw2Tp.Application.MarketData;
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

internal sealed record PlanStepCompletion(int Quantity, string? UnitPriceCopper, bool NotPerformed = false);

internal sealed class PlanEndpointService(
    IPrimaryRecommendationService recommendations,
    IAccountPortfolioGateway accountPortfolio,
    IPersonalTradingPostGateway personalTradingPost,
    IAccountCraftingSnapshotService craftingSnapshots,
    IPersonalTradingPostRepository profiles,
    IPlanRepository repository,
    IPlanOrchestrationService orchestration)
{
    public async Task<object> GetAsync(CancellationToken cancellationToken)
    {
        var context = await BuildContextAsync(cancellationToken).ConfigureAwait(false);
        if (context is null) return new { state = "unavailable", proposals = Array.Empty<object>(), plans = Array.Empty<object>() };
        var plans = await ReconcilePlansAsync(context, cancellationToken).ConfigureAwait(false);
        if (context.Recommendations?.State != PrimaryRecommendationState.Ready || context.Recommendations.Portfolio is null)
            return new { state = "ready", degraded = true, proposals = Array.Empty<object>(), excludedCandidateIds = Array.Empty<string>(), intentionallyFreeCash = Money.Zero, plans = plans.Select(ToResponse) };

        var effective = PlanOrchestrationService.ProjectEffectiveResources(context.Snapshot.AvailableCash, context.VerifiedQuantities, plans.SelectMany(plan => plan.Events).ToArray());
        var reservations = plans.SelectMany(PlanOrchestrationService.OutstandingReservations).ToArray();
        var reservedCash = reservations.Aggregate(Money.Zero, (total, requirement) => total + requirement.Cash);
        var availableCash = effective.EffectiveCash - reservedCash;
        var availableQuantities = AvailableQuantities(effective.Quantities, reservations);
        var reservedGenericKeys = reservations.Where(requirement => requirement.Quantity > 0 && requirement.Kind is not (PlanResourceKind.Cash or PlanResourceKind.Inventory))
            .Select(ResourceKey).ToHashSet(StringComparer.Ordinal);
        var candidates = context.Candidates.Where(candidate => candidate.Requirements.Where(requirement => requirement.Quantity > 0)
            .Where(requirement => requirement.Kind == PlanResourceKind.Inventory)
            .All(requirement => availableQuantities.TryGetValue(ResourceKey(requirement), out var quantity) && quantity >= requirement.Quantity) &&
            !candidate.Requirements.Where(requirement => requirement.Quantity > 0 && requirement.Kind is not (PlanResourceKind.Cash or PlanResourceKind.Inventory))
                .Any(requirement => reservedGenericKeys.Contains(ResourceKey(requirement)))).ToArray();
        var selection = availableCash.Copper < 0 || availableCash.Copper < context.Recommendations.Portfolio.CashReserve.Copper
            ? new PlanBundleSelection([], Money.Zero, 0, candidates.Select(candidate => candidate.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray(), new Money(Math.Max(0, availableCash.Copper)))
            : await orchestration.SelectAsync(candidates, availableCash, context.Recommendations.Portfolio.CashReserve, cancellationToken, availableQuantities).ConfigureAwait(false);
        return new { state = "ready", degraded = false, proposals = selection.Plans.Select(ToResponse), excludedCandidateIds = selection.ExcludedCandidateIds,
            intentionallyFreeCash = selection.IntentionallyFreeCash, plans = plans.Select(ToResponse) };
    }

    public async Task<IResult> StartAsync(string planId, CancellationToken cancellationToken)
    {
        var context = await BuildContextAsync(cancellationToken).ConfigureAwait(false);
        if (context?.Recommendations?.State != PrimaryRecommendationState.Ready || context.Recommendations.Portfolio is null || !context.AccountEvidenceAvailable)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        await ReconcilePlansAsync(context, cancellationToken).ConfigureAwait(false);
        var candidate = context.Candidates.SingleOrDefault(value => string.Equals(value.Id, planId, StringComparison.Ordinal));
        if (candidate is null) return Results.NotFound(new { error = "plan_candidate_not_found" });
        var plan = orchestration.Start(candidate, DateTimeOffset.UtcNow, context.Snapshot.AvailableCash, context.VerifiedQuantities);
        var outcome = await repository.TryStartAsync(context.Profile.Id, plan, context.Snapshot.AvailableCash,
            context.Recommendations.Portfolio.CashReserve, context.VerifiedQuantities, cancellationToken).ConfigureAwait(false);
        if (outcome == PlanStartResult.ResourcesUnavailable) return Results.Conflict(new { error = "plan_resources_unavailable" });
        if (outcome == PlanStartResult.AlreadyStarted)
        {
            var existing = await FindByCandidateAsync(context.Profile.Id, planId, cancellationToken).ConfigureAwait(false);
            return existing is null ? Results.Conflict(new { error = "plan_already_started" }) : Results.Json(new { state = "already_started", plan = ToResponse(existing) });
        }
        return Results.Json(new { state = "started", plan = ToResponse(plan) });
    }

    public async Task<IResult> CompleteAsync(string planId, PlanStepCompletion request, CancellationToken cancellationToken)
    {
        var context = await RequireContextAsync(cancellationToken).ConfigureAwait(false);
        var plan = await FindAsync(context.Profile.Id, planId, cancellationToken).ConfigureAwait(false);
        if (plan is null) return Results.NotFound(new { error = "plan_not_found" });
        if (request.NotPerformed)
        {
            var cancelled = orchestration.CancelUnperformedStep(plan);
            try { await repository.SaveAsync(context.Profile.Id, cancelled, cancellationToken).ConfigureAwait(false); }
            catch (PlanConcurrencyException) { return Results.Conflict(new { error = "plan_changed" }); }
            return Results.Json(new { state = "cancelled", plan = ToResponse(cancelled) });
        }
        Money? price = null;
        if (request.UnitPriceCopper is not null && (!long.TryParse(request.UnitPriceCopper, out var copper) || copper < 0)) return Results.BadRequest(new { error = "invalid_unit_price" });
        if (request.UnitPriceCopper is not null) price = new Money(long.Parse(request.UnitPriceCopper, System.Globalization.CultureInfo.InvariantCulture));
        var updated = orchestration.ReportStep(plan, request.Quantity, price, DateTimeOffset.UtcNow);
        try { await repository.SaveAsync(context.Profile.Id, updated, cancellationToken).ConfigureAwait(false); }
        catch (PlanConcurrencyException) { return Results.Conflict(new { error = "plan_changed" }); }
        return Results.Json(new { state = "reported", plan = ToResponse(updated) });
    }

    public async Task<IResult> UndoAsync(string planId, CancellationToken cancellationToken)
    {
        var context = await RequireContextAsync(cancellationToken).ConfigureAwait(false);
        var plan = await FindAsync(context.Profile.Id, planId, cancellationToken).ConfigureAwait(false);
        if (plan is null) return Results.NotFound(new { error = "plan_not_found" });
        var updated = orchestration.UndoLastStep(plan, DateTimeOffset.UtcNow);
        try { await repository.SaveAsync(context.Profile.Id, updated, cancellationToken).ConfigureAwait(false); }
        catch (PlanConcurrencyException) { return Results.Conflict(new { error = "plan_changed" }); }
        return Results.Json(new { state = "undone", plan = ToResponse(updated) });
    }

    private async Task<Context?> BuildContextAsync(CancellationToken cancellationToken)
    {
        var account = await accountPortfolio.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        AccountPortfolioSnapshot snapshot;
        var accountEvidenceAvailable = account.IsSuccess && account.Value is not null;
        if (accountEvidenceAvailable) snapshot = account.Value!;
        else
        {
            var scope = await personalTradingPost.GetAccountScopeAsync(cancellationToken).ConfigureAwait(false);
            if (!scope.IsSuccess || scope.Value is null) return null;
            snapshot = new AccountPortfolioSnapshot(scope.Value, Money.Zero);
        }
        var profile = await profiles.FindAccountProfileAsync(snapshot.AccountScope.AccountId, cancellationToken).ConfigureAwait(false);
        if (profile is null) return null;
        PrimaryRecommendationResult? recommendationsResult;
        try
        {
            recommendationsResult = accountEvidenceAvailable ? await recommendations.GetAsync(cancellationToken).ConfigureAwait(false) : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            recommendationsResult = null;
        }
        AccountCraftingSnapshot? crafting;
        try
        {
            crafting = await craftingSnapshots.GetLatestAsync(snapshot.AccountScope, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            crafting = null;
        }
        var quantities = VerifiedQuantities(snapshot, crafting);
        var evidence = accountEvidenceAvailable ? await ReadEvidenceAsync(cancellationToken).ConfigureAwait(false) : new EvidenceCapture([], null, new HashSet<PlanEvidenceKind>());
        var candidates = recommendationsResult?.State == PrimaryRecommendationState.Ready
            ? recommendationsResult.Actions.Where(IsActionable).Select(ToCandidate).ToArray()
            : Array.Empty<PlanCandidate>();
        return new Context(profile, snapshot, quantities, recommendationsResult, candidates, accountEvidenceAvailable, evidence.Evidence, evidence.CapturedAtUtc, evidence.CompleteKinds);
    }

    private async Task<IReadOnlyList<PlanRecord>> ReconcilePlansAsync(Context context, CancellationToken cancellationToken)
    {
        var plans = await repository.GetStartedAsync(context.Profile.Id, cancellationToken).ConfigureAwait(false);
        var updated = new List<PlanRecord>(plans.Count);
        foreach (var plan in plans)
        {
            if (!context.AccountEvidenceAvailable) { updated.Add(plan); continue; }
            var observedAt = context.Snapshot.CapturedAtUtc ?? DateTimeOffset.UtcNow;
            var reconciled = orchestration.ReconcileWithVerifiedState(plan, context.Snapshot.AvailableCash, context.VerifiedQuantities, observedAt,
                context.Evidence, context.EvidenceCapturedAtUtc, context.CompleteEvidenceKinds);
            var candidate = context.Candidates.SingleOrDefault(value => value.SourceOpportunityId == plan.SourceOpportunityId);
            reconciled = orchestration.ApplyRefresh(reconciled, candidate, context.Recommendations?.State == PrimaryRecommendationState.Ready);
            try
            {
                await repository.SaveAsync(context.Profile.Id, reconciled, cancellationToken).ConfigureAwait(false);
                updated.Add(reconciled with { Revision = reconciled.Revision + 1 });
            }
            catch (PlanConcurrencyException)
            {
                var latest = await FindAsync(context.Profile.Id, plan.Id, cancellationToken).ConfigureAwait(false);
                if (latest is not null) updated.Add(latest);
            }
        }
        return updated;
    }

    private async Task<Context> RequireContextAsync(CancellationToken cancellationToken) =>
        await BuildContextAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Plan account evidence is unavailable.");

    private async Task<PlanRecord?> FindAsync(long profileId, string planId, CancellationToken cancellationToken) =>
        (await repository.GetStartedAsync(profileId, cancellationToken).ConfigureAwait(false)).SingleOrDefault(plan => plan.Id == planId);

    private async Task<PlanRecord?> FindByCandidateAsync(long profileId, string candidateId, CancellationToken cancellationToken) =>
        (await repository.GetStartedAsync(profileId, cancellationToken).ConfigureAwait(false)).SingleOrDefault(plan => plan.SourceOpportunityId == candidateId);

    private static bool IsActionable(PrimaryRecommendationRecord record) => record.Action is PrimaryRecommendationAction.Buy or PrimaryRecommendationAction.BuySmall or PrimaryRecommendationAction.UpdateBid or PrimaryRecommendationAction.CancelBid or PrimaryRecommendationAction.List or PrimaryRecommendationAction.Reduce or PrimaryRecommendationAction.SellPartial or PrimaryRecommendationAction.Sell;

    internal static PlanCandidate ToCandidate(PrimaryRecommendationRecord record)
    {
        var id = $"recommendation:{record.Action}:{record.Source}:{record.OrderId ?? record.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var bid = record.Prices.PlannedBid;
        var action = record.Action switch
        {
            PrimaryRecommendationAction.Buy or PrimaryRecommendationAction.BuySmall => PlanStepAction.PlaceBuyOrder,
            PrimaryRecommendationAction.UpdateBid => PlanStepAction.PlaceBuyOrder,
            PrimaryRecommendationAction.CancelBid => PlanStepAction.CancelBuyOrder,
            PrimaryRecommendationAction.List => PlanStepAction.List,
            PrimaryRecommendationAction.Reduce or PrimaryRecommendationAction.SellPartial or PrimaryRecommendationAction.Sell => PlanStepAction.SellNow,
            _ => throw new ArgumentOutOfRangeException(nameof(record)),
        };
        var price = record.Action switch
        {
            PrimaryRecommendationAction.Buy or PrimaryRecommendationAction.BuySmall or PrimaryRecommendationAction.UpdateBid => bid,
            PrimaryRecommendationAction.CancelBid => record.Prices.CurrentOrderUnitPrice,
            PrimaryRecommendationAction.List => record.Prices.PlannedListPrice,
            _ => record.Prices.ImmediateSalePriceRange?.LowestUnitPrice ?? record.Prices.LowestSell ?? record.Prices.CurrentOrderUnitPrice,
        };
        var steps = new List<PlanStep>();
        if (record.Action == PrimaryRecommendationAction.UpdateBid)
        {
            var cancelId = $"{id}:1";
            steps.Add(new PlanStep(cancelId, PlanStepAction.CancelBuyOrder, record.ItemId, record.ItemName, record.Quantity, record.Prices.CurrentOrderUnitPrice, [], PlanStepState.Pending, record.OrderId));
            steps.Add(new PlanStep($"{id}:2", PlanStepAction.PlaceBuyOrder, record.ItemId, record.ItemName, record.Quantity, price, [cancelId], PlanStepState.Pending));
        }
        else
        {
            steps.Add(new PlanStep($"{id}:1", action, record.ItemId, record.ItemName, record.Quantity, price, [], PlanStepState.Pending));
        }
        var attention = record.Action is PrimaryRecommendationAction.Buy or PrimaryRecommendationAction.BuySmall or PrimaryRecommendationAction.List ? PlanAttention.Passive : PlanAttention.Active;
        var requirements = new List<PlanResourceRequirement>();
        if (record.Action is PrimaryRecommendationAction.Buy or PrimaryRecommendationAction.BuySmall or PrimaryRecommendationAction.UpdateBid)
        {
            requirements.Add(new(PlanResourceKind.Cash, "cash", 0, record.Capital));
            if (record.OrderId is { Length: > 0 } orderId)
                requirements.Add(new(PlanResourceKind.OpenOrderExposure, orderId, record.Quantity, Money.Zero));
            else
                requirements.Add(new(PlanResourceKind.ExpectedIncoming, record.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture), record.Quantity, Money.Zero));
        }
        if (record.Action is PrimaryRecommendationAction.List or PrimaryRecommendationAction.Reduce or PrimaryRecommendationAction.SellPartial or PrimaryRecommendationAction.Sell)
        {
            requirements.Add(new(PlanResourceKind.Inventory, record.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture), record.Quantity, Money.Zero));
            if (record.Action == PrimaryRecommendationAction.List && price is { } listingPrice)
                requirements.Add(new(PlanResourceKind.Cash, "cash", 0, Gw2TradingPostFeePolicy.Create().CalculateFees(new Money(checked(listingPrice.Copper * record.Quantity))).ListingFee));
        }
        var score = (long)Math.Round((record.Score?.TotalPoints ?? 0m) * 100m, MidpointRounding.AwayFromZero);
        var confidence = record.History?.Confidence switch
        {
            OpportunityHistoricalConfidence.Strong => 10_000,
            OpportunityHistoricalConfidence.Partial => 5_000,
            _ => 0,
        };
        var urgency = record.Action == PrimaryRecommendationAction.CancelBid ? 10_000 : 0;
        var turnover = record.Capital.Copper > 0 && record.Economics is { } economics ? checked(economics.NetProfit.Copper * 600 / record.Capital.Copper) : 0;
        var utility = checked(score * 100 + confidence + urgency + turnover - (attention == PlanAttention.Passive ? 0 : 100));
        var interaction = attention == PlanAttention.Passive ? 120 : 600;
        return new PlanCandidate(id, 1, id, attention, steps, requirements, record.Economics?.NetProfit ?? Money.Zero,
            requirements.Aggregate(Money.Zero, (sum, value) => sum + value.Cash), confidence, urgency, interaction, utility, true, []);
    }

    private static IReadOnlyDictionary<string, long> VerifiedQuantities(AccountPortfolioSnapshot snapshot, AccountCraftingSnapshot? crafting)
    {
        var quantities = new Dictionary<string, long>(IsFresh(snapshot.CapturedAtUtc) ? snapshot.VerifiedQuantities ?? new Dictionary<string, long>() : new Dictionary<string, long>(), StringComparer.Ordinal);
        if (crafting is null || !IsFresh(crafting.CapturedAtUtc)) return quantities;
        if (crafting.BankInventory.Availability == CraftingFeatureAvailability.Available)
            foreach (var entry in crafting.BankInventory.Value ?? []) AddQuantity(quantities, entry.ItemId, entry.Quantity);
        if (crafting.MaterialStorage.Availability == CraftingFeatureAvailability.Available)
            foreach (var entry in crafting.MaterialStorage.Value ?? []) AddQuantity(quantities, entry.ItemId, entry.Quantity);
        return quantities;
    }

    private async Task<EvidenceCapture> ReadEvidenceAsync(CancellationToken cancellationToken)
    {
        var captured = DateTimeOffset.UtcNow;
        var entries = new List<PlanVerifiedEvidence>();
        var completeKinds = new HashSet<PlanEvidenceKind>();
        async Task Read(Func<int, Task<Gw2ApiResult<PersonalTransactionPage>>> fetch, PlanEvidenceKind kind)
        {
            try
            {
                var result = await fetch(0).ConfigureAwait(false);
                if (!result.IsSuccess || result.Value is null) return;
                var pages = new List<PersonalTransactionPage> { result.Value };
                for (var page = 1; page < result.Value.PageCount; page++)
                {
                    var next = await fetch(page).ConfigureAwait(false);
                    if (!next.IsSuccess || next.Value is null) return;
                    pages.Add(next.Value);
                }
                completeKinds.Add(kind);
                entries.AddRange(pages.SelectMany(page => page.Transactions).Where(tx => tx.Quantity > 0 && tx.ItemId > 0 && tx.PriceInCopper >= 0)
                    .Select(tx => new PlanVerifiedEvidence($"{kind}:{tx.TransactionId}", kind, tx.ItemId, tx.Quantity, new Money(tx.PriceInCopper),
                        kind is PlanEvidenceKind.CompletedBuy or PlanEvidenceKind.CompletedSell ? tx.PurchasedAtUtc ?? tx.CreatedAtUtc : tx.CreatedAtUtc, captured,
                        tx.TransactionId.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { }
        }
        await Read(page => personalTradingPost.GetCurrentBuyOrdersAsync(page, cancellationToken), PlanEvidenceKind.BuyOrder).ConfigureAwait(false);
        await Read(page => personalTradingPost.GetCurrentSellListingsAsync(page, cancellationToken), PlanEvidenceKind.SellListing).ConfigureAwait(false);
        await Read(page => personalTradingPost.GetCompletedBuyHistoryAsync(page, cancellationToken), PlanEvidenceKind.CompletedBuy).ConfigureAwait(false);
        await Read(page => personalTradingPost.GetCompletedSellHistoryAsync(page, cancellationToken), PlanEvidenceKind.CompletedSell).ConfigureAwait(false);
        return new EvidenceCapture(entries, completeKinds.Count == 0 ? null : captured, completeKinds);
    }

    private static bool IsFresh(DateTimeOffset? capturedAtUtc) => capturedAtUtc is { } captured && DateTimeOffset.UtcNow - captured <= TimeSpan.FromMinutes(15);

    private static void AddQuantity(IDictionary<string, long> quantities, int itemId, long quantity)
    {
        if (itemId > 0 && quantity > 0)
        {
            var key = $"{(int)PlanResourceKind.Inventory}:{itemId}";
            quantities[key] = checked((quantities.TryGetValue(key, out var existing) ? existing : 0) + quantity);
        }
    }

    private static IReadOnlyDictionary<string, long> AvailableQuantities(IReadOnlyDictionary<string, long> effective, IReadOnlyCollection<PlanResourceRequirement> reservations)
    {
        var available = new Dictionary<string, long>(effective, StringComparer.Ordinal);
        foreach (var reservation in reservations.Where(value => value.Kind == PlanResourceKind.Inventory && value.Quantity > 0))
        {
            var key = ResourceKey(reservation);
            available[key] = (available.TryGetValue(key, out var quantity) ? quantity : 0) - reservation.Quantity;
        }
        return available;
    }

    private static string ResourceKey(PlanResourceRequirement requirement) => $"{(int)requirement.Kind}:{requirement.ResourceId}";
    private static object ToResponse(PlanCandidate plan) => new { id = plan.Id, attention = plan.Attention.ToString(), modeledProfit = plan.ModeledProfit, committedCapital = plan.CommittedCapital, interactionSeconds = plan.ExpectedInteractionSeconds, steps = plan.Steps.Select(ToResponse) };
    private static object ToResponse(PlanRecord plan) => new { id = plan.Id, attention = plan.Attention.ToString(), state = plan.State.ToString(), reconciliationState = plan.ReconciliationState.ToString(), modeledProfit = plan.ModeledProfit, currentStepOrdinal = plan.CurrentStepOrdinal, steps = plan.Steps.Select(ToResponse), hasUndoableEvent = plan.Events.Any(e => e.State is PlanShadowEventState.PendingConfirmation or PlanShadowEventState.PartiallyConfirmed) };
    private static object ToResponse(PlanStep step) => new { id = step.Id, action = step.Action.ToString(), itemName = step.ItemName, quantity = step.Quantity, unitPrice = step.UnitPrice, state = step.State.ToString() };

    private sealed record EvidenceCapture(IReadOnlyCollection<PlanVerifiedEvidence> Evidence, DateTimeOffset? CapturedAtUtc, IReadOnlySet<PlanEvidenceKind> CompleteKinds);
    private sealed record Context(AccountProfile Profile, AccountPortfolioSnapshot Snapshot, IReadOnlyDictionary<string, long> VerifiedQuantities, PrimaryRecommendationResult? Recommendations, IReadOnlyList<PlanCandidate> Candidates, bool AccountEvidenceAvailable, IReadOnlyCollection<PlanVerifiedEvidence> Evidence, DateTimeOffset? EvidenceCapturedAtUtc, IReadOnlySet<PlanEvidenceKind> CompleteEvidenceKinds);
}
