namespace Gw2Tp.Application.AccountAccess;

public interface IAccountAccessService
{
    Task<AccountAccessStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
