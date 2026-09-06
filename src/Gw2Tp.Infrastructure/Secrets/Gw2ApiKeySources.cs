using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Gw2Tp.Infrastructure.Secrets;

/// <summary>
/// Internal-only raw-key boundary. Only the authenticated Infrastructure
/// transport may receive this value; Application and Web contracts cannot.
/// </summary>
internal interface IGw2ApiKeySource
{
    ValueTask<Gw2ApiKeyReadResult> ReadAsync(CancellationToken cancellationToken = default);
}

internal enum Gw2ApiKeyReadState
{
    NotConfigured,
    Available,
    Unavailable,
}

// This intentionally has no native diagnostic detail. It is the stable local
// operational error named by ADR-006 and is mapped to the browser-safe
// `unavailable` connection state by AccountConnectionStatusService.
internal enum Gw2ApiKeyReadFailure
{
    LocalConfigurationError,
}

internal sealed record Gw2ApiKeyReadResult(
    Gw2ApiKeyReadState State,
    string? Value,
    Gw2ApiKeyReadFailure? Failure = null)
{
    public static Gw2ApiKeyReadResult NotConfigured { get; } = new(Gw2ApiKeyReadState.NotConfigured, null);

    public static Gw2ApiKeyReadResult Unavailable { get; } = new(
        Gw2ApiKeyReadState.Unavailable,
        null,
        Gw2ApiKeyReadFailure.LocalConfigurationError);

    public static Gw2ApiKeyReadResult FromValue(string? value) =>
        EnvironmentGw2ApiKeySource.Normalize(value) is { } normalized
            ? new(Gw2ApiKeyReadState.Available, normalized)
            : NotConfigured;
}

internal sealed class EnvironmentGw2ApiKeySource : IGw2ApiKeySource
{
    internal const string EnvironmentVariableName = "TYRIAN_LEDGER_GW2_API_KEY";
    private readonly Func<string, string?> _readEnvironmentVariable;

    public EnvironmentGw2ApiKeySource()
        : this(Environment.GetEnvironmentVariable)
    {
    }

    internal EnvironmentGw2ApiKeySource(Func<string, string?> readEnvironmentVariable)
    {
        _readEnvironmentVariable = readEnvironmentVariable ?? throw new ArgumentNullException(nameof(readEnvironmentVariable));
    }

    public ValueTask<Gw2ApiKeyReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Gw2ApiKeyReadResult.FromValue(
            _readEnvironmentVariable(EnvironmentVariableName)));
    }

    internal static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class DevelopmentGw2ApiKeySource : IGw2ApiKeySource
{
    private readonly IGw2ApiKeySource _environmentSource;
    private readonly IGw2ApiKeySource _operatingSystemSource;

    public DevelopmentGw2ApiKeySource(
        IGw2ApiKeySource environmentSource,
        IGw2ApiKeySource operatingSystemSource)
    {
        _environmentSource = environmentSource ?? throw new ArgumentNullException(nameof(environmentSource));
        _operatingSystemSource = operatingSystemSource ?? throw new ArgumentNullException(nameof(operatingSystemSource));
    }

    public async ValueTask<Gw2ApiKeyReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        var environmentResult = await _environmentSource.ReadAsync(cancellationToken).ConfigureAwait(false);
        return environmentResult.State == Gw2ApiKeyReadState.Available
            ? environmentResult
            : await _operatingSystemSource.ReadAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class OperatingSystemGw2ApiKeySource : IGw2ApiKeySource
{
    internal const string ServiceName = "com.tyrianledger.gw2-api-key";
    internal const string WindowsTargetName = "TyrianLedger.Gw2ApiKey";

    public async ValueTask<Gw2ApiKeyReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        string? value;
        switch (SelectStore(
            OperatingSystem.IsMacOS(),
            OperatingSystem.IsWindows(),
            OperatingSystem.IsLinux()))
        {
            case OperatingSystemSecretStore.MacOsKeychain:
                value = await ReadCommandOutputAsync(
                    "/usr/bin/security",
                    ["find-generic-password", "-s", ServiceName, "-w"],
                    cancellationToken).ConfigureAwait(false);
                break;
            case OperatingSystemSecretStore.WindowsCredentialManager:
                value = ReadWindowsCredential();
                break;
            case OperatingSystemSecretStore.LinuxSecretService:
                value = await ReadCommandOutputAsync(
                    "secret-tool",
                    ["lookup", "service", ServiceName],
                    cancellationToken).ConfigureAwait(false);
                break;
            default:
                return Gw2ApiKeyReadResult.Unavailable;
        }

        // Native stores intentionally conceal absent-item, locked-vault, and
        // access-denied diagnostics. They must therefore never be represented
        // as the definite "not configured" state returned by the explicit
        // Development/Testing source.
        return EnvironmentGw2ApiKeySource.Normalize(value) is { } normalized
            ? new Gw2ApiKeyReadResult(Gw2ApiKeyReadState.Available, normalized)
            : Gw2ApiKeyReadResult.Unavailable;
    }

    internal static OperatingSystemSecretStore SelectStore(
        bool isMacOs,
        bool isWindows,
        bool isLinux) =>
        isMacOs ? OperatingSystemSecretStore.MacOsKeychain
        : isWindows ? OperatingSystemSecretStore.WindowsCredentialManager
        : isLinux ? OperatingSystemSecretStore.LinuxSecretService
        : OperatingSystemSecretStore.Unsupported;

    private static async ValueTask<string?> ReadCommandOutputAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            _ = await errorTask.ConfigureAwait(false);

            return process.ExitCode == 0 ? EnvironmentGw2ApiKeySource.Normalize(output) : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Secret-service diagnostics can contain private context. Absence is
            // intentionally indistinguishable from a secret-store failure here.
            return null;
        }
    }

    private static string? ReadWindowsCredential()
    {
        IntPtr credentialPointer = IntPtr.Zero;
        try
        {
            if (!CredRead(WindowsTargetName, CredentialTypeGeneric, 0, out credentialPointer))
            {
                return null;
            }

            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }

            var value = Marshal.PtrToStringUni(
                credential.CredentialBlob,
                checked((int)credential.CredentialBlobSize / sizeof(char)));
            return EnvironmentGw2ApiKeySource.Normalize(value);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (credentialPointer != IntPtr.Zero)
            {
                CredFree(credentialPointer);
            }
        }
    }

    private const uint CredentialTypeGeneric = 1;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string targetName,
        uint type,
        uint flags,
        out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}

internal enum OperatingSystemSecretStore
{
    MacOsKeychain,
    WindowsCredentialManager,
    LinuxSecretService,
    Unsupported,
}
