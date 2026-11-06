using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
namespace HippoCrm.Core;

public static class PasswordBuffer
{
    public const int iIterations = 600_000;
    public const int iSaltLength = 32;
    public const int iHashLength = 32;
    public const int iMaximumPasswordLength = 256;

    public static void Derive(SecureString secPassword, ReadOnlySpan<byte> spnSalt, Span<byte> spnDestination)
    {
        ArgumentNullException.ThrowIfNull(secPassword);
        if (spnSalt.Length < 16 || spnDestination.Length != iHashLength)
            throw new ArgumentException("Invalid password derivation parameters.");
        var arrPassword = SensitiveMemory.AllocateBytes(Encoding.UTF8.GetMaxByteCount(iMaximumPasswordLength));
        try
        {
            var iLength = Encode(secPassword, arrPassword);
            Rfc2898DeriveBytes.Pbkdf2(arrPassword.AsSpan(0, iLength), spnSalt, spnDestination, iIterations, HashAlgorithmName.SHA256);
        }
        finally { SensitiveMemory.Clear(arrPassword); }
    }

    public static bool Matches(SecureString secFirst, SecureString secSecond)
    {
        var iCapacity = Encoding.UTF8.GetMaxByteCount(iMaximumPasswordLength);
        var arrFirst = SensitiveMemory.AllocateBytes(iCapacity);
        var arrSecond = SensitiveMemory.AllocateBytes(iCapacity);
        try
        {
            var iFirstLength = Encode(secFirst, arrFirst);
            var iSecondLength = Encode(secSecond, arrSecond);
            return CryptographicOperations.FixedTimeEquals(arrFirst.AsSpan(0, iFirstLength), arrSecond.AsSpan(0, iSecondLength));
        }
        finally { SensitiveMemory.Clear(arrFirst); SensitiveMemory.Clear(arrSecond); }
    }

    private static int Encode(SecureString secPassword, byte[] arrDestination)
    {
        ArgumentNullException.ThrowIfNull(secPassword);
        using var secSnapshot = secPassword.Copy();
        secSnapshot.MakeReadOnly();
        if (secSnapshot.Length > iMaximumPasswordLength) throw new ArgumentException("Passwords must contain at most 256 characters.");
        var arrCharacters = SensitiveMemory.AllocateCharacters(secSnapshot.Length);
        var ptrPassword = IntPtr.Zero;
        try
        {
            ptrPassword = Marshal.SecureStringToGlobalAllocUnicode(secSnapshot);
            for (var iCharacter = 0; iCharacter < arrCharacters.Length; iCharacter++)
                arrCharacters[iCharacter] = (char)Marshal.ReadInt16(ptrPassword, iCharacter * sizeof(char));
            return Encoding.UTF8.GetBytes(arrCharacters.AsSpan(), arrDestination.AsSpan());
        }
        finally
        {
            SensitiveMemory.Clear(arrCharacters);
            if (ptrPassword != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(ptrPassword);
        }
    }
}
