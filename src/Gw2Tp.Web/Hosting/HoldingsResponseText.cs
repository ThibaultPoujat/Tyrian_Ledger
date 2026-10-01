using Gw2Tp.Application.AccountEvidence;

namespace Gw2Tp.Web.Hosting;

/// <summary>French presentation of the selected instruction prerequisites, without private capture payloads.</summary>
internal static class HoldingsResponseText
{
    public static IReadOnlyList<string> Explain(PlanHoldingsAuthority? authority)
    {
        if (authority is null) return [];
        var lines = new List<string>();
        if (authority.CraftingActorName is { } actor) lines.Add($"Personnage requis : {actor}.");
        var sources = authority.Commitments.Select(value => Source(value.Location.Source)).Distinct().ToArray();
        if (sources.Length > 0) lines.Add($"Accès requis : {string.Join(", ", sources)}. Une seule localisation est retenue par objet ; les autres quantités observées ne sont pas additionnées.");
        return lines;
    }

    private static string Source(AccountHoldingsSource source) => source switch
    {
        AccountHoldingsSource.Bank => "banque",
        AccountHoldingsSource.MaterialStorage => "stockage de matériaux",
        AccountHoldingsSource.SharedInventory => "inventaire partagé",
        AccountHoldingsSource.CharacterInventory => "sacs du personnage",
        _ => "localisation à vérifier",
    };
}
