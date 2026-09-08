using System.Text.Json;
using Gw2Tp.Application.Persistence;

namespace Gw2Tp.Web.Hosting;

internal static class WatchlistEndpoints
{
    public static IEndpointRouteBuilder MapWatchlistEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/watchlist", async (IWatchlistRepository repository, CancellationToken cancellationToken) =>
            NoStoreJson(StatusCodes.Status200OK, new { itemIds = (await repository.GetAllAsync(cancellationToken).ConfigureAwait(false)).Select(entry => entry.ItemId).ToArray() }));

        endpoints.MapPut("/api/watchlist/{itemId:int}", async (int itemId, IWatchlistRepository repository, CancellationToken cancellationToken) =>
        {
            if (itemId <= 0) return NoStoreJson(StatusCodes.Status400BadRequest, new { error = "invalid_watchlist_item" });
            await repository.AddAsync(new WatchlistEntry(itemId, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
            return NoStoreJson(StatusCodes.Status200OK, new { itemId, outcome = "watching" });
        });

        endpoints.MapDelete("/api/watchlist/{itemId:int}", async (int itemId, IWatchlistRepository repository, CancellationToken cancellationToken) =>
        {
            if (itemId <= 0) return NoStoreJson(StatusCodes.Status400BadRequest, new { error = "invalid_watchlist_item" });
            await repository.RemoveAsync(itemId, cancellationToken).ConfigureAwait(false);
            return NoStoreJson(StatusCodes.Status200OK, new { itemId, outcome = "removed" });
        });

        return endpoints;
    }

    private static IResult NoStoreJson(int statusCode, object payload) => new NoStoreJsonResult(statusCode, payload);

    private sealed class NoStoreJsonResult(int statusCode, object payload) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            httpContext.Response.Headers.CacheControl = "no-store";
            await JsonSerializer.SerializeAsync(httpContext.Response.Body, payload, cancellationToken: httpContext.RequestAborted).ConfigureAwait(false);
        }
    }
}
