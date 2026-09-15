using Gw2Tp.Application.Accounting;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class PersonalTurnoverCalculatorTests
{
    private static readonly DateTimeOffset AsOfUtc = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly PersonalTurnoverCalculator calculator = new();

    [Fact]
    public void Uses_source_created_and_purchased_timestamps_as_exact_known_durations()
    {
        var result = calculator.Rebuild(Request(
            [Stored(Buy(101, 42, 100, 1, -10, -8)), Stored(Sell(102, 42, 200, 1, -8, -6))]));

        var duration = Assert.Single(result.ExactFillDurations, value => value.TransactionId == 101);
        Assert.Equal(TimeSpan.FromDays(2), duration.Duration);
        Assert.Contains("source timestamps", result.TimestampLimitation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Emits_an_interval_censored_completion_window_without_claiming_a_poll_timestamp_is_exact()
    {
        var created = AsOfUtc.AddDays(-10);
        var completed = new CompletedPersonalTradingPostTransaction(101, PersonalTradingPostSide.Buy, 42, 100, 5, created, AsOfUtc.AddDays(-8));
        var result = calculator.Rebuild(Request(
            [new StoredCompletedPersonalTradingPostTransaction(completed, AsOfUtc.AddDays(-7), AsOfUtc.AddDays(-7))],
            [Snapshot(-10, Order(101, PersonalTradingPostSide.Buy, 42, 100, 5, created)), Snapshot(-9, Order(101, PersonalTradingPostSide.Buy, 42, 100, 5, created))]));

        var window = Assert.Single(result.IntervalCensoredCompletions);
        Assert.Equal(AsOfUtc.AddDays(-9), window.LastObservedOpenAtUtc);
        Assert.Equal(AsOfUtc.AddDays(-7), window.FirstConfirmedAtUtc);
        Assert.NotEqual(window.LastObservedOpenAtUtc, window.FirstConfirmedAtUtc);
        Assert.Equal(PersonalTurnoverEvidenceStatus.InsufficientSamples, result.Status);
    }

    [Fact]
    public void Records_observed_quantity_reduction_as_partial_behavior_without_an_exact_fill_claim()
    {
        var created = AsOfUtc.AddDays(-10);
        var completed = new CompletedPersonalTradingPostTransaction(101, PersonalTradingPostSide.Buy, 42, 100, 5, created, AsOfUtc.AddDays(-8));
        var result = calculator.Rebuild(Request(
            [new StoredCompletedPersonalTradingPostTransaction(completed, AsOfUtc.AddDays(-7), AsOfUtc.AddDays(-7))],
            [Snapshot(-10, Order(101, PersonalTradingPostSide.Buy, 42, 100, 5, created)), Snapshot(-9, Order(101, PersonalTradingPostSide.Buy, 42, 100, 2, created))]));

        var window = Assert.Single(result.IntervalCensoredCompletions);
        Assert.True(window.HasObservedQuantityReduction);
        Assert.Equal(5, window.FirstObservedQuantity);
        Assert.Equal(2, window.LastObservedQuantity);
    }

    [Fact]
    public void Keeps_disappeared_orders_unknown_without_completed_history_evidence()
    {
        var created = AsOfUtc.AddDays(-5);
        var result = calculator.Rebuild(Request([], [
            Snapshot(-5, Order(101, PersonalTradingPostSide.Buy, 42, 100, 1, created)),
            Snapshot(-4),
        ]));

        var unknown = Assert.Single(result.UnknownOrderTimings);
        Assert.Equal(101, unknown.OrderId);
        Assert.Contains("disappeared", unknown.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Labels_low_sample_metrics_without_promoting_them_to_supported_evidence()
    {
        var result = calculator.Rebuild(Request(
            [Stored(Buy(101, 42, 100, 1, -10, -9)), Stored(Sell(102, 42, 200, 1, -9, -8))]));

        Assert.Equal(PersonalTurnoverEvidenceStatus.InsufficientSamples, result.Status);
        Assert.Equal(1, result.Metrics!.KnownBasisSampleCount);
        Assert.Equal(3, result.MinimumKnownBasisSamples);
        Assert.NotNull(result.Metrics.RealizedProfitPerDay);
    }

    [Fact]
    public void Withholds_turnover_metrics_when_continuous_history_coverage_is_unknown()
    {
        var result = calculator.Rebuild(new PersonalTurnoverRequest(
            AsOfUtc,
            1,
            new PersonalTradingPostHistoryCoverage(null, null),
            [Stored(Buy(101, 42, 100, 1, -10, -9)), Stored(Sell(102, 42, 200, 1, -9, -8))],
            []));

        Assert.Equal(PersonalTurnoverEvidenceStatus.InsufficientCoverage, result.Status);
        Assert.Null(result.Metrics);
    }

    [Fact]
    public void Labels_old_sufficient_evidence_as_stale()
    {
        var result = calculator.Rebuild(Request(
        [
            Stored(Buy(101, 42, 100, 1, -200, -199)), Stored(Sell(102, 42, 200, 1, -199, -198)),
            Stored(Buy(103, 43, 100, 1, -197, -196)), Stored(Sell(104, 43, 200, 1, -196, -195)),
            Stored(Buy(105, 44, 100, 1, -194, -193)), Stored(Sell(106, 44, 200, 1, -193, -192)),
        ]));

        Assert.Equal(PersonalTurnoverEvidenceStatus.Stale, result.Status);
        Assert.Equal(3, result.Metrics!.KnownBasisSampleCount);
    }

    [Fact]
    public void Produces_supported_turnover_metrics_for_recent_known_basis_samples()
    {
        var result = calculator.Rebuild(Request(
        [
            Stored(Buy(101, 42, 100, 1, -10, -9)), Stored(Sell(102, 42, 200, 1, -9, -8)),
            Stored(Buy(103, 43, 100, 1, -7, -6)), Stored(Sell(104, 43, 200, 1, -6, -5)),
            Stored(Buy(105, 44, 100, 1, -4, -3)), Stored(Sell(106, 44, 200, 1, -3, -2)),
        ]));

        Assert.Equal(PersonalTurnoverEvidenceStatus.Supported, result.Status);
        Assert.Equal(3, result.Metrics!.KnownBasisSampleCount);
        Assert.Equal(new Money(300), result.Metrics.MatchedAcquisitionBasis);
        Assert.NotNull(result.Metrics.RealizedProfitPerDay);
        Assert.NotNull(result.Metrics.CapitalTurns);
    }

    [Fact]
    public void Is_deterministic_for_reordered_transactions_and_multi_observation_evidence()
    {
        var created = AsOfUtc.AddDays(-10);
        var transactions = new[]
        {
            Stored(Buy(101, 42, 100, 1, -10, -9)), Stored(Sell(102, 42, 200, 1, -9, -8)),
            Stored(Buy(103, 43, 100, 1, -7, -6)), Stored(Sell(104, 43, 200, 1, -6, -5)),
            Stored(Buy(105, 44, 100, 1, -4, -3)), Stored(Sell(106, 44, 200, 1, -3, -2)),
        };
        var observations = new[]
        {
            Snapshot(-10, Order(101, PersonalTradingPostSide.Buy, 42, 100, 1, created)),
            Snapshot(-9, Order(101, PersonalTradingPostSide.Buy, 42, 100, 1, created)),
        };

        var first = calculator.Rebuild(Request(transactions, observations));
        var second = calculator.Rebuild(Request(transactions.Reverse().ToArray(), observations.Reverse().ToArray()));

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.ExactFillDurations, second.ExactFillDurations);
        Assert.Equal(first.IntervalCensoredCompletions, second.IntervalCensoredCompletions);
        Assert.Equal(first.UnknownOrderTimings, second.UnknownOrderTimings);
        Assert.Equal(first.Metrics, second.Metrics);
    }

    [Fact]
    public void Rejects_non_utc_evidence_at_the_boundary()
    {
        var invalid = new CompletedPersonalTradingPostTransaction(101, PersonalTradingPostSide.Buy, 42, 100, 1,
            new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(1)), AsOfUtc.AddDays(-1));

        Assert.Throws<ArgumentException>(() => calculator.Rebuild(Request([Stored(invalid)])));
    }

    private static PersonalTurnoverRequest Request(
        IReadOnlyList<StoredCompletedPersonalTradingPostTransaction> transactions,
        IReadOnlyList<CurrentPersonalTradingPostOrderSnapshot>? observations = null) => new(
            AsOfUtc,
            1,
            new PersonalTradingPostHistoryCoverage(AsOfUtc.AddDays(-365), AsOfUtc),
            transactions,
            observations ?? []);

    private static StoredCompletedPersonalTradingPostTransaction Stored(CompletedPersonalTradingPostTransaction transaction) =>
        new(transaction, transaction.CompletedAtUtc.AddHours(1), transaction.CompletedAtUtc.AddHours(1));

    private static CompletedPersonalTradingPostTransaction Buy(long id, int itemId, int price, int quantity, int createdDays, int completedDays) =>
        new(id, PersonalTradingPostSide.Buy, itemId, price, quantity, AsOfUtc.AddDays(createdDays), AsOfUtc.AddDays(completedDays));

    private static CompletedPersonalTradingPostTransaction Sell(long id, int itemId, int price, int quantity, int createdDays, int completedDays) =>
        new(id, PersonalTradingPostSide.Sell, itemId, price, quantity, AsOfUtc.AddDays(createdDays), AsOfUtc.AddDays(completedDays));

    private static CurrentPersonalTradingPostOrder Order(long id, PersonalTradingPostSide side, int itemId, int price, int quantity, DateTimeOffset createdAtUtc) =>
        new(id, side, itemId, price, quantity, createdAtUtc);

    private static CurrentPersonalTradingPostOrderSnapshot Snapshot(int observedDays, params CurrentPersonalTradingPostOrder[] orders) =>
        new(AsOfUtc.AddDays(observedDays), orders);
}
