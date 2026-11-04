using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace HippoCrm.Core;

// Keeps the SQLCipher key in the current macOS user's Keychain.
[SupportedOSPlatform("macos")]
public sealed class MacKeychainKeyStore : IDatabaseKeyStore
{
    private const int iItemNotFound = -25300;
    private const int iDuplicateItem = -25299;
    private const uint uiUtf8Encoding = 0x08000100;
    private const string strService = "com.bigeaglegroup.hippocrm";
    private const string strSecurityFramework = "/System/Library/Frameworks/Security.framework/Security";
    private const string strCoreFoundationFramework = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private static readonly IntPtr ptrSecurityFramework = NativeLibrary.Load(strSecurityFramework);
    private static readonly IntPtr ptrCoreFoundationFramework = NativeLibrary.Load(strCoreFoundationFramework);

    public void EnsureKey(string strDatabasePath, bool bAllowCreate)
    {
        var iStatus = ReadStoredKey(strDatabasePath, out var arrExistingKey);
        if (iStatus == 0)
        {
            SensitiveMemory.Clear(arrExistingKey);
            return;
        }
        if (iStatus != iItemNotFound || !bAllowCreate)
            throw new CryptographicException("The workspace key is unavailable. Contact your administrator.");

        var arrKey = SensitiveMemory.AllocateBytes(32);
        var ptrData = IntPtr.Zero;
        var ptrQuery = IntPtr.Zero;
        try
        {
            RandomNumberGenerator.Fill(arrKey);
            using var objNativeKey = new NativeSecretBuffer(arrKey);
            try
            {
                // CFData must release before its external backing buffer. Apple
                // may make internal copies; those are not adapter-owned memory.
                ptrData = CFDataCreateWithBytesNoCopy(IntPtr.Zero, objNativeKey.DangerousGetHandle(), arrKey.Length,
                    GetConstant(ptrCoreFoundationFramework, "kCFAllocatorNull"));
                if (ptrData == IntPtr.Zero)
                    throw new CryptographicException("The workspace key could not be prepared for the macOS Keychain.");
                ptrQuery = CreateQuery(strDatabasePath, ptrData, false);
                iStatus = SecItemAdd(ptrQuery, out var ptrResult);
                if (ptrResult != IntPtr.Zero) CFRelease(ptrResult);
                if (iStatus != 0 && iStatus != iDuplicateItem)
                    throw new CryptographicException("The workspace key could not be protected in the macOS Keychain.");
            }
            finally
            {
                try { if (ptrQuery != IntPtr.Zero) CFRelease(ptrQuery); }
                finally { ptrQuery = IntPtr.Zero; if (ptrData != IntPtr.Zero) CFRelease(ptrData); ptrData = IntPtr.Zero; }
                GC.KeepAlive(objNativeKey);
            }
        }
        finally
        {
            SensitiveMemory.Clear(arrKey);
        }

        // Another process may have created the item between the lookup and add.
        var iConfirmedStatus = ReadStoredKey(strDatabasePath, out var arrConfirmedKey);
        if (iConfirmedStatus != 0)
            throw new CryptographicException("The workspace key could not be read from the macOS Keychain.");
        SensitiveMemory.Clear(arrConfirmedKey);
    }

    public byte[] ReadKey(string strDatabasePath)
    {
        var iStatus = ReadStoredKey(strDatabasePath, out var arrKey);
        if (iStatus != 0 || arrKey is null)
            throw new CryptographicException("The workspace key is unavailable. Contact your administrator.");
        return arrKey;
    }

    // Used by the local native integration check to remove only its unique test item.
    internal void DeleteKeyForTesting(string strDatabasePath)
    {
        var ptrQuery = CreateQuery(strDatabasePath, IntPtr.Zero, false);
        try
        {
            SetValue(ptrQuery, ptrSecurityFramework, "kSecMatchLimit", GetConstant(ptrSecurityFramework, "kSecMatchLimitOne"));
            var iStatus = SecItemDelete(ptrQuery);
            if (iStatus != 0 && iStatus != iItemNotFound)
                throw new CryptographicException("The temporary macOS Keychain test item could not be removed (status " + iStatus + ").");
        }
        finally { CFRelease(ptrQuery); }
    }

    private static int ReadStoredKey(string strDatabasePath, out byte[]? arrKey)
    {
        arrKey = null;
        byte[]? arrResult = null;
        var bTransferred = false;
        try
        {
            var ptrQuery = CreateQuery(strDatabasePath, IntPtr.Zero, true);
            try
            {
                var iStatus = SecItemCopyMatching(ptrQuery, out var ptrData);
                try
                {
                    if (iStatus != 0) return iStatus;
                    if (ptrData == IntPtr.Zero || CFDataGetLength(ptrData) != 32)
                        throw new CryptographicException("The workspace key is invalid.");
                    arrResult = NativeSecretBuffer.CopyToManaged(CFDataGetBytePtr(ptrData));
                }
                // OS-owned immutable CFData must not be overwritten through its
                // read-only pointer. Clear every adapter-owned copy instead.
                finally { if (ptrData != IntPtr.Zero) CFRelease(ptrData); }
            }
            finally { CFRelease(ptrQuery); }
            // Transfer only after native cleanup succeeds, including on errors.
            arrKey = arrResult; bTransferred = true; return 0;
        }
        finally { if (!bTransferred) SensitiveMemory.Clear(arrResult); }
    }

    private static IntPtr CreateQuery(string strDatabasePath, IntPtr ptrData, bool bReturnData)
    {
        var ptrQuery = CFDictionaryCreateMutable(IntPtr.Zero, 0,
            NativeLibrary.GetExport(ptrCoreFoundationFramework, "kCFTypeDictionaryKeyCallBacks"),
            NativeLibrary.GetExport(ptrCoreFoundationFramework, "kCFTypeDictionaryValueCallBacks"));
        if (ptrQuery == IntPtr.Zero)
            throw new CryptographicException("A macOS Keychain query could not be prepared.");
        var ptrService = IntPtr.Zero;
        var ptrAccount = IntPtr.Zero;
        try
        {
            ptrService = CreateString(strService);
            ptrAccount = CreateString(Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(Path.GetFullPath(strDatabasePath)))));
            if (ptrService == IntPtr.Zero || ptrAccount == IntPtr.Zero)
                throw new CryptographicException("A macOS Keychain query could not be prepared.");
            SetValue(ptrQuery, ptrSecurityFramework, "kSecClass", GetConstant(ptrSecurityFramework, "kSecClassGenericPassword"));
            SetValue(ptrQuery, ptrSecurityFramework, "kSecAttrService", ptrService);
            SetValue(ptrQuery, ptrSecurityFramework, "kSecAttrAccount", ptrAccount);
            if (ptrData != IntPtr.Zero)
            {
                SetValue(ptrQuery, ptrSecurityFramework, "kSecValueData", ptrData);
            }
            if (bReturnData)
            {
                SetValue(ptrQuery, ptrSecurityFramework, "kSecReturnData", GetConstant(ptrCoreFoundationFramework, "kCFBooleanTrue"));
                SetValue(ptrQuery, ptrSecurityFramework, "kSecMatchLimit", GetConstant(ptrSecurityFramework, "kSecMatchLimitOne"));
            }
            return ptrQuery;
        }
        catch
        {
            CFRelease(ptrQuery);
            throw;
        }
        finally
        {
            if (ptrService != IntPtr.Zero) CFRelease(ptrService);
            if (ptrAccount != IntPtr.Zero) CFRelease(ptrAccount);
        }
    }

    private static void SetValue(IntPtr ptrDictionary, IntPtr ptrFramework, string strKeyName, IntPtr ptrValue)
    {
        var ptrKey = GetConstant(ptrFramework, strKeyName);
        CFDictionarySetValue(ptrDictionary, ptrKey, ptrValue);
    }

    private static IntPtr CreateString(string strValue)
    {
        var arrValue = Encoding.UTF8.GetBytes(strValue + "\0");
        var ptrString = CFStringCreateWithCString(IntPtr.Zero, arrValue, uiUtf8Encoding);
        if (ptrString == IntPtr.Zero)
            throw new CryptographicException("A macOS Keychain query could not be prepared.");
        return ptrString;
    }

    private static IntPtr GetConstant(IntPtr ptrFramework, string strName) =>
        Marshal.ReadIntPtr(NativeLibrary.GetExport(ptrFramework, strName));

    [DllImport(strSecurityFramework, EntryPoint = "SecItemAdd")]
    private static extern int SecItemAdd(IntPtr ptrAttributes, out IntPtr ptrResult);

    [DllImport(strSecurityFramework, EntryPoint = "SecItemCopyMatching")]
    private static extern int SecItemCopyMatching(IntPtr ptrQuery, out IntPtr ptrResult);

    [DllImport(strSecurityFramework, EntryPoint = "SecItemDelete")]
    private static extern int SecItemDelete(IntPtr ptrQuery);

    [DllImport(strCoreFoundationFramework, EntryPoint = "CFDictionaryCreateMutable")]
    private static extern IntPtr CFDictionaryCreateMutable(IntPtr ptrAllocator, nint lngCapacity,
        IntPtr ptrKeyCallbacks, IntPtr ptrValueCallbacks);

    [DllImport(strCoreFoundationFramework, EntryPoint = "CFDictionarySetValue")]
    private static extern void CFDictionarySetValue(IntPtr ptrDictionary, IntPtr ptrKey, IntPtr ptrValue);

    [DllImport(strCoreFoundationFramework, EntryPoint = "CFStringCreateWithCString")]
    private static extern IntPtr CFStringCreateWithCString(IntPtr ptrAllocator, byte[] arrValue, uint uiEncoding);

    [DllImport(strCoreFoundationFramework, EntryPoint = "CFDataCreateWithBytesNoCopy")]
    private static extern IntPtr CFDataCreateWithBytesNoCopy(IntPtr ptrAllocator, IntPtr ptrValue, nint lngLength, IntPtr ptrDeallocator);

    [DllImport(strCoreFoundationFramework, EntryPoint = "CFDataGetLength")]
    private static extern nint CFDataGetLength(IntPtr ptrData);

    [DllImport(strCoreFoundationFramework, EntryPoint = "CFDataGetBytePtr")]
    private static extern IntPtr CFDataGetBytePtr(IntPtr ptrData);

    [DllImport(strCoreFoundationFramework, EntryPoint = "CFRelease")]
    private static extern void CFRelease(IntPtr ptrObject);
}
