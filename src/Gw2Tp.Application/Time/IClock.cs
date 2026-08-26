namespace Gw2Tp.Application.Time;

/// <summary>
/// Provides the current UTC time to application use cases.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
