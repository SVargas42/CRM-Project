using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace HippoCrm.Core;

// Credential Manager protects keys under the current OS user; no plaintext key files or fallback.
public sealed class WindowsCredentialKeyStore : IDatabaseKeyStore
{
    private const uint uiGenericCredential = 1;
    private const uint uiLocalMachinePersistence = 2;
    private const int iNotFound = 1168;

    public void EnsureKey(string strDatabasePath, bool bAllowCreate)
    {
        EnsureWindows();
        var strTarget = Target(strDatabasePath);
        if (CredRead(strTarget, uiGenericCredential, 0, out var ptrCredential))
        {
            ClearAndFree(ptrCredential);
            return;
        }
        if (Marshal.GetLastWin32Error() != iNotFound || !bAllowCreate)
            throw new CryptographicException("The workspace key is unavailable. Contact your administrator.");
        var arrKey = SensitiveMemory.AllocateBytes(32);
        var gchKey = GCHandle.Alloc(arrKey, GCHandleType.Pinned);
        try
        {
            RandomNumberGenerator.Fill(arrKey);
            var objCredential = new NativeCredential
            {
                uiType = uiGenericCredential,
                strTargetName = strTarget,
                uiCredentialBlobSize = (uint)arrKey.Length,
                ptrCredentialBlob = gchKey.AddrOfPinnedObject(),
                uiPersist = uiLocalMachinePersistence,
                strUserName = "HippoCrm"
            };
            if (!CredWrite(ref objCredential, 0))
                throw new CryptographicException("The workspace key could not be protected.");
        }
        finally { SensitiveMemory.Clear(arrKey); gchKey.Free(); }
    }

    public byte[] ReadKey(string strDatabasePath)
    {
        EnsureWindows();
        if (!CredRead(Target(strDatabasePath), uiGenericCredential, 0, out var ptrCredential))
            throw new CryptographicException("The workspace key is unavailable. Contact your administrator.");
        return TransferKey(() =>
        {
            var objCredential = Marshal.PtrToStructure<NativeCredential>(ptrCredential);
            if (objCredential.uiCredentialBlobSize != 32 || objCredential.ptrCredentialBlob == IntPtr.Zero)
                throw new CryptographicException("The workspace key is invalid.");
            return objCredential.ptrCredentialBlob;
        }, arrKey => Marshal.Copy(Marshal.PtrToStructure<NativeCredential>(ptrCredential).ptrCredentialBlob, arrKey, 0, arrKey.Length),
            () => ClearAndFree(ptrCredential));
    }

    // Complete native cleanup before transferring the managed owner. Kept separate
    // so copy/cleanup failures can be tested without real OS credentials.
    internal static byte[] TransferKey(Func<IntPtr> fnValidate, Action<byte[]> actCopy, Action actRelease)
    {
        byte[]? arrKey = null; var bTransferred = false;
        try
        {
            try { _ = fnValidate(); arrKey = SensitiveMemory.AllocateBytes(32); actCopy(arrKey); }
            finally { actRelease(); }
            bTransferred = true; return arrKey;
        }
        finally { if (!bTransferred) SensitiveMemory.Clear(arrKey); }
    }

    private static string Target(string strDatabasePath) => "BigEagleGroup/HippoCrm/" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(strDatabasePath).ToUpperInvariant())));

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("An approved OS key store is required on this platform.");
    }

    private static void ClearAndFree(IntPtr ptrCredential)
    {
        try
        {
            var objCredential = Marshal.PtrToStructure<NativeCredential>(ptrCredential);
            if (objCredential.ptrCredentialBlob != IntPtr.Zero)
                for (var iByte = 0; iByte < objCredential.uiCredentialBlobSize; iByte++)
                    Marshal.WriteByte(objCredential.ptrCredentialBlob, iByte, 0);
        }
        finally { CredFree(ptrCredential); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint uiFlags;
        public uint uiType;
        public string? strTargetName;
        public string? strComment;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWritten;
        public uint uiCredentialBlobSize;
        public IntPtr ptrCredentialBlob;
        public uint uiPersist;
        public uint uiAttributeCount;
        public IntPtr ptrAttributes;
        public string? strTargetAlias;
        public string? strUserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string strTarget, uint uiType, uint uiFlags, out IntPtr ptrCredential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential objCredential, uint uiFlags);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr ptrCredential);
}
