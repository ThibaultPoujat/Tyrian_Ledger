using System.Globalization;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Application.Plans;
using Gw2Tp.Application.Recommendations;
using Gw2Tp.Application.Time;
using Gw2Tp.Domain.Finance;
using Microsoft.Extensions.Hosting;

namespace Gw2Tp.Web.Hosting;

internal enum DecisionLoopRunState
{
    NeverRun = 1,
    Running,
    Ready,
    Degraded,
}

internal enum DecisionLoopSourceState
{
    Unknown = 1,
    Fresh,
    Failed,
}

internal enum DecisionLoopNotificationKind
{
    Signal = 1,
    Plan,
}

internal enum DecisionLoopNotificationReason
{
    SignalEvidence = 1,
    PlanReady,
    PlanRecheck,
    PlanReconciliation,
}

internal sealed record DecisionLoopSourceStatus(
    DecisionLoopSourceState State,
    DateTimeOffset? LastSuccessfulAtUtc,
    string? ErrorCode);

internal sealed record DecisionLoopNotification(
    string Id,
    string Identity,
    DecisionLoopNotificationKind Kind,
    string Route,
    string ActionCode,
    int ItemId,
    string ItemName,
    int Quantity,
    Money? UnitPrice,
    PrimaryRecommendationReasonCode? SignalReasonCode,
    DecisionLoopNotificationReason Reason,
    string? PlanId,
    bool IsUrgent,
    DateTimeOffset CreatedAtUtc,
    string Fingerprint);

internal sealed record DecisionLoopPreferences(bool Enabled);

internal sealed record NotificationPreferenceRequest(bool Enabled);

internal sealed record DecisionLoopStatus(
    DecisionLoopRunState State,
    string? AccountScopeId,
    DateTimeOffset? LastCycleAtUtc,
    DateTimeOffset? NextCycleAtUtc,
    int ConsecutiveFailures,
    string? LastErrorCode,
    DecisionLoopSourceStatus Market,
    DecisionLoopSourceStatus Account,
    DecisionLoopSourceStatus History,
    bool NotificationsEnabled,
    IReadOnlyList<DecisionLoopNotification> Notifications,
    PrimaryRecommendationResult? Recommendations);

internal sealed record DecisionLoopRunResult(
    bool IsSuccess,
    PersonalTradingPostSynchronizationResult? Synchronization,
    PrimaryRecommendationResult? Recommendations,
    DecisionLoopStatus Status);

internal sealed record DecisionLoopSchedulerSettings(
    TimeSpan CycleInterval,
    TimeSpan MinimumRetryInterval,
    TimeSpan MaximumRetryInterval)
{
    internal static DecisionLoopSchedulerSettings Default { get; } = new(
        TimeSpan.FromMinutes(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(15));

    internal void Validate()
    {
        if (CycleInterval <= TimeSpan.Zero || MinimumRetryInterval <= TimeSpan.Zero ||
            MaximumRetryInterval < MinimumRetryInterval || MaximumRetryInterval > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(DecisionLoopSchedulerSettings));
        }
    }
}

internal interface IContinuousDecisionLoopService
{
    Task<DecisionLoopRunResult> RunNowAsync(CancellationToken cancellationToken = default);

    DecisionLoopStatus GetStatus();

    DecisionLoopPreferences GetPreferences(string accountScopeId);

    void SetNotificationsEnabled(string accountScopeId, bool enabled);

    bool Acknowledge(string accountScopeId, string notificationId);
}

internal sealed class ContinuousDecisionLoopService : IContinuousDecisionLoopService
{
    private readonly IPersonalTradingPostSynchronizationService synchronization;
    private readonly IAccountCraftingSnapshotService craftingSnapshots;
    private readonly PlanEndpointService plans;
    private readonly IClock clock;
    private readonly IHostApplicationLifetime applicationLifetime;
    private readonly object stateGate = new();
    private readonly DecisionLoopNotificationLedger notificationLedger = new();
    private Task<DecisionLoopRunResult>? activeRun;
    private DecisionLoopStatus status = InitialStatus();

    public ContinuousDecisionLoopService(
        IPersonalTradingPostSynchronizationService synchronization,
        IAccountCraftingSnapshotService craftingSnapshots,
        PlanEndpointService plans,
        IClock clock,
        IHostApplicationLifetime applicationLifetime)
    {
        this.synchronization = synchronization ?? throw new ArgumentNullException(nameof(synchronization));
        this.craftingSnapshots = craftingSnapshots ?? throw new ArgumentNullException(nameof(craftingSnapshots));
        this.plans = plans ?? throw new ArgumentNullException(nameof(plans));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.applicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
    }

    public Task<DecisionLoopRunResult> RunNowAsync(CancellationToken cancellationToken = default)
    {
        Task<DecisionLoopRunResult> run;
        lock (stateGate)
        {
            if (activeRun is null)
            {
                SetStatus(status with { State = DecisionLoopRunState.Running });
                activeRun = RunCoreAsync(applicationLifetime.ApplicationStopping);
                _ = ClearActiveRunAsync(activeRun);
            }

            run = activeRun;
        }

        return run.WaitAsync(cancellationToken);
    }

    public DecisionLoopStatus GetStatus()
    {
        lock (stateGate)
        {
            return status with { Notifications = status.Notifications.ToArray() };
        }
    }

    internal void SetNextCycleAtUtc(DateTimeOffset? nextCycleAtUtc)
    {
        if (nextCycleAtUtc is { } value && value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The next decision-loop cycle must be UTC.", nameof(nextCycleAtUtc));
        }

        lock (stateGate)
        {
            status = status with { NextCycleAtUtc = nextCycleAtUtc };
        }
    }

    public DecisionLoopPreferences GetPreferences(string accountScopeId)
        => notificationLedger.GetPreferences(accountScopeId);

    public void SetNotificationsEnabled(string accountScopeId, bool enabled)
    {
        notificationLedger.SetEnabled(accountScopeId, enabled);
        lock (stateGate)
        {
            if (!string.Equals(status.AccountScopeId, accountScopeId, StringComparison.Ordinal)) return;
            status = status with
            {
                NotificationsEnabled = enabled,
                Notifications = notificationLedger.Pending(accountScopeId),
            };
        }
    }

    public bool Acknowledge(string accountScopeId, string notificationId)
    {
        var acknowledged = notificationLedger.Acknowledge(accountScopeId, notificationId);
        if (acknowledged)
        {
            lock (stateGate)
            {
                if (string.Equals(status.AccountScopeId, accountScopeId, StringComparison.Ordinal))
                    status = status with { Notifications = notificationLedger.Pending(accountScopeId) };
            }
        }

        return acknowledged;
    }

    private async Task<DecisionLoopRunResult> RunCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await RunCoreBodyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            var attemptedAtUtc = RequireUtc(clock.UtcNow);
            var error = exception.GetType().Name;
            SetFailedStatus(attemptedAtUtc, error);
            return new(false, null,
                PrimaryRecommendationResult.Unavailable(PrimaryRecommendationState.EvidenceUnavailable, error, Program.DefaultRecommendationPolicies()),
                GetStatus());
        }
    }

    private async Task<DecisionLoopRunResult> RunCoreBodyAsync(CancellationToken cancellationToken)
    {
        var synchronizationResult = await synchronization.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
        if (!synchronizationResult.IsSuccess)
        {
            var error = synchronizationResult.IsPersistenceFailure
                ? "PersistenceFailure"
                : synchronizationResult.ErrorCategory?.ToString();
            var unavailable = PrimaryRecommendationResult.Unavailable(
                PrimaryRecommendationState.AccountUnavailable,
                error,
                Program.DefaultRecommendationPolicies());
            SetFailedStatus(synchronizationResult.AttemptedAtUtc, error);
            return new(false, synchronizationResult, unavailable, GetStatus());
        }

        try
        {
            // Crafting is refreshed as part of the same cycle, but its failure
            // remains scoped to crafting candidates. Trading recommendations
            // still use the complete personal snapshot below.
            await craftingSnapshots.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The planner will retain only a still-fresh, typed crafting
            // snapshot. It never treats a failed read as negative evidence.
        }

        var decision = await plans.GetDecisionSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (decision?.Recommendations is null)
        {
            SetFailedStatus(synchronizationResult.AttemptedAtUtc, "DecisionGenerationFailed");
            return new(false, synchronizationResult, null, GetStatus());
        }

        var observedAt = RequireUtc(clock.UtcNow);
        var notificationObservation = notificationLedger.Observe(
            decision.Profile.AccountScopeId,
            BuildNotifications(decision.Recommendations, decision.Plans, observedAt));
        var historyObservedAt = decision.Recommendations.Actions
            .Select(action => action.History?.LastObservedAtUtc)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .OrderByDescending(value => value)
            .FirstOrDefault();
        if (decision.Recommendations.State != PrimaryRecommendationState.Ready)
        {
            var error = decision.Recommendations.EvidenceError ?? decision.Recommendations.State.ToString();
            SetDegradedStatus(observedAt, decision.Recommendations, synchronizationResult, historyObservedAt,
                decision.Profile.AccountScopeId,
                notificationLedger.GetPreferences(decision.Profile.AccountScopeId).Enabled, notificationObservation.Pending, error);
            return new(false, synchronizationResult, decision.Recommendations, GetStatus());
        }

        SetReadyStatus(observedAt, decision.Recommendations, synchronizationResult, historyObservedAt,
            decision.Profile.AccountScopeId,
            notificationLedger.GetPreferences(decision.Profile.AccountScopeId).Enabled, notificationObservation.Pending);
        return new(true, synchronizationResult, decision.Recommendations, GetStatus());
    }

    internal static IReadOnlyList<DecisionLoopNotification> BuildNotifications(
        PrimaryRecommendationResult recommendations,
        IReadOnlyList<PlanRecord> plans,
        DateTimeOffset observedAtUtc)
    {
        var current = new Dictionary<string, DecisionLoopNotification>(StringComparer.Ordinal);
        foreach (var action in recommendations.Actions.Where(IsActionable))
        {
            var identity = $"signal:{action.Source}:{action.OrderId ?? action.ItemId.ToString(CultureInfo.InvariantCulture)}";
            var fingerprint = SignalFingerprint(action);
            var reason = action.Reasons.FirstOrDefault(value => value.Code != PrimaryRecommendationReasonCode.ReadOnlyManualAction)?.Code
                ?? PrimaryRecommendationReasonCode.EvidenceMissing;
            current[identity] = new DecisionLoopNotification(
                NotificationId(identity), identity, DecisionLoopNotificationKind.Signal, "signals",
                action.Action.ToString(), action.ItemId, action.ItemName, action.Quantity, ExecutionPrice(action),
                reason, DecisionLoopNotificationReason.SignalEvidence, null,
                action.Action == PrimaryRecommendationAction.CancelBid,
                observedAtUtc, fingerprint);
        }

        foreach (var plan in plans.Where(value => value.State is PlanState.InProgress or PlanState.RecheckRequired or PlanState.ReconciliationRequired))
        {
            var step = plan.Steps.FirstOrDefault(value => value.State == PlanStepState.Current);
            var reason = plan.State switch
            {
                PlanState.RecheckRequired => DecisionLoopNotificationReason.PlanRecheck,
                PlanState.ReconciliationRequired => DecisionLoopNotificationReason.PlanReconciliation,
                _ => DecisionLoopNotificationReason.PlanReady,
            };
            if (step is null && reason == DecisionLoopNotificationReason.PlanReady) continue;
            var stepId = step?.Id ?? "reconciliation";
            var identity = $"plan:{plan.Id}:{stepId}";
            var fingerprint = $"{plan.State}|{step?.Action}|{step?.ItemId}|{step?.Quantity}|{step?.UnitPrice?.Copper.ToString(CultureInfo.InvariantCulture)}";
            current[identity] = new DecisionLoopNotification(
                NotificationId(identity), identity, DecisionLoopNotificationKind.Plan, "plans",
                step?.Action.ToString() ?? "REVIEW", step?.ItemId ?? 0, step?.ItemName ?? "Plan", step?.Quantity ?? 0,
                step?.UnitPrice, null, reason, plan.Id,
                reason is DecisionLoopNotificationReason.PlanRecheck or DecisionLoopNotificationReason.PlanReconciliation,
                observedAtUtc, fingerprint);
        }

        return current.Values.ToArray();
    }

    private void SetReadyStatus(
        DateTimeOffset observedAtUtc,
        PrimaryRecommendationResult recommendations,
        PersonalTradingPostSynchronizationResult synchronizationResult,
        DateTimeOffset? historyObservedAtUtc,
        string accountScopeId,
        bool notificationsEnabled,
        IReadOnlyList<DecisionLoopNotification> notifications)
    {
        lock (stateGate)
        {
            status = status with
            {
                State = DecisionLoopRunState.Ready,
                AccountScopeId = accountScopeId,
                LastCycleAtUtc = observedAtUtc,
                ConsecutiveFailures = 0,
                LastErrorCode = null,
                NotificationsEnabled = notificationsEnabled,
                Notifications = notifications,
                Recommendations = recommendations,
                Market = new DecisionLoopSourceStatus(recommendations.ScannerObservedAtUtc is null ? DecisionLoopSourceState.Failed : DecisionLoopSourceState.Fresh, recommendations.ScannerObservedAtUtc, recommendations.ScannerObservedAtUtc is null ? recommendations.EvidenceError : null),
                Account = new DecisionLoopSourceStatus(DecisionLoopSourceState.Fresh, synchronizationResult.AttemptedAtUtc, null),
                History = new DecisionLoopSourceStatus(historyObservedAtUtc is null ? DecisionLoopSourceState.Unknown : DecisionLoopSourceState.Fresh, historyObservedAtUtc, null),
            };
        }
    }

    private void SetDegradedStatus(
        DateTimeOffset observedAtUtc,
        PrimaryRecommendationResult recommendations,
        PersonalTradingPostSynchronizationResult synchronizationResult,
        DateTimeOffset? historyObservedAtUtc,
        string accountScopeId,
        bool notificationsEnabled,
        IReadOnlyList<DecisionLoopNotification> notifications,
        string errorCode)
    {
        lock (stateGate)
        {
            status = status with
            {
                State = DecisionLoopRunState.Degraded,
                AccountScopeId = accountScopeId,
                LastCycleAtUtc = observedAtUtc,
                ConsecutiveFailures = status.ConsecutiveFailures + 1,
                LastErrorCode = errorCode,
                NotificationsEnabled = notificationsEnabled,
                Notifications = notifications,
                Recommendations = recommendations,
                Market = new DecisionLoopSourceStatus(
                    recommendations.ScannerObservedAtUtc is not null && recommendations.EvidenceError is null
                        ? DecisionLoopSourceState.Fresh
                        : DecisionLoopSourceState.Failed,
                    recommendations.ScannerObservedAtUtc,
                    recommendations.EvidenceError),
                Account = new DecisionLoopSourceStatus(DecisionLoopSourceState.Fresh, synchronizationResult.AttemptedAtUtc, null),
                History = new DecisionLoopSourceStatus(historyObservedAtUtc is null ? DecisionLoopSourceState.Unknown : DecisionLoopSourceState.Fresh, historyObservedAtUtc, null),
            };
        }
    }

    private void SetFailedStatus(DateTimeOffset attemptedAtUtc, string? errorCode)
    {
        lock (stateGate)
        {
            status = status with
            {
                State = DecisionLoopRunState.Degraded,
                AccountScopeId = null,
                LastCycleAtUtc = attemptedAtUtc,
                ConsecutiveFailures = status.ConsecutiveFailures + 1,
                LastErrorCode = errorCode,
                Account = new DecisionLoopSourceStatus(DecisionLoopSourceState.Failed, status.Account.LastSuccessfulAtUtc, errorCode),
                Recommendations = null,
                Notifications = [],
            };
        }
    }

    private void SetStatus(DecisionLoopStatus next)
    {
        lock (stateGate)
        {
            status = next;
        }
    }

    private async Task ClearActiveRunAsync(Task<DecisionLoopRunResult> run)
    {
        try { await run.ConfigureAwait(false); }
        catch (OperationCanceledException) when (applicationLifetime.ApplicationStopping.IsCancellationRequested) { }
        catch { }
        finally
        {
            lock (stateGate)
            {
                if (ReferenceEquals(activeRun, run)) activeRun = null;
            }
        }
    }

    private static bool IsActionable(PrimaryRecommendationRecord action) => action.Action is
        PrimaryRecommendationAction.Buy or PrimaryRecommendationAction.BuySmall or PrimaryRecommendationAction.UpdateBid or
        PrimaryRecommendationAction.CancelBid or PrimaryRecommendationAction.List or PrimaryRecommendationAction.Reduce or
        PrimaryRecommendationAction.SellPartial or PrimaryRecommendationAction.Sell;

    private static Money? ExecutionPrice(PrimaryRecommendationRecord action) => action.Action switch
    {
        PrimaryRecommendationAction.Buy or PrimaryRecommendationAction.BuySmall or PrimaryRecommendationAction.UpdateBid => action.Prices.PlannedBid,
        PrimaryRecommendationAction.List => action.Prices.PlannedListPrice,
        PrimaryRecommendationAction.Reduce or PrimaryRecommendationAction.SellPartial or PrimaryRecommendationAction.Sell => action.Prices.ImmediateSalePriceRange?.LowestUnitPrice ?? action.Prices.LowestSell,
        _ => action.Prices.CurrentOrderUnitPrice,
    };

    private static string SignalFingerprint(PrimaryRecommendationRecord action) => string.Join('|',
        action.Action,
        action.Source,
        action.OrderId ?? string.Empty,
        action.ItemId.ToString(CultureInfo.InvariantCulture),
        action.Quantity.ToString(CultureInfo.InvariantCulture),
        action.Prices.PlannedBid?.Copper.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        action.Prices.PlannedListPrice?.Copper.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        action.Prices.CurrentOrderUnitPrice?.Copper.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        ExecutionPrice(action)?.Copper.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

    private static string NotificationId(string identity) => identity.Replace(':', '_');

    private static DateTimeOffset RequireUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero) throw new ArgumentException("Decision-loop timestamps must be UTC.", nameof(value));
        return value;
    }

    private static DecisionLoopStatus InitialStatus() => new(
        DecisionLoopRunState.NeverRun, null, null, null, 0, null,
        new(DecisionLoopSourceState.Unknown, null, null),
        new(DecisionLoopSourceState.Unknown, null, null),
        new(DecisionLoopSourceState.Unknown, null, null),
        true, [], null);

}

internal sealed class ContinuousDecisionLoopHostedService(
    IContinuousDecisionLoopService loop,
    DecisionLoopSchedulerSettings settings,
    IClock clock,
    IDecisionLoopDelay delay) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        settings.Validate();
        var failures = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = RequireUtc(clock.UtcNow);
                var wait = failures == 0 ? settings.CycleInterval : RetryDelay(failures);
                SetNextCycle(loop, now + wait);
                await delay.DelayAsync(wait, stoppingToken).ConfigureAwait(false);
                if (stoppingToken.IsCancellationRequested) break;

                var result = await loop.RunNowAsync(stoppingToken).ConfigureAwait(false);
                failures = result.IsSuccess ? 0 : Math.Min(failures + 1, 30);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown is not an operational failure.
        }
        finally
        {
            SetNextCycle(loop, null);
        }

        TimeSpan RetryDelay(int count)
        {
            var multiplier = 1L << Math.Min(count - 1, 30);
            var milliseconds = Math.Min(settings.MinimumRetryInterval.TotalMilliseconds * multiplier, settings.MaximumRetryInterval.TotalMilliseconds);
            return TimeSpan.FromMilliseconds(milliseconds);
        }
    }

    private static void SetNextCycle(IContinuousDecisionLoopService service, DateTimeOffset? nextCycleAtUtc)
    {
        if (service is not ContinuousDecisionLoopService concrete) return;
        concrete.SetNextCycleAtUtc(nextCycleAtUtc);
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero
        ? value
        : throw new ArgumentException("Decision-loop timestamps must be UTC.", nameof(value));
}

internal interface IDecisionLoopDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class SystemDecisionLoopDelay : IDecisionLoopDelay
{
    internal static readonly SystemDecisionLoopDelay Instance = new();

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}
