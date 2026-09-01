using System.Globalization;
using Gw2Tp.Application.MarketData;

namespace Gw2Tp.Application.MarketSnapshots;

/// <summary>
/// Stable public contract constants for the static market snapshot artifact.
/// </summary>
public static class MarketSnapshotContract
{
    public const int Version = 1;
    public const int MaximumCandidateCount = 200;
    public const string MoneyUnit = "copper";
    public const string RecommendationPolicyVersion = "m9-v1";

    public static MarketSnapshotDocument Create(
        DateTimeOffset generatedAtUtc,
        IReadOnlyList<MarketSnapshotCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var document = new MarketSnapshotDocument(
            Version,
            FormatUtcTimestamp(generatedAtUtc),
            new MarketSnapshotCompatibility(
                MoneyUnit,
                RecommendationPolicyVersion,
                MarketItemStackPolicy.NormalStackLimit),
            MarketSnapshotCapturePolicy.Metadata,
            candidates
                .OrderBy(candidate => candidate.ItemId)
                .Select(CanonicalizeCandidate)
                .ToArray());
        Validate(document);
        return document;
    }

    public static void Validate(MarketSnapshotDocument? document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.ContractVersion != Version)
        {
            throw new ArgumentException($"Unsupported market snapshot contract version {document.ContractVersion}.", nameof(document));
        }

        ValidateGeneratedAtUtc(document.GeneratedAtUtc);
        ValidateCompatibility(document.Compatibility);
        ValidateCapturePolicy(document.CapturePolicy);
        ValidateCandidates(document.Candidates);
    }

    private static MarketSnapshotCandidate CanonicalizeCandidate(MarketSnapshotCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(candidate.Buys);
        ArgumentNullException.ThrowIfNull(candidate.Sells);

        return candidate with
        {
            Buys = OrderLevels(candidate.Buys),
            Sells = OrderLevels(candidate.Sells),
        };
    }

    private static IReadOnlyList<MarketSnapshotOrderLevel> OrderLevels(
        IReadOnlyList<MarketSnapshotOrderLevel> levels) => levels
        .OrderBy(level => level.UnitPriceInCopper)
        .ThenBy(level => level.Quantity)
        .ThenBy(level => level.ListingCount)
        .ToArray();

    private static string FormatUtcTimestamp(DateTimeOffset value)
    {
        if (value == default)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "The snapshot generation timestamp is required.");
        }

        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    }

    private static void ValidateGeneratedAtUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.EndsWith('Z') ||
            !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed) ||
            parsed.Offset != TimeSpan.Zero ||
            !string.Equals(FormatUtcTimestamp(parsed), value, StringComparison.Ordinal))
        {
            throw new ArgumentException("The snapshot generation timestamp must be a canonical UTC ISO-8601 value.", nameof(value));
        }
    }

    private static void ValidateCompatibility(MarketSnapshotCompatibility? compatibility)
    {
        if (compatibility is null ||
            compatibility.MoneyUnit != MoneyUnit ||
            compatibility.RecommendationPolicyVersion != RecommendationPolicyVersion ||
            compatibility.NormalStackLimit != MarketItemStackPolicy.NormalStackLimit)
        {
            throw new ArgumentException("The snapshot compatibility metadata is unsupported.", nameof(compatibility));
        }
    }

    private static void ValidateCapturePolicy(MarketSnapshotCapturePolicyMetadata? capturePolicy)
    {
        if (capturePolicy is null || capturePolicy != MarketSnapshotCapturePolicy.Metadata)
        {
            throw new ArgumentException("The snapshot capture policy metadata is unsupported.", nameof(capturePolicy));
        }
    }

    private static void ValidateCandidates(IReadOnlyList<MarketSnapshotCandidate>? candidates)
    {
        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }

        if (candidates.Count > MaximumCandidateCount)
        {
            throw new ArgumentException(
                $"A market snapshot cannot contain more than {MaximumCandidateCount} candidates.",
                nameof(candidates));
        }

        var previousItemId = 0;
        foreach (var candidate in candidates)
        {
            if (candidate is null || candidate.ItemId <= previousItemId || string.IsNullOrWhiteSpace(candidate.ItemName))
            {
                throw new ArgumentException("Snapshot candidates must have distinct, ascending positive item IDs and names.", nameof(candidates));
            }

            ValidateLevels(candidate.Buys, nameof(candidate.Buys));
            ValidateLevels(candidate.Sells, nameof(candidate.Sells));
            previousItemId = candidate.ItemId;
        }
    }

    private static void ValidateLevels(IReadOnlyList<MarketSnapshotOrderLevel>? levels, string parameterName)
    {
        if (levels is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        MarketSnapshotOrderLevel? previous = null;
        foreach (var level in levels)
        {
            if (level is null || level.ListingCount <= 0 || level.Quantity <= 0 || level.UnitPriceInCopper <= 0 ||
                previous is not null && CompareLevel(previous, level) > 0)
            {
                throw new ArgumentException("Snapshot order levels must be positive and canonically ordered.", parameterName);
            }

            previous = level;
        }
    }

    private static int CompareLevel(MarketSnapshotOrderLevel left, MarketSnapshotOrderLevel right)
    {
        var priceComparison = left.UnitPriceInCopper.CompareTo(right.UnitPriceInCopper);
        if (priceComparison != 0)
        {
            return priceComparison;
        }

        var quantityComparison = left.Quantity.CompareTo(right.Quantity);
        return quantityComparison != 0
            ? quantityComparison
            : left.ListingCount.CompareTo(right.ListingCount);
    }
}

/// <summary>
/// Versioned browser-consumable market snapshot. It intentionally contains no
/// account, preference, credential, or generated recommendation data.
/// </summary>
public sealed record MarketSnapshotDocument(
    int ContractVersion,
    string GeneratedAtUtc,
    MarketSnapshotCompatibility Compatibility,
    MarketSnapshotCapturePolicyMetadata CapturePolicy,
    IReadOnlyList<MarketSnapshotCandidate> Candidates)
{
    public void Validate() => MarketSnapshotContract.Validate(this);
}

/// <summary>
/// Contract metadata that a browser must understand before it can safely use
/// the current recommendation policy with the snapshot inputs.
/// </summary>
public sealed record MarketSnapshotCompatibility(
    string MoneyUnit,
    string RecommendationPolicyVersion,
    int NormalStackLimit);

/// <summary>
/// Public evidence of the conservative gateway limits applied during capture.
/// These are application policy, not assertions about an upstream API quota.
/// </summary>
public sealed record MarketSnapshotCapturePolicyMetadata(
    int RequestsPerSecond,
    int MaxConcurrentRequests,
    int BurstBudget);

/// <summary>
/// Public market input for a single recommendation candidate.
/// </summary>
public sealed record MarketSnapshotCandidate(
    int ItemId,
    string ItemName,
    IReadOnlyList<MarketSnapshotOrderLevel> Buys,
    IReadOnlyList<MarketSnapshotOrderLevel> Sells);

/// <summary>
/// One public order-book level. All values are safe JSON integers and money is
/// represented as integer copper.
/// </summary>
public sealed record MarketSnapshotOrderLevel(
    int ListingCount,
    int Quantity,
    int UnitPriceInCopper);

/// <summary>
/// The dedicated, deliberately conservative capture limits for the static
/// generator. Changing them requires an explicit contract and workflow review.
/// </summary>
public static class MarketSnapshotCapturePolicy
{
    public const int RequestsPerSecond = 2;
    public const int MaxConcurrentRequests = 2;
    public const int BurstBudget = 20;

    public static MarketSnapshotCapturePolicyMetadata Metadata { get; } = new(
        RequestsPerSecond,
        MaxConcurrentRequests,
        BurstBudget);
}
