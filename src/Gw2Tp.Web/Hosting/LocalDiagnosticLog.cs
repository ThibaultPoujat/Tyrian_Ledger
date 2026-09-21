using System.Collections.Concurrent;
using System.Text;

namespace Gw2Tp.Web.Hosting;

internal sealed record DiagnosticEvent(
    DateTimeOffset TimestampUtc,
    string Severity,
    string Subsystem,
    string Operation,
    string Code,
    string Message,
    string? ExceptionType,
    string CorrelationId);

internal sealed class LocalDiagnosticLog
{
    private const int Capacity = 500;
    private readonly ConcurrentQueue<DiagnosticEvent> events = new();

    public void Record(
        string severity,
        string subsystem,
        string operation,
        string code,
        string message,
        Exception? exception = null,
        string? correlationId = null)
    {
        events.Enqueue(new DiagnosticEvent(
            DateTimeOffset.UtcNow,
            severity,
            subsystem,
            operation,
            code,
            message,
            exception?.GetType().Name,
            correlationId ?? Guid.NewGuid().ToString("N")));
        while (events.Count > Capacity)
        {
            events.TryDequeue(out _);
        }
    }

    public string ExportText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Tyrian Ledger - diagnostic local");
        builder.AppendLine("Les secrets, clés API, en-têtes d'autorisation et contenus de réponses ne sont pas enregistrés.");
        builder.AppendLine();
        foreach (var item in events.ToArray())
        {
            builder.Append(item.TimestampUtc.ToString("O"))
                .Append(" [").Append(item.Severity).Append("] ")
                .Append(item.Subsystem).Append('/').Append(item.Operation)
                .Append(" code=").Append(item.Code)
                .Append(" correlation=").Append(item.CorrelationId);
            if (item.ExceptionType is not null)
            {
                builder.Append(" exception=").Append(item.ExceptionType);
            }
            builder.Append(" - ").AppendLine(item.Message);
        }
        return builder.ToString();
    }
}
