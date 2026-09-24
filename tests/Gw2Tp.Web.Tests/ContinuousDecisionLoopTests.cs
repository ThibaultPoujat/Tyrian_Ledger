using Gw2Tp.Application.Plans;
using Gw2Tp.Application.Recommendations;
using Gw2Tp.Application.Time;
using Gw2Tp.Domain.Finance;
using Gw2Tp.Web.Hosting;
using Xunit;

namespace Gw2Tp.Web.Tests;

public sealed class ContinuousDecisionLoopTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Identical_signal_is_emitted_once_and_material_change_rearms_it()
    {
        var ledger = new DecisionLoopNotificationLedger();
        var first = Notification("signal:newOpportunity:42", "price:25", Now);

        var initial = ledger.Observe("account-a", [first]);
        var repeated = ledger.Observe("account-a", [first with { CreatedAtUtc = Now.AddMinutes(5) }]);
        var changed = ledger.Observe("account-a", [first with { Fingerprint = "price:26", UnitPrice = new Money(26), CreatedAtUtc = Now.AddMinutes(10) }]);

        Assert.Single(initial.NewlyActionable);
        Assert.Empty(repeated.NewlyActionable);
        Assert.Single(changed.NewlyActionable);
        Assert.Single(changed.Pending);
    }

    [Fact]
    public void Disappearance_completion_and_reappearance_rearm_without_leaking_accounts()
    {
        var ledger = new DecisionLoopNotificationLedger();
        var notification = Notification("signal:inventory:42", "quantity:2", Now);

        _ = ledger.Observe("account-a", [notification]);
        var disappeared = ledger.Observe("account-a", []);
        var renewed = ledger.Observe("account-a", [notification with { CreatedAtUtc = Now.AddMinutes(20) }]);
        var otherAccount = ledger.Observe("account-b", [notification]);

        Assert.Empty(disappeared.Pending);
        Assert.Single(renewed.NewlyActionable);
        Assert.Single(otherAccount.NewlyActionable);
        Assert.True(ledger.Acknowledge("account-a", notification.Id));
        Assert.Empty(ledger.Pending("account-a"));
        Assert.Single(ledger.Pending("account-b"));
    }

    [Fact]
    public void Disabling_delivery_does_not_disable_state_and_reenable_delivers_current_action()
    {
        var ledger = new DecisionLoopNotificationLedger();
        var notification = Notification("signal:newOpportunity:7", "price:12", Now);

        ledger.SetEnabled("account-a", false);
        var suppressed = ledger.Observe("account-a", [notification]);
        ledger.SetEnabled("account-a", true);
        var restored = ledger.Observe("account-a", [notification with { CreatedAtUtc = Now.AddMinutes(5) }]);

        Assert.Empty(suppressed.NewlyActionable);
        Assert.Empty(suppressed.Pending);
        Assert.Single(restored.NewlyActionable);
        Assert.Single(restored.Pending);
    }

    [Fact]
    public void No_action_recommendations_do_not_create_notifications()
    {
        var result = new PrimaryRecommendationResult(
            PrimaryRecommendationState.Ready, null, Now, Now, Now, Now,
            new PrimaryRecommendationPolicies(1, 1, 1, 1, 1, 1, 1, 1, "FastFlip", "TradingPost"),
            null,
            [new PrimaryRecommendationRecord(
                PrimaryRecommendationAction.Wait,
                PrimaryRecommendationSource.BuyOrder,
                PrimaryRecommendationOrderState.Outbid,
                "order-1",
                42,
                "Objet",
                2,
                Money.Zero,
                new PrimaryRecommendationPriceState(null, null, null, null, null, null),
                null,
                null,
                null,
                null,
                [],
                [new PrimaryRecommendationReason(PrimaryRecommendationReasonCode.BidOutbid, "No action")])]);

        Assert.Empty(ContinuousDecisionLoopService.BuildNotifications(result, [], Now));
    }

    [Fact]
    public async Task Scheduler_waits_for_configured_interval_and_stops_on_cancellation()
    {
        var loop = new FixedLoopService();
        var delay = new BlockingDelay();
        var service = new ContinuousDecisionLoopHostedService(
            loop,
            new DecisionLoopSchedulerSettings(TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4)),
            new FixedClock(Now),
            delay);

        await service.StartAsync(CancellationToken.None);
        await delay.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(7), delay.RecordedDelay);
        await service.StopAsync(CancellationToken.None);
        Assert.Equal(0, loop.RunCount);
    }

    private static DecisionLoopNotification Notification(string id, string fingerprint, DateTimeOffset createdAtUtc) =>
        new(id, id, DecisionLoopNotificationKind.Signal, "signals", "Buy", 42, "Objet", 2, new Money(25),
            PrimaryRecommendationReasonCode.StrongEvidence, DecisionLoopNotificationReason.SignalEvidence, null, false,
            createdAtUtc, fingerprint);

    private sealed class FixedLoopService : IContinuousDecisionLoopService
    {
        public int RunCount { get; private set; }
        public Task<DecisionLoopRunResult> RunNowAsync(CancellationToken cancellationToken = default)
        {
            RunCount++;
            return Task.FromResult<DecisionLoopRunResult>(null!);
        }

        public DecisionLoopStatus GetStatus() => throw new NotSupportedException();
        public DecisionLoopPreferences GetPreferences(string accountScopeId) => new(true);
        public void SetNotificationsEnabled(string accountScopeId, bool enabled) { }
        public bool Acknowledge(string accountScopeId, string notificationId) => false;
    }

    private sealed class BlockingDelay : IDecisionLoopDelay
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TimeSpan RecordedDelay { get; private set; }

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            RecordedDelay = delay;
            Started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
