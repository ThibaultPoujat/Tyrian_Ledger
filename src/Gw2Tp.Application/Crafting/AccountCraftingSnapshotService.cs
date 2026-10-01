using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.LocalData;

namespace Gw2Tp.Application.Crafting;

/// <summary>
/// Persists only normalized account crafting facts after a successful
/// account-scope read. Individual feature failures remain explicit in the
/// snapshot so one absent API scope cannot erase other usable capabilities.
/// </summary>
public sealed class AccountCraftingSnapshotService(
    IAccountCraftingGateway gateway,
    IAccountCraftingSnapshotRepository repository,
    IAccountWorkFence? fence = null) : IAccountCraftingSnapshotService
{
    public async Task<Gw2ApiResult<AccountCraftingSnapshot>> RefreshAsync(
        CancellationToken cancellationToken = default)
        => (await RefreshWithOutcomeAsync(cancellationToken).ConfigureAwait(false)).Result;

    public Task<AccountCraftingRefreshResult> RefreshWithOutcomeAsync(
        CancellationToken cancellationToken = default)
        => fence is null ? RefreshCoreAsync(cancellationToken) : fence.RunAsync(RefreshCoreAsync, cancellationToken);

    private async Task<AccountCraftingRefreshResult> RefreshCoreAsync(CancellationToken cancellationToken)
    {
        var result = await gateway.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null)
        {
            return new(result, null);
        }

        try
        {
            if (fence is not null) await fence.BindAccountAsync(result.Value.AccountScope, cancellationToken).ConfigureAwait(false);
            var previous = await repository.GetLatestAsync(result.Value.AccountScope, cancellationToken).ConfigureAwait(false);
            await repository.ReplaceAsync(result.Value, cancellationToken).ConfigureAwait(false);
            return new(result, previous is null || !SameFacts(previous, result.Value));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new(Gw2ApiResult<AccountCraftingSnapshot>.Failure(Gw2ApiErrorCategory.UnexpectedResponse), null);
        }
    }

    public Task<AccountCraftingSnapshot?> GetLatestAsync(
        PersonalTradingPost.AccountScope accountScope,
        CancellationToken cancellationToken = default) =>
        repository.GetLatestAsync(accountScope, cancellationToken);

    private static bool SameFacts(AccountCraftingSnapshot left, AccountCraftingSnapshot right) =>
        left.AccountScope == right.AccountScope &&
        Same(left.BankInventory, right.BankInventory) &&
        Same(left.MaterialStorage, right.MaterialStorage) &&
        Same(left.RecipeUnlocks, right.RecipeUnlocks) &&
        Same(left.CharacterCrafting, right.CharacterCrafting);

    private static bool Same<T>(CraftingFeatureResult<IReadOnlyList<T>> left, CraftingFeatureResult<IReadOnlyList<T>> right) =>
        left.Availability == right.Availability &&
        left.ErrorCategory == right.ErrorCategory &&
        (left.Value is null || right.Value is null
            ? left.Value is null && right.Value is null
            : left.Value.SequenceEqual(right.Value));
}
