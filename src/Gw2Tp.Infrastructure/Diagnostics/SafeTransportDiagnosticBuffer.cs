using System.Collections.Concurrent;

namespace Gw2Tp.Infrastructure.Diagnostics;

public sealed record SafeTransportDiagnostic(
    DateTimeOffset TimestampUtc,
    string Operation,
    string Code,
    string ExceptionType,
    string? InnerExceptionType,
    long ElapsedMilliseconds);

public sealed class SafeTransportDiagnosticBuffer
{
    private const int Capacity = 100;
    private readonly ConcurrentQueue<SafeTransportDiagnostic> events = new();

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
}
