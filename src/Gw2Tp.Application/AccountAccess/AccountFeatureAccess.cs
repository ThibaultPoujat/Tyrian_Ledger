namespace Gw2Tp.Application.AccountAccess;

public sealed record AccountFeatureAccess(
    string Feature,
    bool IsAvailable,
    IReadOnlyList<string> MissingPermissions);
