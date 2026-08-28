using Microsoft.Extensions.Options;

namespace Gw2Tp.Infrastructure.AccountSnapshots;

/// <summary>
/// Local freshness policy for minimized authenticated account snapshots.
/// This is application policy, not an assertion about upstream cache headers.
/// </summary>
internal sealed class Gw2AccountSnapshotCacheOptions
{
    public const string ConfigurationSectionName = "AccountSnapshotCache";

    public int TimeToLiveSeconds { get; set; } = 300;

    public bool TryValidate(out string validationError)
    {
        if (TimeToLiveSeconds <= 0)
        {
            validationError =
                "Gw2Api:AccountSnapshotCache:TimeToLiveSeconds must be greater than zero.";
            return false;
        }

        validationError = string.Empty;
        return true;
    }
}

internal sealed class Gw2AccountSnapshotCacheOptionsValidator
    : IValidateOptions<Gw2AccountSnapshotCacheOptions>
{
    public ValidateOptionsResult Validate(string? name, Gw2AccountSnapshotCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.TryValidate(out var validationError)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(validationError);
    }
}
