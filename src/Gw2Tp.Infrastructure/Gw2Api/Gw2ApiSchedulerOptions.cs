using Microsoft.Extensions.Options;

namespace Gw2Tp.Infrastructure.Gw2Api;

/// <summary>
/// Adjustable local settings for the GW2 request scheduler. These values are
/// application policy rather than assertions about the upstream API contract.
/// </summary>
internal sealed class Gw2ApiSchedulerOptions
{
    public const string ConfigurationSectionName = "Gw2Api";

    public Gw2RateLimitOptions RateLimit { get; set; } = new();

    public Gw2RetryOptions Retry { get; set; } = new();

    public int RequestTimeoutMs { get; set; } = 10_000;

    public bool TryValidate(out string validationError)
    {
        if (RateLimit.BurstSize <= 0)
        {
            validationError = "Gw2Api:RateLimit:BurstSize must be greater than zero.";
            return false;
        }

        if (RateLimit.RefillTokensPerSecond <= 0)
        {
            validationError = "Gw2Api:RateLimit:RefillTokensPerSecond must be greater than zero.";
            return false;
        }

        if (RateLimit.MaxConcurrentRequests <= 0)
        {
            validationError = "Gw2Api:RateLimit:MaxConcurrentRequests must be greater than zero.";
            return false;
        }

        if (RateLimit.MaxQueuedRequests < 0)
        {
            validationError = "Gw2Api:RateLimit:MaxQueuedRequests cannot be negative.";
            return false;
        }

        if (!Retry.On429.IsValid("Gw2Api:Retry:On429", out validationError) ||
            !Retry.On5xx.IsValid("Gw2Api:Retry:On5xx", out validationError))
        {
            return false;
        }

        if (RequestTimeoutMs <= 0)
        {
            validationError = "Gw2Api:RequestTimeoutMs must be greater than zero.";
            return false;
        }

        validationError = string.Empty;
        return true;
    }
}

internal sealed class Gw2RateLimitOptions
{
    public int BurstSize { get; set; } = 300;

    public int RefillTokensPerSecond { get; set; } = 5;

    public int MaxConcurrentRequests { get; set; } = 5;

    public int MaxQueuedRequests { get; set; } = 100;
}

internal sealed class Gw2ApiSchedulerOptionsValidator : IValidateOptions<Gw2ApiSchedulerOptions>
{
    public ValidateOptionsResult Validate(string? name, Gw2ApiSchedulerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.TryValidate(out var validationError)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(validationError);
    }
}

internal sealed class Gw2RetryOptions
{
    public Gw2BackoffOptions On429 { get; set; } = new()
    {
        InitialBackoffMs = 1_000,
        MaxBackoffMs = 30_000,
        MaxAttempts = 5,
    };

    public bool HonorServerRetryAfter { get; set; } = true;

    public Gw2BackoffOptions On5xx { get; set; } = new()
    {
        InitialBackoffMs = 1_000,
        MaxBackoffMs = 30_000,
        MaxAttempts = 3,
    };
}

internal sealed class Gw2BackoffOptions
{
    public int InitialBackoffMs { get; set; }

    public int MaxBackoffMs { get; set; }

    /// <summary>
    /// Maximum total outbound attempts for one logical request, including the
    /// initial request.
    /// </summary>
    public int MaxAttempts { get; set; }

    public bool IsValid(string sectionName, out string validationError)
    {
        if (InitialBackoffMs <= 0)
        {
            validationError = $"{sectionName}:InitialBackoffMs must be greater than zero.";
            return false;
        }

        if (MaxBackoffMs < InitialBackoffMs)
        {
            validationError = $"{sectionName}:MaxBackoffMs must be at least InitialBackoffMs.";
            return false;
        }

        if (MaxAttempts <= 0)
        {
            validationError = $"{sectionName}:MaxAttempts must be greater than zero.";
            return false;
        }

        validationError = string.Empty;
        return true;
    }
}
