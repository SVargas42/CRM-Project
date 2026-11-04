using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HippoCrm.Core;

namespace HippoCrm.Desktop;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        VerifyStartup();
        Avalonia.Threading.AvaloniaSynchronizationContext.InstallIfNeeded();
        AvaloniaXamlLoader.Load(this);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        VerifyStartup();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime objDesktopLifetime)
        {
            objDesktopLifetime.MainWindow = new MainWindow();
#if DEBUG && HIPPOCRM_UI_DEVELOPMENT
            if (DebugUiDevelopment.bEnabled) objDesktopLifetime.Exit += (objSender, objArguments) => DebugUiDevelopment.DisposeWorkspace();
#endif
        }
        base.OnFrameworkInitializationCompleted();
    }
    private static void VerifyStartup()
    {
#if DEBUG && HIPPOCRM_UI_DEVELOPMENT
        if (DebugUiDevelopment.bEnabled) { DebugUiDevelopment.Verify(); return; }
#endif
        StartupSecurity.Verify();
    }
}
