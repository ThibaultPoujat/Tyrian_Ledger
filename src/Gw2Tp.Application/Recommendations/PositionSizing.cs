using Gw2Tp.Application.MarketScanning;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Recommendations;

public enum PositionSizingLiquidity
{
    High = 1,
    Medium = 2,
    Low = 3,
}

public enum PortfolioSizingSnapshotState
{
    Available = 1,
    Unknown = 2,
}

public enum PortfolioExposureKind
{
    CurrentBuyOrder = 1,
    CurrentSellListing = 2,
    HeldPosition = 3,
}

public enum PositionSizingResultState
{
    Sized = 1,
    Unavailable = 2,
}

public enum PositionSizingUnavailableReason
{
    UnknownPortfolioState = 1,
    InvalidPortfolioSnapshot = 2,
}

public enum PositionSizingAllocationState
{
    Suggested = 1,
    NoCapacity = 2,
    Unavailable = 3,
}

public enum CashReserveStatus
{
    Satisfied = 1,
    Breached = 2,
}

public enum PositionSizingConstraintName
{
    CashAfterReserve = 1,
    ItemExposure = 2,
    LiquidityParticipation = 3,
    StrategyConcentration = 4,
    CategoryConcentration = 5,
}

/// <summary>
/// Versioned, visible portfolio-risk policy. Percentages are expressed in basis points.
/// </summary>
public sealed record PositionSizingPolicy(
    int Version,
    int CashReserveBasisPoints,
    int HighLiquidityItemCapBasisPoints,
    int MediumLiquidityItemCapBasisPoints,
    int LowLiquidityItemCapBasisPoints,
    int StrategyCapBasisPoints,
    int CategoryCapBasisPoints)
{
    public const int CurrentVersion = 1;
    public const int BasisPointsPerWhole = 10_000;

    public static PositionSizingPolicy Default { get; } = new(
        CurrentVersion,
        CashReserveBasisPoints: 1_500,
        HighLiquidityItemCapBasisPoints: 500,
        MediumLiquidityItemCapBasisPoints: 300,
        LowLiquidityItemCapBasisPoints: 150,
        StrategyCapBasisPoints: 2_000,
        CategoryCapBasisPoints: 2_500);

    public void Validate()
    {
        var values = new[]
        {
            CashReserveBasisPoints,
            HighLiquidityItemCapBasisPoints,
            MediumLiquidityItemCapBasisPoints,
            LowLiquidityItemCapBasisPoints,
            StrategyCapBasisPoints,
            CategoryCapBasisPoints,
        };
        if (Version != CurrentVersion || values.Any(value => value is < 0 or > BasisPointsPerWhole) ||
            HighLiquidityItemCapBasisPoints < MediumLiquidityItemCapBasisPoints ||
            MediumLiquidityItemCapBasisPoints < LowLiquidityItemCapBasisPoints)
        {
            throw new ArgumentOutOfRangeException(nameof(PositionSizingPolicy));
        }
    }
}

/// <summary>
/// A caller-supplied capital-at-risk entry. Callers must avoid representing the
/// same committed capital twice across orders and positions.
/// </summary>
public sealed record PortfolioExposure(
    string ExposureId,
    PortfolioExposureKind Kind,
    int ItemId,
    string Strategy,
    string Category,
    Money CapitalAtRisk);

/// <summary>
/// Complete, explicit portfolio evidence. No wallet/API read or durable state is implied.
/// </summary>
public sealed record PortfolioSizingSnapshot(
    PortfolioSizingSnapshotState State,
    Money? AvailableCash,
    IReadOnlyList<PortfolioExposure>? ExistingExposures);

public sealed record PositionSizingCandidate(
    OpportunityScore Score,
    LiveMarketScannerCandidate Market,
    PositionSizingLiquidity Liquidity,
    string Strategy,
    string Category);

public sealed record PositionSizingConstraint(
    PositionSizingConstraintName Name,
    Money CapitalCapacity,
    int QuantityCapacity,
    bool IsBinding);

public sealed record PositionSizingAllocation(
    int ItemId,
    int ScoreRank,
    string Strategy,
    string Category,
    PositionSizingAllocationState State,
    int SuggestedQuantity,
    Money SuggestedCapital,
    IReadOnlyList<PositionSizingConstraint> Constraints);

public sealed record PositionSizingResult(
    PositionSizingResultState State,
    PositionSizingUnavailableReason? UnavailableReason,
    int PolicyVersion,
    Money? TotalBankroll,
    Money? CashReserve,
    CashReserveStatus? CashReserveStatus,
    Money? CashReserveShortfall,
    Money? RemainingCashAfterSizing,
    IReadOnlyList<PositionSizingAllocation> Allocations);

public interface IPositionSizingService
{
    PositionSizingResult Size(PortfolioSizingSnapshot snapshot, IReadOnlyCollection<PositionSizingCandidate> candidates);
}

/// <summary>
/// Pure deterministic sizing over complete caller-supplied portfolio evidence.
/// It allocates no money or orders and treats unavailable or invalid evidence as no allocation.
/// </summary>
public sealed class PositionSizingService : IPositionSizingService
{
    private readonly PositionSizingPolicy policy;

    public PositionSizingService(PositionSizingPolicy? policy = null)
    {
        this.policy = policy ?? PositionSizingPolicy.Default;
        this.policy.Validate();
    }

    public PositionSizingResult Size(PortfolioSizingSnapshot snapshot, IReadOnlyCollection<PositionSizingCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(candidates);

        if (snapshot.State == PortfolioSizingSnapshotState.Unknown)
        {
            return Unavailable(PositionSizingUnavailableReason.UnknownPortfolioState, candidates);
        }

        if (!TryValidate(snapshot, candidates, out var availableCash, out var exposures, out var totalBankroll))
        {
            return Unavailable(PositionSizingUnavailableReason.InvalidPortfolioSnapshot, candidates);
        }

        var cashReserve = PercentageRoundUp(totalBankroll, policy.CashReserveBasisPoints);
        var reserveShortfall = new Money(Math.Max(0L, cashReserve.Copper - availableCash.Copper));
        var remainingDeployableCash = Math.Max(0L, availableCash.Copper - cashReserve.Copper);
        var itemExposure = exposures.GroupBy(exposure => exposure.ItemId).ToDictionary(group => group.Key, group => Sum(group.Select(exposure => exposure.CapitalAtRisk.Copper)));
        var strategyExposure = exposures.GroupBy(exposure => NormalizeGroup(exposure.Strategy), StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => Sum(group.Select(exposure => exposure.CapitalAtRisk.Copper)), StringComparer.OrdinalIgnoreCase);
        var categoryExposure = exposures.GroupBy(exposure => NormalizeGroup(exposure.Category), StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => Sum(group.Select(exposure => exposure.CapitalAtRisk.Copper)), StringComparer.OrdinalIgnoreCase);
        var allocations = new List<PositionSizingAllocation>();

        foreach (var candidate in candidates.OrderBy(candidate => candidate.Score.Rank).ThenBy(candidate => candidate.Market.Item.ItemId))
        {
            var itemId = candidate.Market.Item.ItemId;
            var strategy = NormalizeGroup(candidate.Strategy);
            var category = NormalizeGroup(candidate.Category);
            var unitCost = candidate.Market.TotalCost;
            var itemCap = PercentageRoundDown(totalBankroll, ItemCap(candidate.Liquidity));
            var strategyCap = PercentageRoundDown(totalBankroll, policy.StrategyCapBasisPoints);
            var categoryCap = PercentageRoundDown(totalBankroll, policy.CategoryCapBasisPoints);
            var capacities = new[]
            {
                (PositionSizingConstraintName.CashAfterReserve, new Money(remainingDeployableCash), QuantityFor(new Money(remainingDeployableCash), unitCost)),
                (PositionSizingConstraintName.ItemExposure, Remaining(itemCap, itemExposure.GetValueOrDefault(itemId)), QuantityFor(Remaining(itemCap, itemExposure.GetValueOrDefault(itemId)), unitCost)),
                (PositionSizingConstraintName.LiquidityParticipation, CapitalForQuantity(unitCost, candidate.Market.Liquidity.ParticipationCapQuantity), candidate.Market.Liquidity.ParticipationCapQuantity),
                (PositionSizingConstraintName.StrategyConcentration, Remaining(strategyCap, strategyExposure.GetValueOrDefault(strategy)), QuantityFor(Remaining(strategyCap, strategyExposure.GetValueOrDefault(strategy)), unitCost)),
                (PositionSizingConstraintName.CategoryConcentration, Remaining(categoryCap, categoryExposure.GetValueOrDefault(category)), QuantityFor(Remaining(categoryCap, categoryExposure.GetValueOrDefault(category)), unitCost)),
            };
            var quantity = capacities.Min(capacity => capacity.Item3);
            var constraints = capacities
                .Select(capacity => new PositionSizingConstraint(capacity.Item1, capacity.Item2, capacity.Item3, capacity.Item3 == quantity))
                .ToArray();
            var capital = CapitalForQuantity(unitCost, quantity);
            allocations.Add(new PositionSizingAllocation(
                itemId,
                candidate.Score.Rank,
                strategy,
                category,
                quantity > 0 ? PositionSizingAllocationState.Suggested : PositionSizingAllocationState.NoCapacity,
                quantity,
                capital,
                constraints));

            remainingDeployableCash = checked(remainingDeployableCash - capital.Copper);
            itemExposure[itemId] = checked(itemExposure.GetValueOrDefault(itemId) + capital.Copper);
            strategyExposure[strategy] = checked(strategyExposure.GetValueOrDefault(strategy) + capital.Copper);
            categoryExposure[category] = checked(categoryExposure.GetValueOrDefault(category) + capital.Copper);
        }

        return new PositionSizingResult(
            PositionSizingResultState.Sized,
            null,
            policy.Version,
            totalBankroll,
            cashReserve,
            reserveShortfall.Copper == 0 ? CashReserveStatus.Satisfied : CashReserveStatus.Breached,
            reserveShortfall,
            new Money(checked(availableCash.Copper - allocations.Sum(allocation => allocation.SuggestedCapital.Copper))),
            allocations);
    }

    private PositionSizingResult Unavailable(PositionSizingUnavailableReason reason, IReadOnlyCollection<PositionSizingCandidate> candidates) => new(
        PositionSizingResultState.Unavailable,
        reason,
        policy.Version,
        null,
        null,
        null,
        null,
        null,
        candidates.Where(candidate => candidate is not null).Select(candidate => new PositionSizingAllocation(
            candidate.Market?.Item?.ItemId ?? 0,
            candidate.Score?.Rank ?? 0,
            candidate.Strategy ?? string.Empty,
            candidate.Category ?? string.Empty,
            PositionSizingAllocationState.Unavailable,
            0,
            new Money(0),
            [])).ToArray());

    private static bool TryValidate(
        PortfolioSizingSnapshot snapshot,
        IReadOnlyCollection<PositionSizingCandidate> candidates,
        out Money availableCash,
        out IReadOnlyList<PortfolioExposure> exposures,
        out Money totalBankroll)
    {
        availableCash = new Money(0);
        exposures = [];
        totalBankroll = new Money(0);
        if (snapshot.State != PortfolioSizingSnapshotState.Available || snapshot.AvailableCash is not { } suppliedCash || snapshot.ExistingExposures is not { } suppliedExposures ||
            suppliedCash.Copper < 0 || suppliedExposures.Any(exposure => !IsValid(exposure)) ||
            candidates.Any(candidate => !IsValid(candidate)))
        {
            return false;
        }

        var exposureIds = suppliedExposures.Select(exposure => NormalizeGroup(exposure.ExposureId)).ToArray();
        if (exposureIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != exposureIds.Length ||
            suppliedExposures.Any(exposure => !Enum.IsDefined(exposure.Kind)) ||
            candidates.Select(candidate => candidate.Market.Item.ItemId).Distinct().Count() != candidates.Count ||
            candidates.Select(candidate => candidate.Score.Rank).Distinct().Count() != candidates.Count)
        {
            return false;
        }

        try
        {
            availableCash = suppliedCash;
            exposures = suppliedExposures;
            totalBankroll = new Money(checked(availableCash.Copper + Sum(exposures.Select(exposure => exposure.CapitalAtRisk.Copper))));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool IsValid(PortfolioExposure? exposure) => exposure is not null &&
        !string.IsNullOrWhiteSpace(exposure.ExposureId) && exposure.ItemId > 0 &&
        !string.IsNullOrWhiteSpace(exposure.Strategy) && !string.IsNullOrWhiteSpace(exposure.Category) &&
        exposure.CapitalAtRisk.Copper >= 0;

    private static bool IsValid(PositionSizingCandidate? candidate) => candidate?.Score is not null && candidate.Market?.Item is not null &&
        candidate.Score.Rank > 0 && candidate.Score.ItemId == candidate.Market.Item.ItemId && candidate.Market.Item.ItemId > 0 &&
        candidate.Market.TotalCost.Copper > 0 && candidate.Market.Liquidity is not null &&
        candidate.Market.Liquidity.TotalBuyQuantity >= 0 && candidate.Market.Liquidity.TotalSellQuantity >= 0 &&
        candidate.Market.Liquidity.ParticipationCapQuantity >= 0 &&
        candidate.Market.Liquidity.ParticipationCapQuantity <= Math.Min(candidate.Market.Liquidity.TotalBuyQuantity, candidate.Market.Liquidity.TotalSellQuantity) / 10 &&
        Enum.IsDefined(candidate.Liquidity) &&
        !string.IsNullOrWhiteSpace(candidate.Strategy) && !string.IsNullOrWhiteSpace(candidate.Category);

    private int ItemCap(PositionSizingLiquidity liquidity) => liquidity switch
    {
        PositionSizingLiquidity.High => policy.HighLiquidityItemCapBasisPoints,
        PositionSizingLiquidity.Medium => policy.MediumLiquidityItemCapBasisPoints,
        PositionSizingLiquidity.Low => policy.LowLiquidityItemCapBasisPoints,
        _ => throw new ArgumentOutOfRangeException(nameof(liquidity)),
    };

    private static Money Remaining(Money cap, long exposure) => new(Math.Max(0L, cap.Copper - exposure));

    private static int QuantityFor(Money capital, Money unitCost)
    {
        var quantity = capital.Copper / unitCost.Copper;
        return quantity > int.MaxValue ? int.MaxValue : (int)quantity;
    }

    private static Money CapitalForQuantity(Money unitCost, int quantity)
    {
        try
        {
            return new Money(checked(unitCost.Copper * quantity));
        }
        catch (OverflowException)
        {
            // A displayed liquidity capacity may exceed representable money;
            // saturating it leaves the independently smaller financial caps in control.
            return new Money(long.MaxValue);
        }
    }

    private static Money PercentageRoundDown(Money total, int basisPoints) => new(checked((total.Copper / PositionSizingPolicy.BasisPointsPerWhole) * basisPoints +
        ((total.Copper % PositionSizingPolicy.BasisPointsPerWhole) * basisPoints / PositionSizingPolicy.BasisPointsPerWhole)));

    private static Money PercentageRoundUp(Money total, int basisPoints) => new(checked((total.Copper / PositionSizingPolicy.BasisPointsPerWhole) * basisPoints +
        (((total.Copper % PositionSizingPolicy.BasisPointsPerWhole) * basisPoints + PositionSizingPolicy.BasisPointsPerWhole - 1) / PositionSizingPolicy.BasisPointsPerWhole)));

    private static long Sum(IEnumerable<long> values)
    {
        long sum = 0;
        foreach (var value in values) sum = checked(sum + value);
        return sum;
    }

    private static string NormalizeGroup(string value) => value.Trim();
}
