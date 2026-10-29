using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace HippoCrm.Core;

public static class PasswordBuffer
{
    // The caller owns/disposes SecureString. Every plaintext copy made here is pinned and zeroed.
    public static byte[] Derive(SecureString secPassword, ReadOnlySpan<byte> spnSalt, int iIterations)
    {
        var arrHash = GC.AllocateUninitializedArray<byte>(32, pinned: true);
        byte[]? arrPassword = null;
        try
        {
            arrPassword = Encode(secPassword);
            Rfc2898DeriveBytes.Pbkdf2(arrPassword, spnSalt, arrHash, iIterations, HashAlgorithmName.SHA256);
            return arrHash;
        }
        catch { CryptographicOperations.ZeroMemory(arrHash); throw; }
        finally { if (arrPassword is not null) CryptographicOperations.ZeroMemory(arrPassword); }
    }

    public static bool Matches(SecureString secFirst, SecureString secSecond)
    {
        byte[]? arrFirst = null;
        byte[]? arrSecond = null;
        try
        {
            arrFirst = Encode(secFirst); arrSecond = Encode(secSecond);
            return CryptographicOperations.FixedTimeEquals(arrFirst, arrSecond);
        }
        finally
        {
            if (arrFirst is not null) CryptographicOperations.ZeroMemory(arrFirst);
            if (arrSecond is not null) CryptographicOperations.ZeroMemory(arrSecond);
        }
    }

    private static byte[] Encode(SecureString secPassword)
    {
        ArgumentNullException.ThrowIfNull(secPassword);
        // A caller may own a mutable SecureString. Use one immutable snapshot for length and marshaling.
        using var secSnapshot = secPassword.Copy();
        secSnapshot.MakeReadOnly();
        if (secSnapshot.Length > 256) throw new ArgumentException("Passwords must contain at most 256 characters.");
        var ptrPassword = IntPtr.Zero;
        var arrCharacters = GC.AllocateUninitializedArray<char>(secSnapshot.Length, pinned: true);
        byte[]? arrEncoded = null;
        try
        {
            ptrPassword = Marshal.SecureStringToGlobalAllocUnicode(secSnapshot);
            for (var iCharacter = 0; iCharacter < arrCharacters.Length; iCharacter++)
                arrCharacters[iCharacter] = (char)Marshal.ReadInt16(ptrPassword, iCharacter * sizeof(char));
            arrEncoded = GC.AllocateUninitializedArray<byte>(Encoding.UTF8.GetByteCount(arrCharacters), pinned: true);
            Encoding.UTF8.GetBytes(arrCharacters, arrEncoded);
            return arrEncoded;
        }
        catch { if (arrEncoded is not null) CryptographicOperations.ZeroMemory(arrEncoded); throw; }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(arrCharacters.AsSpan()));
            if (ptrPassword != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(ptrPassword);
        }
    }
}
