using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HippoCrm.Core;

namespace HippoCrm.Desktop;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        StartupSecurity.Verify();
        Avalonia.Threading.AvaloniaSynchronizationContext.InstallIfNeeded();
        AvaloniaXamlLoader.Load(this);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        StartupSecurity.Verify();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime objDesktopLifetime) objDesktopLifetime.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
