using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HippoCrm.Core;
using HippoCrm.Desktop;
using HippoCrm.Desktop.ViewModels;
using HippoCrm.Desktop.Views;

internal static class MasterSearchChecks
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var objSnapshot = new MasterSearchSnapshot(
            [new Contact { Id = 10, FirstName = "Alex", LastName = "Rivera", Phone = "555-0100", strTags = "Priority", Skills = "Logistics", lstOrganizationIds = [1] },
             new Contact { Id = 11, FirstName = "Morgan", LastName = "Lane", Status = RecordStatus.Inactive, Skills = "Planning", lstOrganizationIds = [2] }],
            [new Organization { Id = 1, Name = "Routing Organization", Email = "local@example.test", Tags = "Priority", Services = "Logistics" },
             new Organization { Id = 2, Name = "Second Organization", Status = RecordStatus.Inactive, Tags = "Review", Services = "Planning" }]);
        var iLoads = 0;
        var vmDashboard = new DashboardViewModel(() => { iLoads++; return Task.FromResult<MasterSearchSnapshot?>(objSnapshot); });
        var vmSearch = vmDashboard.vmMasterSearch;
        var frmDashboard = new Window { Width = 1100, Height = 720, Content = new DashboardView { DataContext = vmDashboard } };
        frmDashboard.Show();
        var iChecks = 0;
        try
        {
            vmDashboard.iSelectedTab = 2; Search(false);
            Check(iLoads == 1 && vmSearch.lstResults.Count == 4, "Opening Master Search loads contacts and organizations");
            Check(vmSearch.lstResults.Count(vmRow => vmRow.strType == "Contact") == 2 && vmSearch.lstResults.Count(vmRow => vmRow.strType == "Organization") == 2,
                "Unified results identify both record types");
            Check(frmDashboard.GetVisualDescendants().OfType<Button>().Where(btnControl => btnControl.Name == "btnViewMasterResult")
                .All(btnControl => !btnControl.IsEffectivelyEnabled), "View actions are explicit navigation placeholders");
            frmDashboard.GetVisualDescendants().OfType<TextBox>().Single(txtControl => txtControl.Name == "txtMasterSearch").Text = "Alix";
            Search(); Check(vmSearch.lstResults.Single().lngRecordId == 10, "Master Search supports phonetic contact names");
            vmSearch.strSearchText = "Sekond"; Search();
            Check(vmSearch.lstResults.Single().strType == "Organization", "Master Search supports phonetic organization names");
            vmSearch.strSearchText = "555-0100"; Search();
            Check(vmSearch.lstResults.Single().lngRecordId == 10, "Master Search matches primary information");
            vmSearch.strSearchText = ""; vmSearch.objSelectedTag = vmSearch.lstTags.Single(vmOption => vmOption.strValue == "Priority"); Search();
            Check(vmSearch.lstResults.Count == 2, "Tag filter applies across both record types");
            vmSearch.objSelectedOrganization = vmSearch.lstOrganizations.Single(vmOption => vmOption.lngOrganizationId == 1);
            vmSearch.objSelectedSkill = vmSearch.lstSkills.Single(vmOption => vmOption.strValue == "Logistics"); Search();
            Check(vmSearch.lstResults.Count == 2 && vmSearch.lstResults.All(vmRow => vmRow.lngRecordId is 1 or 10), "Organization and skill filters combine with tags");
            vmSearch.objSelectedStatus = vmSearch.lstStatuses.Single(vmOption => vmOption.strValue == "Inactive"); Search();
            Check(vmSearch.lstResults.Count == 0, "Status combines with the remaining filters");
            vmSearch.cmdClearFilters.Execute(null); vmSearch.objSelectedStatus = vmSearch.lstStatuses.Single(vmOption => vmOption.strValue == "Inactive"); Search();
            Check(vmSearch.lstResults.Count == 2, "Status matches both contacts and organizations");
            vmSearch.cmdClearFilters.Execute(null); vmSearch.strSearchText = "' OR 1=1 --"; Search();
            Check(vmSearch.lstResults.Count == 0, "Malformed search text is treated literally");
            var tcsSnapshot = new TaskCompletionSource<MasterSearchSnapshot?>();
            var vmDelayed = new MasterSearchViewModel(() => tcsSnapshot.Task);
            vmDelayed.cmdSearch.Execute(null); var bHeartbeat = false;
            Dispatcher.UIThread.Post(() => bHeartbeat = true); Dispatcher.UIThread.RunJobs();
            Check(bHeartbeat && vmDelayed.bSearching, "Pending search does not block the UI dispatcher");
            vmDelayed.cmdSearch.Execute(null); vmDelayed.Reset(); tcsSnapshot.SetResult(objSnapshot);
            ContactRoutingChecks.WaitForTask(((AsyncRelayCommand)vmDelayed.cmdSearch).tskExecution);
            Check(vmDelayed.lstResults.Count == 0 && vmDelayed.lstTags.Count == 1, "Reset prevents pending results from returning after lock");
            Console.WriteLine($"{iChecks} focused Master Search checks passed.");
        }
        finally { frmDashboard.Close(); }

        void Search(bool bExecute = true)
        {
            if (bExecute) vmSearch.cmdSearch.Execute(null);
            ContactRoutingChecks.WaitForTask(((AsyncRelayCommand)vmSearch.cmdSearch).tskExecution);
            Dispatcher.UIThread.RunJobs(); frmDashboard.UpdateLayout();
        }
        void Check(bool bCondition, string strDescription)
        {
            if (!bCondition) throw new InvalidOperationException("FAIL " + strDescription);
            iChecks++; Console.WriteLine("PASS " + strDescription);
        }
    }
}
