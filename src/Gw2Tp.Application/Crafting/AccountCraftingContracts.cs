using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.PersonalTradingPost;

namespace Gw2Tp.Application.Crafting;

/// <summary>
/// Read-only account facts required to decide whether a craft is feasible.
/// Implementations own credentials and upstream HTTP details.
/// </summary>
public interface IAccountCraftingGateway
{
    Task<Gw2ApiResult<AccountCraftingSnapshot>> GetSnapshotAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Normalized public recipe metadata. This boundary deliberately does not
/// expose raw ArenaNet DTOs or account credentials.
/// </summary>
public interface ICraftingReferenceGateway
{
    Task<Gw2ApiResult<IReadOnlyList<CraftingRecipe>>> GetRecipesAsync(
        IReadOnlyCollection<int> recipeIds,
        CancellationToken cancellationToken = default);
}

public enum CraftingAccountFeature
{
    BankInventory = 1,
    MaterialStorage = 2,
    RecipeUnlocks = 3,
    CharacterCrafting = 4,
}

public enum CraftingFeatureAvailability
{
    Available = 1,
    MissingPermission = 2,
    Unavailable = 3,
}

/// <summary>
/// A feature can be unavailable without invalidating other crafting facts.
/// Missing permission is deliberately distinct from a transient failure.
/// </summary>
public sealed record CraftingFeatureResult<T>(
    CraftingFeatureAvailability Availability,
    T? Value,
    Gw2ApiErrorCategory? ErrorCategory)
{
    public static CraftingFeatureResult<T> Available(T value) => new(
        CraftingFeatureAvailability.Available,
        value,
        null);

    public static CraftingFeatureResult<T> FromFailure(Gw2ApiErrorCategory errorCategory) => new(
        errorCategory is Gw2ApiErrorCategory.Unauthorized or Gw2ApiErrorCategory.Forbidden
            ? CraftingFeatureAvailability.MissingPermission
            : CraftingFeatureAvailability.Unavailable,
        default,
        errorCategory);
}

/// <summary>
/// One point-in-time account-scoped snapshot. Account identifiers remain an
/// internal persistence scope and must not be sent to the browser.
/// </summary>
public sealed record AccountCraftingSnapshot(
    AccountScope AccountScope,
    DateTimeOffset CapturedAtUtc,
    CraftingFeatureResult<IReadOnlyList<AccountInventoryEntry>> BankInventory,
    CraftingFeatureResult<IReadOnlyList<AccountMaterialEntry>> MaterialStorage,
    CraftingFeatureResult<IReadOnlyList<int>> RecipeUnlocks,
    CraftingFeatureResult<IReadOnlyList<CraftingDisciplineCapability>> CharacterCrafting);

public enum AccountItemBinding
{
    Unspecified = 0,
    AccountBound = 1,
    CharacterBound = 2,
    OtherBound = 3,
}

/// <summary>
/// A bank slot quantity. Unspecified binding is not evidence that an item is
/// tradable; it only records that this endpoint supplied no binding evidence.
/// </summary>
public sealed record AccountInventoryEntry(int ItemId, int Quantity, AccountItemBinding Binding);

/// <summary>
/// Material storage preserves the game category and quantity, including zero
/// quantities, without retaining the upstream raw payload.
/// </summary>
public sealed record AccountMaterialEntry(int ItemId, int CategoryId, int Quantity, AccountItemBinding Binding);

/// <summary>
/// Character names are intentionally discarded. Capability is the strongest
/// observed rating for each discipline, with active evidence retained.
/// </summary>
public sealed record CraftingDisciplineCapability(string Discipline, int Rating, bool IsActive);

public sealed record CraftingRecipe(
    int RecipeId,
    int OutputItemId,
    int OutputItemCount,
    IReadOnlyList<string> Disciplines,
    int MinRating,
    IReadOnlyList<string> Flags,
    IReadOnlyList<CraftingRecipeIngredient> Ingredients);

public sealed record CraftingRecipeIngredient(string Type, int Id, int Count);

public interface IAccountCraftingSnapshotRepository
{
    Task ReplaceAsync(AccountCraftingSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<AccountCraftingSnapshot?> GetLatestAsync(
        AccountScope accountScope,
        CancellationToken cancellationToken = default);
}

public interface IAccountCraftingSnapshotService
{
    Task<Gw2ApiResult<AccountCraftingSnapshot>> RefreshAsync(
        CancellationToken cancellationToken = default);

    Task<AccountCraftingSnapshot?> GetLatestAsync(
        AccountScope accountScope,
        CancellationToken cancellationToken = default);
}
