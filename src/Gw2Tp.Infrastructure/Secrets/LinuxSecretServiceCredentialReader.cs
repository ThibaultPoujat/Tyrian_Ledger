using System.Diagnostics;

namespace Gw2Tp.Infrastructure.Secrets;

/// <summary>
/// Reads the credential from an unlocked implementation of the freedesktop.org
/// Secret Service API, such as GNOME Keyring or KWallet.
/// </summary>
internal sealed class LinuxSecretServiceCredentialReader : IPlatformSecretReader
{
    private const string SecretToolPath = "secret-tool";

    public string StoreName => "Linux Secret Service";

    public async ValueTask<string?> ReadGw2ApiCredentialAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo(SecretToolPath)
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("lookup");
            startInfo.ArgumentList.Add(SecretStoreMetadata.LinuxApplicationAttribute);
            startInfo.ArgumentList.Add(SecretStoreMetadata.LinuxApplicationValue);
            startInfo.ArgumentList.Add(SecretStoreMetadata.LinuxCredentialAttribute);
            startInfo.ArgumentList.Add(SecretStoreMetadata.LinuxCredentialValue);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);

            var standardOutput = await standardOutputTask;
            _ = await standardErrorTask;

            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(standardOutput)
                ? standardOutput.TrimEnd('\r', '\n')
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
