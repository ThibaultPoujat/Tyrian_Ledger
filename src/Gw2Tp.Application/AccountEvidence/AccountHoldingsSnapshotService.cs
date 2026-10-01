using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.LocalData;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Application.Time;

namespace Gw2Tp.Application.AccountEvidence;

/// <summary>One collector bundle, existing gateway budget, one fenced atomic publication.</summary>
public sealed class AccountHoldingsSnapshotService(
    IAccountHoldingsCollector collector, IAccountHoldingsSnapshotRepository repository,
    IGw2ApiClient market, IAccountHoldingsRulesProvider rules, IClock clock,
    IAccountWorkFence fence) : IAccountHoldingsSnapshotService
{
    public Task<Gw2ApiResult<AccountHoldingsSnapshot>> RefreshAsync(CancellationToken cancellationToken = default) =>
        fence.RunAsync(RefreshCoreAsync, cancellationToken);

    private async Task<Gw2ApiResult<AccountHoldingsSnapshot>> RefreshCoreAsync(CancellationToken cancellationToken)
    {
        var result = await collector.CollectAsync(clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null)
            return Gw2ApiResult<AccountHoldingsSnapshot>.Failure(result.ErrorCategory ?? Gw2ApiErrorCategory.UnexpectedResponse);
        var capture = result.Value;
        await fence.BindAccountAsync(capture.AccountScope, cancellationToken).ConfigureAwait(false);
        var context = fence.Current ?? throw new AccountWorkRejectedException();
        var categories = new Dictionary<int, HoldingsItemCategory>();
        var ids = AccountEvidencePolicyFacts.InventorySources(capture).SelectMany(source => source.Value?.Items ?? [])
            .Select(row => row.ItemId).Distinct().Order().ToArray();
        // Sequential bounded batches use the existing public gateway scheduler, no new timer/fan-out.
        foreach (var batch in ids.Chunk(200))
        {
            var metadata = await market.GetItemMetadataAsync(batch, cancellationToken).ConfigureAwait(false);
            if (!metadata.IsSuccess || metadata.IsPartialData || metadata.Value is null) continue;
            foreach (var item in metadata.Value.Where(item => batch.Contains(item.ItemId)))
                categories[item.ItemId] = Category(item.ItemType);
        }
        var policyRules = rules.Get(capture.AccountScope);
        var protection = new AccountHoldingsProtectionPolicy().Evaluate(capture, policyRules, categories);
        var snapshot = new AccountHoldingsSnapshot(capture, context.StoreIncarnation, context.Generation,
            categories, policyRules, protection.Floor, []);
        try
        {
            if (!await repository.ReplaceAsync(snapshot, cancellationToken).ConfigureAwait(false))
                return Gw2ApiResult<AccountHoldingsSnapshot>.Failure(Gw2ApiErrorCategory.UnexpectedResponse);
            // Repository read validates the same captured context and returns the merged protective floor.
            var committed = await repository.GetLatestAsync(capture.AccountScope, cancellationToken).ConfigureAwait(false);
            return committed is null ? Gw2ApiResult<AccountHoldingsSnapshot>.Failure(Gw2ApiErrorCategory.UnexpectedResponse)
                : Gw2ApiResult<AccountHoldingsSnapshot>.Success(committed);
        }
        catch (AccountWorkRejectedException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return Gw2ApiResult<AccountHoldingsSnapshot>.Failure(Gw2ApiErrorCategory.UnexpectedResponse); }
    }

    public Task<AccountHoldingsSnapshot?> GetLatestAsync(AccountScope scope, CancellationToken cancellationToken = default) =>
        repository.GetLatestAsync(scope, cancellationToken);

    public async Task<AccountHoldingsProjection?> GetProjectionAsync(AccountScope scope, DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await repository.GetLatestAsync(scope, cancellationToken).ConfigureAwait(false);
        if (snapshot is null) return null;
        var context = fence.Current ?? throw new AccountWorkRejectedException();
        return new AccountHoldingsProjector().Project(snapshot, evaluatedAtUtc, context.Generation, context.StoreIncarnation);
    }

    public static HoldingsItemCategory Category(string? type) => type switch
    {
        "CraftingMaterial" or "Consumable" or "Container" or "Trophy" or "Key" => HoldingsItemCategory.Commodity,
        "Armor" or "Back" or "Bag" or "Gathering" or "Trinket" or "Weapon" or "UpgradeComponent" or
            "Relic" or "JadeTechModule" or "PowerCore" => HoldingsItemCategory.Equipment,
        _ => HoldingsItemCategory.Unknown,
    };

    /// <summary>Compatibility read model only. It never becomes a competing owned-inventory authority.</summary>
    public static AccountCraftingSnapshot CraftingStatus(AccountHoldingsSnapshot snapshot)
    {
        var capture = snapshot.Capture;
        CraftingFeatureResult<IReadOnlyList<T>> Feature<T>(bool available, IReadOnlyList<T> value, Gw2ApiErrorCategory? error = null) => available
            ? CraftingFeatureResult<IReadOnlyList<T>>.Available(value)
            : CraftingFeatureResult<IReadOnlyList<T>>.FromFailure(error ?? Gw2ApiErrorCategory.UnexpectedResponse);
        var currentActors = AccountEvidencePolicyFacts.CurrentActors(capture);
        var crafting = currentActors is not null && capture.Characters.Any(actor => AccountEvidencePolicyFacts.Complete(actor.Crafting, AccountHoldingsSource.CharacterCrafting, actor.Actor.ActorId));
        return new(capture.AccountScope, capture.AccountIdentityFetch.StartedAtUtc,
            Feature(AccountEvidencePolicyFacts.Complete(capture.Bank, AccountHoldingsSource.Bank),
                (capture.Bank.Value?.Items ?? []).Where(row => row.Quantity > 0).Select(row => new AccountInventoryEntry(row.ItemId, row.Quantity, row.Binding)).ToArray(), capture.Bank.ErrorCategory),
            Feature(AccountEvidencePolicyFacts.Complete(capture.MaterialStorage, AccountHoldingsSource.MaterialStorage),
                (capture.MaterialStorage.Value?.Items ?? []).Select(row => new AccountMaterialEntry(row.ItemId, row.Location.MaterialCategoryIds.FirstOrDefault(), row.Quantity, row.Binding)).ToArray(), capture.MaterialStorage.ErrorCategory),
            Feature(AccountEvidencePolicyFacts.Complete(capture.RecipeUnlocks, AccountHoldingsSource.RecipeUnlocks), capture.RecipeUnlocks.Value ?? [], capture.RecipeUnlocks.ErrorCategory),
            Feature(crafting, capture.Characters.OrderBy(actor => actor.Actor.ActorId, StringComparer.Ordinal)
                .FirstOrDefault(actor => AccountEvidencePolicyFacts.Complete(actor.Crafting, AccountHoldingsSource.CharacterCrafting, actor.Actor.ActorId))?.Crafting.Value ?? []));
    }
}
