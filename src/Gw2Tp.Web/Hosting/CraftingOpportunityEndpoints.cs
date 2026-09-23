using Gw2Tp.Application.Crafting;
using Gw2Tp.Domain.Finance;
using System.Globalization;

namespace Gw2Tp.Web.Hosting;

internal static class CraftingOpportunityEndpoints
{
    internal static IEndpointRouteBuilder MapCraftingOpportunityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/crafting-opportunities", async (HttpContext context, ICraftingOpportunityService service, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                return Results.Json(ToResponse(await service.GetAsync(cancellationToken).ConfigureAwait(false)));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return Results.Json(ToResponse(new CraftingPlannerResult(CraftingOpportunityState.Degraded, [], [], [CraftingOpportunityExclusion.MissingInputEvidence])));
            }
        });
        return endpoints;
    }

    internal static object ToResponse(CraftingPlannerResult result) => new
    {
        state = result.State.ToString(),
        truncationReasons = result.TruncationReasons.Select(value => value.ToString()),
        summaryExclusions = result.SummaryExclusions.Select(value => value.ToString()),
        opportunities = result.Opportunities.Select(value => new
        {
            id = value.Id,
            recipeId = value.Recipe.RecipeId,
            outputItemId = value.Recipe.OutputItemId,
            outputName = value.OutputName,
            outputIconUrl = value.OutputIconUrl,
            outputQuantity = value.Recipe.OutputItemCount,
            economics = new { netProfit = Optional(value.Economics.NetProfit), totalCost = Optional(value.Economics.TotalCost), state = value.Economics.State.ToString() },
            attention = value.Candidate?.Attention.ToString(),
            interactionSeconds = value.Candidate?.ExpectedInteractionSeconds,
            isActionable = value.IsActionable,
            exclusions = value.Exclusions.Select(reason => reason.ToString()),
            procurementExplanation = value.ProcurementExplanation,
            planId = value.Candidate?.Id,
            steps = value.Candidate?.Steps.Select(step => new { action = step.Action.ToString(), itemName = step.ItemName, quantity = step.Quantity, unitPrice = Optional(step.UnitPrice) }) ?? [],
        }),
    };
    private static MoneyResponse? Optional(Money? value) => value is { } amount ? MoneyResponse.From(amount) : null;
    private sealed record MoneyResponse(string Copper) { public static MoneyResponse From(Money value) => new(value.Copper.ToString(CultureInfo.InvariantCulture)); }
}
