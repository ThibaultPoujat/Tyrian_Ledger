using System.Diagnostics;

namespace Gw2Tp.Infrastructure.Secrets;

internal interface IPlatformSecretReader
{
    string StoreName { get; }

    ValueTask<string?> ReadGw2ApiCredentialAsync(CancellationToken cancellationToken);
}

internal sealed class MacOsKeychainCredentialReader : IPlatformSecretReader
{
    private const string SecurityToolPath = "/usr/bin/security";

    public string StoreName => "macOS Keychain";

    public async ValueTask<string?> ReadGw2ApiCredentialAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo(SecurityToolPath)
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("find-generic-password");
            startInfo.ArgumentList.Add("-s");
            startInfo.ArgumentList.Add(SecretStoreMetadata.ServiceName);
            startInfo.ArgumentList.Add("-w");

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
