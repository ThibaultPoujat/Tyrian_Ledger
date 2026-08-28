namespace Gw2Tp.Application.AccountAccess;

/// <summary>
/// Safe account-access metadata suitable for application features and local
/// web responses. It never contains an API credential.
/// </summary>
public sealed record AccountAccessStatus(
    AccountAccessValidationStatus ValidationStatus,
    string? KeyId,
    string? KeyName,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<AccountFeatureAccess> Features);
