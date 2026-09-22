using Gw2Tp.Application.MarketData;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.PersonalTradingPost;

/// <summary>
/// Read-only access to the authenticated personal Trading Post resources.
/// Credentials and upstream HTTP details remain outside this contract.
/// </summary>
public interface IPersonalTradingPostGateway
{
    Task<Gw2ApiResult<AccountScope>> GetAccountScopeAsync(
        CancellationToken cancellationToken = default);

    Task<Gw2ApiResult<PersonalTransactionPage>> GetCurrentBuyOrdersAsync(
        int page,
        CancellationToken cancellationToken = default);

    Task<Gw2ApiResult<PersonalTransactionPage>> GetCurrentSellListingsAsync(
        int page,
        CancellationToken cancellationToken = default);

    Task<Gw2ApiResult<PersonalTransactionPage>> GetCompletedBuyHistoryAsync(
        int page,
        CancellationToken cancellationToken = default);

    Task<Gw2ApiResult<PersonalTransactionPage>> GetCompletedSellHistoryAsync(
        int page,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-only access to authenticated account identity and spendable Coin.
/// Implementations must obtain both values with one captured credential.
/// </summary>
public interface IAccountPortfolioGateway
{
    Task<Gw2ApiResult<AccountPortfolioSnapshot>> GetSnapshotAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Normalized portfolio identity, Coin balance and optional verified resource
/// quantities. Resource keys are local typed keys; the account identifier is an
/// opaque local scoping value and must never be returned to the browser.
/// </summary>
public sealed record AccountPortfolioSnapshot(
    AccountScope AccountScope,
    Money AvailableCash,
    IReadOnlyDictionary<string, long>? VerifiedQuantities = null);

/// <summary>
/// Stable opaque identity used to scope local personal data. Account names and
/// other mutable account metadata deliberately do not cross this boundary.
/// </summary>
public sealed record AccountScope(string AccountId);

/// <summary>
/// A normalized Trading Post transaction. Prices are integer copper.
/// </summary>
public sealed record PersonalTradingPostTransaction(
    long TransactionId,
    int ItemId,
    int PriceInCopper,
    int Quantity,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PurchasedAtUtc);

/// <summary>
/// One upstream transaction page together with the paging metadata returned
/// by ArenaNet. Page numbers are zero-based.
/// </summary>
public sealed record PersonalTransactionPage(
    IReadOnlyList<PersonalTradingPostTransaction> Transactions,
    int PageNumber,
    int PageSize,
    int PageCount,
    int ResultCount,
    int ResultTotal);
