using Avalonia;
using HippoCrm.Core;

namespace HippoCrm.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] arrArguments)
    {
#if DEBUG && HIPPOCRM_UI_DEVELOPMENT
        if (DebugUiDevelopment.TryEnable(arrArguments))
        {
            DebugUiDevelopment.Verify();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
            return;
        }
#endif
        try
        {
            StartupSecurity.Verify();
            SqlCipherConnectionFactory.VerifyApprovedRuntime();
        }
        catch (Exception objException) when (objException is System.Security.Cryptography.CryptographicException
            or IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
            or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            Console.Error.WriteLine("This installation could not pass its security checks. Contact your administrator.");
            Environment.ExitCode = 1;
            return;
        }
        if (arrArguments.Length == 2 && arrArguments[0] == "--delete-macos-smoke-key")
        {
            if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("This cleanup is macOS-only.");
            var strTestRoot = Path.GetFullPath("/private/tmp/hippocrm-macos-smoke") + Path.DirectorySeparatorChar;
            var strTestDatabase = Path.GetFullPath(arrArguments[1]);
            if (!strTestDatabase.StartsWith(strTestRoot, StringComparison.Ordinal) || !strTestDatabase.EndsWith("crm.db", StringComparison.Ordinal))
                throw new ArgumentException("Refusing to remove a key file outside the disposable macOS smoke-test path.");
            new MacFileKeyStore().DeleteKeyForTesting(strTestDatabase);
            Console.WriteLine("Removed the disposable macOS smoke-test key file.");
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(arrArguments);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}
