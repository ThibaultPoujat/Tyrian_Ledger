using Microsoft.Extensions.Options;

namespace Gw2Tp.Infrastructure.Gw2Api;

/// <summary>
/// Local freshness policy for public market responses. It is configurable
/// application policy, not a claim about GW2 API cache behavior.
/// </summary>
internal sealed class Gw2MarketCacheOptions
{
    public const string ConfigurationSectionName = "MarketCache";

    public int TimeToLiveSeconds { get; set; } = 120;

    public bool TryValidate(out string validationError)
    {
        if (TimeToLiveSeconds <= 0)
        {
            validationError =
                "Gw2Api:MarketCache:TimeToLiveSeconds must be greater than zero.";
            return false;
        }

        validationError = string.Empty;
        return true;
    }
}

internal sealed class Gw2MarketCacheOptionsValidator : IValidateOptions<Gw2MarketCacheOptions>
{
    public ValidateOptionsResult Validate(string? name, Gw2MarketCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.TryValidate(out var validationError)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(validationError);
    }
}
