using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HippoCrm.Core;

// Owns only adapter-allocated writable memory. Never wraps immutable OS CFData.
internal sealed class NativeSecretBuffer : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly int iLength;
    internal NativeSecretBuffer(byte[] arrSecret) : base(ownsHandle: true)
    {
        ArgumentNullException.ThrowIfNull(arrSecret);
        if (arrSecret.Length != 32) throw new ArgumentException("A 256-bit key is required.");
        iLength = arrSecret.Length;
        SetHandle(Marshal.AllocHGlobal(iLength));
        try { Marshal.Copy(arrSecret, 0, handle, iLength); }
        catch { Dispose(); throw; }
    }
    protected override bool ReleaseHandle()
    {
        ClearAndRelease(handle, iLength, Marshal.FreeHGlobal);
        return true;
    }

    internal static void ClearAndRelease(IntPtr ptrBuffer, int iLength, Action<IntPtr> actRelease)
    {
        try { for (var iByte = 0; iByte < iLength; iByte++) Marshal.WriteByte(ptrBuffer, iByte, 0); }
        finally { actRelease(ptrBuffer); }
    }

    internal static byte[] CopyToManaged(IntPtr ptrSource, Action<IntPtr, byte[]>? actCopy = null)
    {
        if (ptrSource == IntPtr.Zero) throw new System.Security.Cryptography.CryptographicException("The workspace key is invalid.");
        var arrResult = SensitiveMemory.AllocateBytes(32);
        var bTransferred = false;
        try
        {
            if (actCopy is null) Marshal.Copy(ptrSource, arrResult, 0, arrResult.Length);
            else actCopy(ptrSource, arrResult);
            bTransferred = true; return arrResult;
        }
        finally { if (!bTransferred) SensitiveMemory.Clear(arrResult); }
    }
}
