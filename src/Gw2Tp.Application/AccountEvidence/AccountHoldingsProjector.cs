using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.Plans;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.AccountEvidence;

/// <summary>Pure conservative cap. Source success and local time never establish physical coherence.</summary>
public sealed class AccountHoldingsProjector
{
    public static readonly TimeSpan MaximumEvidenceAge = TimeSpan.FromMinutes(15);

    public AccountHoldingsProjection Project(AccountHoldingsSnapshot snapshot, DateTimeOffset atUtc,
        string generation, string incarnation)
    {
        var current = snapshot.Generation == generation && snapshot.StoreIncarnation == incarnation;
        var capture = ForEvaluation(snapshot.Capture, atUtc, current);
        // Retention must apply to the single-location cap, never to summed independent observations.
        var unreservedRules = snapshot.Rules with { MinimumRetainedQuantities = new Dictionary<int, int>() };
        var policy = new AccountHoldingsProtectionPolicy();
        var rows = policy.Evaluate(capture, unreservedRules, snapshot.Categories, previousFloor: snapshot.ProtectionFloor).Rows;
        var actorRows = capture.Characters.ToDictionary(actor => actor.Actor.ActorId,
            actor => policy.Evaluate(capture, unreservedRules, snapshot.Categories, actor.Actor, snapshot.ProtectionFloor).Rows,
            StringComparer.Ordinal);
        var items = new List<HoldingsItemProjection>();
        foreach (var group in rows.GroupBy(row => row.Observation.ItemId).OrderBy(group => group.Key))
        {
            int Allowance(ProtectedHoldingsObservation row)
            {
                if (row.Observation.Binding != AccountItemBinding.CharacterBound) return row.ConsumableAllowance;
                var id = row.Observation.BoundActor?.ActorId;
                return id is not null && actorRows.TryGetValue(id, out var values)
                    ? values.Single(value => value.Observation == row.Observation).ConsumableAllowance : 0;
            }
            var selected = group.Where(row => Allowance(row) > 0)
                .OrderByDescending(Allowance).ThenBy(row => row.Observation.Location.Source)
                .ThenBy(row => row.Observation.Location.ActorId, StringComparer.Ordinal)
                .ThenBy(row => row.Observation.Location.BagIndex).ThenBy(row => row.Observation.Location.SlotIndex)
                .ThenBy(row => row.Observation.Location.SourceRowIndices.FirstOrDefault()).FirstOrDefault();
            var observed = group.Aggregate(0L, (sum, row) => checked(sum + row.Observation.Quantity));
            var eligibleObserved = group.Aggregate(0L, (sum, row) => checked(sum + Allowance(row)));
            var cap = selected is null ? 0 : Allowance(selected);
            var kept = Math.Min(cap, snapshot.Rules.MinimumRetainedQuantities.GetValueOrDefault(group.Key));
            var admitted = cap - kept;
            var reasons = new List<HoldingsAdmissionReason>();
            if (eligibleObserved > cap) reasons.Add(HoldingsAdmissionReason.IndependentLocationsOmitted);
            if (observed > eligibleObserved) reasons.Add(HoldingsAdmissionReason.ProtectionOrUnknownEvidence);
            if (!current) reasons.Add(HoldingsAdmissionReason.StaleGeneration);
            if (kept > 0) reasons.Add(HoldingsAdmissionReason.RetainedQuantity);
            var admission = selected is null ? null : new HoldingsAdmission(group.Key, selected.Observation, admitted,
                selected.Observation.Binding == AccountItemBinding.Unspecified ? admitted : 0, reasons);
            items.Add(new(group.Key, observed, checked(observed - eligibleObserved + kept), Math.Max(0, eligibleObserved - cap), admission));
        }
        return new(snapshot, capture, rows, items, current);
    }

    public static bool SameLocation(HoldingsLocation left, HoldingsLocation right) =>
        left.Source == right.Source && left.ActorId == right.ActorId && left.BagIndex == right.BagIndex && left.SlotIndex == right.SlotIndex &&
        left.MaterialCategoryIds.SequenceEqual(right.MaterialCategoryIds) && left.SourceRowIndices.SequenceEqual(right.SourceRowIndices);

    public static bool Fresh(EvidenceFetchProvenance fetch, DateTimeOffset atUtc) =>
        fetch.StartedAtUtc <= fetch.CompletedAtUtc && fetch.CompletedAtUtc <= atUtc && atUtc - fetch.StartedAtUtc <= MaximumEvidenceAge;

    public static AccountHoldingsCapture ForEvaluation(AccountHoldingsCapture capture, DateTimeOffset atUtc, bool current = true)
    {
        AccountSourceEvidence<T> Source<T>(AccountSourceEvidence<T> value) => current && Fresh(value.Fetch, atUtc)
            ? value : value with { Completeness = EvidenceCompleteness.Partial };
        return capture with
        {
            Roster = Source(capture.Roster), Bank = Source(capture.Bank), MaterialStorage = Source(capture.MaterialStorage),
            SharedInventory = Source(capture.SharedInventory), Delivery = Source(capture.Delivery), RecipeUnlocks = Source(capture.RecipeUnlocks),
            Characters = capture.Characters.Select(actor => actor with
            {
                Inventory = Source(actor.Inventory), Crafting = Source(actor.Crafting), Equipment = Source(actor.Equipment),
                EquipmentTabRoster = Source(actor.EquipmentTabRoster), EquipmentTabs = Source(actor.EquipmentTabs),
            }).ToArray(),
        };
    }

    public static PlanEvidenceSource<IReadOnlyDictionary<string, long>> PhysicalEvidence(AccountHoldingsProjection? projection)
    {
        var capture = projection?.Snapshot.Capture;
        var sources = capture is null ? [] : AccountEvidencePolicyFacts.InventorySources(capture).ToArray();
        var times = sources.Where(source => source.Value is not null).Select(source => source.Fetch.StartedAtUtc).ToArray();
        return new(new PlanEvidenceProvenance(capture?.RefreshId.ToString("N"), times.Length == 0 ? null : times.Min(), null,
            projection?.IsCurrentGeneration == true ? PlanEvidenceAvailability.Available : PlanEvidenceAvailability.Unavailable,
            projection?.IsCurrentGeneration == true ? PlanEvidenceCompleteness.Partial : PlanEvidenceCompleteness.Unknown,
            projection?.Quantities.Keys.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>()), projection?.Quantities);
    }

    /// <summary>Only negative inventory effects constrain observed admission; provisional gains remain separate.</summary>
    public static PlanEffectiveResources AdmissibleResources(Money cash, IReadOnlyDictionary<string, long> quantities,
        IReadOnlyCollection<PlanExecutionEvent> events)
    {
        var projectedCash = PlanOrchestrationService.ProjectEffectiveResources(cash, new Dictionary<string, long>(),
            events.Select(value => value with { Effects = value.Effects.Where(effect => effect.Kind == PlanResourceKind.Cash && effect.Cash.Copper < 0).ToArray() }).ToArray()).EffectiveCash;
        var inventory = PlanOrchestrationService.ProjectEffectiveResources(Money.Zero, quantities,
            events.Select(value => value with { Effects = value.Effects.Where(effect => effect.Kind == PlanResourceKind.Inventory && effect.Quantity < 0).ToArray() }).ToArray()).Quantities;
        return new(cash, projectedCash, inventory.ToDictionary(pair => pair.Key, pair => Math.Max(0, pair.Value), StringComparer.Ordinal));
    }
}
