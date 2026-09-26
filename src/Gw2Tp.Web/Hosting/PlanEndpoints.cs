using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.Finance;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Application.Plans;
using Gw2Tp.Application.Recommendations;
using Gw2Tp.Domain.Finance;
using System.Diagnostics;
using System.Globalization;

namespace Gw2Tp.Web.Hosting;

internal static class PlanEndpoints
{
    public static void MapPlanEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/plans", async (HttpContext context, PlanEndpointService service, CancellationToken cancellationToken) =>
        {
            var response = await service.GetAsync(cancellationToken).ConfigureAwait(false);
            context.Response.Headers["X-Tyrian-Plan-Timing"] = response.Timing.ToHeaderValue();
            return Results.Json(response.Payload);
        });
        endpoints.MapPost("/api/plans/{planId}/start", (string planId, PlanEndpointService service, CancellationToken cancellationToken) =>
            service.StartAsync(planId, cancellationToken));
        endpoints.MapPost("/api/plans/{planId}/complete", (string planId, PlanStepCompletion request, PlanEndpointService service, CancellationToken cancellationToken) =>
            service.CompleteAsync(planId, request, cancellationToken));
        endpoints.MapPost("/api/plans/{planId}/undo", (string planId, PlanEndpointService service, CancellationToken cancellationToken) =>
            service.UndoAsync(planId, cancellationToken));
    }
}

internal sealed record PlanStepCompletion(int Quantity, string? UnitPriceCopper, bool NotPerformed = false);

/// <summary>Sanitized phase timings for a Plans read; contains no account or market facts.</summary>
internal sealed record PlanDecisionTiming(
    long AccountEvidenceLoadingMilliseconds = 0,
    long ReconciliationPersistenceMilliseconds = 0,
    long RecommendationGenerationMilliseconds = 0,
    long CraftingCandidateGenerationMilliseconds = 0,
    long CraftingListingsMilliseconds = 0,
    long CraftingHistoryMilliseconds = 0,
    long PlanRefreshPersistenceMilliseconds = 0,
    long ResourceSelectionMilliseconds = 0)
{
    internal string ToHeaderValue() => string.Join(",",
        $"account;dur={AccountEvidenceLoadingMilliseconds.ToString(CultureInfo.InvariantCulture)}",
        $"reconcile;dur={ReconciliationPersistenceMilliseconds.ToString(CultureInfo.InvariantCulture)}",
        $"recommendations;dur={RecommendationGenerationMilliseconds.ToString(CultureInfo.InvariantCulture)}",
        $"crafting;dur={CraftingCandidateGenerationMilliseconds.ToString(CultureInfo.InvariantCulture)}",
        $"craft-listings;dur={CraftingListingsMilliseconds.ToString(CultureInfo.InvariantCulture)}",
        $"craft-history;dur={CraftingHistoryMilliseconds.ToString(CultureInfo.InvariantCulture)}",
        $"refresh;dur={PlanRefreshPersistenceMilliseconds.ToString(CultureInfo.InvariantCulture)}",
        $"selection;dur={ResourceSelectionMilliseconds.ToString(CultureInfo.InvariantCulture)}");
}

internal sealed record PlanEndpointResponse(object Payload, PlanDecisionTiming Timing);

/// <summary>
/// Holds one completed decision-loop projection in process.  The projection is
/// not a market cache: it expires at the next permitted cycle (or earlier at
/// account-evidence expiry), is account-scoped, and is cleared for mutations.
/// </summary>
internal sealed class PlanDecisionProjectionStore(
    DecisionLoopSchedulerSettings settings,
    Func<DateTimeOffset>? utcNow = null)
{
    private readonly object gate = new();
    private readonly Func<DateTimeOffset> clock = utcNow ?? (() => DateTimeOffset.UtcNow);
    private PlanDecisionSnapshot? latest;
    private long generation;
    private ActiveLoopRun? active;

    internal long BeginLoopRun()
    {
        lock (gate)
        {
            latest = null;
            generation = checked(generation + 1);
            active = new(generation, new(TaskCreationOptions.RunContinuationsAsynchronously));
            return generation;
        }
    }

    internal bool TryGetActive(out Task<PlanDecisionSnapshot?>? decision)
    {
        lock (gate)
        {
            decision = active?.Completion.Task;
            return decision is not null;
        }
    }

    internal void CompleteLoopRun(long loopGeneration)
    {
        lock (gate)
        {
            if (active?.Generation != loopGeneration) return;
            active.Completion.TrySetResult(null);
            active = null;
        }
    }

    internal bool TryPublishAndObserve(PlanDecisionSnapshot decision, long loopGeneration, Action observe)
    {
        ArgumentNullException.ThrowIfNull(observe);
        if (!decision.AccountEvidenceAvailable || decision.Recommendations?.State != PrimaryRecommendationState.Ready) return false;
        lock (gate)
        {
            if (active?.Generation != loopGeneration) return false;
            latest = decision;
            // Keep the notification observation in the same critical section as
            // projection publication. A successful plan mutation invalidates this
            // generation under the same lock, so it cannot slip between these
            // two externally visible results.
            observe();
            active.Completion.TrySetResult(decision);
            return true;
        }
    }

    internal void Invalidate()
    {
        lock (gate)
        {
            latest = null;
            generation = checked(generation + 1);
            active?.Completion.TrySetResult(null);
            active = null;
        }
    }

    internal bool TryGet(string accountScopeId, out PlanDecisionSnapshot? decision)
    {
        if (string.IsNullOrWhiteSpace(accountScopeId)) throw new ArgumentException("An account scope is required.", nameof(accountScopeId));
        lock (gate)
        {
            var candidate = latest;
            var now = clock();
            if (candidate is null ||
                !string.Equals(candidate.Profile.AccountScopeId, accountScopeId, StringComparison.Ordinal) ||
                candidate.Recommendations?.AccountEvidenceExpiresAtUtc is not { } accountExpires ||
                now > accountExpires || now > candidate.CachedAtUtc + settings.CycleInterval)
            {
                if (candidate is not null && (now > candidate.CachedAtUtc + settings.CycleInterval || now > candidate.Recommendations?.AccountEvidenceExpiresAtUtc)) latest = null;
                decision = null;
                return false;
            }

            decision = candidate;
            return true;
        }
    }

    private sealed record ActiveLoopRun(long Generation, TaskCompletionSource<PlanDecisionSnapshot?> Completion);
}

internal sealed class PlanEndpointService(
    IPrimaryRecommendationService recommendations,
    IAccountPortfolioGateway accountPortfolio,
    IPersonalTradingPostGateway personalTradingPost,
    IAccountCraftingSnapshotService craftingSnapshots,
    ICraftingOpportunityService craftingOpportunities,
    IPersonalTradingPostRepository profiles,
    IPlanRepository repository,
    IPlanOrchestrationService orchestration,
    PlanDecisionProjectionStore loopDecisions)
{
    internal event Action<string>? LoopDecisionInvalidated;

    public async Task<PlanEndpointResponse> GetAsync(CancellationToken cancellationToken)
    {
        var context = await TryGetLoopDecisionContextAsync(cancellationToken).ConfigureAwait(false)
            ?? await BuildDecisionContextAsync(cancellationToken).ConfigureAwait(false);
        if (context is null) return Complete(new { state = "unavailable", proposals = Array.Empty<object>(), plans = Array.Empty<object>(), selection = EmptySelection("account_evidence_unavailable") }, new PlanDecisionTiming());
        var plans = context.Plans;
        var visiblePlans = plans.Where(plan => plan.State != PlanState.Invalid).ToArray();
        if (context.Recommendations?.State != PrimaryRecommendationState.Ready)
            return Complete(new { state = "ready", degraded = true, proposals = Array.Empty<object>(), excludedCandidateIds = Array.Empty<string>(), intentionallyFreeCash = Money.Zero, plans = visiblePlans.Select(ToResponse), selection = EmptySelection("recommendations_not_ready", context.DecisionCacheState, context.ReusedDecision) }, context.Timing);
        var selectionTimer = Stopwatch.StartNew();
        var selection = await SelectCandidatesAsync(context.Snapshot, context.VerifiedQuantities, context.Recommendations, context.Candidates, context.Plans, cancellationToken).ConfigureAwait(false);
        if (selection.PortfolioSizingUnavailable && selection.SafeWithoutPortfolioSizing.Count == 0)
            return Complete(new
            {
                state = "ready",
                degraded = true,
                proposals = Array.Empty<object>(),
                excludedCandidateIds = Array.Empty<string>(),
                intentionallyFreeCash = Money.Zero,
                plans = visiblePlans.Select(ToResponse),
                selection = new
                {
                    recommendationCandidates = context.Recommendations.Actions.Count(IsActionable),
                    generatedCandidates = context.Candidates.Count,
                    rejectedHardConstraints = 0,
                    rejectedResourceConflicts = 0,
                    rejectedSelectionConstraints = 0,
                    rejectedDecisionSafetyGate = context.Candidates.Count,
                    selected = 0,
                    reason = context.Recommendations.EvidenceError ?? "portfolio_sizing_unavailable",
                    reusedDecision = context.ReusedDecision,
                    cacheState = context.DecisionCacheState,
                },
            }, context.Timing, selectionTimer);

        var reason = selection.Selection.Plans.Count > 0 ? "plans_selected"
            : selection.HardEligible.Count == 0 ? "hard_constraints"
            : selection.ResourceEligible.Count == 0 ? "resource_conflicts"
            : selection.AvailableCash.Copper < selection.HardReserve.Copper ? "cash_reserve"
            : "selection_constraints";
        return Complete(new { state = "ready", degraded = false, proposals = selection.Selection.Plans.Select(ToResponse), excludedCandidateIds = selection.Selection.ExcludedCandidateIds,
            intentionallyFreeCash = selection.Selection.IntentionallyFreeCash, plans = visiblePlans.Select(ToResponse), selection = new
            {
                recommendationCandidates = context.Recommendations.Actions.Count(IsActionable),
                generatedCandidates = context.Candidates.Count,
                rejectedHardConstraints = context.Candidates.Count - selection.HardEligible.Count,
                rejectedResourceConflicts = selection.HardEligible.Count - selection.ResourceEligible.Count,
                rejectedSelectionConstraints = selection.ResourceEligible.Count - selection.Selection.Plans.Count,
                rejectedDecisionSafetyGate = selection.PortfolioSizingUnavailable ? context.Candidates.Count - selection.SafeWithoutPortfolioSizing.Count : 0,
                selected = selection.Selection.Plans.Count,
                reason,
                reusedDecision = context.ReusedDecision,
                cacheState = context.DecisionCacheState,
            } }, context.Timing, selectionTimer);
    }

    public async Task<IResult> StartAsync(string planId, CancellationToken cancellationToken)
    {
        var context = await BuildDecisionContextAsync(cancellationToken).ConfigureAwait(false);
        if (context?.Recommendations?.State != PrimaryRecommendationState.Ready || !context.AccountEvidenceAvailable)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        var candidate = context.Candidates.SingleOrDefault(value => string.Equals(value.Id, planId, StringComparison.Ordinal));
        if (candidate is null) return Results.NotFound(new { error = "plan_candidate_not_found" });
        if (context.Recommendations.Portfolio is null && !CanExecuteWithoutPortfolioSizing(candidate))
            return Results.Conflict(new { error = "plan_portfolio_sizing_unavailable" });
        var plan = orchestration.Start(candidate, DateTimeOffset.UtcNow, context.Snapshot.AvailableCash, context.VerifiedQuantities);
        var hardReserve = context.Recommendations.Portfolio?.CashReserve ?? Money.Zero;
        var outcome = await repository.TryStartAsync(context.Profile.Id, plan, context.Snapshot.AvailableCash,
            hardReserve, context.VerifiedQuantities, cancellationToken).ConfigureAwait(false);
        if (outcome == PlanStartResult.ResourcesUnavailable) return Results.Conflict(new { error = "plan_resources_unavailable" });
        if (outcome == PlanStartResult.AlreadyStarted)
        {
            var existing = await FindByCandidateAsync(context.Profile.Id, planId, cancellationToken).ConfigureAwait(false);
            return existing is null ? Results.Conflict(new { error = "plan_already_started" }) : Results.Json(new { state = "already_started", plan = ToResponse(existing) });
        }
        InvalidateLoopDecision(context.Profile.AccountScopeId);
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
            InvalidateLoopDecision(context.Profile.AccountScopeId);
            return Results.Json(new { state = "cancelled", plan = ToResponse(cancelled) });
        }
        Money? price = null;
        if (request.UnitPriceCopper is not null && (!long.TryParse(request.UnitPriceCopper, out var copper) || copper < 0)) return Results.BadRequest(new { error = "invalid_unit_price" });
        if (request.UnitPriceCopper is not null) price = new Money(long.Parse(request.UnitPriceCopper, System.Globalization.CultureInfo.InvariantCulture));
        var updated = orchestration.ReportStep(plan, request.Quantity, price, DateTimeOffset.UtcNow);
        try { await repository.SaveAsync(context.Profile.Id, updated, cancellationToken).ConfigureAwait(false); }
        catch (PlanConcurrencyException) { return Results.Conflict(new { error = "plan_changed" }); }
        InvalidateLoopDecision(context.Profile.AccountScopeId);
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
        InvalidateLoopDecision(context.Profile.AccountScopeId);
        return Results.Json(new { state = "undone", plan = ToResponse(updated) });
    }

    internal async Task<PlanDecisionSnapshot?> GetDecisionSnapshotAsync(CancellationToken cancellationToken)
    {
        var context = await BuildDecisionContextAsync(cancellationToken).ConfigureAwait(false);
        var decision = context is null
            ? null
            : new PlanDecisionSnapshot(context.Profile, context.Snapshot, context.VerifiedQuantities, context.Recommendations, context.Plans, context.Candidates, context.AccountEvidenceAvailable, context.Timing, DateTimeOffset.UtcNow);
        return decision;
    }

    internal async Task<IReadOnlySet<string>> GetExecutableSignalCandidateIdsAsync(PlanDecisionSnapshot decision, CancellationToken cancellationToken)
    {
        var trace = await GetSelectionTraceAsync(decision, cancellationToken).ConfigureAwait(false);
        return trace.ExecutableSignalCandidateIds;
    }

    /// <summary>
    /// Captures the result of the canonical selection pass for display. This is
    /// intentionally a projection of <see cref="SelectCandidatesAsync"/>, not
    /// an explanation-only selector with its own resource rules.
    /// </summary>
    internal async Task<PlanDecisionSelectionTrace> GetSelectionTraceAsync(PlanDecisionSnapshot decision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (!decision.AccountEvidenceAvailable || decision.Recommendations?.State != PrimaryRecommendationState.Ready)
            return PlanDecisionSelectionTrace.Unavailable(decision.Candidates.Count, decision.Recommendations?.EvidenceError ?? "recommendations_not_ready");

        var selection = await SelectCandidatesAsync(decision.Snapshot, decision.VerifiedQuantities, decision.Recommendations,
            decision.Candidates, decision.Plans, cancellationToken).ConfigureAwait(false);
        return new(
            decision.Candidates.Count,
            selection.HardEligible.Count,
            selection.ResourceEligible.Count,
            selection.Selection.Plans.Count,
            selection.PortfolioSizingUnavailable,
            selection.Selection.ExcludedCandidateIds,
            selection.Selection.Plans.Select(candidate => candidate.Id)
                .Where(id => id.StartsWith("recommendation:", StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal),
            selection.PortfolioSizingUnavailable && selection.SafeWithoutPortfolioSizing.Count == 0
                ? decision.Recommendations.EvidenceError ?? "portfolio_sizing_unavailable"
                : null);
    }

    /// <summary>
    /// Publishes only a completed decision-loop projection.  It is bounded by
    /// the next permitted loop cycle and the original account-evidence expiry;
    /// a plan mutation or a new cycle clears it before any read can reuse it.
    /// </summary>
    internal bool TryPublishLoopDecisionAndObserve(PlanDecisionSnapshot decision, long loopGeneration, Action observe)
    {
        return loopDecisions.TryPublishAndObserve(decision, loopGeneration, observe);
    }

    internal void InvalidateLoopDecision(string accountScopeId)
    {
        loopDecisions.Invalidate();
        LoopDecisionInvalidated?.Invoke(accountScopeId);
    }

    internal long BeginLoopDecision() => loopDecisions.BeginLoopRun();

    internal void CompleteLoopDecision(long loopGeneration) => loopDecisions.CompleteLoopRun(loopGeneration);

    private async Task<Context?> BuildDecisionContextAsync(CancellationToken cancellationToken)
    {
        var accountTimer = Stopwatch.StartNew();
        var context = await BuildContextAsync(cancellationToken).ConfigureAwait(false);
        accountTimer.Stop();
        if (context is null) return null;

        // Verified account evidence is reconciled before any new recommendation
        // or crafting candidate is generated. This ordering prevents a stale
        // local shadow from feeding the next economic decision.
        var reconciliationTimer = Stopwatch.StartNew();
        var reconciledPlans = await ReconcilePlansAsync(context, applyRefresh: false, cancellationToken).ConfigureAwait(false);
        reconciliationTimer.Stop();
        var recommendationTimer = Stopwatch.StartNew();
        var recommendationsResult = await GetRecommendationsAsync(cancellationToken).ConfigureAwait(false);
        recommendationTimer.Stop();
        var craftingTimer = Stopwatch.StartNew();
        var candidateBuild = await BuildCandidatesAsync(recommendationsResult, cancellationToken).ConfigureAwait(false);
        craftingTimer.Stop();
        var timing = new PlanDecisionTiming(
            Milliseconds(accountTimer.Elapsed),
            Milliseconds(reconciliationTimer.Elapsed),
            Milliseconds(recommendationTimer.Elapsed),
            Milliseconds(craftingTimer.Elapsed),
            candidateBuild.CraftingTiming?.ListingsMilliseconds ?? 0,
            candidateBuild.CraftingTiming?.HistoryMilliseconds ?? 0);
        var refreshedContext = context with
        {
            Recommendations = recommendationsResult,
            Candidates = candidateBuild.Candidates,
            Plans = reconciledPlans,
            ReusedDecision = false,
            DecisionCacheState = "not_cached",
            Timing = timing,
        };
        var refreshTimer = Stopwatch.StartNew();
        var refreshedPlans = await ApplyPlanRefreshAsync(refreshedContext, cancellationToken).ConfigureAwait(false);
        refreshTimer.Stop();
        return refreshedContext with { Plans = refreshedPlans, Timing = timing with { PlanRefreshPersistenceMilliseconds = Milliseconds(refreshTimer.Elapsed) } };
    }

    private async Task<CandidateSelection> SelectCandidatesAsync(
        AccountPortfolioSnapshot snapshot,
        IReadOnlyDictionary<string, long> verifiedQuantities,
        PrimaryRecommendationResult recommendationsResult,
        IReadOnlyList<PlanCandidate> allCandidates,
        IReadOnlyList<PlanRecord> plans,
        CancellationToken cancellationToken)
    {
        var portfolioSizingUnavailable = recommendationsResult.Portfolio is null;
        var safeWithoutPortfolioSizing = portfolioSizingUnavailable
            ? allCandidates.Where(CanExecuteWithoutPortfolioSizing).ToArray()
            : allCandidates.ToArray();
        var effective = PlanOrchestrationService.ProjectEffectiveResources(snapshot.AvailableCash, verifiedQuantities, plans.SelectMany(plan => plan.Events).ToArray());
        var reservations = plans.SelectMany(PlanOrchestrationService.OutstandingReservations).ToArray();
        var reservedCash = reservations.Aggregate(Money.Zero, (total, requirement) => total + requirement.Cash);
        var availableCash = effective.EffectiveCash - reservedCash;
        var availableQuantities = AvailableQuantities(effective.Quantities, reservations);
        var reservedGenericKeys = reservations.Where(requirement => requirement.Quantity > 0 && requirement.Kind is not (PlanResourceKind.Cash or PlanResourceKind.Inventory))
            .Select(ResourceKey).ToHashSet(StringComparer.Ordinal);
        var hardEligible = safeWithoutPortfolioSizing.Where(PlanOrchestrationService.IsExecutable).ToArray();
        var resourceEligible = hardEligible.Where(candidate => candidate.Requirements.Where(requirement => requirement.Quantity > 0)
            .Where(requirement => requirement.Kind == PlanResourceKind.Inventory)
            .All(requirement => availableQuantities.TryGetValue(ResourceKey(requirement), out var quantity) && quantity >= requirement.Quantity) &&
            !candidate.Requirements.Where(requirement => requirement.Quantity > 0 && requirement.Kind is not (PlanResourceKind.Cash or PlanResourceKind.Inventory))
                .Any(requirement => reservedGenericKeys.Contains(ResourceKey(requirement)))).ToArray();
        var hardReserve = recommendationsResult.Portfolio?.CashReserve ?? Money.Zero;
        var selected = availableCash.Copper < 0 || availableCash.Copper < hardReserve.Copper
            ? new PlanBundleSelection([], Money.Zero, 0, resourceEligible.Select(candidate => candidate.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray(), new Money(Math.Max(0, availableCash.Copper)))
            : await orchestration.SelectAsync(resourceEligible, availableCash, hardReserve, cancellationToken, availableQuantities).ConfigureAwait(false);
        return new CandidateSelection(portfolioSizingUnavailable, safeWithoutPortfolioSizing, hardEligible, resourceEligible, selected, availableCash, hardReserve);
    }

    private async Task<Context?> TryGetLoopDecisionContextAsync(CancellationToken cancellationToken)
    {
        // A fresh account scope read prevents one account from ever observing
        // another account's in-memory decision projection.
        var scope = await personalTradingPost.GetAccountScopeAsync(cancellationToken).ConfigureAwait(false);
        if (!scope.IsSuccess || scope.Value is null) return null;
        if (!loopDecisions.TryGet(scope.Value.AccountId, out var decision) || decision is null)
        {
            if (!loopDecisions.TryGetActive(out var active) || active is null) return null;
            _ = await active.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!loopDecisions.TryGet(scope.Value.AccountId, out decision) || decision is null) return null;
        }

        return new Context(decision.Profile, decision.Snapshot, decision.VerifiedQuantities, decision.Recommendations,
            decision.Candidates, decision.AccountEvidenceAvailable, [], null, new HashSet<PlanEvidenceKind>(), decision.Plans,
            true, "loop_projection", decision.Timing);
    }

    private static object EmptySelection(string reason, string cacheState = "not_requested", bool reusedDecision = false) => new
    {
        recommendationCandidates = 0,
        generatedCandidates = 0,
        rejectedHardConstraints = 0,
        rejectedResourceConflicts = 0,
        rejectedSelectionConstraints = 0,
        selected = 0,
        reason,
        reusedDecision,
        cacheState,
    };

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
        var evidence = accountEvidenceAvailable ? await ReadEvidenceAsync(profile, cancellationToken).ConfigureAwait(false) : new EvidenceCapture([], null, new HashSet<PlanEvidenceKind>());
        return new Context(profile, snapshot, quantities, null, [], accountEvidenceAvailable, evidence.Evidence, evidence.CapturedAtUtc, evidence.CompleteKinds, [], false, "not_requested");
    }

    private async Task<PrimaryRecommendationResult?> GetRecommendationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await recommendations.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<CandidateBuild> BuildCandidatesAsync(
        PrimaryRecommendationResult? recommendationsResult,
        CancellationToken cancellationToken)
    {
        var recommendationCandidates = recommendationsResult?.State == PrimaryRecommendationState.Ready
            ? recommendationsResult.Actions.Where(IsActionable).Select(ToCandidate).ToArray()
            : Array.Empty<PlanCandidate>();
        IReadOnlyList<PlanCandidate> craftingCandidates = [];
        CraftingOpportunityTiming? craftingTiming = null;
        try
        {
            var crafting = await craftingOpportunities.GetAsync(cancellationToken).ConfigureAwait(false);
            craftingTiming = crafting.Timing;
            craftingCandidates = crafting.Opportunities
                .Where(value => value.IsActionable && value.Candidate is not null).Select(value => value.Candidate!).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { /* Crafting remains conservatively unavailable without suppressing trading plans. */ }
        return new(recommendationCandidates.Concat(craftingCandidates).OrderBy(value => value.Id, StringComparer.Ordinal).ToArray(), craftingTiming);
    }

    private async Task<IReadOnlyList<PlanRecord>> ReconcilePlansAsync(Context context, bool applyRefresh, CancellationToken cancellationToken)
    {
        var plans = context.Plans.Count > 0
            ? context.Plans
            : await repository.GetReconciliationCandidatesAsync(context.Profile.Id, cancellationToken).ConfigureAwait(false);
        var updated = new List<PlanRecord>(plans.Count);
        foreach (var plan in plans)
        {
            if (!context.AccountEvidenceAvailable) { updated.Add(plan); continue; }
            var observedAt = context.Snapshot.CapturedAtUtc ?? DateTimeOffset.UtcNow;
            var reconciled = orchestration.ReconcileWithVerifiedState(plan, context.Snapshot.AvailableCash, context.VerifiedQuantities, observedAt,
                context.Evidence, context.EvidenceCapturedAtUtc, context.CompleteEvidenceKinds);
            if (applyRefresh)
            {
                var candidate = context.Candidates.SingleOrDefault(value => value.SourceOpportunityId == plan.SourceOpportunityId);
                reconciled = orchestration.ApplyRefresh(reconciled, candidate, context.Recommendations?.State == PrimaryRecommendationState.Ready);
            }
            try
            {
                await repository.SaveAsync(context.Profile.Id, reconciled, cancellationToken).ConfigureAwait(false);
                updated.Add(reconciled with { Revision = reconciled.Revision + 1 });
            }
            catch (PlanConcurrencyException)
            {
                var latest = await FindReconciliationCandidateAsync(context.Profile.Id, plan.Id, cancellationToken).ConfigureAwait(false);
                if (latest is not null) updated.Add(latest);
            }
        }
        return updated;
    }

    private async Task<IReadOnlyList<PlanRecord>> ApplyPlanRefreshAsync(Context context, CancellationToken cancellationToken)
    {
        var updated = new List<PlanRecord>(context.Plans.Count);
        foreach (var plan in context.Plans)
        {
            var candidate = context.Candidates.SingleOrDefault(value => value.SourceOpportunityId == plan.SourceOpportunityId);
            var refreshed = orchestration.ApplyRefresh(plan, candidate, context.Recommendations?.State == PrimaryRecommendationState.Ready);
            if (ReferenceEquals(refreshed, plan) || refreshed == plan)
            {
                updated.Add(plan);
                continue;
            }

            try
            {
                await repository.SaveAsync(context.Profile.Id, refreshed, cancellationToken).ConfigureAwait(false);
                updated.Add(refreshed with { Revision = refreshed.Revision + 1 });
            }
            catch (PlanConcurrencyException)
            {
                var latest = await FindReconciliationCandidateAsync(context.Profile.Id, plan.Id, cancellationToken).ConfigureAwait(false);
                if (latest is not null) updated.Add(latest);
            }
        }
        return updated;
    }

    private async Task<Context> RequireContextAsync(CancellationToken cancellationToken) =>
        await BuildDecisionContextAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Plan account evidence is unavailable.");

    private async Task<PlanRecord?> FindAsync(long profileId, string planId, CancellationToken cancellationToken) =>
        (await repository.GetStartedAsync(profileId, cancellationToken).ConfigureAwait(false)).SingleOrDefault(plan => plan.Id == planId);

    private async Task<PlanRecord?> FindByCandidateAsync(long profileId, string candidateId, CancellationToken cancellationToken) =>
        (await repository.GetStartedAsync(profileId, cancellationToken).ConfigureAwait(false)).SingleOrDefault(plan => plan.SourceOpportunityId == candidateId);

    private async Task<PlanRecord?> FindReconciliationCandidateAsync(long profileId, string planId, CancellationToken cancellationToken) =>
        (await repository.GetReconciliationCandidatesAsync(profileId, cancellationToken).ConfigureAwait(false)).SingleOrDefault(plan => plan.Id == planId);

    private static bool IsActionable(PrimaryRecommendationRecord record) => record.Action is PrimaryRecommendationAction.Buy or PrimaryRecommendationAction.BuySmall or PrimaryRecommendationAction.UpdateBid or PrimaryRecommendationAction.CancelBid or PrimaryRecommendationAction.List or PrimaryRecommendationAction.Reduce or PrimaryRecommendationAction.SellPartial or PrimaryRecommendationAction.Sell;

    // Unknown historical basis forbids new capital allocation, but it does not
    // erase fresh proof of an existing asset. Only actions that can reduce an
    // exposure may bypass portfolio sizing; their inventory and fee resources
    // still flow through the ordinary verified-resource gates below.
    private static bool CanExecuteWithoutPortfolioSizing(PlanCandidate candidate) =>
        candidate.Steps.Count > 0 && candidate.Steps.All(step => step.Action is
            PlanStepAction.CancelBuyOrder or PlanStepAction.List or PlanStepAction.SellNow);

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

    private async Task<EvidenceCapture> ReadEvidenceAsync(AccountProfile profile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = await profiles.GetLatestCurrentOrderSnapshotAsync(profile, cancellationToken).ConfigureAwait(false);
        if (current is null || profile.LastSuccessfulSyncAtUtc is null)
            return new EvidenceCapture([], null, new HashSet<PlanEvidenceKind>());

        var captured = current.ObservedAtUtc;
        var entries = new List<PlanVerifiedEvidence>();
        foreach (var order in current.Orders)
        {
            var kind = order.Side == PersonalTradingPostSide.Buy ? PlanEvidenceKind.BuyOrder : PlanEvidenceKind.SellListing;
            entries.Add(new PlanVerifiedEvidence(
                $"{kind}:{order.ExternalOrderId}", kind, order.ItemId, order.Quantity, new Money(order.UnitPriceInCopper),
                order.CreatedAtUtc, captured, order.ExternalOrderId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        var completed = await profiles.GetCompletedTransactionsAsync(profile, cancellationToken).ConfigureAwait(false);
        foreach (var stored in completed)
        {
            var transaction = stored.Transaction;
            var kind = transaction.Side == PersonalTradingPostSide.Buy ? PlanEvidenceKind.CompletedBuy : PlanEvidenceKind.CompletedSell;
            entries.Add(new PlanVerifiedEvidence(
                $"{kind}:{transaction.ExternalTransactionId}", kind, transaction.ItemId, transaction.Quantity,
                new Money(transaction.UnitPriceInCopper), transaction.CompletedAtUtc, captured,
                transaction.ExternalTransactionId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        var completeKinds = new HashSet<PlanEvidenceKind>
        {
            PlanEvidenceKind.BuyOrder,
            PlanEvidenceKind.SellListing,
            PlanEvidenceKind.CompletedBuy,
            PlanEvidenceKind.CompletedSell,
        };
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
    private sealed record CandidateBuild(IReadOnlyList<PlanCandidate> Candidates, CraftingOpportunityTiming? CraftingTiming);
    private sealed record CandidateSelection(bool PortfolioSizingUnavailable, IReadOnlyList<PlanCandidate> SafeWithoutPortfolioSizing,
        IReadOnlyList<PlanCandidate> HardEligible, IReadOnlyList<PlanCandidate> ResourceEligible, PlanBundleSelection Selection,
        Money AvailableCash, Money HardReserve);
    private static long Milliseconds(TimeSpan elapsed) => Math.Max(0, (long)elapsed.TotalMilliseconds);

    private static PlanEndpointResponse Complete(object payload, PlanDecisionTiming timing, Stopwatch? selectionTimer = null)
    {
        if (selectionTimer is null) return new(payload, timing);
        selectionTimer.Stop();
        return new(payload, timing with { ResourceSelectionMilliseconds = Milliseconds(selectionTimer.Elapsed) });
    }

    private sealed record Context(AccountProfile Profile, AccountPortfolioSnapshot Snapshot, IReadOnlyDictionary<string, long> VerifiedQuantities, PrimaryRecommendationResult? Recommendations, IReadOnlyList<PlanCandidate> Candidates, bool AccountEvidenceAvailable, IReadOnlyCollection<PlanVerifiedEvidence> Evidence, DateTimeOffset? EvidenceCapturedAtUtc, IReadOnlySet<PlanEvidenceKind> CompleteEvidenceKinds, IReadOnlyList<PlanRecord> Plans, bool ReusedDecision, string DecisionCacheState, PlanDecisionTiming Timing = null!);
}

internal sealed record PlanDecisionSnapshot(
    AccountProfile Profile,
    AccountPortfolioSnapshot Snapshot,
    IReadOnlyDictionary<string, long> VerifiedQuantities,
    PrimaryRecommendationResult? Recommendations,
    IReadOnlyList<PlanRecord> Plans,
    IReadOnlyList<PlanCandidate> Candidates,
    bool AccountEvidenceAvailable,
    PlanDecisionTiming Timing,
    DateTimeOffset CachedAtUtc,
    PlanDecisionSelectionTrace? SelectionTrace = null);

/// <summary>Sanitized selection lineage retained with one decision projection.</summary>
internal sealed record PlanDecisionSelectionTrace(
    int GeneratedCandidates,
    int HardEligibleCandidates,
    int ResourceEligibleCandidates,
    int SelectedCandidates,
    bool PortfolioSizingUnavailable,
    IReadOnlyList<string> ExcludedCandidateIds,
    IReadOnlySet<string> ExecutableSignalCandidateIds,
    string? UnavailableReason)
{
    internal static PlanDecisionSelectionTrace Unavailable(int generatedCandidates, string reason) => new(
        generatedCandidates, 0, 0, 0, true, [], new HashSet<string>(StringComparer.Ordinal), reason);
}
