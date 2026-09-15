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
    public void Retains_quantity_reduction_for_an_active_order_without_completed_history_correlation()
    {
        var created = AsOfUtc.AddDays(-10);
        var result = calculator.Rebuild(Request([], [
            Snapshot(-10, Order(101, PersonalTradingPostSide.Buy, 42, 100, 5, created)),
            Snapshot(-9, Order(101, PersonalTradingPostSide.Buy, 42, 100, 2, created)),
        ]));

        var reduction = Assert.Single(result.ObservedQuantityReductions);
        Assert.Equal(5, reduction.EarlierQuantity);
        Assert.Equal(2, reduction.LaterQuantity);
        Assert.Empty(result.IntervalCensoredCompletions);
        Assert.Empty(result.UnknownOrderTimings);
        Assert.Single(Assert.Single(result.Items).ObservedQuantityReductions);
    }

    [Fact]
    public void Treats_a_disappearance_then_reappearance_under_one_identifier_as_unknown_without_bridging_reductions()
    {
        var created = AsOfUtc.AddDays(-10);
        var result = calculator.Rebuild(Request([], [
            Snapshot(-5, Order(101, PersonalTradingPostSide.Buy, 42, 100, 5, created)),
            Snapshot(-4),
            Snapshot(-3, Order(101, PersonalTradingPostSide.Buy, 42, 100, 2, created)),
        ]));

        Assert.Empty(result.ObservedQuantityReductions);
        var unknown = Assert.Single(result.UnknownOrderTimings);
        Assert.Contains("reappearing", unknown.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Preserves_consecutive_reductions_before_a_snapshot_gap_without_creating_a_cross_gap_reduction()
    {
        var created = AsOfUtc.AddDays(-10);
        var result = calculator.Rebuild(Request([], [
            Snapshot(-6, Order(101, PersonalTradingPostSide.Buy, 42, 100, 5, created)),
            Snapshot(-5, Order(101, PersonalTradingPostSide.Buy, 42, 100, 3, created)),
            Snapshot(-4),
            Snapshot(-3, Order(101, PersonalTradingPostSide.Buy, 42, 100, 2, created)),
        ]));

        var reduction = Assert.Single(result.ObservedQuantityReductions);
        Assert.Equal(5, reduction.EarlierQuantity);
        Assert.Equal(3, reduction.LaterQuantity);
        Assert.Single(result.UnknownOrderTimings);
    }

    [Fact]
    public void Restricts_confirmation_window_reductions_to_observations_before_confirmation()
    {
        var created = AsOfUtc.AddDays(-10);
        var completed = new CompletedPersonalTradingPostTransaction(101, PersonalTradingPostSide.Buy, 42, 100, 5, created, AsOfUtc.AddDays(-8));
        var result = calculator.Rebuild(Request(
            [new StoredCompletedPersonalTradingPostTransaction(completed, AsOfUtc.AddDays(-7), AsOfUtc.AddDays(-7))],
            [
                Snapshot(-10, Order(101, PersonalTradingPostSide.Buy, 42, 100, 5, created)),
                Snapshot(-9, Order(101, PersonalTradingPostSide.Buy, 42, 100, 5, created)),
                Snapshot(-6, Order(101, PersonalTradingPostSide.Buy, 42, 100, 2, created)),
            ]));

        Assert.False(Assert.Single(result.IntervalCensoredCompletions).HasObservedQuantityReduction);
        Assert.Single(result.ObservedQuantityReductions);
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
    public void Labels_empty_covered_history_as_insufficient_samples()
    {
        var result = calculator.Rebuild(Request([]));

        Assert.Equal(PersonalTurnoverEvidenceStatus.InsufficientSamples, result.Status);
        Assert.Empty(result.Items);
        Assert.Null(result.Metrics);
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
            Stored(Buy(103, 42, 100, 1, -197, -196)), Stored(Sell(104, 42, 200, 1, -196, -195)),
            Stored(Buy(105, 42, 100, 1, -194, -193)), Stored(Sell(106, 42, 200, 1, -193, -192)),
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
            Stored(Buy(103, 42, 100, 1, -7, -6)), Stored(Sell(104, 42, 200, 1, -6, -5)),
            Stored(Buy(105, 42, 100, 1, -4, -3)), Stored(Sell(106, 42, 200, 1, -3, -2)),
        ]));

        Assert.Equal(PersonalTurnoverEvidenceStatus.Supported, result.Status);
        Assert.Equal(3, result.Metrics!.KnownBasisSampleCount);
        Assert.Equal(new Money(300), result.Metrics.MatchedAcquisitionBasis);
        Assert.NotNull(result.Metrics.RealizedProfitPerDay);
        Assert.NotNull(result.Metrics.CapitalTurns);
        var item = Assert.Single(result.Items);
        Assert.Equal(PersonalTurnoverEvidenceStatus.Supported, item.Status);
        var distribution = Assert.IsType<PersonalRealizedRoiDistribution>(item.RealizedRoiDistribution);
        Assert.Equal(3, distribution.CompletedSaleCount);
        Assert.Equal(distribution.MinimumBasisPoints, distribution.MedianBasisPoints);
        Assert.Equal(distribution.MedianBasisPoints, distribution.MaximumBasisPoints);
        Assert.Equal(item.Metrics!.KnownBasisSampleCount, item.FullyKnownMetrics!.KnownBasisSampleCount);
    }

    [Fact]
    public void Does_not_promote_unrelated_one_off_markets_to_supported_evidence()
    {
        var result = calculator.Rebuild(Request(
        [
            Stored(Buy(101, 42, 100, 1, -10, -9)), Stored(Sell(102, 42, 200, 1, -9, -8)),
            Stored(Buy(103, 43, 100, 1, -7, -6)), Stored(Sell(104, 43, 200, 1, -6, -5)),
            Stored(Buy(105, 44, 100, 1, -4, -3)), Stored(Sell(106, 44, 200, 1, -3, -2)),
        ]));

        Assert.Equal(PersonalTurnoverEvidenceStatus.InsufficientSamples, result.Status);
        Assert.All(result.Items, item =>
        {
            Assert.Equal(PersonalTurnoverEvidenceStatus.InsufficientSamples, item.Status);
            Assert.Equal(1, item.Metrics!.KnownBasisSampleCount);
        });
    }

    [Fact]
    public void Counts_distinct_completed_sales_not_fifo_allocation_fragments_for_evidence_strength()
    {
        var result = calculator.Rebuild(Request(
        [
            Stored(Buy(101, 42, 100, 1, -14, -13)),
            Stored(Buy(102, 42, 100, 1, -12, -11)),
            Stored(Buy(103, 42, 100, 1, -10, -9)),
            Stored(Sell(104, 42, 200, 3, -8, -7)),
        ]));

        var metrics = Assert.Single(result.Items).Metrics!;
        Assert.Equal(PersonalTurnoverEvidenceStatus.InsufficientSamples, result.Status);
        Assert.Equal(1, metrics.KnownBasisSampleCount);
        Assert.Equal(3, metrics.KnownBasisQuantity);
    }

    [Fact]
    public void Excludes_partially_unknown_sales_from_evidence_strength_while_retaining_known_fragment_economics()
    {
        var result = calculator.Rebuild(Request(
        [
            Stored(Buy(101, 42, 100, 1, -20, -19)), Stored(Sell(102, 42, 200, 2, -18, -17)),
            Stored(Buy(103, 42, 100, 1, -16, -15)), Stored(Sell(104, 42, 200, 2, -14, -13)),
            Stored(Buy(105, 42, 100, 1, -12, -11)), Stored(Sell(106, 42, 200, 2, -10, -9)),
        ]));

        var metrics = result.Metrics!;
        Assert.Equal(PersonalTurnoverEvidenceStatus.InsufficientSamples, result.Status);
        Assert.Equal(0, metrics.KnownBasisSampleCount);
        Assert.Equal(3, metrics.KnownBasisQuantity);
        Assert.NotEqual(Money.Zero, metrics.NetProfit);
    }

    [Fact]
    public void Does_not_let_a_recent_partially_unknown_sale_make_stale_entirely_known_evidence_supported()
    {
        var result = calculator.Rebuild(Request(
        [
            Stored(Buy(101, 42, 100, 1, -200, -199)), Stored(Sell(102, 42, 200, 1, -199, -198)),
            Stored(Buy(103, 42, 100, 1, -197, -196)), Stored(Sell(104, 42, 200, 1, -196, -195)),
            Stored(Buy(105, 42, 100, 1, -194, -193)), Stored(Sell(106, 42, 200, 1, -193, -192)),
            Stored(Buy(107, 42, 100, 1, -4, -3)), Stored(Sell(108, 42, 200, 2, -3, -2)),
        ]));

        Assert.Equal(PersonalTurnoverEvidenceStatus.Stale, result.Status);
        Assert.Equal(AsOfUtc.AddDays(-2), result.Metrics!.MeasuredEndUtc);
        Assert.Equal(AsOfUtc.AddDays(-192), Assert.Single(result.Items).LatestKnownBasisCompletionAtUtc);
    }

    [Fact]
    public void Labels_sufficient_sales_without_computable_turnover_rates_as_insufficient_metrics()
    {
        var result = calculator.Rebuild(Request(
        [
            Stored(Buy(101, 42, 100, 1, -10, -10)), Stored(Sell(102, 42, 200, 1, -10, -10)),
            Stored(Buy(103, 42, 100, 1, -10, -10)), Stored(Sell(104, 42, 200, 1, -10, -10)),
            Stored(Buy(105, 42, 100, 1, -10, -10)), Stored(Sell(106, 42, 200, 1, -10, -10)),
        ]));

        Assert.Equal(PersonalTurnoverEvidenceStatus.InsufficientMetrics, result.Status);
        Assert.Equal(3, result.Metrics!.KnownBasisSampleCount);
        Assert.Null(result.Metrics.RealizedProfitPerDay);
        Assert.Null(result.Metrics.CapitalTurns);
    }

    [Fact]
    public void Calculates_exact_profit_per_day_and_time_weighted_capital_turns_for_unequal_basis_and_holding_intervals()
    {
        var result = calculator.Rebuild(Request(
        [
            Stored(Buy(101, 42, 100, 1, -11, -10)), Stored(Sell(102, 42, 200, 1, -10, -8)),
            Stored(Buy(103, 42, 200, 1, -8, -7)), Stored(Sell(104, 42, 500, 1, -7, -4)),
            Stored(Buy(105, 42, 300, 1, -4, -3)), Stored(Sell(106, 42, 400, 1, -3, -2)),
        ]));

        var metrics = result.Metrics!;
        Assert.Equal(new Money(600), metrics.MatchedAcquisitionBasis);
        Assert.Equal(new Money(335), metrics.NetProfit);
        Assert.Equal(new System.Numerics.BigInteger(335L * TimeSpan.TicksPerDay), metrics.RealizedProfitPerDay!.Numerator);
        Assert.Equal(new System.Numerics.BigInteger(8L * TimeSpan.TicksPerDay), metrics.RealizedProfitPerDay.Denominator);
        Assert.Equal(new System.Numerics.BigInteger(600L * 8 * TimeSpan.TicksPerDay), metrics.CapitalTurns!.Numerator);
        Assert.Equal(new System.Numerics.BigInteger(1_100L * TimeSpan.TicksPerDay), metrics.CapitalTurns.Denominator);
        Assert.Equal(TimeSpan.FromHours(44), metrics.AverageHoldingDuration);
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
        Assert.Equal(first.ObservedQuantityReductions, second.ObservedQuantityReductions);
        Assert.Equal(
            first.Items.Select(item => new
            {
                item.ItemId,
                item.Status,
                item.LatestKnownBasisCompletionAtUtc,
                ExactDurationCount = item.ExactFillDurations.Count,
                CensoredCompletionCount = item.IntervalCensoredCompletions.Count,
                UnknownTimingCount = item.UnknownOrderTimings.Count,
                QuantityReductionCount = item.ObservedQuantityReductions.Count,
                item.Metrics,
            }),
            second.Items.Select(item => new
            {
                item.ItemId,
                item.Status,
                item.LatestKnownBasisCompletionAtUtc,
                ExactDurationCount = item.ExactFillDurations.Count,
                CensoredCompletionCount = item.IntervalCensoredCompletions.Count,
                UnknownTimingCount = item.UnknownOrderTimings.Count,
                QuantityReductionCount = item.ObservedQuantityReductions.Count,
                item.Metrics,
            }));
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
