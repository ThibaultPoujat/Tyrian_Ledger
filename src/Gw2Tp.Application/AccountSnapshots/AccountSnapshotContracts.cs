using Gw2Tp.Application.MarketData;
using Gw2Tp.Domain.MarketData;

namespace Gw2Tp.Application.AccountSnapshots;

/// <summary>
/// Controls whether a caller accepts a fresh process-local account snapshot
/// or explicitly requests a replacement from the GW2 API.
/// </summary>
public enum AccountSnapshotRefreshMode
{
    UseCache,
    ForceRefresh,
}

/// <summary>
/// Stable outcome for an account snapshot request. These values intentionally
/// avoid exposing raw HTTP responses or credential details.
/// </summary>
public enum AccountSnapshotLoadStatus
{
    Available,
    PartialData,
    NotConfigured,
    InvalidCredential,
    AccessUnavailable,
    MissingPermission,
    AuthenticationFailed,
    PermissionDenied,
    SourceUnavailable,
    InvalidRemoteData,
}

public enum AccountSnapshotComponent
{
    Bank,
    Materials,
    UnlockedRecipes,
    CharacterList,
    CharacterCrafting,
}

public sealed record AccountSnapshotComponentFailure(
    AccountSnapshotComponent Component,
    Gw2ApiErrorCategory ErrorCategory,
    string? CharacterName = null);

/// <summary>
/// One feature-scoped account-data read. Only successful complete reads carry
/// freshness metadata and may be served from the local account cache.
/// </summary>
public sealed record AccountSnapshotLoadResult<TSnapshot>(
    AccountSnapshotLoadStatus Status,
    TSnapshot? Snapshot,
    DataFreshness? Freshness,
    IReadOnlyList<string> MissingPermissions,
    IReadOnlyList<AccountSnapshotComponentFailure> Failures)
    where TSnapshot : class;

/// <summary>
/// Minimal bank and material-storage facts required for owned-item economics.
/// </summary>
public sealed record AccountOwnedItemsSnapshot(
    string ProfileId,
    IReadOnlyList<AccountBankItem> BankItems,
    IReadOnlyList<AccountMaterial> Materials);

public sealed record AccountBankItem(int ItemId, int Count, string? Binding);

public sealed record AccountMaterial(int ItemId, int Category, int Count);

/// <summary>
/// Minimal account crafting facts required to evaluate recipe feasibility.
/// </summary>
public sealed record AccountCraftingSnapshot(
    string ProfileId,
    IReadOnlyList<int> UnlockedRecipeIds,
    IReadOnlyList<AccountCharacterCrafting> Characters);

public sealed record AccountCharacterCrafting(
    string CharacterName,
    IReadOnlyList<AccountCraftingDiscipline> Disciplines);

public sealed record AccountCraftingDiscipline(
    string Discipline,
    int Rating,
    bool IsActive);

/// <summary>
/// Read-only, feature-scoped access to minimized local account snapshots.
/// The API credential is never an input or result of this abstraction.
/// </summary>
public interface IAccountSnapshotService
{
    Task<AccountSnapshotLoadResult<AccountOwnedItemsSnapshot>> GetOwnedItemsAsync(
        AccountSnapshotRefreshMode refreshMode = AccountSnapshotRefreshMode.UseCache,
        CancellationToken cancellationToken = default);

    Task<AccountSnapshotLoadResult<AccountCraftingSnapshot>> GetCraftingAsync(
        AccountSnapshotRefreshMode refreshMode = AccountSnapshotRefreshMode.UseCache,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Clears minimized account snapshots retained only for the current local
/// application process. This operation never changes persistent history,
/// preferences, public market cache data, or an operating-system credential.
/// </summary>
public interface IAccountSnapshotCacheClearer
{
    void ClearCachedSnapshots();
}
