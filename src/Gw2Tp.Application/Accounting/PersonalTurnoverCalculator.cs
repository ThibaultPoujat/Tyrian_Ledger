using System.Numerics;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Accounting;

/// <summary>
/// Rebuilds personal timing and turnover evidence from immutable local
/// observations. It owns no gateway, persistence, clock, or ranking policy.
/// </summary>
public sealed class PersonalTurnoverCalculator
{
    private readonly PersonalPerformanceCalculator performanceCalculator = new();

    public PersonalTurnoverIntelligence Rebuild(PersonalTurnoverRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var exactDurations = request.CompletedTransactions
            .OrderBy(value => value.Transaction.CompletedAtUtc)
            .ThenBy(value => value.Transaction.ExternalTransactionId)
            .Select(value => new SourceTimestampFillDuration(
                value.Transaction.ExternalTransactionId,
                value.Transaction.Side,
                value.Transaction.ItemId,
                value.Transaction.Quantity,
                value.Transaction.CreatedAtUtc,
                value.Transaction.CompletedAtUtc,
                value.Transaction.CompletedAtUtc - value.Transaction.CreatedAtUtc))
            .ToArray();
        var (censored, unknown) = BuildObservationEvidence(request);

        if (request.HistoryCoverage.StartUtc is null || request.HistoryCoverage.EndUtc is null)
        {
            return Result(PersonalTurnoverEvidenceStatus.InsufficientCoverage, null, exactDurations, censored, unknown);
        }

        var coveredTransactions = request.CompletedTransactions
            .Where(value => value.Transaction.CompletedAtUtc >= request.HistoryCoverage.StartUtc.Value &&
                            value.Transaction.CompletedAtUtc <= request.HistoryCoverage.EndUtc.Value)
            .Select(value => new AccountScopedCompletedTransaction(request.AccountProfileId, value.Transaction))
            .ToArray();
        var performance = performanceCalculator.Rebuild(new PersonalPerformanceRequest(
            request.AsOfUtc,
            new PerformanceHistoryCoverage(request.HistoryCoverage.StartUtc.Value, request.HistoryCoverage.EndUtc.Value),
            coveredTransactions,
            []));
        var metrics = BuildMetrics(performance.KnownBasisSaleAllocations);
        if (metrics is null)
        {
            return Result(PersonalTurnoverEvidenceStatus.InsufficientSamples, null, exactDurations, censored, unknown);
        }

        var status = metrics.KnownBasisSampleCount < PersonalTurnoverPolicy.MinimumKnownBasisSamples
            ? PersonalTurnoverEvidenceStatus.InsufficientSamples
            : request.AsOfUtc - metrics.MeasuredEndUtc > PersonalTurnoverPolicy.MaximumEvidenceAge
                ? PersonalTurnoverEvidenceStatus.Stale
                : PersonalTurnoverEvidenceStatus.Supported;
        return Result(status, metrics, exactDurations, censored, unknown);
    }

    private static PersonalTurnoverIntelligence Result(
        PersonalTurnoverEvidenceStatus status,
        PersonalCapitalTurnoverMetrics? metrics,
        IReadOnlyList<SourceTimestampFillDuration> exactDurations,
        IReadOnlyList<IntervalCensoredCompletion> censored,
        IReadOnlyList<UnknownOrderTiming> unknown) => new(
            PersonalTurnoverPolicy.Version,
            status,
            PersonalTurnoverPolicy.TimestampLimitation,
            PersonalTurnoverPolicy.MinimumKnownBasisSamples,
            metrics?.MeasuredEndUtc,
            exactDurations,
            censored,
            unknown,
            metrics);

    private static (IReadOnlyList<IntervalCensoredCompletion> Censored, IReadOnlyList<UnknownOrderTiming> Unknown)
        BuildObservationEvidence(PersonalTurnoverRequest request)
    {
        var observations = request.CurrentOrderObservations
            .OrderBy(snapshot => snapshot.ObservedAtUtc)
            .ToArray();
        var completedById = request.CompletedTransactions.ToDictionary(value => value.Transaction.ExternalTransactionId);
        var snapshotsByOrder = observations
            .SelectMany(snapshot => snapshot.Orders.Select(order => new ObservedOrder(snapshot.ObservedAtUtc, order)))
            .GroupBy(value => value.Order.ExternalOrderId)
            .ToDictionary(group => group.Key, group => group.OrderBy(value => value.ObservedAtUtc).ToArray());
        var censored = new List<IntervalCensoredCompletion>();
        var unknown = new List<UnknownOrderTiming>();

        foreach (var (orderId, orderSnapshots) in snapshotsByOrder.OrderBy(value => value.Key))
        {
            var first = orderSnapshots[0];
            var last = orderSnapshots[^1];
            if (!completedById.TryGetValue(orderId, out var completed) ||
                !MatchesCompletedOrder(first.Order, completed.Transaction))
            {
                if (WasObservedToDisappear(orderId, last.ObservedAtUtc, observations))
                {
                    unknown.Add(new UnknownOrderTiming(orderId, first.Order.Side, first.Order.ItemId, last.ObservedAtUtc,
                        "The order later disappeared from polling without a compatible completed-history event."));
                }

                continue;
            }

            if (orderSnapshots.Any(value => !MatchesCompletedOrder(value.Order, completed.Transaction)))
            {
                unknown.Add(new UnknownOrderTiming(orderId, first.Order.Side, first.Order.ItemId, last.ObservedAtUtc,
                    "The same observed identifier has incompatible immutable order fields."));
                continue;
            }

            var lastBeforeConfirmation = orderSnapshots.LastOrDefault(value => value.ObservedAtUtc <= completed.FirstImportedAtUtc);
            if (lastBeforeConfirmation is null || lastBeforeConfirmation.ObservedAtUtc >= completed.FirstImportedAtUtc)
            {
                continue;
            }

            censored.Add(new IntervalCensoredCompletion(
                orderId,
                first.Order.Side,
                first.Order.ItemId,
                first.ObservedAtUtc,
                lastBeforeConfirmation.ObservedAtUtc,
                completed.FirstImportedAtUtc,
                first.Order.Quantity,
                lastBeforeConfirmation.Order.Quantity,
                orderSnapshots.Zip(orderSnapshots.Skip(1), (previous, next) => next.Order.Quantity < previous.Order.Quantity).Any(value => value)));
        }

        return (censored.AsReadOnly(), unknown.AsReadOnly());
    }

    private static bool MatchesCompletedOrder(CurrentPersonalTradingPostOrder order, CompletedPersonalTradingPostTransaction completed) =>
        order.Side == completed.Side &&
        order.ItemId == completed.ItemId &&
        order.UnitPriceInCopper == completed.UnitPriceInCopper &&
        order.CreatedAtUtc == completed.CreatedAtUtc;

    private static bool WasObservedToDisappear(long orderId, DateTimeOffset lastObservedAtUtc,
        IReadOnlyList<CurrentPersonalTradingPostOrderSnapshot> observations) =>
        observations.Any(snapshot => snapshot.ObservedAtUtc > lastObservedAtUtc &&
                                     snapshot.Orders.All(order => order.ExternalOrderId != orderId));

    private static PersonalCapitalTurnoverMetrics? BuildMetrics(
        IReadOnlyList<KnownBasisRealizedSaleAllocation> allocations)
    {
        if (allocations.Count == 0)
        {
            return null;
        }

        var ordered = allocations
            .OrderBy(value => value.Match.SellCompletedAtUtc)
            .ThenBy(value => value.Match.SellTransactionId)
            .ThenBy(value => value.Match.BuyTransactionId)
            .ToArray();
        var measuredStart = ordered.Min(value => value.Match.BuyCompletedAtUtc);
        var measuredEnd = ordered.Max(value => value.Match.SellCompletedAtUtc);
        var measuredDuration = measuredEnd - measuredStart;
        var totalLockedTicks = ordered.Aggregate(0L, (total, value) => checked(total + (value.Match.SellCompletedAtUtc - value.Match.BuyCompletedAtUtc).Ticks));
        var totalBasis = Sum(ordered.Select(value => value.Match.AllocatedAcquisitionBasis));
        var totalProfit = Sum(ordered.Select(value => value.NetProfit));
        var weightedCapitalTicks = ordered.Aggregate(BigInteger.Zero, (total, value) => total +
            new BigInteger(value.Match.AllocatedAcquisitionBasis.Copper) * (value.Match.SellCompletedAtUtc - value.Match.BuyCompletedAtUtc).Ticks);
        var profitPerDay = measuredDuration.Ticks > 0
            ? new ExactPersonalRate(new BigInteger(totalProfit.Copper) * TimeSpan.TicksPerDay, measuredDuration.Ticks)
            : null;
        var capitalTurns = measuredDuration.Ticks > 0 && weightedCapitalTicks > BigInteger.Zero
            ? new ExactPersonalRate(new BigInteger(totalBasis.Copper) * measuredDuration.Ticks, weightedCapitalTicks)
            : null;

        return new PersonalCapitalTurnoverMetrics(
            ordered.Length,
            checked((int)ordered.Sum(value => (long)value.Match.MatchedQuantity)),
            totalBasis,
            totalProfit,
            measuredStart,
            measuredEnd,
            measuredDuration,
            TimeSpan.FromTicks(totalLockedTicks),
            TimeSpan.FromTicks(totalLockedTicks / ordered.Length),
            profitPerDay,
            capitalTurns);
    }

    private static Money Sum(IEnumerable<Money> values)
    {
        var total = Money.Zero;
        foreach (var value in values)
        {
            total += value;
        }

        return total;
    }

    private static void ValidateRequest(PersonalTurnoverRequest request)
    {
        if (request.AsOfUtc.Offset != TimeSpan.Zero || request.AccountProfileId <= 0 || request.HistoryCoverage is null ||
            request.CompletedTransactions is null || request.CurrentOrderObservations is null ||
            (request.HistoryCoverage.StartUtc is null) != (request.HistoryCoverage.EndUtc is null))
        {
            throw new ArgumentException("Personal turnover evidence must be complete and UTC.", nameof(request));
        }

        if (request.HistoryCoverage.StartUtc is { } coverageStart && request.HistoryCoverage.EndUtc is { } coverageEnd &&
            (coverageStart.Offset != TimeSpan.Zero || coverageEnd.Offset != TimeSpan.Zero || coverageStart > coverageEnd || coverageEnd > request.AsOfUtc))
        {
            throw new ArgumentException("History coverage must be ordered UTC evidence ending no later than the as-of time.", nameof(request));
        }

        if (request.CompletedTransactions.Any(value => value is null || value.Transaction is null ||
                value.Transaction.CreatedAtUtc.Offset != TimeSpan.Zero || value.Transaction.CompletedAtUtc.Offset != TimeSpan.Zero ||
                value.FirstImportedAtUtc.Offset != TimeSpan.Zero || value.LastSeenAtUtc.Offset != TimeSpan.Zero ||
                value.Transaction.CreatedAtUtc > value.Transaction.CompletedAtUtc || value.FirstImportedAtUtc > request.AsOfUtc ||
                value.Transaction.ExternalTransactionId <= 0 || value.Transaction.ItemId <= 0 || value.Transaction.Quantity <= 0 || value.Transaction.UnitPriceInCopper < 0) ||
            request.CompletedTransactions.Select(value => value.Transaction.ExternalTransactionId).Distinct().Count() != request.CompletedTransactions.Count)
        {
            throw new ArgumentException("Completed transaction evidence is invalid.", nameof(request));
        }

        if (request.CurrentOrderObservations.Any(snapshot => snapshot is null))
        {
            throw new ArgumentException("Current-order observation evidence is invalid.", nameof(request));
        }

        DateTimeOffset? previousObservation = null;
        foreach (var snapshot in request.CurrentOrderObservations.OrderBy(value => value.ObservedAtUtc))
        {
            if (snapshot.Orders is null || snapshot.ObservedAtUtc.Offset != TimeSpan.Zero || snapshot.ObservedAtUtc > request.AsOfUtc ||
                previousObservation == snapshot.ObservedAtUtc || snapshot.Orders.Any(order => order is null || order.ExternalOrderId <= 0 || order.ItemId <= 0 ||
                    order.Quantity <= 0 || order.UnitPriceInCopper < 0 || order.CreatedAtUtc.Offset != TimeSpan.Zero) ||
                snapshot.Orders.Select(order => order.ExternalOrderId).Distinct().Count() != snapshot.Orders.Count)
            {
                throw new ArgumentException("Current-order observation evidence is invalid.", nameof(request));
            }

            previousObservation = snapshot.ObservedAtUtc;
        }
    }

    private sealed record ObservedOrder(DateTimeOffset ObservedAtUtc, CurrentPersonalTradingPostOrder Order);
}
