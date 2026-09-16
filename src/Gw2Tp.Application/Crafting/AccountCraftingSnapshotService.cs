using Gw2Tp.Application.MarketData;

namespace Gw2Tp.Application.Crafting;

/// <summary>
/// Persists only normalized account crafting facts after a successful
/// account-scope read. Individual feature failures remain explicit in the
/// snapshot so one absent API scope cannot erase other usable capabilities.
/// </summary>
public sealed class AccountCraftingSnapshotService(
    IAccountCraftingGateway gateway,
    IAccountCraftingSnapshotRepository repository) : IAccountCraftingSnapshotService
{
    public async Task<Gw2ApiResult<AccountCraftingSnapshot>> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await gateway.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null)
        {
            return result;
        }

        try
        {
            await repository.ReplaceAsync(result.Value, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Gw2ApiResult<AccountCraftingSnapshot>.Failure(Gw2ApiErrorCategory.UnexpectedResponse);
        }
    }

    public Task<AccountCraftingSnapshot?> GetLatestAsync(
        PersonalTradingPost.AccountScope accountScope,
        CancellationToken cancellationToken = default) =>
        repository.GetLatestAsync(accountScope, cancellationToken);
}
