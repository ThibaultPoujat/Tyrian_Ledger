using Gw2Tp.Application.MarketSnapshots;

namespace Gw2Tp.MarketSnapshotGenerator;

/// <summary>
/// Testable command surface for creating one complete public market artifact.
/// </summary>
public sealed class MarketSnapshotGeneratorCommand
{
    private readonly PublicMarketSnapshotCollector collector;
    private readonly MarketSnapshotJsonWriter writer;

    public MarketSnapshotGeneratorCommand(
        PublicMarketSnapshotCollector collector,
        MarketSnapshotJsonWriter writer)
    {
        this.collector = collector ?? throw new ArgumentNullException(nameof(collector));
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        if (!TryParseOutputPath(arguments, out var outputPath))
        {
            await standardError.WriteLineAsync("Usage: Gw2Tp.MarketSnapshotGenerator --output <artifact-path>").ConfigureAwait(false);
            return 2;
        }

        try
        {
            var collection = await collector.CollectAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var document = MarketSnapshotContract.Create(collection.GeneratedAtUtc, collection.Candidates);
            await writer.WriteAsync(document, outputPath, cancellationToken).ConfigureAwait(false);
            await standardOutput.WriteLineAsync(
                $"Wrote a complete market snapshot with {document.Candidates.Count} candidates " +
                $"under {MarketSnapshotCapturePolicy.RequestsPerSecond} requests/second, " +
                $"{MarketSnapshotCapturePolicy.MaxConcurrentRequests} concurrent requests, and " +
                $"a burst budget of {MarketSnapshotCapturePolicy.BurstBudget}.").ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await standardError.WriteLineAsync("Market snapshot generation was cancelled before an artifact was published.").ConfigureAwait(false);
            return 1;
        }
        catch (PublicMarketSnapshotCollectionException exception)
        {
            await standardError.WriteLineAsync(
                $"Market snapshot generation rejected incomplete public market data ({exception.ErrorCategory}).").ConfigureAwait(false);
            return 1;
        }
        catch (IOException)
        {
            await standardError.WriteLineAsync("Market snapshot generation could not write the artifact.").ConfigureAwait(false);
            return 1;
        }
        catch (UnauthorizedAccessException)
        {
            await standardError.WriteLineAsync("Market snapshot generation could not write the artifact.").ConfigureAwait(false);
            return 1;
        }
        catch (ArgumentException)
        {
            await standardError.WriteLineAsync("Market snapshot generation could not write the artifact.").ConfigureAwait(false);
            return 1;
        }
        catch (NotSupportedException)
        {
            await standardError.WriteLineAsync("Market snapshot generation could not write the artifact.").ConfigureAwait(false);
            return 1;
        }
    }

    private static bool TryParseOutputPath(IReadOnlyList<string> arguments, out string outputPath)
    {
        if (arguments.Count == 2 && arguments[0] == "--output" && !string.IsNullOrWhiteSpace(arguments[1]))
        {
            outputPath = arguments[1];
            return true;
        }

        outputPath = string.Empty;
        return false;
    }
}
