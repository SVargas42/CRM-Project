using System.Security;
using HippoCrm.Core;

namespace HippoCrm.TestSupport;

// Test fixtures only: no production API accepts immutable password strings.
public static class TestPasswords
{
    public static SecureString Secret(string strFictionalPassword)
    {
        var secPassword = new SecureString();
        foreach (var chrCharacter in strFictionalPassword) secPassword.AppendChar(chrCharacter);
        secPassword.MakeReadOnly(); return secPassword;
    }
    public static Session SignInForTest(this AuthService objAuthentication, string strUsername, string strFictionalPassword)
    {
        using var secPassword = Secret(strFictionalPassword);
        return objAuthentication.SignIn(strUsername, secPassword);
    }
    public static Session CreateAdministratorForTest(this AuthService objAuthentication, string strUsername, string strFictionalPassword)
    {
        using var secPassword = Secret(strFictionalPassword);
        return objAuthentication.CreateAdministrator(strUsername, secPassword);
    }
}
