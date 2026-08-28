namespace Gw2Tp.Application.MarketHistory;

/// <summary>
/// Explicit local collection tiers. No items are collected automatically.
/// </summary>
public enum MarketSamplingClass
{
    Untracked = 0,
    Background = 1,
    Watchlist = 2,
}

/// <summary>
/// The collection intervals for one item class. Null intervals mean that the
/// item must not be collected.
/// </summary>
public sealed record MarketSamplingSchedule(
    TimeSpan? PriceSnapshotInterval,
    TimeSpan? OrderBookSnapshotInterval)
{
    public bool IsTracked => PriceSnapshotInterval is not null || OrderBookSnapshotInterval is not null;
}

/// <summary>
/// Deterministic, conservative defaults for future historical collection.
/// </summary>
public static class MarketSamplingPolicy
{
    public const int MaximumTrackedItemCount = 25;
    public const int DefaultOrderBookLevelEstimate = 40;
    public const int DaysPerPlanningYear = 365;
    public const int EstimatedBytesPerSnapshotRecord = 80;
    public const int EstimatedBytesPerOrderBookLevel = 80;
    public const int StoragePlanningOverheadBasisPoints = 1_000;

    public static MarketSamplingSchedule GetSchedule(MarketSamplingClass samplingClass) => samplingClass switch
    {
        MarketSamplingClass.Untracked => new(null, null),
        MarketSamplingClass.Background => new(TimeSpan.FromDays(1), TimeSpan.FromDays(7)),
        MarketSamplingClass.Watchlist => new(TimeSpan.FromHours(1), TimeSpan.FromDays(1)),
        _ => throw new ArgumentOutOfRangeException(
            nameof(samplingClass),
            samplingClass,
            "The market sampling class is unknown."),
    };

    public static MarketStorageEstimate EstimateAnnualStorage(
        int watchlistItemCount,
        int backgroundItemCount,
        int averageOrderBookLevelCount = DefaultOrderBookLevelEstimate)
    {
        if (watchlistItemCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(watchlistItemCount), "The watchlist item count cannot be negative.");
        }

        if (backgroundItemCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(backgroundItemCount), "The background item count cannot be negative.");
        }

        var totalTrackedItemCount = (long)watchlistItemCount + backgroundItemCount;
        if (totalTrackedItemCount > MaximumTrackedItemCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalTrackedItemCount),
                totalTrackedItemCount,
                $"The combined watchlist and background item count cannot exceed {MaximumTrackedItemCount}.");
        }

        if (averageOrderBookLevelCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(averageOrderBookLevelCount),
                "The average order-book level count cannot be negative.");
        }

        var watchlistSchedule = GetSchedule(MarketSamplingClass.Watchlist);
        var backgroundSchedule = GetSchedule(MarketSamplingClass.Background);
        var annualPriceSnapshotCount = checked(
            SamplesPerPlanningYear(watchlistSchedule.PriceSnapshotInterval!.Value) * watchlistItemCount +
            SamplesPerPlanningYear(backgroundSchedule.PriceSnapshotInterval!.Value) * backgroundItemCount);
        var annualOrderBookSnapshotCount = checked(
            SamplesPerPlanningYear(watchlistSchedule.OrderBookSnapshotInterval!.Value) * watchlistItemCount +
            SamplesPerPlanningYear(backgroundSchedule.OrderBookSnapshotInterval!.Value) * backgroundItemCount);
        var annualOrderBookLevelCount = checked(annualOrderBookSnapshotCount * (long)averageOrderBookLevelCount);
        var estimatedBytes = checked(
            annualPriceSnapshotCount * (long)EstimatedBytesPerSnapshotRecord +
            annualOrderBookSnapshotCount * (long)EstimatedBytesPerSnapshotRecord +
            annualOrderBookLevelCount * EstimatedBytesPerOrderBookLevel);
        var planningBudgetBytes = checked(
            estimatedBytes * (10_000 + StoragePlanningOverheadBasisPoints) / 10_000);

        return new MarketStorageEstimate(
            watchlistItemCount,
            backgroundItemCount,
            averageOrderBookLevelCount,
            annualPriceSnapshotCount,
            annualOrderBookSnapshotCount,
            annualOrderBookLevelCount,
            estimatedBytes,
            planningBudgetBytes);
    }

    private static long SamplesPerPlanningYear(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "A sampling interval must be positive.");
        }

        var planningYear = TimeSpan.FromDays(DaysPerPlanningYear);
        return checked((planningYear.Ticks + interval.Ticks - 1) / interval.Ticks);
    }
}

/// <summary>
/// A transparent estimate based on the configured cadence and a supplied
/// average order-book depth. It is planning guidance, not an observed size.
/// </summary>
public sealed record MarketStorageEstimate(
    int WatchlistItemCount,
    int BackgroundItemCount,
    int AverageOrderBookLevelCount,
    long AnnualPriceSnapshotCount,
    long AnnualOrderBookSnapshotCount,
    long AnnualOrderBookLevelCount,
    long EstimatedBytes,
    long PlanningBudgetBytes);
