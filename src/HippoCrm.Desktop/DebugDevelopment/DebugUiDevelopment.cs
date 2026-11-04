#if DEBUG && HIPPOCRM_UI_DEVELOPMENT
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using HippoCrm.Core;

namespace HippoCrm.Desktop;

internal static class DebugUiDevelopment
{
    private static readonly StartupSecurityGate objGate = new(new MockManifestVerifier(), new MockFipsValidator());
    private static readonly object objWorkspaceGate = new();
    private static Database? objWorkspace;
    internal static bool bEnabled { get; private set; }
    internal const string strMarking = "UI DEVELOPMENT — MEMORY ONLY — NO OPERATIONAL DATA";

    internal static bool TryEnable(string[] arrArguments)
    {
        if (arrArguments.Length != 1 || arrArguments[0] != "--ui-development") return false;
        bEnabled = true; return true;
    }
    internal static void Verify()
    {
        if (!bEnabled) throw new InvalidOperationException("UI development was not explicitly selected.");
        objGate.Verify();
    }
    internal static Database Workspace(Database? objProvided)
    {
        Verify();
        if (objProvided is not null) throw new InvalidOperationException("UI development cannot accept an external workspace.");
        lock (objWorkspaceGate) return objWorkspace ??= Database.CreateUiDevelopmentWorkspace();
    }
    internal static void AuthorizeFixtureAdministrator(Database objDatabase, Session objSession)
    {
        lock (objWorkspaceGate)
        {
            if (!bEnabled || !ReferenceEquals(objDatabase, objWorkspace)) throw new InvalidOperationException("A UI fixture workspace is required.");
            new UserAdministrationService(objDatabase, objSession).SetCuiAuthorization(objSession.UserId, true);
        }
    }
    internal static void DisposeWorkspace()
    {
        lock (objWorkspaceGate) { objWorkspace?.Dispose(); objWorkspace = null; }
    }
    internal static Control Mark(Control ctlContent)
    {
        var pnlDevelopment = new DockPanel();
        var txtMarking = new TextBlock { Text = strMarking, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(txtMarking, strMarking);
        var bdrMarking = new Border { Background = Brushes.Gold, Padding = new Thickness(12, 6), Child = txtMarking };
        DockPanel.SetDock(bdrMarking, Dock.Top);
        pnlDevelopment.Children.Add(bdrMarking); pnlDevelopment.Children.Add(ctlContent);
        return pnlDevelopment;
    }
}

internal sealed class MockFipsValidator : IFipsValidator
{
    public void VerifyHostPolicy() { }
    public void VerifyModuleMode(object? objStatus) { }
}
internal sealed class MockManifestVerifier : IManifestVerifier
{
    public void Verify() { }
}
#endif
