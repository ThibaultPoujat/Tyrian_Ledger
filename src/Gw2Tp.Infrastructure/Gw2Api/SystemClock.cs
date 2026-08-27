using Gw2Tp.Application.Time;

namespace Gw2Tp.Infrastructure.Gw2Api;

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
