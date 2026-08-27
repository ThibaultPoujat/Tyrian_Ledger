namespace Gw2Tp.Application.MarketData;

/// <summary>
/// Stable, transport-independent categories for expected GW2 API failures.
/// </summary>
public enum Gw2ApiErrorCategory
{
    InvalidRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    RateLimited,
    UpstreamUnavailable,
    TransportFailure,
    InvalidPayload,
    UnexpectedResponse,
}
