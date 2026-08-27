namespace Gw2Tp.Application.MarketData;

/// <summary>
/// UTC instants that let analytics and presentation layers calculate the age
/// and expiry state of a market-data response without seeing cache internals.
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
