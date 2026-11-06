using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HippoCrm.Core;
using HippoCrm.Desktop;
using HippoCrm.Desktop.Utilities;
using HippoCrm.Desktop.ViewModels;
using HippoCrm.Desktop.Views;
using HippoCrm.TestSupport;
using Microsoft.Data.Sqlite;

internal static class AnalyticsTableChecks
{
    internal static void Run()
    {
#if DEBUG
        RunDebug();
#else
        throw new InvalidOperationException("Use the isolated Debug Analytics fixture.");
#endif
    }
#if DEBUG
    private static void RunDebug()
    {
        var iChecks = 0;
        UiDevelopmentChecks.Invoke("TryEnable", (object)new[] { "--ui-development" });
        using var objDatabase = Database.CreateUiDevelopmentWorkspace();
        UiDevelopmentChecks.Invoke("ConfigureWorkspaceFactory", (Func<Database>)(() => objDatabase));
        AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        using var secPassword = TestPasswords.Secret("FICTIONAL v2 page password!");
        var objAuth = new AuthService(objDatabase); using var objAdmin = objAuth.CreateAdministrator("v2-page-owner", secPassword);
        var objUsers = new UserAdministrationService(objDatabase, objAdmin); objUsers.SetCuiAuthorization(objAdmin.UserId, true);
        var objCrm = new CrmService(objDatabase, objAdmin);
        var lstOrganizations = new List<Organization>();
        foreach (var strName in new[] { "Atlas fixture", "Beacon fixture", "Cedar archived fixture", "PRIVATE_PAGE_ORGANIZATION" })
        {
            var objOrganization = new Organization { Name = strName, Phone = "+1 (312) 555-0100", Email = "organization@example.test", Services = "heavy equipment, Logistics", Tags = "Safety" };
            objCrm.Save(objOrganization); lstOrganizations.Add(objOrganization);
        }
        var strLongSkill = "heavy equipment " + new string('x', 90);
        var lstContacts = new List<Contact>();
        for (var iContact = 0; iContact < 10; iContact++)
        {
            var objContact = new Contact { FirstName = "PAGE_CONTACT_" + iContact, Phone = "+1 (312) 555-" + (1100 + iContact), Email = "contact" + iContact + "@example.test",
                Skills = iContact is 1 or 3 or 5 or 7 ? "" : iContact == 0 ? strLongSkill : iContact == 9 ? "Navigation" : "heavy equipment",
                strTags = iContact is 2 or 9 ? "" : iContact == 1 ? "Priority" : "Safety",
                lstOrganizationIds = [lstOrganizations[iContact == 9 ? 3 : iContact == 2 ? 1 : 0].Id] };
            objCrm.Save(objContact); lstContacts.Add(objContact);
            objCrm.AddContract(objContact.Id, new DateOnly(2026, 1, 1), 123.45m, "Fixture location", "");
            if (iContact is 2 or 3 or 4 or 5 or 8 or 9) objCrm.AddInteraction(objContact.Id, "FICTIONAL current note");
        }
        foreach (var iContact in new[] { 0, 4 })
        {
            objCrm.AddEmployment(lstContacts[iContact].Id, lstOrganizations[0].Id, "Fixture role", new DateOnly(2023, 1, 1), new DateOnly(2023, 12, 31));
            objCrm.AddEmployment(lstContacts[iContact].Id, lstOrganizations[iContact == 4 ? 2 : 1].Id, "Fixture role", new DateOnly(2024, 1, 1), null);
        }
        objCrm.AddEmployment(lstContacts[9].Id, lstOrganizations[0].Id, "Fixture role", new DateOnly(2023, 1, 1), new DateOnly(2023, 12, 31));
        objCrm.AddEmployment(lstContacts[9].Id, lstOrganizations[3].Id, "Fixture role", new DateOnly(2024, 1, 1), null);
        foreach (var (iContact, enStatus) in new[] { (4, RecordStatus.Inactive), (6, RecordStatus.Retired), (7, RecordStatus.Deceased) }) objCrm.SetRecordStatus(RecordKind.Contact, lstContacts[iContact].Id, lstContacts[iContact].Revision, enStatus);
        objCrm.SetArchived(RecordKind.Contact, lstContacts[5].Id, lstContacts[5].Revision, true);
        objCrm.SetArchived(RecordKind.Organization, lstOrganizations[2].Id, lstOrganizations[2].Revision, true);
        var lngViewer = objUsers.CreateUser("v2-page-viewer", secPassword, UserRole.Viewer); objUsers.SetCuiAuthorization(lngViewer, true);
        foreach (var objContact in lstContacts.Where((_, iIndex) => iIndex != 8)) objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, objContact.Id, true, false);
        foreach (var objOrganization in lstOrganizations.Take(3)) objUsers.SetRecordAccess(lngViewer, RecordKind.Organization, objOrganization.Id, true, false);
        var vmMain = new MainWindowViewModel(); Complete(vmMain.tskInitialization);
        SignIn(); var frmMain = new MainWindow(vmMain); frmMain.Show();
        var vmDashboard = (DashboardViewModel)vmMain.vmShell.vmCurrentPage; var vmAnalytics = vmDashboard.vmAnalytics;
        var lstCells = new List<TextBlock>(); var lstInputs = new List<TextBox>(); var lstRows = new List<AnalyticsDisplayRow>();
        var lstTableControls = new List<ListBox>();
        var arrCanaries = lstContacts.SelectMany(objContact => new[] { objContact.FirstName, objContact.Email, objContact.Phone, objContact.Skills, objContact.strTags })
            .Concat(lstOrganizations.SelectMany(objOrganization => new[] { objOrganization.Name, objOrganization.Email, objOrganization.Phone, objOrganization.Services, objOrganization.Tags }))
            .Concat(new[] { "Fixture role", "Fixture location" }).Where(strValue => strValue.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var iCountChecks = 0;
        try
        {
            var lngBefore = AuditCount(); Load(); var lngLoad = AuditCount() - lngBefore;
            Check(lngLoad is >= 5 and <= 12, "Single-page load preserves bounded audited hydration"); Console.WriteLine($"Analytics v2 audited reads per load: {lngLoad}.");
            Check(vmAnalytics.lstTables.Count == 6 && vmAnalytics.vmLastContacted.lstRows.Count == 2 && vmAnalytics.vmRecentContacts.lstRows.Count == 3
                && vmAnalytics.vmContracts.lstRows.Count == 5 && vmAnalytics.vmMovement.lstRows.Count == 2, "Sections A, B, E and F render their correctly scoped Active rows");
            Check(frmMain.GetVisualDescendants().OfType<AnalyticsPageView>().Count() == 1 && !frmMain.GetVisualDescendants().OfType<TabControl>().Any(objTabs => objTabs.Name == "tcAnalyticsSub"),
                "Analytics is one scrolling page without old subtabs");
            Check(frmMain.GetVisualDescendants().OfType<Grid>().Any(grdControl => grdControl.Name == "grdAnalyticsGraphs" && grdControl.Children.Count == 0), "Named graph container is reserved and empty");
            lngBefore = AuditCount();
            foreach (var vmTable in vmAnalytics.lstTables) vmTable.PropertyChanged += (_, objEvent) =>
            {
                if (objEvent.PropertyName != nameof(AnalyticsTableViewModel.strCount) || vmAnalytics.objSnapshot is not { } objData) return;
                var (iLoaded, lngTotal) = ReferenceEquals(vmTable, vmAnalytics.vmLastContacted) ? (objData.LastContacted.Count, objData.LastContactedTotal)
                    : ReferenceEquals(vmTable, vmAnalytics.vmRecentContacts) ? (objData.RecentContacts.Count, objData.RecentContactsTotal)
                    : ReferenceEquals(vmTable, vmAnalytics.vmMembers) ? (objData.OrganizationMembers.Count, objData.OrganizationMembersTotal)
                    : ReferenceEquals(vmTable, vmAnalytics.vmContracts) ? (objData.LatestContracts.Count, objData.LatestContractsTotal)
                    : ReferenceEquals(vmTable, vmAnalytics.vmMovement) ? (objData.Movement.Count, objData.MovementTotal)
                    : ((vmAnalytics.iSearchType == 1 ? 0 : objData.ContactSearch.Count) + (vmAnalytics.iSearchType == 0 ? 0 : objData.OrganizationRoster.Count),
                       (vmAnalytics.iSearchType == 1 ? 0 : objData.ContactSearchTotal) + (vmAnalytics.iSearchType == 0 ? 0 : objData.OrganizationRosterTotal));
                if (vmTable.strCount != $"Showing {vmTable.lstRows.Count} of {iLoaded} loaded ({lngTotal} total)") throw new InvalidOperationException("FAIL exact table count after an in-memory filter change");
                iCountChecks++;
            };
            foreach (var strBad in new[] { "", "0", "3651", "abc", "-1", "1.5" }) { vmAnalytics.strDays = strBad; Layout(); Check(vmAnalytics.bDaysError && vmAnalytics.vmLastContacted.bEmpty, "Days rejects invalid input without fallback"); }
            vmAnalytics.strDays = "3650"; Layout(); Check(vmAnalytics.vmLastContacted.lstRows.Count == 2, "Never-contacted rows remain eligible at the maximum threshold"); vmAnalytics.strDays = "30";
            vmAnalytics.vmLastFilter.strSearch = "Priority"; Layout(); Check(vmAnalytics.vmLastContacted.lstRows.Single().lngId == lstContacts[1].Id, "A searches tags in memory");
            Check(SoundexHelper.GetSoundexCode("hevy") == SoundexHelper.GetSoundexCode("heavy"), "Phonetic fixture codes match per word");
            vmAnalytics.vmLastFilter.strSearch = "hevy"; Layout(); Check(vmAnalytics.vmLastContacted.lstRows.Single().lngId == lstContacts[0].Id, "A matches an individual word within a multi-word skill");
            vmAnalytics.vmRecentFilter.strSearch = "Beacon"; Layout(); Check(vmAnalytics.vmRecentContacts.lstRows.Single().lngId == lstContacts[2].Id && vmAnalytics.vmLastContacted.lstRows.Count == 1, "B has independent organization search and no days threshold");
            vmAnalytics.vmLastFilter.strSearch = ""; vmAnalytics.vmRecentFilter.strSearch = "";
            vmAnalytics.vmRecentFilter.bActive = false; vmAnalytics.vmRecentFilter.bInactive = true; Layout();
            Check(vmAnalytics.vmRecentContacts.bEmpty && !Find<CheckBox>("chkAnalyticsRecentInactive").IsEffectivelyEnabled
                && !Find<CheckBox>("chkAnalyticsLastRetired").IsEffectivelyEnabled, "Master off excludes non-Active rows and disables non-Active filters");
            vmAnalytics.bIncludeInactive = true; Layout(); Check(vmAnalytics.vmRecentContacts.lstRows.Count == 2 && vmAnalytics.vmLastContacted.lstRows.Count == 2, "Master on allows independent Inactive selection including archived contacts");
            vmAnalytics.vmLastFilter.bRetired = true; vmAnalytics.vmLastFilter.bDeceased = true; Layout(); Check(vmAnalytics.vmLastContacted.lstRows.Count == 4, "A status checkboxes narrow within master eligibility");
            vmAnalytics.bIncludeInactive = false; Layout(); Check(vmAnalytics.vmLastContacted.lstRows.Count == 2 && vmAnalytics.vmRecentContacts.bEmpty, "Master off overrides every section checkbox");
            vmAnalytics.vmRecentFilter.bActive = true; vmAnalytics.vmRecentFilter.bInactive = false;
            vmAnalytics.iSearchType = 2; vmAnalytics.strFindSearch = ""; Layout();
            Check(vmAnalytics.vmSearch.lstRows.Count == 7 && !vmAnalytics.bSearchEmpty, "C empty search shows all eligible loaded contacts and organizations");
            vmAnalytics.strFindSearch = "hevy"; Layout(); Check(vmAnalytics.vmSearch.lstRows.Count == 4
                && vmAnalytics.vmSearch.lstRows.Where(objRow => objRow.enKind == RecordKind.Contact).All(objRow => objRow.strSummary.Contains("heavy equipment", StringComparison.Ordinal)), "C matches each skill/service phrase phonetically");
            var objLong = vmAnalytics.vmSearch.lstRows.Single(objRow => objRow.enKind == RecordKind.Contact && objRow.lngId == lstContacts[0].Id);
            Check(objLong.arrCells[2].Length == 80 && objLong.arrCells[2].EndsWith('…') && objLong.strSummary.Contains(strLongSkill + " (skill)", StringComparison.Ordinal), "Matched cell truncates at 80 while the accessible row retains the complete phrase and kind");
            vmAnalytics.strFindSearch = "Priority"; Layout(); Check(vmAnalytics.vmSearch.lstRows.Single().lngId == lstContacts[1].Id, "C includes tags-only contacts");
            vmAnalytics.iSearchType = 0; vmAnalytics.strFindSearch = "heavy"; Layout(); Check(vmAnalytics.vmSearch.lstRows.Count == 2 && vmAnalytics.vmSearch.lstRows.All(objRow => objRow.enKind == RecordKind.Contact), "C Contacts choice includes skills-only contacts");
            vmAnalytics.iSearchType = 1; vmAnalytics.strFindSearch = "Safety"; Layout(); Check(vmAnalytics.vmSearch.lstRows.Count == 2 && vmAnalytics.vmSearch.lstRows.All(objRow => objRow.enKind == RecordKind.Organization && objRow.arrCells[3] == "—" && objRow.arrCells[6] == "n/a"), "C Organizations choice matches tags and labels unavailable columns");
            vmAnalytics.strFindSearch = "zzzz-no-match"; Layout(); Check(vmAnalytics.vmSearch.bEmpty && vmAnalytics.bSearchEmpty, "C unmatched search has an explicit empty state and no looser fallback");
            vmAnalytics.strFindSearch = ""; vmAnalytics.iSearchType = 2; Layout();
            Check(vmAnalytics.lstOrganizationChoices.Select(objChoice => objChoice.strCaption).SequenceEqual(new[] { lstOrganizations[0].Name, lstOrganizations[1].Name }), "D lists eligible loaded organizations alphabetically");
            var txtOrg = Find<CrmTextBox>("txtAnalyticsOrganization"); txtOrg.Focus(); Press(frmMain, PhysicalKey.ArrowDown); Layout();
            var lstOrg = Find<ListBox>("lstAnalyticsOrganizations"); Check(vmAnalytics.bOrganizationDropOpen && lstOrg.SelectedIndex == 0, "Down opens the organization dropdown");
            Press(frmMain, PhysicalKey.ArrowDown); Press(frmMain, PhysicalKey.Enter); Layout();
            Console.WriteLine($"Analytics dropdown diagnostic: selectedIndex={lstOrg.SelectedIndex}, members={vmAnalytics.vmMembers.lstRows.Count}, open={vmAnalytics.bOrganizationDropOpen}, focusType={frmMain.FocusManager?.GetFocusedElement()?.GetType().Name ?? "none"}.");
            Check(!vmAnalytics.bOrganizationDropOpen && vmAnalytics.strSelectedOrganization == lstOrganizations[1].Name && vmAnalytics.vmMembers.lstRows.Count == 1, "Arrows and Enter select an organization and filter members");
            txtOrg.Focus(); Press(frmMain, PhysicalKey.ArrowDown); Press(frmMain, PhysicalKey.Escape); Layout(); Check(!vmAnalytics.bOrganizationDropOpen, "Escape closes the organization dropdown");
            vmAnalytics.strOrganizationSearch = "Atlas"; Layout(); Check(vmAnalytics.lstOrganizationChoices.Count == 1, "Typing filters the organization dropdown in memory");
            vmAnalytics.SelectOrganization(vmAnalytics.lstOrganizationChoices[0]); Layout(); Check(vmAnalytics.vmMembers.lstRows.Count == 3 && vmAnalytics.strOrganizationPhone == lstOrganizations[0].Phone, "D shows authorized linked members and organization details");
            vmAnalytics.bIncludeInactive = true; Layout(); Check(vmAnalytics.vmMembers.lstRows.Count == 7 && vmAnalytics.vmContracts.lstRows.Count == 9 && vmAnalytics.vmMovement.lstRows.Count == 4, "Master switch also governs D, E and F");
            vmAnalytics.strOrganizationSearch = ""; Layout(); Check(vmAnalytics.lstOrganizationChoices.Count == 3, "Master switch includes archived organizations in D");
            vmAnalytics.SelectOrganization(vmAnalytics.lstOrganizationChoices.Single(objChoice => objChoice.lngId == lstOrganizations[0].Id));
            Check(vmAnalytics.vmMovement.lstRows.All(objRow => objRow.lngId != lstContacts[9].Id), "Hidden organization placements neither qualify nor appear in Movement");
            Check(!vmAnalytics.lstTables.SelectMany(vmTable => vmTable.lstRows).Any(objRow => objRow.strSummary.Contains(lstContacts[8].FirstName, StringComparison.Ordinal) || objRow.strSummary.Contains(lstOrganizations[3].Name, StringComparison.Ordinal)), "Viewer page excludes all ungranted record names");
            foreach (var vmTable in vmAnalytics.lstTables) Keyboard(vmTable);
            Check(AuditCount() == lngBefore, "All filters, dropdown selections and keyboard table traversal add zero audited reads");
            Check(vmAnalytics.lstTables.All(vmTable => vmTable.strCount.StartsWith("Showing " + vmTable.lstRows.Count + " of ", StringComparison.Ordinal) && vmTable.strCount.EndsWith(" total)", StringComparison.Ordinal)), "Every table labels filtered, loaded and true totals");
            Check(iCountChecks > 100, "Exact counts update on every table notification across the full filter sequence");
            foreach (var strProperty in new[] { "vmLastContacted", "vmRecentContacts", "vmSearch", "vmMembers" })
            {
                var vmTable = (AnalyticsTableViewModel)typeof(AnalyticsViewModel).GetProperty(strProperty)!.GetValue(vmAnalytics)!;
                var objExpected = vmTable.lstRows.First(objRow => objRow.enKind == (strProperty == "vmSearch" ? RecordKind.Organization : RecordKind.Contact));
                var enExpected = objExpected.enKind; var lngExpected = objExpected.lngId;
                // A real mouse click goes through the shared row handler and existing route.
                var objHost = new AnalyticsTableView { DataContext = vmTable }; var frmClick = new Window { Width = 1200, Height = 400, Content = objHost }; frmClick.Show(); frmClick.Activate(); Dispatcher.UIThread.RunJobs(); frmClick.UpdateLayout();
                try
                {
                    var lstTable = objHost.GetVisualDescendants().OfType<ListBox>().Single(); var iIndex = vmTable.lstRows.ToList().IndexOf(objExpected);
                    lstTable.ScrollIntoView(iIndex); Dispatcher.UIThread.RunJobs(); frmClick.UpdateLayout();
                    var objItem = (ListBoxItem)lstTable.ContainerFromIndex(iIndex)!; var ptClick = objItem.TranslatePoint(new Point(8, 8), frmClick)!.Value;
                    frmClick.MouseDown(ptClick, MouseButton.Left, RawInputModifiers.None); frmClick.MouseUp(ptClick, MouseButton.Left, RawInputModifiers.None); Dispatcher.UIThread.RunJobs(); Complete(vmTable.tskActivation); Complete(vmMain.tskPageHydration);
                    Check(enExpected == RecordKind.Contact ? vmMain.strPage == "Contacts" && vmMain.objContact?.Id == lngExpected : vmMain.strPage == "Organizations" && vmMain.objOrganization?.Id == lngExpected, "Row click reuses authorized profile navigation for the correct record kind and ID");
                }
                finally { frmClick.Close(); }
                vmMain.cmdOverview.Execute(null); Complete(vmMain.tskPendingOperation); vmDashboard = (DashboardViewModel)vmMain.vmShell.vmCurrentPage; vmAnalytics = vmDashboard.vmAnalytics; Load();
                vmAnalytics.iSearchType = 2; vmAnalytics.strOrganizationSearch = ""; vmAnalytics.SelectOrganization(vmAnalytics.lstOrganizationChoices.First()); Layout();
            }
            // Exhaustive cleanup: keep old cell/input/row references as well as scan the live tree.
            vmAnalytics.strDays = "123"; vmAnalytics.vmLastFilter.strSearch = "Priority"; vmAnalytics.strFindSearch = "Safety"; vmAnalytics.strOrganizationSearch = "Atlas"; vmAnalytics.bOrganizationDropOpen = true; Layout();
            objUsers.SetCuiAuthorization(lngViewer, true); Dispatcher.UIThread.RunJobs(); Layout(); AssertCleared("Permission epoch");
            Complete(vmMain.ProbeCuiAuthorizationAsync()); vmAnalytics.EnsureLoaded(); Complete(vmAnalytics.tskLoad); Layout(); Check(vmAnalytics.strFindSearch == "" && vmAnalytics.vmLastFilter.strSearch == "", "Fresh epoch load does not resurrect search text");
            vmAnalytics.vmLastFilter.strSearch = "Priority"; vmAnalytics.strOrganizationSearch = "Atlas"; vmAnalytics.bOrganizationDropOpen = true; Layout();
            vmMain.cmdLock.Execute(null); Complete(((AsyncRelayCommand)vmMain.cmdLock).tskExecution); Layout(); AssertCleared("Lock");
            SignIn(); Load(); Check(vmAnalytics.strFindSearch == "" && vmAnalytics.strOrganizationSearch == "" && vmAnalytics.vmLastFilter.strSearch == "" && vmAnalytics.vmLastContacted.lstRows.Count == 2, "Lock then sign-in loads fresh defaults and current authorized data");
            FailureView();
            Console.WriteLine($"{iChecks} Analytics v2 page checks passed.");
        }
        finally { Complete(vmMain.CloseAsync()); frmMain.Close(); objAuth.SignOut(objAdmin); }
        void SignIn() { vmMain.strUsername = "v2-page-viewer"; foreach (var chrValue in "FICTIONAL v2 page password!") vmMain.secPassword.AppendChar(chrValue); vmMain.cmdSignIn.Execute(null); Complete(vmMain.tskPendingOperation); }
        void Load() { var vmCurrent = (DashboardViewModel)vmMain.vmShell.vmCurrentPage; vmCurrent.iSelectedTab = 1; Complete(vmCurrent.vmAnalytics.tskLoad); Layout(); }
        T Find<T>(string strName) where T : Control => frmMain.GetVisualDescendants().OfType<T>().Single(objControl => objControl.Name == strName);
        void Layout()
        {
            Dispatcher.UIThread.RunJobs(); frmMain.UpdateLayout();
            lstCells.AddRange(frmMain.GetVisualDescendants().OfType<TextBlock>().Where(txtText => txtText.GetType().Name == "AnalyticsCell"));
            lstInputs.AddRange(frmMain.GetVisualDescendants().OfType<CrmTextBox>().Where(txtInput => txtInput.Name?.StartsWith("txtAnalytics", StringComparison.Ordinal) == true));
            lstTableControls.AddRange(frmMain.GetVisualDescendants().OfType<AnalyticsTableView>().SelectMany(objTable => objTable.GetVisualDescendants().OfType<ListBox>()));
            if (vmAnalytics is not null) lstRows.AddRange(vmAnalytics.lstTables.SelectMany(vmTable => vmTable.lstRows));
        }
        void AssertCleared(string strReason)
        {
            Check(vmAnalytics.objSnapshot is null && vmAnalytics.lstTables.All(vmTable => vmTable.lstRows.Count == 0 && vmTable.strCount == "")
                && vmAnalytics.lstOrganizationChoices.Count == 0 && vmAnalytics.objHighlightedOrganization is null && vmAnalytics.bSelectOrganization
                && vmAnalytics.strDays == "" && vmAnalytics.strFindSearch == "" && vmAnalytics.strOrganizationSearch == "" && vmAnalytics.vmLastFilter.strSearch == "" && vmAnalytics.vmRecentFilter.strSearch == "",
                strReason + " clears every table, input, selection, dropdown and count model");
            Check(lstCells.All(txtCell => string.IsNullOrEmpty(txtCell.Text)) && lstInputs.All(txtInput => string.IsNullOrEmpty(txtInput.Text))
                && lstRows.All(objRow => objRow.arrCells.All(string.IsNullOrEmpty) && objRow.strSummary == "")
                && lstTableControls.All(lstTable => lstTable.Items.Count == 0 && lstTable.SelectedItem is null), strReason + " clears retained visual cells, input controls, row summaries and native selections");
            Check(!frmMain.GetVisualDescendants().OfType<TextBlock>().Select(txtText => txtText.Text ?? "")
                    .Concat(frmMain.GetVisualDescendants().OfType<TextBox>().Select(txtInput => txtInput.Text ?? "")).Any(strValue => arrCanaries.Any(strCanary => strValue.Contains(strCanary, StringComparison.Ordinal))),
                strReason + " live visual tree contains no row-derived fixture text in any section");
        }
        void Keyboard(AnalyticsTableViewModel vmTable)
        {
            var btnBefore = new Button { Content = "Before" }; var btnAfter = new Button { Content = "After" }; var objTable = new AnalyticsTableView { DataContext = vmTable };
            var frmKeys = new Window { Width = 1200, Height = 400, Content = new StackPanel { Children = { btnBefore, objTable, btnAfter } } }; frmKeys.Show(); frmKeys.Activate(); Dispatcher.UIThread.RunJobs(); frmKeys.UpdateLayout();
            try
            {
                var lstTable = objTable.GetVisualDescendants().OfType<ListBox>().Single(); btnBefore.Focus(); Press(frmKeys, PhysicalKey.Tab);
                Check(frmKeys.FocusManager!.GetFocusedElement() is Control objFocused && (objFocused == lstTable || objFocused.GetVisualAncestors().Contains(lstTable)), "Tab enters " + vmTable.strTitle);
                if (lstTable.Items.Count > 1) { lstTable.SelectedIndex = 0; Dispatcher.UIThread.RunJobs(); ((ListBoxItem)lstTable.ContainerFromIndex(0)!).Focus(); Press(frmKeys, PhysicalKey.ArrowDown); Check(lstTable.SelectedIndex == 1 && ReferenceEquals(frmKeys.FocusManager!.GetFocusedElement(), lstTable.ContainerFromIndex(1)), "Arrows move row focus in " + vmTable.strTitle); }
                Press(frmKeys, PhysicalKey.Tab); Check(ReferenceEquals(frmKeys.FocusManager!.GetFocusedElement(), btnAfter), "Tab leaves " + vmTable.strTitle);
            }
            finally { frmKeys.Close(); }
        }
        void FailureView()
        {
            var bFail = false;
            AnalyticsSnapshot Copy() { var objData = new AnalyticsSnapshot(DateTimeOffset.UtcNow); objData.LastContacted.Add(new(1, "PRIVATE_FAILURE_CELL", RecordStatus.Active, false, "", null)); return objData; }
            using var vmFailure = new AnalyticsViewModel(_ => bFail ? Task.FromException<AnalyticsSnapshot?>(new IOException("PRIVATE_FAILURE_CELL")) : Task.FromResult<AnalyticsSnapshot?>(Copy()), () => true);
            vmFailure.EnsureLoaded(); Complete(vmFailure.tskLoad); var objPage = new AnalyticsPageView { DataContext = vmFailure }; var frmFailure = new Window { Width = 1200, Height = 700, Content = objPage }; frmFailure.Show(); Dispatcher.UIThread.RunJobs(); frmFailure.UpdateLayout();
            try { bFail = true; Complete(vmFailure.RefreshAsync()); Dispatcher.UIThread.RunJobs(); Check(vmFailure.objSnapshot is null && vmFailure.bFailed && vmFailure.lstTables.All(vmTable => vmTable.lstRows.Count == 0)
                && !objPage.GetVisualDescendants().OfType<TextBlock>().Any(txtText => (txtText.Text ?? "").Contains("PRIVATE_FAILURE_CELL", StringComparison.Ordinal)), "Failed hydration clears the page and shows only a neutral error"); }
            finally { frmFailure.Close(); }
        }
        long AuditCount() { using var dbConnection = (SqliteConnection)typeof(Database).GetMethod("Open", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(objDatabase, null)!; using var cmdCount = dbConnection.CreateCommand(); cmdCount.CommandText = "SELECT count(*) FROM audit_log WHERE action='AccessVerified'"; return Convert.ToInt64(cmdCount.ExecuteScalar()); }
        void Check(bool bCondition, string strDescription) { if (!bCondition) throw new InvalidOperationException("FAIL " + strDescription); iChecks++; Console.WriteLine("PASS " + strDescription); }
    }
    private static void Complete(Task tskWork) => UiDevelopmentChecks.Complete(tskWork);
    private static void Press(Window frmWindow, PhysicalKey enKey) { frmWindow.KeyPressQwerty(enKey, RawInputModifiers.None); frmWindow.KeyReleaseQwerty(enKey, RawInputModifiers.None); Dispatcher.UIThread.RunJobs(); frmWindow.UpdateLayout(); }
#endif
}
