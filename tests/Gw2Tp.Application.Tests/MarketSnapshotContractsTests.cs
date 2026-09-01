using System.Text.Json;
using Gw2Tp.Application.MarketSnapshots;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class MarketSnapshotContractsTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_canonicalizes_candidates_and_order_book_levels_for_safe_json_serialization()
    {
        var document = MarketSnapshotContract.Create(
            GeneratedAt,
            [
                new MarketSnapshotCandidate(
                    2,
                    "Synthetic two",
                    [
                        new MarketSnapshotOrderLevel(3, 10, 1200),
                        new MarketSnapshotOrderLevel(2, 8, 1200),
                    ],
                    [new MarketSnapshotOrderLevel(1, 5, 1400)]),
                new MarketSnapshotCandidate(
                    1,
                    "Synthetic one",
                    [new MarketSnapshotOrderLevel(1, 5, 1000)],
                    [new MarketSnapshotOrderLevel(2, 10, 1500)]),
            ]);

        Assert.Equal([1, 2], document.Candidates.Select(candidate => candidate.ItemId));
        Assert.Equal([8, 10], document.Candidates[1].Buys.Select(level => level.Quantity));
        Assert.Equal("2026-09-01T12:00:00.0000000Z", document.GeneratedAtUtc);

        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal(1, parsed.RootElement.GetProperty("contractVersion").GetInt32());
        Assert.Equal(JsonValueKind.Number, parsed.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("itemId").ValueKind);
        Assert.Equal(2, parsed.RootElement
            .GetProperty("capturePolicy")
            .GetProperty("maxConcurrentRequests").GetInt32());
    }

    [Fact]
    public void Validate_rejects_unknown_version_or_incompatible_metadata()
    {
        var valid = MarketSnapshotContract.Create(GeneratedAt, []);

        Assert.Throws<ArgumentException>(() => (valid with { ContractVersion = 2 }).Validate());
        Assert.Throws<ArgumentException>(() => (valid with
        {
            Compatibility = valid.Compatibility with { MoneyUnit = "gold" },
        }).Validate());
        Assert.Throws<ArgumentException>(() => (valid with
        {
            GeneratedAtUtc = "2026-09-01T12:00:00Z",
        }).Validate());
    }

    [Fact]
    public void Create_rejects_a_snapshot_larger_than_the_bounded_finalist_set()
    {
        var candidates = Enumerable.Range(1, MarketSnapshotContract.MaximumCandidateCount + 1)
            .Select(itemId => new MarketSnapshotCandidate(
                itemId,
                $"Synthetic {itemId}",
                [],
                []))
            .ToArray();

        Assert.Throws<ArgumentException>(() => MarketSnapshotContract.Create(GeneratedAt, candidates));
    }
}
