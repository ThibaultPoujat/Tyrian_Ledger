namespace Gw2Tp.Domain.MarketData;

/// <summary>
/// UTC instants that let callers classify market-data age without depending on
/// transport or cache implementation details.
/// </summary>
public sealed record DataFreshness
{
    public DataFreshness(DateTimeOffset capturedAtUtc, DateTimeOffset expiresAtUtc)
    {
        CapturedAtUtc = capturedAtUtc.ToUniversalTime();
        ExpiresAtUtc = expiresAtUtc.ToUniversalTime();

        if (ExpiresAtUtc < CapturedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAtUtc),
                "The expiry instant cannot precede the capture instant.");
        }
    }

    public DateTimeOffset CapturedAtUtc { get; }

    public DateTimeOffset ExpiresAtUtc { get; }
}
