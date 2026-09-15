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
        var (censored, unknown, reductions) = BuildObservationEvidence(request);

        if (request.HistoryCoverage.StartUtc is null || request.HistoryCoverage.EndUtc is null)
        {
            var itemsWithoutCoverage = BuildItems(request, exactDurations, censored, unknown, reductions, [], new HashSet<long>());
            return Result(PersonalTurnoverEvidenceStatus.InsufficientCoverage, null, null,
                exactDurations, censored, unknown, reductions, itemsWithoutCoverage);
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
        var partiallyUnknownSaleIds = performance.UnknownBasisSaleAllocations
            .Select(allocation => allocation.Sale.SellTransactionId)
            .ToHashSet();
        var entirelyKnownAllocations = performance.KnownBasisSaleAllocations
            .Where(allocation => !partiallyUnknownSaleIds.Contains(allocation.Match.SellTransactionId))
            .ToArray();
        var metrics = BuildMetrics(performance.KnownBasisSaleAllocations, partiallyUnknownSaleIds);
        var evidenceMetrics = BuildMetrics(entirelyKnownAllocations, new HashSet<long>());
        var items = BuildItems(request, exactDurations, censored, unknown, reductions,
            performance.KnownBasisSaleAllocations, partiallyUnknownSaleIds);
        return Result(PortfolioStatus(items), metrics, evidenceMetrics?.MeasuredEndUtc,
            exactDurations, censored, unknown, reductions, items);
    }

    private static PersonalTurnoverIntelligence Result(
        PersonalTurnoverEvidenceStatus status,
        PersonalCapitalTurnoverMetrics? metrics,
        DateTimeOffset? latestKnownBasisCompletionAtUtc,
        IReadOnlyList<SourceTimestampFillDuration> exactDurations,
        IReadOnlyList<IntervalCensoredCompletion> censored,
        IReadOnlyList<UnknownOrderTiming> unknown,
        IReadOnlyList<ObservedOrderQuantityReduction> reductions,
        IReadOnlyList<PersonalItemTurnoverIntelligence> items) => new(
            PersonalTurnoverPolicy.Version,
            status,
            PersonalTurnoverPolicy.TimestampLimitation,
            PersonalTurnoverPolicy.MinimumKnownBasisSamples,
            latestKnownBasisCompletionAtUtc,
            exactDurations,
            censored,
            unknown,
            reductions,
            items,
            metrics);

    private static (IReadOnlyList<IntervalCensoredCompletion> Censored, IReadOnlyList<UnknownOrderTiming> Unknown,
        IReadOnlyList<ObservedOrderQuantityReduction> Reductions)
        BuildObservationEvidence(PersonalTurnoverRequest request)
    {
        var observations = request.CurrentOrderObservations
            .OrderBy(snapshot => snapshot.ObservedAtUtc)
            .ToArray();
        var completedById = request.CompletedTransactions.ToDictionary(value => value.Transaction.ExternalTransactionId);
        var snapshotsByOrder = observations
            .SelectMany((snapshot, snapshotIndex) => snapshot.Orders.Select(order => new ObservedOrder(snapshotIndex, snapshot.ObservedAtUtc, order)))
            .GroupBy(value => value.Order.ExternalOrderId)
            .ToDictionary(group => group.Key, group => group.OrderBy(value => value.ObservedAtUtc).ToArray());
        var censored = new List<IntervalCensoredCompletion>();
        var unknown = new List<UnknownOrderTiming>();
        var reductions = new List<ObservedOrderQuantityReduction>();

        foreach (var (orderId, orderSnapshots) in snapshotsByOrder.OrderBy(value => value.Key))
        {
            var contiguousRuns = SplitContiguousRuns(orderSnapshots);
            foreach (var run in contiguousRuns)
            {
                reductions.AddRange(BuildQuantityReductions(orderId, run));
            }

            if (contiguousRuns.Count > 1)
            {
                var discontinuityFirst = orderSnapshots[0];
                var discontinuityLast = orderSnapshots[^1];
                unknown.Add(new UnknownOrderTiming(orderId, discontinuityFirst.Order.Side, discontinuityFirst.Order.ItemId, discontinuityLast.ObservedAtUtc,
                    "The order disappeared from a complete snapshot before later reappearing under the same identifier."));
                continue;
            }

            var contiguousSnapshots = contiguousRuns[0];
            var first = contiguousSnapshots[0];
            var last = contiguousSnapshots[^1];
            if (!completedById.TryGetValue(orderId, out var completed) ||
                !MatchesCompletedOrder(first.Order, completed.Transaction))
            {
                if (observations[^1].ObservedAtUtc > last.ObservedAtUtc)
                {
                    unknown.Add(new UnknownOrderTiming(orderId, first.Order.Side, first.Order.ItemId, last.ObservedAtUtc,
                        "The order later disappeared from polling without a compatible completed-history event."));
                }

                continue;
            }

            if (contiguousSnapshots.Any(value => !MatchesCompletedOrder(value.Order, completed.Transaction)))
            {
                unknown.Add(new UnknownOrderTiming(orderId, first.Order.Side, first.Order.ItemId, last.ObservedAtUtc,
                    "The same observed identifier has incompatible immutable order fields."));
                continue;
            }

            var lastBeforeConfirmation = contiguousSnapshots.LastOrDefault(value => value.ObservedAtUtc <= completed.FirstImportedAtUtc);
            if (lastBeforeConfirmation is null || lastBeforeConfirmation.ObservedAtUtc >= completed.FirstImportedAtUtc)
            {
                continue;
            }

            var observationsThroughConfirmation = contiguousSnapshots
                .Where(value => value.ObservedAtUtc <= completed.FirstImportedAtUtc)
                .ToArray();
            censored.Add(new IntervalCensoredCompletion(
                orderId,
                first.Order.Side,
                first.Order.ItemId,
                first.ObservedAtUtc,
                lastBeforeConfirmation.ObservedAtUtc,
                completed.FirstImportedAtUtc,
                first.Order.Quantity,
                lastBeforeConfirmation.Order.Quantity,
                BuildQuantityReductions(orderId, observationsThroughConfirmation).Count > 0));
        }

        return (censored.AsReadOnly(), unknown.AsReadOnly(), reductions.AsReadOnly());
    }

    private static IReadOnlyList<ObservedOrderQuantityReduction> BuildQuantityReductions(
        long orderId,
        IReadOnlyList<ObservedOrder> observations) => observations
            .Zip(observations.Skip(1), (earlier, later) => (Earlier: earlier, Later: later))
            .Where(pair => HasSameObservedOrderIdentity(pair.Earlier.Order, pair.Later.Order) &&
                           pair.Later.Order.Quantity < pair.Earlier.Order.Quantity)
            .Select(pair => new ObservedOrderQuantityReduction(
                orderId,
                pair.Earlier.Order.Side,
                pair.Earlier.Order.ItemId,
                pair.Earlier.ObservedAtUtc,
                pair.Later.ObservedAtUtc,
                pair.Earlier.Order.Quantity,
                pair.Later.Order.Quantity))
            .ToArray();

    private static bool HasSameObservedOrderIdentity(CurrentPersonalTradingPostOrder earlier, CurrentPersonalTradingPostOrder later) =>
        earlier.Side == later.Side && earlier.ItemId == later.ItemId &&
        earlier.UnitPriceInCopper == later.UnitPriceInCopper && earlier.CreatedAtUtc == later.CreatedAtUtc;

    private static IReadOnlyList<IReadOnlyList<ObservedOrder>> SplitContiguousRuns(
        IReadOnlyList<ObservedOrder> observations)
    {
        var runs = new List<IReadOnlyList<ObservedOrder>>();
        var currentRun = new List<ObservedOrder>();
        foreach (var observation in observations)
        {
            if (currentRun.Count > 0 && observation.SnapshotIndex != currentRun[^1].SnapshotIndex + 1)
            {
                runs.Add(currentRun);
                currentRun = [];
            }

            currentRun.Add(observation);
        }

        runs.Add(currentRun);
        return runs;
    }

    private static bool MatchesCompletedOrder(CurrentPersonalTradingPostOrder order, CompletedPersonalTradingPostTransaction completed) =>
        order.Side == completed.Side &&
        order.ItemId == completed.ItemId &&
        order.UnitPriceInCopper == completed.UnitPriceInCopper &&
        order.CreatedAtUtc == completed.CreatedAtUtc;

    private static PersonalCapitalTurnoverMetrics? BuildMetrics(
        IReadOnlyList<KnownBasisRealizedSaleAllocation> allocations,
        IReadOnlySet<long> partiallyUnknownSaleIds)
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
        var distinctCompletedSaleCount = ordered
            .Select(value => value.Match.SellTransactionId)
            .Where(sellTransactionId => !partiallyUnknownSaleIds.Contains(sellTransactionId))
            .Distinct()
            .Count();
        var weightedCapitalTicks = ordered.Aggregate(BigInteger.Zero, (total, value) => total +
            new BigInteger(value.Match.AllocatedAcquisitionBasis.Copper) * (value.Match.SellCompletedAtUtc - value.Match.BuyCompletedAtUtc).Ticks);
        var basisWeightedHoldingTicks = totalBasis.Copper == 0
            ? 0L
            : checked((long)(weightedCapitalTicks / totalBasis.Copper));
        var profitPerDay = measuredDuration.Ticks > 0
            ? new ExactPersonalRate(new BigInteger(totalProfit.Copper) * TimeSpan.TicksPerDay, measuredDuration.Ticks)
            : null;
        var capitalTurns = measuredDuration.Ticks > 0 && weightedCapitalTicks > BigInteger.Zero
            ? new ExactPersonalRate(new BigInteger(totalBasis.Copper) * measuredDuration.Ticks, weightedCapitalTicks)
            : null;

        return new PersonalCapitalTurnoverMetrics(
            distinctCompletedSaleCount,
            checked((int)ordered.Sum(value => (long)value.Match.MatchedQuantity)),
            totalBasis,
            totalProfit,
            measuredStart,
            measuredEnd,
            measuredDuration,
            TimeSpan.FromTicks(totalLockedTicks),
            TimeSpan.FromTicks(basisWeightedHoldingTicks),
            profitPerDay,
            capitalTurns);
    }

    private static IReadOnlyList<PersonalItemTurnoverIntelligence> BuildItems(
        PersonalTurnoverRequest request,
        IReadOnlyList<SourceTimestampFillDuration> exactDurations,
        IReadOnlyList<IntervalCensoredCompletion> censored,
        IReadOnlyList<UnknownOrderTiming> unknown,
        IReadOnlyList<ObservedOrderQuantityReduction> reductions,
        IReadOnlyList<KnownBasisRealizedSaleAllocation> allocations,
        IReadOnlySet<long> partiallyUnknownSaleIds)
    {
        var itemIds = exactDurations.Select(value => value.ItemId)
            .Concat(censored.Select(value => value.ItemId))
            .Concat(unknown.Select(value => value.ItemId))
            .Concat(reductions.Select(value => value.ItemId))
            .Concat(allocations.Select(value => value.Match.ItemId))
            .Distinct()
            .OrderBy(itemId => itemId);
        return itemIds.Select(itemId =>
        {
            var itemAllocations = allocations.Where(value => value.Match.ItemId == itemId).ToArray();
            var entirelyKnownItemAllocations = itemAllocations
                .Where(value => !partiallyUnknownSaleIds.Contains(value.Match.SellTransactionId))
                .ToArray();
            var itemMetrics = request.HistoryCoverage.StartUtc is null
                ? null
                : BuildMetrics(itemAllocations, partiallyUnknownSaleIds);
            var itemEvidenceMetrics = request.HistoryCoverage.StartUtc is null
                ? null
                : BuildMetrics(entirelyKnownItemAllocations, new HashSet<long>());
            return new PersonalItemTurnoverIntelligence(
                itemId,
                ItemStatus(request, itemEvidenceMetrics),
                itemEvidenceMetrics?.MeasuredEndUtc,
                exactDurations.Where(value => value.ItemId == itemId).ToArray(),
                censored.Where(value => value.ItemId == itemId).ToArray(),
                unknown.Where(value => value.ItemId == itemId).ToArray(),
                reductions.Where(value => value.ItemId == itemId).ToArray(),
                itemMetrics);
        }).ToArray();
    }

    private static PersonalTurnoverEvidenceStatus ItemStatus(PersonalTurnoverRequest request, PersonalCapitalTurnoverMetrics? metrics) =>
        request.HistoryCoverage.StartUtc is null || request.HistoryCoverage.EndUtc is null
            ? PersonalTurnoverEvidenceStatus.InsufficientCoverage
            : metrics is null || metrics.KnownBasisSampleCount < PersonalTurnoverPolicy.MinimumKnownBasisSamples
                ? PersonalTurnoverEvidenceStatus.InsufficientSamples
                : metrics.RealizedProfitPerDay is null || metrics.CapitalTurns is null
                    ? PersonalTurnoverEvidenceStatus.InsufficientMetrics
                : request.AsOfUtc - metrics.MeasuredEndUtc > PersonalTurnoverPolicy.MaximumEvidenceAge
                    ? PersonalTurnoverEvidenceStatus.Stale
                    : PersonalTurnoverEvidenceStatus.Supported;

    private static PersonalTurnoverEvidenceStatus PortfolioStatus(IReadOnlyList<PersonalItemTurnoverIntelligence> items) =>
        items.Count == 0
            ? PersonalTurnoverEvidenceStatus.InsufficientSamples
            : items.Any(item => item.Status == PersonalTurnoverEvidenceStatus.Supported)
            ? PersonalTurnoverEvidenceStatus.Supported
            : items.Any(item => item.Status == PersonalTurnoverEvidenceStatus.Stale)
                ? PersonalTurnoverEvidenceStatus.Stale
                : items.Any(item => item.Status == PersonalTurnoverEvidenceStatus.InsufficientMetrics)
                    ? PersonalTurnoverEvidenceStatus.InsufficientMetrics
                : items.Any(item => item.Status == PersonalTurnoverEvidenceStatus.InsufficientSamples)
                    ? PersonalTurnoverEvidenceStatus.InsufficientSamples
                    : PersonalTurnoverEvidenceStatus.InsufficientCoverage;

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

    private sealed record ObservedOrder(int SnapshotIndex, DateTimeOffset ObservedAtUtc, CurrentPersonalTradingPostOrder Order);
}
