namespace Gw2Tp.Application.Recommendations;

/// <summary>
/// Defines the maximum usable age of the account-owned evidence that changes
/// recommendation eligibility. Public market freshness is independently owned
/// by the live scanner and is not folded into this policy.
/// </summary>
public sealed record PrimaryRecommendationFreshnessPolicy(
    TimeSpan PersonalSyncMaximumAge,
    TimeSpan CurrentOrdersMaximumAge)
{
    public static PrimaryRecommendationFreshnessPolicy Default { get; } = new(
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(15));

    public DateTimeOffset? AccountEvidenceExpiresAtUtc(
        DateTimeOffset? lastSuccessfulSyncAtUtc,
        DateTimeOffset? currentOrdersObservedAtUtc)
    {
        Validate();
        if (lastSuccessfulSyncAtUtc is not { } sync || currentOrdersObservedAtUtc is not { } orders)
        {
            return null;
        }

        return Min(
            RequireUtc(sync, nameof(lastSuccessfulSyncAtUtc)) + PersonalSyncMaximumAge,
            RequireUtc(orders, nameof(currentOrdersObservedAtUtc)) + CurrentOrdersMaximumAge);
    }

    public bool IsAccountEvidenceCurrent(
        DateTimeOffset? lastSuccessfulSyncAtUtc,
        DateTimeOffset? currentOrdersObservedAtUtc,
        DateTimeOffset asOfUtc)
    {
        var now = RequireUtc(asOfUtc, nameof(asOfUtc));
        return lastSuccessfulSyncAtUtc is { } sync &&
            currentOrdersObservedAtUtc is { } orders &&
            RequireUtc(sync, nameof(lastSuccessfulSyncAtUtc)) <= now &&
            RequireUtc(orders, nameof(currentOrdersObservedAtUtc)) <= now &&
            AccountEvidenceExpiresAtUtc(sync, orders) is { } expiresAtUtc &&
            now <= expiresAtUtc;
    }

    private void Validate()
    {
        if (PersonalSyncMaximumAge <= TimeSpan.Zero || CurrentOrdersMaximumAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(PrimaryRecommendationFreshnessPolicy));
        }
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName) =>
        value.Offset == TimeSpan.Zero
            ? value
            : throw new ArgumentException("Freshness instants must be UTC.", parameterName);

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) =>
        left <= right ? left : right;
}
