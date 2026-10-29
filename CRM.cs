using Avalonia;

namespace HippoCrm.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] arrArguments) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(arrArguments);
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}
