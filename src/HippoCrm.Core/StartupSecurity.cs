using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace HippoCrm.Core;

public interface IFipsValidator
{
    void VerifyHostPolicy();
    void VerifyModuleMode(object? objStatus);
}

public interface IManifestVerifier
{
    void Verify();
}

// Instances can be dependency-injected into isolated tests. The production gate
// is immutable and has no environment, command-line, or Debug bypass.
internal sealed class StartupSecurityGate(IManifestVerifier objManifest, IFipsValidator objFips)
{
    internal void Verify()
    {
        objFips.VerifyHostPolicy();
        objManifest.Verify();
    }
}

public static class StartupSecurity
{
    private static readonly ReleaseManifestVerifier objManifest = new();
    internal static readonly IFipsValidator objFips = new HostFipsValidator();
    // Reverify rather than caching success: files/policy may change after startup.
    public static void Verify() => new StartupSecurityGate(objManifest, objFips).Verify();
    internal static string VerifyNativeModule()
    {
        objFips.VerifyHostPolicy();
        return objManifest.VerifyInstallation();
    }
}

internal sealed class HostFipsValidator : IFipsValidator
{
    public void VerifyHostPolicy()
    {
        if (!OperatingSystem.IsWindows())
            throw new CryptographicException("Approved host security policy validation has not been provisioned for this platform.");
        try
        {
            VerifyWindowsPolicy(BCryptGetFipsAlgorithmMode(out var bytEnabled), bytEnabled);
        }
        catch (Exception objException) when (objException is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { throw new CryptographicException("The host security policy cannot be verified.", objException); }
    }

    internal static void VerifyWindowsPolicy(int iStatus, byte bytEnabled)
    {
        if (iStatus != 0 || bytEnabled != 1)
            throw new CryptographicException("The required host security policy is not enabled or could not be verified.");
    }

    public void VerifyModuleMode(object? objStatus)
    {
        // Unknown PRAGMAs return no row. Never coerce null, strings or booleans
        // to success: the approved SQLCipher API must return SQLite INTEGER 1.
        if (objStatus is not long lngStatus || lngStatus != 1)
            throw new CryptographicException("The encryption module is not operating in a verified approved mode.");
    }

    [DllImport("bcrypt.dll", ExactSpelling = true)]
    private static extern int BCryptGetFipsAlgorithmMode(out byte bytEnabled);
}
