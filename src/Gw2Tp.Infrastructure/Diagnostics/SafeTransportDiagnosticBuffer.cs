using System.Collections.Concurrent;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Infrastructure.Gw2Api;

namespace Gw2Tp.Infrastructure.Diagnostics;

public sealed record SafeTransportDiagnostic(
    DateTimeOffset TimestampUtc,
    string Operation,
    string Code,
    string ExceptionType,
    string? InnerExceptionType,
    long ElapsedMilliseconds);

/// <summary>
/// A local-only, sanitized market trace. It has counts and batch positions,
/// never API keys, request URLs, item IDs, response bodies, or account facts.
/// </summary>
public sealed record SafeMarketGatewayDiagnostic(
    DateTimeOffset TimestampUtc,
    string Stage,
    string Operation,
    int? BatchIndex,
    int? BatchCount,
    int? RequestedItemIdCount,
    int? ResponseItemIdCount,
    int? MissingItemIdCount,
    int? UnexpectedItemIdCount,
    int? DuplicateItemIdCount,
    int? HttpStatusCode,
    Gw2ApiErrorCategory? ErrorCategory,
    bool? IsPartialResponse,
    int? Attempt,
    long? BackoffMilliseconds,
    long ElapsedMilliseconds,
    string? Outcome);

public sealed class SafeTransportDiagnosticBuffer : ICraftingEvidenceDiagnostics
{
    private const int Capacity = 100;
    private readonly ConcurrentQueue<SafeTransportDiagnostic> events = new();
    private readonly ConcurrentQueue<SafeMarketGatewayDiagnostic> marketEvents = new();

    public void Record(string operation, string code, Exception exception, TimeSpan elapsed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(exception);
        events.Enqueue(new SafeTransportDiagnostic(
            DateTimeOffset.UtcNow,
            operation,
            code,
            exception.GetType().Name,
            exception.InnerException?.GetType().Name,
            Math.Max(0, (long)elapsed.TotalMilliseconds)));
        while (events.Count > Capacity)
        {
            events.TryDequeue(out _);
        }
    }

    public IReadOnlyList<SafeTransportDiagnostic> Snapshot() => events.ToArray();

    public void RecordMarketHttpAttempt(
        string operation,
        int requestedItemIdCount,
        int? responseItemIdCount,
        int? httpStatusCode,
        Gw2ApiErrorCategory? errorCategory,
        bool isPartialResponse,
        TimeSpan elapsed) =>
        EnqueueMarket(new(
            DateTimeOffset.UtcNow, "http-attempt", operation, null, null,
            requestedItemIdCount, responseItemIdCount, null, null, null,
            httpStatusCode, errorCategory, isPartialResponse, null, null,
            Milliseconds(elapsed), null));

    public void RecordMarketBatch(
        string operation,
        int batchIndex,
        int batchCount,
        int requestedItemIdCount,
        int? responseItemIdCount,
        int? missingItemIdCount,
        int? unexpectedItemIdCount,
        int? duplicateItemIdCount,
        bool isPartialResponse,
        Gw2ApiErrorCategory? errorCategory,
        TimeSpan elapsed) =>
        EnqueueMarket(new(
            DateTimeOffset.UtcNow, "batch-completeness", operation, batchIndex, batchCount,
            requestedItemIdCount, responseItemIdCount, missingItemIdCount, unexpectedItemIdCount,
            duplicateItemIdCount, null, errorCategory, isPartialResponse, null, null,
            Milliseconds(elapsed), null));

    internal void RecordMarketRetry(
        string operation,
        int attempt,
        Gw2RetryKind retryKind,
        TimeSpan backoff) =>
        EnqueueMarket(new(
            DateTimeOffset.UtcNow, "retry-backoff", operation, null, null,
            null, null, null, null, null, null, null, null, attempt,
            Milliseconds(backoff), 0, retryKind.ToString()));

    public void Record(CraftingMarketEvidenceDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        EnqueueMarket(new(
            DateTimeOffset.UtcNow, "crafting-evidence", "crafting-market", null, null,
            diagnostic.RequestedItemIdCount, diagnostic.ListingResponseItemCount,
            diagnostic.MissingListingItemIdCount, null, null, null,
            diagnostic.ListingsErrorCategory, diagnostic.ListingsPartialResponse,
            null, null, diagnostic.ListingsElapsedMilliseconds, diagnostic.Outcome));
        EnqueueMarket(new(
            DateTimeOffset.UtcNow, "crafting-metadata", "items", null, null,
            diagnostic.RequestedItemIdCount, diagnostic.MetadataResponseItemCount,
            null, null, null, null, diagnostic.MetadataErrorCategory,
            diagnostic.MetadataPartialResponse, null, null,
            diagnostic.MetadataElapsedMilliseconds, diagnostic.Outcome));
    }

    public IReadOnlyList<SafeMarketGatewayDiagnostic> SnapshotMarketGatewayDiagnostics() => marketEvents.ToArray();

    private void EnqueueMarket(SafeMarketGatewayDiagnostic diagnostic)
    {
        marketEvents.Enqueue(diagnostic);
        while (marketEvents.Count > Capacity)
        {
            marketEvents.TryDequeue(out _);
        }
    }

    private static long Milliseconds(TimeSpan elapsed) => Math.Max(0, (long)elapsed.TotalMilliseconds);
}
