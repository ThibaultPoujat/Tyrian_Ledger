namespace Gw2Tp.Application.MarketData;

/// <summary>Trusted server-side purpose, independent of request identity and browser inputs.</summary>
public enum Gw2RequestPurpose
{
    BackgroundResearch,
    AccountRefresh,
    ActionValidation,
}

/// <summary>
/// Purpose flows through asynchronous application/typed-gateway calls. Unclassified
/// work is ordinary refresh; research boundaries explicitly lower broad discovery.
/// This carries no account facts, credentials or resource authority.
/// </summary>
public static class Gw2RequestPurposeScope
{
    private static readonly AsyncLocal<Gw2RequestPurpose?> Ambient = new();
    public static Gw2RequestPurpose Current => Ambient.Value ?? Gw2RequestPurpose.AccountRefresh;

    public static IDisposable Begin(Gw2RequestPurpose purpose)
    {
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        var previous = Ambient.Value;
        Ambient.Value = purpose;
        return new Scope(previous);
    }

    private sealed class Scope(Gw2RequestPurpose? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
