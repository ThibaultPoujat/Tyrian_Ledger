using Gw2Tp.Application.AccountSnapshots;

namespace Gw2Tp.Web.AccountSnapshots;

internal static class AccountSnapshotEndpoints
{
    public static IEndpointRouteBuilder MapAccountSnapshotEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapDelete("/api/account/snapshots", (IAccountSnapshotCacheClearer cacheClearer) =>
        {
            cacheClearer.ClearCachedSnapshots();
            return Results.NoContent();
        });

        return endpoints;
    }
}
