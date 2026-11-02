using Avalonia;
using HippoCrm.Core;

namespace HippoCrm.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] arrArguments)
    {
        if (arrArguments.Length == 2 && arrArguments[0] == "--delete-macos-smoke-key")
        {
            if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Keychain cleanup is macOS-only.");
            var strTestRoot = Path.GetFullPath("/private/tmp/hippocrm-macos-smoke") + Path.DirectorySeparatorChar;
            var strTestDatabase = Path.GetFullPath(arrArguments[1]);
            if (!strTestDatabase.StartsWith(strTestRoot, StringComparison.Ordinal) || !strTestDatabase.EndsWith("crm.db", StringComparison.Ordinal))
                throw new ArgumentException("Refusing to remove a Keychain item outside the disposable macOS smoke-test path.");
            new MacKeychainKeyStore().DeleteKeyForTesting(strTestDatabase);
            Console.WriteLine("Removed the disposable macOS smoke-test Keychain item.");
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(arrArguments);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}
