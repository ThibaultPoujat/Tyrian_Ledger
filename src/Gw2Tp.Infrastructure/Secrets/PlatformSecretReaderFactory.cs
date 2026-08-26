namespace Gw2Tp.Infrastructure.Secrets;

internal enum RuntimePlatform
{
    MacOs,
    Windows,
    Linux,
    Unsupported,
}

internal static class PlatformSecretReaderFactory
{
    internal static IPlatformSecretReader CreateForCurrentOperatingSystem()
    {
        if (OperatingSystem.IsMacOS())
        {
            return Create(RuntimePlatform.MacOs);
        }

        if (OperatingSystem.IsWindows())
        {
            return Create(RuntimePlatform.Windows);
        }

        if (OperatingSystem.IsLinux())
        {
            return Create(RuntimePlatform.Linux);
        }

        return Create(RuntimePlatform.Unsupported);
    }

    internal static IPlatformSecretReader Create(RuntimePlatform platform) => platform switch
    {
        RuntimePlatform.MacOs => new MacOsKeychainCredentialReader(),
        RuntimePlatform.Windows => new WindowsCredentialManagerCredentialReader(),
        RuntimePlatform.Linux => new LinuxSecretServiceCredentialReader(),
        _ => new UnsupportedPlatformSecretReader(),
    };
}
