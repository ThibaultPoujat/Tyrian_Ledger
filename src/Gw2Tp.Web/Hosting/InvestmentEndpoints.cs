using System.Text.Json;
using Gw2Tp.Application.Investments;
using Gw2Tp.Application.Persistence;

namespace Gw2Tp.Web.Hosting;

internal static class InvestmentEndpoints
{
    public static IEndpointRouteBuilder MapInvestmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/investments", async (IInvestmentPortfolioService service, CancellationToken cancellationToken) =>
        {
            var portfolio = await service.GetAsync(cancellationToken).ConfigureAwait(false);
            return NoStore(StatusCodes.Status200OK, ToResponse(portfolio));
        });
        endpoints.MapPost("/api/investments", async (InvestmentRequest? request, IInvestmentPortfolioService service, CancellationToken cancellationToken) =>
        {
            if (!TryCreate(request, out var position)) return NoStore(StatusCodes.Status400BadRequest, new { error = "invalid_investment_position" });
            try
            {
                var created = await service.CreateAsync(position, cancellationToken).ConfigureAwait(false);
                return created is null ? NoStore(StatusCodes.Status503ServiceUnavailable, new { error = "account_unavailable" }) : NoStore(StatusCodes.Status201Created, new { position = ToPosition(created) });
            }
            catch (ArgumentException) { return NoStore(StatusCodes.Status400BadRequest, new { error = "invalid_investment_position" }); }
        });
        endpoints.MapPut("/api/investments/{positionId:long}", async (long positionId, InvestmentRequest? request, IInvestmentPortfolioService service, CancellationToken cancellationToken) =>
        {
            if (positionId <= 0 || !TryUpdate(request, out var position)) return NoStore(StatusCodes.Status400BadRequest, new { error = "invalid_investment_position" });
            try
            {
                var updated = await service.UpdateAsync(positionId, position, cancellationToken).ConfigureAwait(false);
                return updated is null ? NoStore(StatusCodes.Status404NotFound, new { error = "investment_position_not_found" }) : NoStore(StatusCodes.Status200OK, new { position = ToPosition(updated) });
            }
            catch (ArgumentException) { return NoStore(StatusCodes.Status400BadRequest, new { error = "invalid_investment_position" }); }
        });
        endpoints.MapPost("/api/investments/{positionId:long}/exits", async (long positionId, InvestmentExitRequest? request, IInvestmentPortfolioService service, CancellationToken cancellationToken) =>
        {
            if (positionId <= 0 || request is null || request.Quantity <= 0 || request.Notes is { Length: > 4000 }) return NoStore(StatusCodes.Status400BadRequest, new { error = "invalid_investment_exit" });
            try
            {
                var updated = await service.ExitAsync(positionId, request.Quantity, request.Notes, cancellationToken).ConfigureAwait(false);
                return updated is null ? NoStore(StatusCodes.Status404NotFound, new { error = "investment_position_not_found" }) : NoStore(StatusCodes.Status200OK, new { position = ToPosition(updated) });
            }
            catch (ArgumentException) { return NoStore(StatusCodes.Status400BadRequest, new { error = "invalid_investment_exit" }); }
        });
        return endpoints;
    }

    private static bool TryCreate(InvestmentRequest? request, out CreateInvestmentPosition position)
    {
        position = default!;
        if (!TryValidate(request, out var targets, out var openedAt)) return false;
        position = new(request!.ItemId, request.Quantity, request.AcquisitionBasisInCopper, request.Strategy!, request.Category!, openedAt, request.Thesis!, request.Notes, targets);
        return true;
    }
    private static bool TryUpdate(InvestmentRequest? request, out UpdateInvestmentPosition position)
    {
        position = default!;
        if (!TryValidate(request, out var targets, out var openedAt)) return false;
        position = new(request!.Quantity, request.AcquisitionBasisInCopper, request.Strategy!, request.Category!, openedAt, request.Thesis!, request.Notes, targets);
        return true;
    }
    private static bool TryValidate(InvestmentRequest? request, out IReadOnlyList<InvestmentTarget> targets, out DateTimeOffset openedAt)
    {
        targets = []; openedAt = default;
        if (request is null || request.ItemId <= 0 || request.Quantity <= 0 || request.AcquisitionBasisInCopper < 0 || string.IsNullOrWhiteSpace(request.Strategy) || string.IsNullOrWhiteSpace(request.Category) || string.IsNullOrWhiteSpace(request.Thesis) || request.Strategy.Length > 120 || request.Category.Length > 120 || request.Thesis.Length > 4000 || request.Notes is { Length: > 4000 } || request.Targets is null || request.Targets.Count > 20 || !DateTimeOffset.TryParse(request.OpenedAtUtc, out openedAt) || openedAt.Offset != TimeSpan.Zero) return false;
        targets = request.Targets.Select((target, ordinal) => new InvestmentTarget(ordinal, target.UnitPriceInCopper, target.Quantity)).ToArray();
        return targets.All(target => target.UnitPriceInCopper > 0 && target.Quantity > 0) && targets.Sum(target => (long)target.Quantity) <= request.Quantity;
    }
    private static object ToResponse(InvestmentPortfolio portfolio) => new { state = portfolio.State == InvestmentPortfolioState.Ready ? "ready" : "accountUnavailable", error = portfolio.Error, positions = portfolio.Positions.Select(view => new { position = ToPosition(view.Position), itemName = view.ItemName, valuation = new { state = view.Valuation.State.ToString()[0].ToString().ToLowerInvariant() + view.Valuation.State.ToString()[1..], grossLiquidationValue = view.Valuation.GrossLiquidationValue, netLiquidationValue = view.Valuation.NetLiquidationValue, unrealizedProfit = view.Valuation.UnrealizedProfit, unliquidatedQuantity = view.Valuation.UnliquidatedQuantity, currentBestBuyPriceInCopper = view.Valuation.CurrentBestBuyPriceInCopper }, historicalEvidence = view.HistoricalEvidence, opportunityCost = view.OpportunityCost, action = new { action = view.Action.Action == InvestmentAction.SellPartial ? "SELL PARTIAL" : view.Action.Action.ToString().ToUpperInvariant(), suggestedQuantity = view.Action.SuggestedQuantity, reason = view.Action.Reason } }) };
    private static object ToPosition(InvestmentPosition position) => new { id = position.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), itemId = position.ItemId, originalQuantity = position.OriginalQuantity, remainingQuantity = position.RemainingQuantity, acquisitionBasisInCopper = position.AcquisitionBasisInCopper, strategy = position.Strategy, category = position.Category, openedAtUtc = position.OpenedAtUtc, thesis = position.Thesis, notes = position.Notes, isClosed = position.IsClosed, createdAtUtc = position.CreatedAtUtc, updatedAtUtc = position.UpdatedAtUtc, closedAtUtc = position.ClosedAtUtc, exits = position.Exits.Select(exit => new { id = exit.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), exit.Quantity, exit.AllocatedBasisInCopper, exit.ExitedAtUtc, exit.Notes }), targets = position.Targets.Select(target => new { target.Ordinal, target.UnitPriceInCopper, target.Quantity }) };
    private static IResult NoStore(int status, object value) => new NoStoreJsonResult(status, value);
    private sealed class NoStoreJsonResult(int status, object value) : IResult { public async Task ExecuteAsync(HttpContext context) { context.Response.StatusCode = status; context.Response.ContentType = "application/json; charset=utf-8"; context.Response.Headers.CacheControl = "no-store"; await JsonSerializer.SerializeAsync(context.Response.Body, value, cancellationToken: context.RequestAborted).ConfigureAwait(false); } }
    private sealed record InvestmentRequest(int ItemId, int Quantity, int? AcquisitionBasisInCopper, string? Strategy, string? Category, string? OpenedAtUtc, string? Thesis, string? Notes, List<InvestmentTargetRequest>? Targets);
    private sealed record InvestmentTargetRequest(int UnitPriceInCopper, int Quantity);
    private sealed record InvestmentExitRequest(int Quantity, string? Notes);
}
