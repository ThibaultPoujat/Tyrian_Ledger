namespace Gw2Tp.Application.MarketData;

/// <summary>
/// Represents either a successful GW2 API response or an expected, stable
/// failure category. The raw HTTP response is deliberately not exposed.
/// </summary>
public sealed class Gw2ApiResult<T>
{
    private Gw2ApiResult(
        T? value,
        Gw2ApiErrorCategory? errorCategory,
        bool isPartialData,
        DataFreshness? freshness)
    {
        Value = value;
        ErrorCategory = errorCategory;
        IsPartialData = isPartialData;
        Freshness = freshness;
    }

    public T? Value { get; }

    public Gw2ApiErrorCategory? ErrorCategory { get; }

    public bool IsPartialData { get; }

    /// <summary>
    /// Capture and expiry instants for successful market data when the gateway
    /// served it from its freshness-aware cache.
    /// </summary>
    public DataFreshness? Freshness { get; }

    public bool IsSuccess => ErrorCategory is null;

    public static Gw2ApiResult<T> Success(
        T value,
        bool isPartialData = false,
        DataFreshness? freshness = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Gw2ApiResult<T>(value, null, isPartialData, freshness);
    }

    public static Gw2ApiResult<T> Failure(Gw2ApiErrorCategory errorCategory) =>
        new(default, errorCategory, isPartialData: false, freshness: null);
}
