using Gw2Tp.Application.Time;

namespace Gw2Tp.Application.Plans;

/// <summary>Command-only undo: no account, market or candidate reads.</summary>
public sealed class PlanUndoCommandService(IPlanRepository repository,
    IPlanOrchestrationService orchestration, IClock clock) : IPlanUndoCommandService
{
    public Task<PlanUndoResult> UndoAsync(long accountProfileId, string planId, long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        if (accountProfileId <= 0 || string.IsNullOrWhiteSpace(planId) || planId.Length > 256 || expectedRevision < 0)
            return Task.FromResult(new PlanUndoResult(PlanUndoStatus.Invalid));
        return repository.UndoStepAsync(accountProfileId, planId, expectedRevision,
            plan => orchestration.UndoLastStep(plan, clock.UtcNow), cancellationToken);
    }
}
