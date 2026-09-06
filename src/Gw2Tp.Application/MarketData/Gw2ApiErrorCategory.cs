namespace Gw2Tp.Application.MarketData;

/// <summary>
/// Stable, transport-independent categories for expected GW2 gateway failures.
/// </summary>
public enum Gw2ApiErrorCategory
{
    CredentialNotConfigured,
    CredentialUnavailable,
    InvalidRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    RateLimited,
    UpstreamUnavailable,
    TransportFailure,
    InvalidPayload,
    IncompleteData,
    UnexpectedResponse,
}
