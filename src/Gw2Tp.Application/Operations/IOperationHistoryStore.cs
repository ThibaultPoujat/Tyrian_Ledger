namespace Gw2Tp.Application.Operations;

using Gw2Tp.Analytics.Reconciliation;

/// <summary>
/// Local persistence for saved operation calculation contexts.
/// </summary>
public interface IOperationHistoryStore
{
    Task CreateAsync(OperationRecord operation, CancellationToken cancellationToken);

    Task<OperationRecord?> GetAsync(Guid operationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OperationRecord>> ListAsync(CancellationToken cancellationToken);

    Task UpdateStatusAsync(
        Guid operationId,
        OperationStatus status,
        DateTimeOffset lastModifiedAtUtc,
        CancellationToken cancellationToken);

    Task UpdateActualOutcomeAsync(
        Guid operationId,
        OperationActualOutcome actualOutcome,
        DateTimeOffset lastModifiedAtUtc,
        CancellationToken cancellationToken);
}
