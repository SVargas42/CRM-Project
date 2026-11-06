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
        AppBuilder.Configure<Application>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        Application.Current!.Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        AnalyticsViewChecks.Run();
        var objSnapshot = new MasterSearchSnapshot(
            [new Contact { Id = 10, FirstName = "Alex", LastName = "Rivera", Phone = "555-0100", strTags = "Priority", Skills = "Logistics", lstOrganizationIds = [1] },
             new Contact { Id = 11, FirstName = "Morgan", LastName = "Lane", Status = RecordStatus.Inactive, strTags = "ContactOnlyTag", Skills = "Planning", lstOrganizationIds = [2] }],
            [new Organization { Id = 1, Name = "Routing Organization", Email = "local@example.test", Tags = "Priority", Services = "Transportation" },
             new Organization { Id = 2, Name = "Second Organization", Status = RecordStatus.Inactive, Tags = "Review", Services = "Strategy" },
             new Organization { Id = 3, Name = "Archived Organization", Status = RecordStatus.Inactive, Archived = true, Tags = "ArchivedTag", Services = "ArchivedService" }]);
        var iLoads = 0;
        MasterSearchSnapshot CopySnapshot() => new(objSnapshot.lstContacts.Select(objContact => new Contact
        { Id = objContact.Id, FirstName = objContact.FirstName, LastName = objContact.LastName, Phone = objContact.Phone, strTags = objContact.strTags,
            Skills = objContact.Skills, Status = objContact.Status, lstOrganizationIds = objContact.lstOrganizationIds.ToList() }).ToArray(),
            objSnapshot.lstOrganizations.Select(objOrganization => new Organization { Id = objOrganization.Id, Name = objOrganization.Name,
                Email = objOrganization.Email, Tags = objOrganization.Tags, Services = objOrganization.Services, Archived = objOrganization.Archived, Status = objOrganization.Status }).ToArray());
        var vmDashboard = new DashboardViewModel(() => { iLoads++; return Task.FromResult<MasterSearchSnapshot?>(CopySnapshot()); },
            fnCanRead: () => true, fnRevalidate: ctCancellation => Task.FromResult(true), fnToken: () => CancellationToken.None);
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
            Check(vmSearch.lstSkills.Where(vmOption => vmOption.strValue is not null).Select(vmOption => vmOption.strValue)
                .Order().SequenceEqual(new[] { "Logistics", "Planning" }), "Skill choices contain contact skills only");
            Check(vmSearch.lstServices.Where(vmOption => vmOption.strValue is not null).Select(vmOption => vmOption.strValue)
                .Order().SequenceEqual(new[] { "Strategy", "Transportation" }), "Service choices contain organization services only");
            Check(vmSearch.lstTags.Any(vmOption => vmOption.strValue == "ContactOnlyTag") && vmSearch.lstTags.Any(vmOption => vmOption.strValue == "Review"),
                "Shared tag choices include contact and organization tags");
            Check(!vmSearch.lstTags.Any(vmOption => vmOption.strValue is "Logistics" or "Transportation")
                && !vmSearch.lstSkills.Any(vmOption => vmOption.strValue is "Priority" or "Review")
                && !vmSearch.lstServices.Any(vmOption => vmOption.strValue is "Priority" or "Review"), "Tags remain separate from skills and services");
            vmSearch.objSelectedService = vmSearch.lstServices.Single(vmOption => vmOption.strValue == "Transportation"); Search();
            Check(vmSearch.lstResults.Count == 3 && !vmSearch.lstResults.Any(vmRow => vmRow.strType == "Organization" && vmRow.lngRecordId == 2),
                "Service filter uses unchanged exact-token matching only for organizations");
            vmSearch.cmdClearFilters.Execute(null); Search();
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
            Check(vmSearch.lstResults.Count == 1 && vmSearch.lstResults.Single().strType == "Organization", "Contact status combines with other filters without hiding active organizations");
            vmSearch.cmdClearFilters.Execute(null); vmSearch.objSelectedStatus = vmSearch.lstStatuses.Single(vmOption => vmOption.strValue == "Inactive"); Search();
            Check(vmSearch.lstResults.Count(vmRow => vmRow.strType == "Contact") == 1 && vmSearch.lstResults.Count(vmRow => vmRow.strType == "Organization") == 2, "Contact status filters remain exact and independent of organization lifecycle");
            vmSearch.cmdClearFilters.Execute(null); vmSearch.strSearchText = "' OR 1=1 --"; Search();
            Check(vmSearch.lstResults.Count == 0, "Malformed search text is treated literally");
            vmSearch.cmdClearFilters.Execute(null); vmSearch.strSearchText = ""; vmSearch.iOrganizationVisibility = 1; Search();
            Check(vmSearch.lstResults.Where(vmRow => vmRow.strType == "Organization").Single().strStatus == "Archived", "Archived choice uses the flag and displays Archived");
            Check(vmSearch.lstServices.Any(vmOption => vmOption.strValue == "ArchivedService") && !vmSearch.lstServices.Any(vmOption => vmOption.strValue == "Strategy"), "Master catalogs follow organization archive selection");
            vmSearch.Reset();
            Check(vmSearch.iOrganizationVisibility == 0, "Reset restores the default Active organization choice");

            var objSnapshotCompletion = new TaskCompletionSource<MasterSearchSnapshot?>();
            var vmDelayed = new MasterSearchViewModel(() => objSnapshotCompletion.Task, () => true, ctCancellation => Task.FromResult(true), () => CancellationToken.None);
            vmDelayed.cmdSearch.Execute(null); var bHeartbeat = false;
            Dispatcher.UIThread.Post(() => bHeartbeat = true); Dispatcher.UIThread.RunJobs();
            Check(bHeartbeat && vmDelayed.bSearching, "Pending search does not block the UI dispatcher");
            vmDelayed.cmdSearch.Execute(null); vmDelayed.Reset(); objSnapshotCompletion.SetResult(objSnapshot);
            ContactRoutingChecks.WaitForTask(((AsyncRelayCommand)vmDelayed.cmdSearch).tskExecution);
            Check(vmDelayed.lstResults.Count == 0 && vmDelayed.lstTags.Count == 1 && vmDelayed.lstServices.Count == 1, "Reset prevents pending results from returning after lock");
            vmSearch.InvalidateCache();
            Check(vmSearch.lstServices.Count == 1 && vmSearch.lstSkills.Count == 1 && vmSearch.lstTags.Count == 1, "Invalidation clears all three choice lists");
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
