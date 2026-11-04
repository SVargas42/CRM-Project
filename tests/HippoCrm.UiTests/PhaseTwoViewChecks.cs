using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HippoCrm.Core;
using HippoCrm.Desktop;
using HippoCrm.Desktop.ViewModels;
using HippoCrm.Desktop.Views;

internal static class PhaseTwoViewChecks
{
    private static int iPassed;
    internal static void Run()
    {
        // A bare headless Application renders controls only. It is not the CRM
        // App and cannot disable production startup or open a workspace.
        AppBuilder.Configure<Application>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var vmWorkspace = new MainWindowViewModel();
        try
        {
            Complete(vmWorkspace.tskInitialization);
            Check(vmWorkspace.strPage == "Error", "Unprovisioned production startup remains denied during view checks");
            foreach (var enStatus in Enum.GetValues<RecordStatus>())
            {
                vmWorkspace.iContactStatus = (int)enStatus;
                Check(vmWorkspace.iContactStatus == (int)enStatus, "Lookup accepts explicit " + enStatus + " selection");
            }
            vmWorkspace.iContactStatus = 99;
            Check(vmWorkspace.iContactStatus == (int)RecordStatus.Deceased, "Invalid contact selection cannot replace the last valid status");
            var ctlContacts = new ContactsRootView();
            var cboStatus = ctlContacts.FindControl<ComboBox>("cboContactStatus")!;
            Check(cboStatus.Items.Count == 4 && AutomationProperties.GetName(cboStatus) == "Contact lifecycle status", "Contact status selector exposes four accessible choices");
            Check(cboStatus.Items.Cast<ComboBoxItem>().Select(objItem => objItem.Content?.ToString()).SequenceEqual(Enum.GetNames<RecordStatus>()), "Status labels preserve enum ordering for parameterized lookup");
            var frmMain = new MainWindow(vmWorkspace);
            Check(frmMain.GetLogicalDescendants().OfType<CuiBanner>().Any(), "Error/locked startup presentation retains the CUI banner");
            CheckBanner(new CuiBanner());
            Check(new TagSelectionWindow().GetLogicalDescendants().OfType<CuiBanner>().Any(), "Tag selection window is persistently CUI marked");
            Check(new SkillSelectionWindow().GetLogicalDescendants().OfType<CuiBanner>().Any(), "Skill selection window is persistently CUI marked");
            Check(new ArchiveContactDialog().GetLogicalDescendants().OfType<TextBlock>().Any(txtMarking => txtMarking.Text == CuiPolicy.strBanner), "Archive confirmation retains its CUI marking");
        }
        finally { Complete(vmWorkspace.CloseAsync()); }
        Console.WriteLine($"{iPassed} Phase 2 headless view checks passed. Provisioned/native graphical acceptance remains pending.");
    }
    private static void CheckBanner(CuiBanner ctlBanner)
    {
        var txtMarking = ctlBanner.FindControl<TextBlock>("txtCuiMarking")!;
        Check(txtMarking.Text == CuiPolicy.strBanner && !string.IsNullOrWhiteSpace(AutomationProperties.GetName(txtMarking)), "CUI banner has visible and screen-reader text");
    }
    private static void Complete(Task tskWork)
    {
        var swWait = Stopwatch.StartNew();
        while (!tskWork.IsCompleted && swWait.Elapsed < TimeSpan.FromSeconds(10)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        if (!tskWork.IsCompleted) throw new TimeoutException("Headless policy view check timed out.");
        tskWork.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
    }
    private static void Check(bool bCondition, string strMessage)
    { if (!bCondition) throw new InvalidOperationException("FAIL " + strMessage); iPassed++; Console.WriteLine("PASS " + strMessage); }
}
