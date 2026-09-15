using System.Numerics;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Accounting;

/// <summary>
/// Versioned policy for personal learning. The thresholds deliberately affect
/// only the strength label in M20-01; no recommendation score consumes them.
/// </summary>
public static class PersonalTurnoverPolicy
{
    public const int Version = 1;
    public const int MinimumKnownBasisSamples = 3;
    public static readonly TimeSpan MaximumEvidenceAge = TimeSpan.FromDays(90);

    public const string TimestampLimitation =
        "Completed-history created and purchased timestamps are source timestamps. Current-order polling supplies observed bounds only; an order disappearing from a poll is never treated as a completion.";
}

public enum PersonalTurnoverEvidenceStatus
{
    InsufficientCoverage,
    InsufficientSamples,
    InsufficientMetrics,
    Stale,
    Supported,
}

/// <summary>
/// One completed order whose duration comes from the source-provided created
/// and purchased timestamps. This is not a polling-derived fill estimate.
/// </summary>
public sealed record SourceTimestampFillDuration(
    long TransactionId,
    PersonalTradingPostSide Side,
    int ItemId,
    int Quantity,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc,
    TimeSpan Duration);

/// <summary>
/// A local confirmation window. The completion occurred after the last poll
/// that still contained the order and no later than the completed event's first
/// local import. It deliberately has no point fill timestamp.
/// </summary>
public sealed record IntervalCensoredCompletion(
    long OrderId,
    PersonalTradingPostSide Side,
    int ItemId,
    DateTimeOffset FirstObservedOpenAtUtc,
    DateTimeOffset LastObservedOpenAtUtc,
    DateTimeOffset FirstConfirmedAtUtc,
    int FirstObservedQuantity,
    int LastObservedQuantity,
    bool HasObservedQuantityReduction);

/// <summary>
/// An order disappearance or incompatible identifier which cannot safely be
/// interpreted as a completed fill.
/// </summary>
public sealed record UnknownOrderTiming(
    long OrderId,
    PersonalTradingPostSide Side,
    int ItemId,
    DateTimeOffset LastObservedOpenAtUtc,
    string Reason);

/// <summary>
/// A reduction observed between two retained open-order snapshots. It is kept
/// independently of completed-history correlation, so an active partial order
/// remains evidence without claiming a fill timestamp or completed quantity.
/// </summary>
public sealed record ObservedOrderQuantityReduction(
    long OrderId,
    PersonalTradingPostSide Side,
    int ItemId,
    DateTimeOffset EarlierObservedAtUtc,
    DateTimeOffset LaterObservedAtUtc,
    int EarlierQuantity,
    int LaterQuantity);

/// <summary>
/// An exact rational rate. Numerator and denominator are intentionally kept as
/// integers so no floating-point financial value crosses the application boundary.
/// </summary>
public sealed record ExactPersonalRate
{
    public ExactPersonalRate(BigInteger numerator, BigInteger denominator)
    {
        if (denominator <= BigInteger.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator));
        }

        Numerator = numerator;
        Denominator = denominator;
    }

    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }
}

/// <summary>
/// Known-basis realized outcomes over one measured interval. Sample count is
/// the number of distinct completed sells with entirely known basis, never FIFO
/// allocation fragments. Profit/day is net profit divided by elapsed measured
/// days. Capital turns is total matched acquisition basis divided by the
/// time-weighted average matched basis over that same interval. Average holding
/// duration is weighted by allocated acquisition basis. Both rates are exact
/// fractions, not rounded money.
/// </summary>
public sealed record PersonalCapitalTurnoverMetrics(
    int KnownBasisSampleCount,
    int KnownBasisQuantity,
    Money MatchedAcquisitionBasis,
    Money NetProfit,
    DateTimeOffset MeasuredStartUtc,
    DateTimeOffset MeasuredEndUtc,
    TimeSpan MeasuredDuration,
    TimeSpan TotalCapitalLockedDuration,
    TimeSpan AverageHoldingDuration,
    ExactPersonalRate? RealizedProfitPerDay,
    ExactPersonalRate? CapitalTurns);

/// <summary>
/// Evidence for one market. M20-02 must consume this item-scoped result rather
/// than a portfolio aggregate so unrelated one-off markets cannot manufacture
/// sufficient personal evidence.
/// </summary>
public sealed record PersonalItemTurnoverIntelligence(
    int ItemId,
    PersonalTurnoverEvidenceStatus Status,
    DateTimeOffset? LatestKnownBasisCompletionAtUtc,
    IReadOnlyList<SourceTimestampFillDuration> ExactFillDurations,
    IReadOnlyList<IntervalCensoredCompletion> IntervalCensoredCompletions,
    IReadOnlyList<UnknownOrderTiming> UnknownOrderTimings,
    IReadOnlyList<ObservedOrderQuantityReduction> ObservedQuantityReductions,
    PersonalCapitalTurnoverMetrics? Metrics);

public sealed record PersonalTurnoverRequest(
    DateTimeOffset AsOfUtc,
    long AccountProfileId,
    PersonalTradingPostHistoryCoverage HistoryCoverage,
    IReadOnlyList<StoredCompletedPersonalTradingPostTransaction> CompletedTransactions,
    IReadOnlyList<CurrentPersonalTradingPostOrderSnapshot> CurrentOrderObservations);

/// <summary>
/// Deterministic, reproducible learning output. The optional metrics are
/// retained for transparent low-sample/stale review but must not become a
/// strong personal-evidence input unless Status is Supported.
/// </summary>
public sealed record PersonalTurnoverIntelligence(
    int PolicyVersion,
    PersonalTurnoverEvidenceStatus Status,
    string TimestampLimitation,
    int MinimumKnownBasisSamples,
    DateTimeOffset? LatestKnownBasisCompletionAtUtc,
    IReadOnlyList<SourceTimestampFillDuration> ExactFillDurations,
    IReadOnlyList<IntervalCensoredCompletion> IntervalCensoredCompletions,
    IReadOnlyList<UnknownOrderTiming> UnknownOrderTimings,
    IReadOnlyList<ObservedOrderQuantityReduction> ObservedQuantityReductions,
    IReadOnlyList<PersonalItemTurnoverIntelligence> Items,
    PersonalCapitalTurnoverMetrics? Metrics);
