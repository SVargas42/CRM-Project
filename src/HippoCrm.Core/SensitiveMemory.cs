using System.Runtime.InteropServices;
using System.Security.Cryptography;
namespace HippoCrm.Core;

internal static class SensitiveMemory
{
    internal static byte[] AllocateBytes(int iLength) => GC.AllocateArray<byte>(iLength, pinned: true);
    internal static char[] AllocateCharacters(int iLength) => GC.AllocateArray<char>(iLength, pinned: true);
    internal static void Clear(byte[]? arrBuffer)
    {
        if (arrBuffer is null) return;
        try { CryptographicOperations.ZeroMemory(arrBuffer); }
        finally { Array.Clear(arrBuffer, 0, arrBuffer.Length); }
    }
    internal static void Clear(char[]? arrBuffer)
    {
        if (arrBuffer is null) return;
        try { CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(arrBuffer.AsSpan())); }
        finally { Array.Clear(arrBuffer, 0, arrBuffer.Length); }
    }
}
