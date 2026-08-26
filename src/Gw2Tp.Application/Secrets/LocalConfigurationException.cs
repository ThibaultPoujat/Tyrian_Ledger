namespace Gw2Tp.Application.Secrets;

public sealed class LocalConfigurationException : InvalidOperationException
{
    public const string ErrorCode = "LocalConfigurationError";
    public const string StableMessage = "The required local GW2 API credential is unavailable.";

    public string Code => ErrorCode;

    public LocalConfigurationException()
        : base(StableMessage)
    {
    }
}
