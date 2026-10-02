namespace Gw2Tp.Infrastructure.Gw2Api;

internal static class Gw2RetryAfter
{
    internal static TimeSpan? FromResponse(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero) return delta;
        if (response.Headers.RetryAfter?.Date is { } date && date - DateTimeOffset.UtcNow is var remaining && remaining > TimeSpan.Zero)
            return remaining;
        return null;
    }
}
