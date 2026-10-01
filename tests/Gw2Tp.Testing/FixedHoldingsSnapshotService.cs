using Gw2Tp.Application.AccountEvidence;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.PersonalTradingPost;

namespace Gw2Tp.Testing;

public sealed class FixedHoldingsSnapshotService(AccountHoldingsSnapshot snapshot) : IAccountHoldingsSnapshotService
{
    public Task<Gw2ApiResult<AccountHoldingsSnapshot>> RefreshAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Gw2ApiResult<AccountHoldingsSnapshot>.Success(snapshot));
    public Task<AccountHoldingsSnapshot?> GetLatestAsync(AccountScope scope, CancellationToken cancellationToken = default) =>
        Task.FromResult<AccountHoldingsSnapshot?>(scope == snapshot.Capture.AccountScope ? snapshot : null);
    public Task<AccountHoldingsProjection?> GetProjectionAsync(AccountScope scope, DateTimeOffset at, CancellationToken cancellationToken = default) =>
        Task.FromResult<AccountHoldingsProjection?>(scope == snapshot.Capture.AccountScope
            ? new AccountHoldingsProjector().Project(snapshot, at, snapshot.Generation, snapshot.StoreIncarnation) : null);
}
