using System.Runtime.InteropServices;
using System.Text;

namespace Blitztext.App.Platform;

/// <summary>
/// Stores the OpenAI API key in the Windows Credential Manager. This is the Windows
/// counterpart to the macOS KeychainService (Security framework): the secret is held by
/// the OS credential vault, scoped to the current user, never in plain text on disk.
/// </summary>
public static class CredentialStore
{
    private const string TargetName = "app.blitztext.preview.credentials/openAIAPIKey";

    public static bool IsConfigured => !string.IsNullOrEmpty(LoadApiKey());

    public static string? LoadApiKey()
    {
        if (!CredRead(TargetName, CRED_TYPE_GENERIC, 0, out IntPtr credPtr))
        {
            return null;
        }

        try
        {
            CREDENTIAL cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
            {
                return null;
            }

            byte[] bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, (int)cred.CredentialBlobSize);
            string value = Encoding.Unicode.GetString(bytes);
            return string.IsNullOrEmpty(value) ? null : value;
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    public static void SaveApiKey(string value)
    {
        byte[] blob = Encoding.Unicode.GetBytes(value);
        IntPtr blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);

            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = TargetName,
                CredentialBlob = blobPtr,
                CredentialBlobSize = (uint)blob.Length,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = "openAIAPIKey",
            };

            if (!CredWrite(ref cred, 0))
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"Zugangsdaten konnten nicht im Windows Credential Manager gespeichert werden. Fehler: {err}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    public static void DeleteApiKey() => CredDelete(TargetName, CRED_TYPE_GENERIC, 0);

    // ----- advapi32 interop -----

    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredReadW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredWriteW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredDeleteW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
