using Gw2Tp.Application.Time;

namespace Gw2Tp.Testing;

public sealed class FrozenClock : IClock
{
    public FrozenClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow.ToUniversalTime();
    }

    public DateTimeOffset UtcNow { get; }
}
