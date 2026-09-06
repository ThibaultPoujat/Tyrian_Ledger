namespace Gw2Tp.Application.Persistence;

/// <summary>
/// The side of a personal Trading Post transaction or currently open order.
/// </summary>
public enum PersonalTradingPostSide
{
    Buy = 1,
    Sell = 2,
}

/// <summary>
/// A local profile scoped by the opaque account identity returned by the
/// authenticated gateway. It intentionally has no credential or account-name field.
/// </summary>
public sealed record AccountProfile(
    long Id,
    string AccountScopeId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastSuccessfulSyncAtUtc);

/// <summary>
/// One completed personal Trading Post event before local import metadata is added.
/// Monetary values are integer copper and all timestamps must be UTC.
/// </summary>
public sealed record CompletedPersonalTradingPostTransaction(
    long ExternalTransactionId,
    PersonalTradingPostSide Side,
    int ItemId,
    int UnitPriceInCopper,
    int Quantity,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc);

/// <summary>
/// A persisted completed event together with the local import observations.
/// </summary>
public sealed record StoredCompletedPersonalTradingPostTransaction(
    CompletedPersonalTradingPostTransaction Transaction,
    DateTimeOffset FirstImportedAtUtc,
    DateTimeOffset LastSeenAtUtc);

/// <summary>
/// One currently open personal Trading Post order as observed from a complete remote read.
/// </summary>
public sealed record CurrentPersonalTradingPostOrder(
    long ExternalOrderId,
    PersonalTradingPostSide Side,
    int ItemId,
    int UnitPriceInCopper,
    int Quantity,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// A complete point-in-time current-order read. Partial reads must never be passed to
/// the replacement operation.
/// </summary>
public sealed record CurrentPersonalTradingPostOrderSnapshot(
    DateTimeOffset ObservedAtUtc,
    IReadOnlyList<CurrentPersonalTradingPostOrder> Orders);

/// <summary>
/// Normalized public item display metadata. This is reference data, not an ArenaNet
/// payload and not a product-policy store.
/// </summary>
public sealed record StoredItemMetadata(
    int ItemId,
    string Name,
    DateTimeOffset ObservedAtUtc);

/// <summary>
/// Typed, non-secret local settings. Nullable values are intentionally "not set" rather
/// than invented financial defaults; policy interpretation remains outside persistence.
/// </summary>
public sealed record UserSettings(
    int SettingsVersion,
    int? MinimumProfitInCopper,
    int? MinimumRoiBasisPoints,
    int? CashReserveBasisPoints,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Durable access to account-scoped personal Trading Post data. Implementations own
/// storage details; callers must not depend on a database or SQL implementation.
/// </summary>
public interface IPersonalTradingPostRepository
{
    Task<AccountProfile> GetOrCreateAccountProfileAsync(
        string accountScopeId,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default);

    Task RecordSuccessfulSyncAsync(
        AccountProfile accountProfile,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default);

    Task UpsertCompletedTransactionsAsync(
        AccountProfile accountProfile,
        IReadOnlyCollection<CompletedPersonalTradingPostTransaction> transactions,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredCompletedPersonalTradingPostTransaction>> GetCompletedTransactionsAsync(
        AccountProfile accountProfile,
        CancellationToken cancellationToken = default);

    Task ReplaceCurrentOrderSnapshotAsync(
        AccountProfile accountProfile,
        CurrentPersonalTradingPostOrderSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CurrentPersonalTradingPostOrder>> GetCurrentOrdersAsync(
        AccountProfile accountProfile,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CurrentPersonalTradingPostOrderSnapshot>> GetCurrentOrderObservationsAsync(
        AccountProfile accountProfile,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Durable access to normalized public item metadata.
/// </summary>
public interface IItemMetadataRepository
{
    Task UpsertAsync(
        IReadOnlyCollection<StoredItemMetadata> items,
        CancellationToken cancellationToken = default);

    Task<StoredItemMetadata?> GetAsync(int itemId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Durable access to the singleton typed, non-secret settings document.
/// </summary>
public interface IUserSettingsRepository
{
    Task<UserSettings?> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default);
}
