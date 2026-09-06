using Gw2Tp.Application.MarketData;

namespace Gw2Tp.Application.PersonalTradingPost;

/// <summary>
/// Safe outcome of a manual personal Trading Post synchronization.
/// </summary>
public sealed record PersonalTradingPostSynchronizationResult(
    bool IsSuccess,
    DateTimeOffset AttemptedAtUtc,
    int CompletedTransactionCount,
    int CurrentOrderCount,
    DateTimeOffset? HistoryCoverageStartUtc,
    DateTimeOffset? HistoryCoverageEndUtc,
    Gw2ApiErrorCategory? ErrorCategory,
    bool IsPersistenceFailure)
{
    public static PersonalTradingPostSynchronizationResult Succeeded(
        DateTimeOffset attemptedAtUtc,
        int completedTransactionCount,
        int currentOrderCount,
        DateTimeOffset? historyCoverageStartUtc,
        DateTimeOffset? historyCoverageEndUtc) => new(
            true,
            attemptedAtUtc,
            completedTransactionCount,
            currentOrderCount,
            historyCoverageStartUtc,
            historyCoverageEndUtc,
            null,
            false);

    public static PersonalTradingPostSynchronizationResult GatewayFailed(
        DateTimeOffset attemptedAtUtc,
        Gw2ApiErrorCategory errorCategory) => new(
            false,
            attemptedAtUtc,
            0,
            0,
            null,
            null,
            errorCategory,
            false);

    public static PersonalTradingPostSynchronizationResult PersistenceFailed(DateTimeOffset attemptedAtUtc) => new(
        false,
        attemptedAtUtc,
        0,
        0,
        null,
        null,
        null,
        true);
}

/// <summary>
/// Orchestrates one complete, read-only remote snapshot into durable local state.
/// </summary>
public interface IPersonalTradingPostSynchronizationService
{
    Task<PersonalTradingPostSynchronizationResult> SynchronizeAsync(
        CancellationToken cancellationToken = default);
}
