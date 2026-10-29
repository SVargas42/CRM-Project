using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using HippoCrm.Core;

internal static class PasswordMemoryChecks
{
    internal static void Run()
    {
        using var secFirst = Secret("FICTIONAL-é-🔒-password");
        using var secSame = Secret("FICTIONAL-é-🔒-password");
        using var secDifferent = Secret("FICTIONAL-é-🔒-different");
        var arrSalt = Enumerable.Range(0, 32).Select(iValue => (byte)iValue).ToArray();
        var arrDerived = PasswordBuffer.Derive(secFirst, arrSalt, 1000);
        var arrExpected = Rfc2898DeriveBytes.Pbkdf2("FICTIONAL-é-🔒-password", arrSalt, 1000, HashAlgorithmName.SHA256, 32);
        try
        {
            Assert(arrDerived.AsSpan().SequenceEqual(arrExpected), "Secure password derivation preserves UTF-8/Unicode compatibility");
            Assert(PasswordBuffer.Matches(secFirst, secSame), "Equal secure passwords compare equal");
            Assert(!PasswordBuffer.Matches(secFirst, secDifferent), "Different secure passwords are rejected");
            Assert(secFirst.Length > 0, "Password APIs preserve caller ownership of SecureString");
        }
        finally { CryptographicOperations.ZeroMemory(arrDerived); CryptographicOperations.ZeroMemory(arrExpected); }
        using var secTooLong = Secret(new string('x', 257));
        Reject<ArgumentException>(() => PasswordBuffer.Derive(secTooLong, arrSalt, 1000), "Oversized password buffers are rejected");
        using var objFirstSession = new Session(1, "fixture");
        using var objSecondSession = new Session(1, "fixture");
        Assert(!objFirstSession.TokenHash().AsSpan().SequenceEqual(objSecondSession.TokenHash()), "Session credentials are unique even for the same identity");
        Assert(typeof(Session).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length == 0, "External callers cannot construct sessions from identity fields");
        var arrToken = (byte[])typeof(Session).GetField("arrToken", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(objFirstSession)!;
        objFirstSession.Dispose();
        Assert(arrToken.All(bytValue => bytValue == 0), "Session disposal clears the original credential buffer");
        Reject<ObjectDisposedException>(() => objFirstSession.TokenHash(), "Disposed session credentials cannot be reused");
        Console.WriteLine("9 password/session memory checks passed.");
    }
    private static SecureString Secret(string strFictionalPassword)
    {
        var secPassword = new SecureString();
        foreach (var chrCharacter in strFictionalPassword) secPassword.AppendChar(chrCharacter);
        return secPassword;
    }
    private static void Assert(bool bCondition, string strMessage)
    {
        if (!bCondition) throw new InvalidOperationException("FAIL " + strMessage);
        Console.WriteLine("PASS " + strMessage);
    }
    private static void Reject<T>(Action actOperation, string strMessage) where T : Exception
    {
        try { actOperation(); }
        catch (T) { Assert(true, strMessage); return; }
        throw new InvalidOperationException("FAIL " + strMessage);
    }
}
