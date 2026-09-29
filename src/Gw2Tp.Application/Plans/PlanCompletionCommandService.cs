namespace Gw2Tp.Application.Plans;

/// <summary>Applies typed step commands through the repository's atomic admission boundary.</summary>
public sealed class PlanCompletionCommandService(
    IPlanRepository repository,
    IPlanOrchestrationService orchestration) : IPlanCompletionCommandService
{
    public Task<PlanCompletionResult> CompleteAsync(
        long accountProfileId,
        PlanCompletionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        // Keep only the receipt lookup key validation here.  The repository must
        // look up that key before validating the rest of the payload so reuse of
        // an existing command ID with changed (even now-invalid) data is a
        // conflict rather than an unrelated validation response.
        if (accountProfileId <= 0 ||
            string.IsNullOrWhiteSpace(command.PlanId) || command.PlanId.Length > 256 ||
            string.IsNullOrWhiteSpace(command.CommandId) || command.CommandId.Length > 128)
        {
            return Task.FromResult(new PlanCompletionResult(PlanCompletionStatus.Invalid));
        }

        return repository.CompleteStepAsync(accountProfileId, command, plan => command.Operation switch
        {
            PlanCompletionOperation.ReportPerformed => orchestration.ReportStep(
                plan, command.Quantity, command.UnitPrice, DateTimeOffset.UtcNow),
            PlanCompletionOperation.NotPerformed => orchestration.CancelUnperformedStep(plan),
            _ => throw new InvalidOperationException("The completion operation is not supported."),
        }, cancellationToken);
    }
}
