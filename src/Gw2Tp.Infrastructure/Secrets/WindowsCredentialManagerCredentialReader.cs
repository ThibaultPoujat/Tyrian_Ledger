using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Gw2Tp.Infrastructure.Secrets;

/// <summary>
/// Reads a generic credential from the current Windows user's Credential
/// Manager store.
/// </summary>
internal sealed class WindowsCredentialManagerCredentialReader : IPlatformSecretReader
{
    private const uint GenericCredentialType = 1;

    public string StoreName => "Windows Credential Manager";

    public ValueTask<string?> ReadGw2ApiCredentialAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows() ||
            !CredRead(SecretStoreMetadata.ServiceName, GenericCredentialType, 0, out var credentialPointer))
        {
            return ValueTask.FromResult<string?>(null);
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero ||
                credential.CredentialBlobSize == 0 ||
                credential.CredentialBlobSize > int.MaxValue)
            {
                return ValueTask.FromResult<string?>(null);
            }

            var bytes = new byte[(int)credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return ValueTask.FromResult<string?>(Encoding.Unicode.GetString(bytes).TrimEnd('\0'));
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string targetName,
        uint type,
        uint flags,
        out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr credential);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}
