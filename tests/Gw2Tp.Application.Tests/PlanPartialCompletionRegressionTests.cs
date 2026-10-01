using Gw2Tp.Application.Plans;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class PlanPartialCompletionRegressionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Buying_four_of_ten_never_exposes_a_ten_item_exit()
    {
        var service = new PlanOrchestrationService();
        var candidate = new PlanCandidate("partial", 1, "partial", PlanAttention.Active,
            [new("buy", PlanStepAction.BuyNow, 42, "Objet", 10, new Money(100), [], PlanStepState.Pending),
             new("exit", PlanStepAction.List, 42, "Objet", 10, new Money(200), ["buy"], PlanStepState.Pending)],
            [new(PlanResourceKind.Cash, "cash", 0, new Money(1_100))], Money.Zero, new Money(1_100), 0, 0, 1, 1, true, []);
        var result = service.ReportStep(service.Start(candidate, Now, new Money(5_000), new Dictionary<string, long>()), 4, new Money(100), Now);

        Assert.Equal(4, result.Steps[1].Quantity);
        Assert.Equal(PlanStepState.Current, result.Steps[1].State);
        Assert.Equal(-400, Assert.Single(result.Events).Effects.Single(value => value.Kind == PlanResourceKind.Cash).Cash.Copper);
        Assert.Equal(40, PlanOrchestrationService.OutstandingReservations(result).Single(value => value.Kind == PlanResourceKind.Cash).Cash.Copper);
    }
}
