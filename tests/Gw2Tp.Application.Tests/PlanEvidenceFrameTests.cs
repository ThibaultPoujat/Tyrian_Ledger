using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Application.Plans;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class PlanEvidenceFrameTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly AccountScope Scope = new("trusted-account");
    private readonly PlanOrchestrationService service = new();

    [Fact]
    public void Partial_bank_material_observation_and_new_trading_post_captures_do_not_confirm_or_contradict_craft()
    {
        var reported = ReportCraft();
        var first = service.ReconcileWithVerifiedState(reported, Scope, Frame(Now.AddMinutes(1),
            physical: new Dictionary<string, long> { ["2:10"] = 2 },
            inventoryCompleteness: PlanEvidenceCompleteness.Partial,
            inventoryCoverage: ["2:10"], inventoryUpstreamAt: null, inventoryId: "bank-material-old",
            tradingPostAt: Now.AddMinutes(1), tradingPostId: "tp-a"));
        var second = service.ReconcileWithVerifiedState(first, Scope, Frame(Now.AddMinutes(17),
            physical: new Dictionary<string, long> { ["2:10"] = 2 },
            inventoryCompleteness: PlanEvidenceCompleteness.Partial,
            inventoryCoverage: ["2:10"], inventoryUpstreamAt: null, inventoryId: "bank-material-old",
            tradingPostAt: Now.AddMinutes(17), tradingPostId: "tp-b"));

        Assert.Equal(PlanShadowEventState.PendingConfirmation, Assert.Single(second.Events).State);
        Assert.Equal(0, second.ConsecutiveContradictionCount);
        Assert.Equal(0, second.Events[0].NegativeEvidenceCaptureCount);
    }

    [Fact]
    public void Complete_synthetic_inventory_delta_confirms_once_and_wrong_account_is_ignored()
    {
        var reported = ReportCraft();
        var frame = Frame(Now.AddMinutes(1),
            physical: new Dictionary<string, long> { ["2:10"] = 0, ["2:100"] = 1, ["2:999"] = 50 },
            inventoryCompleteness: PlanEvidenceCompleteness.Complete,
            inventoryCoverage: ["2:10", "2:100"], inventoryUpstreamAt: Now.AddMinutes(1), inventoryId: "inventory-1",
            tradingPostAt: Now.AddMinutes(1), tradingPostId: "tp-1");

        var wrongAccount = service.ReconcileWithVerifiedState(reported, new AccountScope("other-account"), frame);
        var confirmed = service.ReconcileWithVerifiedState(reported, Scope, frame);
        var repeated = service.ReconcileWithVerifiedState(confirmed, Scope, frame);

        Assert.True(PlanRecordSemantics.AreEqual(reported, wrongAccount));
        Assert.Equal(PlanShadowEventState.Confirmed, Assert.Single(confirmed.Events).State);
        Assert.Equal(1, confirmed.Events[0].VerifiedQuantity);
        Assert.True(PlanRecordSemantics.AreEqual(confirmed, repeated));
    }

    [Fact]
    public void Two_distinct_complete_inventory_observations_expose_a_structured_craft_contradiction()
    {
        var reported = ReportCraft();
        var first = service.ReconcileWithVerifiedState(reported, Scope, Frame(Now.AddMinutes(1),
            physical: new Dictionary<string, long> { ["2:10"] = 2, ["2:100"] = 0 },
            inventoryCompleteness: PlanEvidenceCompleteness.Complete,
            inventoryCoverage: ["2:10", "2:100"], inventoryUpstreamAt: Now.AddMinutes(1), inventoryId: "inventory-a",
            tradingPostAt: Now.AddMinutes(1), tradingPostId: "tp-a"));
        var second = service.ReconcileWithVerifiedState(first, Scope, Frame(Now.AddMinutes(17),
            physical: new Dictionary<string, long> { ["2:10"] = 2, ["2:100"] = 0 },
            inventoryCompleteness: PlanEvidenceCompleteness.Complete,
            inventoryCoverage: ["2:10", "2:100"], inventoryUpstreamAt: Now.AddMinutes(17), inventoryId: "inventory-b",
            tradingPostAt: Now.AddMinutes(17), tradingPostId: "tp-b"));

        Assert.Equal(PlanState.ReconciliationRequired, second.State);
        Assert.Equal(PlanReconciliationState.Contradicted, second.ReconciliationState);
        Assert.Equal(PlanReconciliationReason.CraftInventoryMismatch, second.ReconciliationReason);
        Assert.Equal(PlanShadowEventState.PendingConfirmation, Assert.Single(second.Events).State);
    }

    [Fact]
    public void Listing_evidence_can_confirm_while_incomplete_inventory_keeps_craft_unproven()
    {
        var craft = new PlanStep("craft", PlanStepAction.Craft, 100, "Insigne", 1, null, [], PlanStepState.Pending,
            CraftEffects: [new(PlanResourceKind.Inventory, "10", -1, Money.Zero), new(PlanResourceKind.Inventory, "100", 1, Money.Zero)]);
        var listing = new PlanStep("listing", PlanStepAction.List, 100, "Insigne", 1, new Money(1_000), ["craft"], PlanStepState.Pending);
        var candidate = new PlanCandidate("craft-list", 1, "craft-list", PlanAttention.Active, [craft, listing], [], Money.Zero,
            Money.Zero, 8_000, 0, 1, 1, true, []);
        var started = service.Start(candidate, Now, new Money(10_000), new Dictionary<string, long> { ["2:10"] = 1, ["2:100"] = 0 });
        var crafted = service.ReportStep(started, 1, null, Now);
        var reported = service.ReportStep(crafted, 1, new Money(1_000), Now.AddSeconds(5));
        var listingEvidence = new PlanVerifiedEvidence("listing:1", PlanEvidenceKind.SellListing, 100, 1, new Money(1_000),
            Now.AddSeconds(5), Now.AddMinutes(1));
        var frame = Frame(Now.AddMinutes(1), physical: new Dictionary<string, long> { ["2:10"] = 0, ["2:100"] = 0 },
            inventoryCompleteness: PlanEvidenceCompleteness.Partial, inventoryCoverage: ["2:10"], inventoryUpstreamAt: null,
            inventoryId: "bank-only", currentRows: [listingEvidence], tradingPostCompleteness: PlanEvidenceCompleteness.Partial,
            tradingPostAt: Now.AddMinutes(1), tradingPostId: "tp-partial");

        var reconciled = service.ReconcileWithVerifiedState(reported, Scope, frame);

        Assert.Equal(PlanShadowEventState.PendingConfirmation, reconciled.Events.Single(value => value.Action == PlanStepAction.Craft).State);
        Assert.Equal(PlanShadowEventState.Confirmed, reconciled.Events.Single(value => value.Action == PlanStepAction.List).State);
    }

    [Fact]
    public void Partial_verified_quantity_is_monotonic_and_only_the_residual_remains_provisional()
    {
        var buy = new PlanStep("buy", PlanStepAction.BuyNow, 42, "Objet", 10, new Money(100), [], PlanStepState.Pending);
        var candidate = new PlanCandidate("buy-ten", 1, "buy-ten", PlanAttention.Active, [buy], [], Money.Zero,
            new Money(1_000), 8_000, 0, 1, 1, true, []);
        var started = service.Start(candidate, Now, new Money(10_000), new Dictionary<string, long> { ["2:42"] = 0 });
        var reported = service.ReportStep(started, 10, new Money(100), Now);
        var firstEvidence = new PlanVerifiedEvidence("buy:4", PlanEvidenceKind.CompletedBuy, 42, 4, new Money(100),
            Now.AddSeconds(1), Now.AddMinutes(1));
        var first = service.ReconcileWithVerifiedState(reported, Scope, Frame(Now.AddMinutes(1),
            physical: new Dictionary<string, long> { ["2:42"] = 4 }, inventoryCompleteness: PlanEvidenceCompleteness.Partial,
            inventoryCoverage: ["2:42"], inventoryUpstreamAt: null, inventoryId: "partial-1",
            completedRows: [firstEvidence], tradingPostCompleteness: PlanEvidenceCompleteness.Partial,
            tradingPostAt: Now.AddMinutes(1), tradingPostId: "tp-partial-1", cash: new Money(9_600)));
        var middleEvidence = firstEvidence with { Identity = "buy:3-middle", Quantity = 3, CapturedAtUtc = Now.AddMinutes(2) };
        var middle = service.ReconcileWithVerifiedState(first, Scope, Frame(Now.AddMinutes(2),
            physical: new Dictionary<string, long> { ["2:42"] = 7 }, inventoryCompleteness: PlanEvidenceCompleteness.Partial,
            inventoryCoverage: ["2:42"], inventoryUpstreamAt: null, inventoryId: "partial-2",
            completedRows: [firstEvidence, middleEvidence], tradingPostCompleteness: PlanEvidenceCompleteness.Partial,
            tradingPostAt: Now.AddMinutes(2), tradingPostId: "tp-partial-2", cash: new Money(9_000)));
        var olderReplay = service.ReconcileWithVerifiedState(middle, Scope, Frame(Now.AddMinutes(3),
            physical: new Dictionary<string, long> { ["2:42"] = 4 }, inventoryCompleteness: PlanEvidenceCompleteness.Partial,
            inventoryCoverage: ["2:42"], inventoryUpstreamAt: null, inventoryId: "partial-old",
            completedRows: [firstEvidence], tradingPostCompleteness: PlanEvidenceCompleteness.Partial,
            tradingPostAt: Now.AddMinutes(1), tradingPostId: "tp-partial-1", cash: new Money(9_600)));
        var finalEvidence = firstEvidence with { Identity = "buy:3-final", Quantity = 3, CapturedAtUtc = Now.AddMinutes(4) };
        var later = service.ReconcileWithVerifiedState(olderReplay, Scope, Frame(Now.AddMinutes(4),
            physical: new Dictionary<string, long> { ["2:42"] = 10 }, inventoryCompleteness: PlanEvidenceCompleteness.Partial,
            inventoryCoverage: ["2:42"], inventoryUpstreamAt: null, inventoryId: "partial-4",
            completedRows: [firstEvidence, middleEvidence, finalEvidence], tradingPostCompleteness: PlanEvidenceCompleteness.Partial,
            tradingPostAt: Now.AddMinutes(4), tradingPostId: "tp-partial-4", cash: new Money(9_000)));

        Assert.Equal(4, first.Events[0].VerifiedQuantity);
        Assert.Equal(PlanShadowEventState.PartiallyConfirmed, first.Events[0].State);
        var projected = PlanOrchestrationService.ProjectEffectiveResources(new Money(9_600), new Dictionary<string, long> { ["2:42"] = 4 }, [first.Events[0]]);
        Assert.Equal(9_000, projected.EffectiveCash.Copper);
        Assert.Equal(10, projected.Quantities["2:42"]);
        Assert.Equal(7, middle.Events[0].VerifiedQuantity);
        Assert.Equal(7, olderReplay.Events[0].VerifiedQuantity);
        Assert.Equal(PlanShadowEventState.PartiallyConfirmed, olderReplay.Events[0].State);
        Assert.Equal(10, later.Events[0].VerifiedQuantity);
        Assert.Equal(PlanShadowEventState.Confirmed, later.Events[0].State);
    }

    private PlanRecord ReportCraft()
    {
        var craft = new PlanStep("craft", PlanStepAction.Craft, 100, "Insigne", 1, null, [], PlanStepState.Pending,
            CraftEffects: [new(PlanResourceKind.Inventory, "10", -2, Money.Zero), new(PlanResourceKind.Inventory, "100", 1, Money.Zero)]);
        var candidate = new PlanCandidate("craft", 1, "craft", PlanAttention.Active, [craft], [], Money.Zero, Money.Zero, 8_000, 0, 1, 1, true, []);
        var started = service.Start(candidate, Now, new Money(10_000), new Dictionary<string, long> { ["2:10"] = 2, ["2:100"] = 0 });
        return service.ReportStep(started, 1, null, Now);
    }

    private static PlanEvidenceFrame Frame(DateTimeOffset evaluationAt,
        IReadOnlyDictionary<string, long> physical,
        PlanEvidenceCompleteness inventoryCompleteness,
        IEnumerable<string> inventoryCoverage,
        DateTimeOffset? inventoryUpstreamAt,
        string inventoryId,
        IReadOnlyList<PlanVerifiedEvidence>? currentRows = null,
        IReadOnlyList<PlanVerifiedEvidence>? completedRows = null,
        PlanEvidenceCompleteness tradingPostCompleteness = PlanEvidenceCompleteness.Complete,
        DateTimeOffset? tradingPostAt = null,
        string? tradingPostId = null,
        Money? cash = null)
    {
        var fetchedAt = tradingPostAt ?? evaluationAt;
        var captureId = tradingPostId ?? $"tp:{fetchedAt.UtcTicks}";
        var currentTp = new PlanEvidenceProvenance(captureId, fetchedAt, null,
            PlanEvidenceAvailability.Available, tradingPostCompleteness, new HashSet<string>(new[] { "buy_orders", "sell_listings" }, StringComparer.Ordinal));
        var completedTp = new PlanEvidenceProvenance(captureId, fetchedAt, null,
            PlanEvidenceAvailability.Available, tradingPostCompleteness, new HashSet<string>(new[] { "completed_buys", "completed_sells" }, StringComparer.Ordinal));
        var physicalSource = new PlanEvidenceSource<IReadOnlyDictionary<string, long>>(
            new(inventoryId, evaluationAt, inventoryUpstreamAt, PlanEvidenceAvailability.Available, inventoryCompleteness,
                inventoryCoverage.ToHashSet(StringComparer.Ordinal)), physical);
        var cashSource = new PlanEvidenceSource<Money>(new($"cash:{evaluationAt.UtcTicks}", evaluationAt, null,
            PlanEvidenceAvailability.Available, PlanEvidenceCompleteness.Complete, new HashSet<string>(new[] { "coin" }, StringComparer.Ordinal)),
            cash ?? new Money(10_000));
        var currentSource = new PlanEvidenceSource<IReadOnlyList<PlanVerifiedEvidence>>(currentTp,
            currentRows ?? Array.Empty<PlanVerifiedEvidence>());
        var completedSource = new PlanEvidenceSource<IReadOnlyList<PlanVerifiedEvidence>>(completedTp,
            completedRows ?? Array.Empty<PlanVerifiedEvidence>());
        return new PlanEvidenceFrame(Scope, evaluationAt, physicalSource, cashSource, currentSource, completedSource);
    }
}
