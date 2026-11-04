using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using HippoCrm.Core;
using HippoCrm.Desktop;
using HippoCrm.Desktop.ViewModels;
using HippoCrm.TestSupport;

internal static class PhaseThreeViewChecks
{
    private static int iPassed;
    internal static void Run()
    {
        AppBuilder.Configure<Application>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        CheckAcceptedInput(); CheckExpiredInput(TimeSpan.Zero); CheckExpiredInput(TimeSpan.FromTicks(1)); CheckExpiredCommand(); CheckModalInput();
        Console.WriteLine($"{iPassed} Phase 3 headless input/dispatch checks passed. No workspace was opened.");
    }
    private static (MainWindowViewModel vmWorkspace, Session objSession, ManualSessionClock objClock) Fixture()
    {
        var vmWorkspace = new MainWindowViewModel(); Complete(vmWorkspace.tskInitialization);
        Check(vmWorkspace.strPage == "Error", "UI fixture preserves production provisioning denial");
        vmWorkspace.cmdNewContact.Execute(null);
        Check(vmWorkspace.objContact is null, "Authenticated command also rejects a missing session");
        var objClock = new ManualSessionClock();
        // Inert credential, never inserted into any database. Reflection is confined to this test assembly.
        var objSession = (Session)Activator.CreateInstance(typeof(Session), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { 1L, "fixture", objClock }, null)!;
        typeof(MainWindowViewModel).GetField("objSession", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vmWorkspace, objSession);
        return (vmWorkspace, objSession, objClock);
    }
    private static void CheckExpiredInput(TimeSpan tsAfter)
    {
        var (vmWorkspace, objSession, objClock) = Fixture();
        using (objSession)
        {
            var frmMain = new MainWindow(vmWorkspace); frmMain.Show();
            var objInput = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A };
            objClock.lngTicks += Session.InactivityLimit.Ticks + tsAfter.Ticks; // Input precedes timer callback delivery.
            frmMain.RaiseEvent(objInput);
            Check(objInput.Handled && objSession.IsExpired, "Tunnel input at/after deadline is consumed before a control accepts it");
            Check(!vmWorkspace.RecordActivity(), "Queued input cannot refresh the expired credential during locking");
            Complete(vmWorkspace.CloseAsync()); frmMain.Close();
        }
    }
    private static void CheckExpiredCommand()
    {
        var (vmWorkspace, objSession, objClock) = Fixture();
        using (objSession)
        {
            objClock.lngTicks += Session.InactivityLimit.Ticks;
            vmWorkspace.cmdNewContact.Execute(null);
            Check(vmWorkspace.objContact is null, "Direct command dispatch after expiry cannot create a draft");
            Check(objSession.IsExpired, "Command dispatch latches expiry without refreshing activity");
            Complete(vmWorkspace.CloseAsync());
        }
    }
    private static void CheckModalInput()
    {
        var (vmWorkspace, objSession, objClock) = Fixture();
        using (objSession)
        {
            var frmMain = new MainWindow(vmWorkspace); frmMain.Show();
            var frmModal = new Window(); var tskDialog = frmModal.ShowDialog(frmMain);
            var objInput = new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "fixture" };
            objClock.lngTicks += Session.InactivityLimit.Ticks;
            frmModal.RaiseEvent(objInput);
            Check(objInput.Handled && objSession.IsExpired, "Owned modal text input obeys the workspace deadline");
            frmModal.Close(); Complete(tskDialog); Complete(vmWorkspace.CloseAsync()); frmMain.Close();
        }
    }
    private static void CheckAcceptedInput()
    {
        var (vmWorkspace, objSession, objClock) = Fixture();
        using (objSession)
        {
            var frmMain = new MainWindow(vmWorkspace); frmMain.Show();
            objClock.lngTicks += TimeSpan.FromMinutes(14).Ticks;
            var objInput = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A };
            frmMain.RaiseEvent(objInput);
            Check(!objInput.Handled && !objSession.IsExpired, "Pre-deadline input remains accepted");
            objClock.lngTicks += TimeSpan.FromMinutes(14).Ticks;
            Check(!objSession.IsExpired, "Accepted window input renews the session deadline");
            Complete(vmWorkspace.CloseAsync()); frmMain.Close();
        }
    }
    private static void Complete(Task tskWork)
    {
        var swWait = Stopwatch.StartNew();
        while (!tskWork.IsCompleted && swWait.Elapsed < TimeSpan.FromSeconds(10)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        if (!tskWork.IsCompleted) throw new TimeoutException("Headless session test timed out.");
        tskWork.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
    }
    private static void Check(bool bCondition, string strMessage)
    { if (!bCondition) throw new InvalidOperationException("FAIL " + strMessage); iPassed++; Console.WriteLine("PASS " + strMessage); }
}
