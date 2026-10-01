using Gw2Tp.Application.LocalData;

namespace Gw2Tp.Web.Hosting;

/// <summary>One captured credential per private request; reject late response bodies as well as writes.</summary>
internal sealed class AccountWorkMiddleware(RequestDelegate next)
{
    private static readonly string[] AccountRoutes =
    [
        "/api/account-connection", "/api/personal-trading-post", "/api/account-crafting",
        "/api/personal-dashboard", "/api/recommendations", "/api/plans", "/api/crafting-opportunities",
        "/api/investments", "/api/notifications", "/api/calculation-explanations", "/api/market-history/collector",
    ];

    public async Task InvokeAsync(HttpContext context, IAccountWorkFence fence)
    {
        // Recovery is a host transition, not work for the captured old account.
        if (!AccountRoutes.Any(route => context.Request.Path.StartsWithSegments(route)))
        {
            await next(context).ConfigureAwait(false);
            return;
        }
        var output = context.Response.Body;
        await using var buffered = new MemoryStream();
        context.Response.Body = buffered;
        try
        {
            await fence.RunAsync(async cancellationToken =>
            {
                await next(context).ConfigureAwait(false);
                await using var publication = await fence.AcquireCommitAsync(cancellationToken).ConfigureAwait(false);
                buffered.Position = 0;
                await buffered.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                return true;
            }, context.RequestAborted).ConfigureAwait(false);
        }
        catch (AccountWorkRejectedException)
        {
            context.Response.Body = output;
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "account_scope_changed",
                message = "Le contexte du compte a changé. Actualisez les données avant de réessayer.",
            }, context.RequestAborted).ConfigureAwait(false);
        }
        finally { context.Response.Body = output; }
    }
}
