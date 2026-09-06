using Gw2Tp.Application.MarketData;

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
