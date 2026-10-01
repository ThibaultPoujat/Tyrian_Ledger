using Gw2Tp.Application.Crafting;

namespace Gw2Tp.Application.AccountEvidence;

public enum CraftingActorSelectionFailure
{
    None, InvalidRecipe, RecipeEvidenceUnavailable, RecipeLocked, ActorEvidenceUnavailable,
    CapabilityUnavailable, UnsupportedChain, InputEvidenceUnavailable, BoundToOtherActor,
}
public enum CraftingActorAccessKind { SelectActor, Bank, MaterialStorage, SharedInventory, TransferFromActor, AdmissionRequired }
public sealed record CraftingActorAccessPrerequisite(CraftingActorAccessKind Kind, int? ItemId = null, string? FromActorId = null);
public sealed record CraftingActorAssignment(int RecipeId, AccountActor Actor);
public sealed record CraftingActorSelection(
    CraftingActorSelectionFailure Failure, IReadOnlyList<CraftingActorAssignment> Assignments,
    IReadOnlyList<CraftingActorAccessPrerequisite> Prerequisites);

/// <summary>Pure feasibility selection. A multi-actor chain is explicitly unsupported until switches are modeled.</summary>
public sealed class CraftingActorSelector
{
    public CraftingActorSelection Select(AccountHoldingsCapture capture, IReadOnlyList<CraftingRecipe> chain,
        IReadOnlyList<HoldingsItemObservation>? inputs = null)
    {
        CraftingActorSelection Reject(CraftingActorSelectionFailure reason) => new(reason, [], []);
        if (chain.Count == 0 || chain.Any(recipe => recipe.RecipeId <= 0 || recipe.MinRating < 0 ||
            recipe.OutputItemId <= 0 || recipe.OutputItemCount <= 0 || recipe.Disciplines.Count == 0 ||
            recipe.Disciplines.Any(string.IsNullOrWhiteSpace))) return Reject(CraftingActorSelectionFailure.InvalidRecipe);
        if (!AccountEvidencePolicyFacts.Complete(capture.RecipeUnlocks, AccountHoldingsSource.RecipeUnlocks))
            return Reject(CraftingActorSelectionFailure.RecipeEvidenceUnavailable);
        if (chain.Any(recipe => !capture.RecipeUnlocks.Value!.Contains(recipe.RecipeId))) return Reject(CraftingActorSelectionFailure.RecipeLocked);
        var roster = AccountEvidencePolicyFacts.CurrentActors(capture);
        if (roster is null) return Reject(CraftingActorSelectionFailure.ActorEvidenceUnavailable);
        var actors = capture.Characters.Where(actor =>
            AccountEvidencePolicyFacts.Complete(actor.Crafting, AccountHoldingsSource.CharacterCrafting, actor.Actor.ActorId))
            .OrderBy(actor => actor.Actor.ActorId, StringComparer.Ordinal).ToArray();
        bool CanCraft(ActorHoldingsEvidence actor, CraftingRecipe recipe) => actor.Crafting.Value!.Any(capability =>
            capability.IsActive && capability.Rating >= recipe.MinRating && recipe.Disciplines.Contains(capability.Discipline, StringComparer.OrdinalIgnoreCase));
        var capable = actors.Where(actor => chain.All(recipe => CanCraft(actor, recipe))).ToArray();
        if (capable.Length == 0)
            return Reject(chain.All(recipe => actors.Any(actor => CanCraft(actor, recipe)))
                ? CraftingActorSelectionFailure.UnsupportedChain : CraftingActorSelectionFailure.CapabilityUnavailable);

        inputs ??= [];
        var observed = AccountEvidencePolicyFacts.InventorySources(capture)
            .Where(source => AccountEvidencePolicyFacts.Complete(source, source.Source, source.ActorId))
            .SelectMany(source => source.Value!.Items).ToArray();
        if (inputs.Any(input => !observed.Contains(input) || !AccountEvidencePolicyFacts.ValidLocation(input) ||
            input.Quantity <= 0 || input.Binding is not (AccountItemBinding.Unspecified or AccountItemBinding.AccountBound or AccountItemBinding.CharacterBound) ||
            (input.Binding != AccountItemBinding.CharacterBound && input.BoundActor is not null) ||
            (input.Location.Source == AccountHoldingsSource.CharacterInventory && !roster.ContainsKey(input.Location.ActorId!))))
            return Reject(CraftingActorSelectionFailure.InputEvidenceUnavailable);
        var selected = capable.FirstOrDefault(actor => inputs.All(input => input.Binding != AccountItemBinding.CharacterBound || input.BoundActor == actor.Actor));
        if (selected is null) return Reject(CraftingActorSelectionFailure.BoundToOtherActor);
        var prerequisites = new List<CraftingActorAccessPrerequisite> { new(CraftingActorAccessKind.SelectActor) };
        foreach (var input in inputs)
        {
            var access = input.Location.Source switch
            {
                AccountHoldingsSource.Bank => new CraftingActorAccessPrerequisite(CraftingActorAccessKind.Bank, input.ItemId),
                AccountHoldingsSource.MaterialStorage => new(CraftingActorAccessKind.MaterialStorage, input.ItemId),
                AccountHoldingsSource.SharedInventory => new(CraftingActorAccessKind.SharedInventory, input.ItemId),
                AccountHoldingsSource.CharacterInventory when input.Location.ActorId != selected.Actor.ActorId =>
                    new(CraftingActorAccessKind.TransferFromActor, input.ItemId, input.Location.ActorId),
                _ => null,
            };
            if (access is not null) prerequisites.Add(access);
        }
        prerequisites.Add(new(CraftingActorAccessKind.AdmissionRequired));
        return new(CraftingActorSelectionFailure.None, chain.Select(recipe => new CraftingActorAssignment(recipe.RecipeId, selected.Actor)).ToArray(),
            prerequisites.Distinct().OrderBy(value => value.Kind).ThenBy(value => value.ItemId).ThenBy(value => value.FromActorId, StringComparer.Ordinal).ToArray());
    }
}
