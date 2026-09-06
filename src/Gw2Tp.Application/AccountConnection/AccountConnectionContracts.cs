namespace Gw2Tp.Application.AccountConnection;

/// <summary>
/// Safe, browser-facing readiness state for optional authenticated account features.
/// </summary>
public enum AccountConnectionState
{
    NotConfigured,
    Valid,
    Invalid,
    InsufficientPermissions,
    Unavailable,
}

/// <summary>
/// A normalized account-key result. This contract intentionally contains no key,
/// token fragment, token name, raw response, or upstream diagnostic information.
/// </summary>
public sealed record AccountConnectionStatus(
    AccountConnectionState State,
    IReadOnlyList<string> GrantedPermissions,
    IReadOnlyList<string> MissingRequiredPermissions);

public interface IAccountConnectionStatusService
{
    Task<AccountConnectionStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);
}

public static class AccountConnectionPermissions
{
    public const string Account = "account";
    public const string TradingPost = "tradingpost";

    public static IReadOnlyList<string> Required { get; } = [Account, TradingPost];

    public static IReadOnlyList<string> Known { get; } = [Account, TradingPost];
}
