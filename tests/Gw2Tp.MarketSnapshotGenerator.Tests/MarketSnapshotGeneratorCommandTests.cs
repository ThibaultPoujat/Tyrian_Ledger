using System.Text.Json;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketSnapshots;
using Gw2Tp.MarketSnapshotGenerator;
using Gw2Tp.Testing;
using Xunit;

namespace Gw2Tp.MarketSnapshotGenerator.Tests;

public sealed class MarketSnapshotGeneratorCommandTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Command_writes_a_complete_public_artifact_without_secret_or_personal_fields()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var outputPath = Path.Combine(directory, "market-snapshot.json");
            var command = CreateCommand(new StubMarketDataClient());
            using var standardOutput = new StringWriter();
            using var standardError = new StringWriter();

            var exitCode = await command.RunAsync(
                ["--output", outputPath],
                standardOutput,
                standardError);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputPath));
            Assert.Contains("2 requests/second", standardOutput.ToString());
            Assert.Empty(standardError.ToString());

            var json = await File.ReadAllTextAsync(outputPath);
            var fixture = await File.ReadAllTextAsync(Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "market-snapshots",
                "market-snapshot-v1.json"));
            Assert.Equal(fixture.TrimEnd(), json.TrimEnd());
            using var document = JsonDocument.Parse(json);
            Assert.Equal(1, document.RootElement.GetProperty("contractVersion").GetInt32());
            Assert.Equal("2026-09-01T12:00:00.0000000Z", document.RootElement.GetProperty("generatedAtUtc").GetString());
            Assert.Equal("Synthetic public item", document.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("itemName").GetString());
            Assert.DoesNotContain("key", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("account", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("preference", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Command_rejects_incomplete_data_and_preserves_the_existing_artifact()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var outputPath = Path.Combine(directory, "market-snapshot.json");
            await File.WriteAllTextAsync(outputPath, "previous-complete-artifact");
            var command = CreateCommand(new StubMarketDataClient(
                metadataResult: Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>.Success([])));
            using var standardOutput = new StringWriter();
            using var standardError = new StringWriter();

            var exitCode = await command.RunAsync(
                ["--output", outputPath],
                standardOutput,
                standardError);

            Assert.Equal(1, exitCode);
            Assert.Equal("previous-complete-artifact", await File.ReadAllTextAsync(outputPath));
            Assert.Contains("incomplete public market data", standardError.ToString());
            Assert.Empty(Directory.GetFiles(directory, ".market-snapshot.json.*.tmp"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Command_returns_usage_error_without_an_output_argument()
    {
        var command = CreateCommand(new StubMarketDataClient());
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();

        var exitCode = await command.RunAsync([], standardOutput, standardError);

        Assert.Equal(2, exitCode);
        Assert.Contains("Usage:", standardError.ToString());
    }

    private static MarketSnapshotGeneratorCommand CreateCommand(StubMarketDataClient client) => new(
        new PublicMarketSnapshotCollector(client, new FrozenClock(GeneratedAt)),
        new MarketSnapshotJsonWriter());

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tyrian-ledger-market-snapshot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class StubMarketDataClient : IGw2ApiClient
    {
        private readonly Gw2ApiResult<IReadOnlyList<MarketItemMetadata>> metadataResult;

        public StubMarketDataClient(
            Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>? metadataResult = null)
        {
            this.metadataResult = metadataResult ?? Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>.Success(
            [
                new MarketItemMetadata(
                    900001,
                    "Synthetic public item",
                    MarketItemStackPolicy.NormalStackLimit),
            ]);
        }

        public Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<IReadOnlyList<int>>.Success([900001]));

        public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
            IReadOnlyCollection<int> itemIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success(
            [
                new MarketPrice(
                    900001,
                    IsWhitelisted: false,
                    new MarketOrderSummary(100, 1_000),
                    new MarketOrderSummary(100, 1_500)),
            ]));

        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
            IReadOnlyCollection<int> itemIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<IReadOnlyList<MarketListing>>.Success(
            [
                new MarketListing(
                    900001,
                    [new MarketOrderLevel(3, 100, 1_000)],
                    [new MarketOrderLevel(3, 100, 1_500)]),
            ]));

        public Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(
            IReadOnlyCollection<int> itemIds,
            CancellationToken cancellationToken = default) => Task.FromResult(metadataResult);
    }
}
