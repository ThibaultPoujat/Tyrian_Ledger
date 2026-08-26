using Gw2Tp.Application.Time;
using Gw2Tp.Testing;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class FrozenClockTests
{
    [Fact]
    public void UtcNow_returns_the_configured_instant_in_utc()
    {
        var expected = new DateTimeOffset(2026, 8, 26, 9, 30, 0, TimeSpan.Zero);
        IClock clock = new FrozenClock(expected.ToOffset(TimeSpan.FromHours(2)));

        Assert.Equal(expected, clock.UtcNow);
    }
}
