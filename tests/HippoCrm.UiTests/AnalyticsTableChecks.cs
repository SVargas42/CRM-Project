using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
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
            // Distinct word codes make one-record search assertions meaningful under Soundex.
            var objContact = new Contact { FirstName = "PAGE_CONTACT_" + iContact, LastName = iContact == 1 ? "Yarrow" : "", Phone = "+1 (312) 555-" + (1100 + iContact), Email = (iContact == 1 ? "yarrow" : iContact == 2 ? "birch" : "contact" + iContact) + "@example.test",
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
            Check(new[] { "grdGraphNotes", "grdGraphContactsPerOrg", "grdGraphCounts" }.All(strName => Find<Grid>(strName).Children.OfType<TextBlock>().Single().Text == "Graph arrives in the next update"), "Three named graph panels contain only their update message");
            Check(!Find<Expander>("expAnalyticsContracts").IsExpanded && !Find<Expander>("expAnalyticsMovement").IsExpanded
                && Find<Expander>("expAnalyticsContracts").Content is null && Find<Expander>("expAnalyticsMovement").Content is null, "E and F are collapsed with no materialized content by default");
            Check(vmAnalytics.strNotContactedCount == "2" && vmAnalytics.strNeverContactedCount == "2" && vmAnalytics.strNotesCount == "3" && vmAnalytics.strOrganizationsReachedCount == "2"
                && vmAnalytics.strNotesChange == "n/a" && vmAnalytics.strNotesPrevious == "was 0" && vmAnalytics.iPeriod == 2, "Cards use loaded contact counts and default 30-day active summaries with guarded changes");
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
            vmAnalytics.strListFilter = "Yarrow"; Layout(); Check(vmAnalytics.vmLastContacted.lstRows.Single().lngId == lstContacts[1].Id, "Shared filter finds contact names in memory");
            Check(vmAnalytics.strNotContactedCount == "2" && vmAnalytics.strNeverContactedCount == "2" && vmAnalytics.bCardsIgnoreText, "Contact cards ignore the text filter and disclose that distinction");
            Check(SoundexHelper.GetSoundexCode("Atlis") == SoundexHelper.GetSoundexCode("Atlas"), "Phonetic fixture codes match per word");
            vmAnalytics.strListFilter = "Atlis"; Layout(); Check(vmAnalytics.vmLastContacted.lstRows.Count == 2, "Shared filter matches organization words phonetically");
            vmAnalytics.strListFilter = "Beacon"; Layout(); Check(vmAnalytics.vmRecentContacts.lstRows.Single().lngId == lstContacts[2].Id && vmAnalytics.vmLastContacted.bEmpty, "Shared organization filter reaches A and B while B has no days threshold");
            vmAnalytics.strListFilter = lstContacts[2].Email; Layout(); Check(vmAnalytics.vmRecentContacts.lstRows.Single().lngId == lstContacts[2].Id, "Shared filter matches email fragments");
            vmAnalytics.strListFilter = "1101"; Layout(); Check(vmAnalytics.vmLastContacted.lstRows.Single().lngId == lstContacts[1].Id, "Shared filter matches phone fragments");
            vmAnalytics.strListFilter = "";
            var cmbStatus = Find<ComboBox>("cmbAnalyticsStatus");
            Check(cmbStatus.Items.Cast<ComboBoxItem>().Skip(1).All(objItem => !objItem.IsEnabled) && Find<TextBlock>("txtAnalyticsStatusHint").IsVisible,
                "Master off disables every non-Active dropdown choice including All statuses and shows one hint");
            vmAnalytics.iStatus = 1; Check(vmAnalytics.iStatus == 0, "Master off refuses non-Active status selection");
            vmAnalytics.bIncludeInactive = true; vmAnalytics.iStatus = 1; Layout();
            Check(vmAnalytics.vmRecentContacts.lstRows.Count == 2 && vmAnalytics.vmLastContacted.bEmpty && vmAnalytics.vmContracts.lstRows.Count == 2
                && vmAnalytics.strNeverContactedCount == "0", "One status selector filters every contact table and preserves genuine zero card values");
            vmAnalytics.iStatus = 2; Layout(); Check(vmAnalytics.vmLastContacted.lstRows.Count == 1, "Retired status remains available within master eligibility");
            vmAnalytics.iStatus = 3; Layout(); Check(vmAnalytics.vmLastContacted.lstRows.Count == 1, "Deceased status remains available within master eligibility");
            vmAnalytics.iStatus = 4; Layout(); Check(vmAnalytics.vmLastContacted.lstRows.Count == 4 && vmAnalytics.vmRecentContacts.lstRows.Count == 5, "All statuses includes every authorized lifecycle under master on");
            vmAnalytics.bIncludeInactive = false; Layout(); Check(vmAnalytics.iStatus == 0 && vmAnalytics.vmLastContacted.lstRows.Count == 2 && vmAnalytics.vmRecentContacts.lstRows.Count == 3, "Master off resets status and excludes every non-Active or archived record");
            var objBeforePeriod = vmAnalytics.vmLastContacted.lstRows;
            for (var iPeriod = 0; iPeriod < 6; iPeriod++)
            {
                vmAnalytics.iPeriod = iPeriod; Layout();
                var objPeriod = vmAnalytics.objSnapshot!.PeriodSummaries.Single(objRow => objRow.strPeriod == vmAnalytics.lstPeriods[iPeriod] && objRow.bActiveOnly && !objRow.bPrevious);
                Check(vmAnalytics.strNotesCount == objPeriod.lngNotes.ToString() && vmAnalytics.strOrganizationsReachedCount == objPeriod.lngOrganizations.ToString()
                    && ReferenceEquals(objBeforePeriod, vmAnalytics.vmLastContacted.lstRows), "Period changes cards 3 and 4 without rebuilding any list");
            }
            vmAnalytics.iPeriod = 2; NumericCards();
            vmAnalytics.iSearchType = 2; vmAnalytics.strFindSearch = ""; Layout();
            Check(vmAnalytics.vmSearch.lstRows.Count == 7 && !vmAnalytics.bSearchEmpty, "C empty search shows all eligible loaded contacts and organizations");
            vmAnalytics.strFindSearch = "hevy"; Layout(); Check(vmAnalytics.vmSearch.lstRows.Count == 4
                && vmAnalytics.vmSearch.lstRows.Where(objRow => objRow.enKind == RecordKind.Contact).All(objRow => objRow.strSummary.Contains("heavy equipment", StringComparison.Ordinal)), "C matches each skill/service phrase phonetically");
            var objLong = vmAnalytics.vmSearch.lstRows.Single(objRow => objRow.enKind == RecordKind.Contact && objRow.lngId == lstContacts[0].Id);
            Check(objLong.arrCells[2].Length == 80 && objLong.arrCells[2].EndsWith('…') && objLong.strSummary.Contains(strLongSkill + " (skill)", StringComparison.Ordinal), "Matched cell truncates at 80 while the accessible row retains the complete phrase and kind");
            vmAnalytics.strFindSearch = "Priority"; Layout(); Check(vmAnalytics.vmSearch.lstRows.Single().lngId == lstContacts[1].Id, "C includes tags-only contacts");
            vmAnalytics.iSearchType = 0; vmAnalytics.strFindSearch = "heavy"; Layout(); Check(vmAnalytics.vmSearch.lstRows.Count == 2 && vmAnalytics.vmSearch.lstRows.All(objRow => objRow.enKind == RecordKind.Contact), "C Contacts choice includes skills-only contacts");
            vmAnalytics.iSearchType = 1; vmAnalytics.strFindSearch = "Safety"; Layout(); Check(vmAnalytics.vmSearch.lstRows.Count == 2 && vmAnalytics.vmSearch.lstRows.All(objRow => objRow.enKind == RecordKind.Organization && objRow.arrCells[3] == "—"), "C Organizations choice matches tags and labels the organization column");
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
            vmAnalytics.strListFilter = lstContacts[1].Email; Layout(); Check(vmAnalytics.vmMembers.lstRows.Single().lngId == lstContacts[1].Id, "Shared filter also reaches the selected organization's members"); vmAnalytics.strListFilter = "";
            vmAnalytics.bIncludeInactive = true; vmAnalytics.iStatus = 4; Layout(); Check(vmAnalytics.vmMembers.lstRows.Count == 7 && vmAnalytics.vmContracts.lstRows.Count == 9 && vmAnalytics.vmMovement.lstRows.Count == 4, "Master and shared Status also govern D, E and F");
            vmAnalytics.strOrganizationSearch = ""; Layout(); Check(vmAnalytics.lstOrganizationChoices.Count == 3, "Master switch includes archived organizations in D");
            Check(vmAnalytics.lstOrganizationChoices.Last().strCaption.EndsWith(" (archived)", StringComparison.Ordinal), "Archived organization choices have the shared lowercase suffix");
            vmAnalytics.SelectOrganization(vmAnalytics.lstOrganizationChoices.Single(objChoice => objChoice.lngId == lstOrganizations[0].Id));
            Check(vmAnalytics.vmMovement.lstRows.All(objRow => objRow.lngId != lstContacts[9].Id), "Hidden organization placements neither qualify nor appear in Movement");
            Check(!vmAnalytics.lstTables.SelectMany(vmTable => vmTable.lstRows).Any(objRow => objRow.strSummary.Contains(lstContacts[8].FirstName, StringComparison.Ordinal) || objRow.strSummary.Contains(lstOrganizations[3].Name, StringComparison.Ordinal)), "Viewer page excludes all ungranted record names");
            vmAnalytics.bContractsExpanded = true; vmAnalytics.bMovementExpanded = true; Layout();
            LayoutAndStyles();
            foreach (var vmTable in vmAnalytics.lstTables) Keyboard(vmTable);
            Check(AuditCount() == lngBefore, "All filters, dropdown selections and keyboard table traversal add zero audited reads");
            Check(vmAnalytics.lstTables.All(vmTable => vmTable.strCount.StartsWith("Showing " + vmTable.lstRows.Count + " of ", StringComparison.Ordinal) && vmTable.strCount.EndsWith(" total)", StringComparison.Ordinal)), "Every table labels filtered, loaded and true totals");
            Check(iCountChecks > 100, "Exact counts update on every table notification across the full filter sequence");
            foreach (var strProperty in new[] { "vmLastContacted", "vmRecentContacts", "vmSearch", "vmMembers", "vmContracts", "vmMovement" })
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
            vmAnalytics.strDays = "123"; vmAnalytics.strListFilter = "Priority"; vmAnalytics.strFindSearch = "Safety"; vmAnalytics.strOrganizationSearch = "Atlas"; vmAnalytics.bOrganizationDropOpen = true; Layout();
            objUsers.SetCuiAuthorization(lngViewer, true); Dispatcher.UIThread.RunJobs(); Layout(); AssertCleared("Permission epoch");
            Complete(vmMain.ProbeCuiAuthorizationAsync()); vmAnalytics.EnsureLoaded(); Complete(vmAnalytics.tskLoad); Layout(); Check(vmAnalytics.strFindSearch == "" && vmAnalytics.strListFilter == "", "Fresh epoch load does not resurrect search text");
            vmAnalytics.strListFilter = "Priority"; vmAnalytics.strOrganizationSearch = "Atlas"; vmAnalytics.bOrganizationDropOpen = true; Layout();
            vmMain.cmdLock.Execute(null); Complete(((AsyncRelayCommand)vmMain.cmdLock).tskExecution); Layout(); AssertCleared("Lock");
            SignIn(); Load(); Check(vmAnalytics.strFindSearch == "" && vmAnalytics.strOrganizationSearch == "" && vmAnalytics.strListFilter == "" && vmAnalytics.vmLastContacted.lstRows.Count == 2, "Lock then sign-in loads fresh defaults and current authorized data");
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
                && vmAnalytics.strDays == "" && vmAnalytics.strFindSearch == "" && vmAnalytics.strOrganizationSearch == "" && vmAnalytics.strListFilter == "" && vmAnalytics.strListFilter == "",
                strReason + " clears every table, input, selection, dropdown and count model");
            Check(new[] { vmAnalytics.strNotContactedCount, vmAnalytics.strNeverContactedCount, vmAnalytics.strNotesCount, vmAnalytics.strOrganizationsReachedCount }.All(strValue => strValue == "No data")
                && vmAnalytics.strListFilter == "" && !vmAnalytics.bContractsExpanded && !vmAnalytics.bMovementExpanded
                && frmMain.GetVisualDescendants().OfType<Expander>().Where(objExpander => objExpander.Name?.StartsWith("expAnalytics", StringComparison.Ordinal) == true)
                    .All(objExpander => !objExpander.IsExpanded && objExpander.Content is null), strReason + " clears card values and shared filters and removes collapsed expander content");
            Check(lstCells.All(txtCell => string.IsNullOrEmpty(txtCell.Text)) && lstInputs.All(txtInput => string.IsNullOrEmpty(txtInput.Text))
                && lstRows.All(objRow => objRow.arrCells.All(string.IsNullOrEmpty) && objRow.strSummary == "")
                && lstTableControls.All(lstTable => lstTable.Items.Count == 0 && lstTable.SelectedItem is null), strReason + " clears retained visual cells, input controls, row summaries and native selections");
            Check(!frmMain.GetVisualDescendants().OfType<TextBlock>().Select(txtText => txtText.Text ?? "")
                    .Concat(frmMain.GetVisualDescendants().OfType<TextBox>().Select(txtInput => txtInput.Text ?? "")).Any(strValue => arrCanaries.Any(strCanary => strValue.Contains(strCanary, StringComparison.Ordinal))),
                strReason + " live visual tree contains no row-derived fixture text in any section");
        }
        void NumericCards()
        {
            var dtNow = DateTimeOffset.UtcNow;
            var objData = new AnalyticsSnapshot(dtNow);
            objData.LastContacted.Add(new(1, "NUMERIC_FIXTURE", RecordStatus.Active, false, "", null));
            objData.LastContacted.Add(new(2, "NUMERIC_FIXTURE", RecordStatus.Active, false, "", dtNow.AddDays(-10)));
            typeof(AnalyticsSnapshot).GetProperty(nameof(AnalyticsSnapshot.LastContactedTotal))!.SetValue(objData, 3L);
            objData.ContactTotals.Add(new(RecordStatus.Active, false, 2)); objData.OrganizationTotals.Add(new(false, 1));
            var arrPeriods = new[] { "24 hours", "7 days", "30 days", "6 months", "1 year", "Year to date" };
            for (var iPeriod = 0; iPeriod < 6; iPeriod++) foreach (var bActive in new[] { true, false })
            {
                var lngNow = bActive ? iPeriod == 1 ? 1 : iPeriod : 20 + iPeriod;
                var lngBefore = iPeriod == 0 ? 0 : 2;
                objData.PeriodSummaries.Add(new(arrPeriods[iPeriod], bActive, false, dtNow.AddDays(-1), dtNow, lngNow, 1, iPeriod));
                objData.PeriodSummaries.Add(new(arrPeriods[iPeriod], bActive, true, dtNow.AddDays(-2), dtNow.AddDays(-1), lngBefore, 1, 1));
            }
            using var vmCards = new AnalyticsViewModel(_ => Task.FromResult<AnalyticsSnapshot?>(objData), () => true);
            vmCards.EnsureLoaded(); Complete(vmCards.tskLoad);
            Check(vmCards.strNotContactedCount == "1" && vmCards.strNeverContactedCount == "1" && vmCards.bContactCardsCapped, "Contact cards count from loaded rows and mark a capped source");
            vmCards.strDays = "5"; Check(vmCards.strNotContactedCount == "2", "Days card includes dated and never-contacted rows at a lower threshold");
            vmCards.strDays = "3650"; Check(vmCards.strNotContactedCount == "1", "Days card retains never-contacted rows at its upper threshold");
            vmCards.strListFilter = "no-match"; Check(vmCards.vmLastContacted.bEmpty && vmCards.strNeverContactedCount == "1" && vmCards.bCardsIgnoreText, "Card counts deliberately ignore a restrictive list text filter");
            vmCards.iPeriod = 0; Check(vmCards.strNotesCount == "0" && vmCards.strNotesChange == "n/a" && vmCards.strNotesPrevious == "was 0", "Known zero period values show zero and retain the zero-divisor guard");
            vmCards.iPeriod = 1; Check(vmCards.strNotesChange == "-50.0%", "Card change supports a negative percentage");
            vmCards.iPeriod = 3; Check(vmCards.strNotesChange == "50.0%", "Card change supports a positive percentage");
            vmCards.bIncludeInactive = true; Check(vmCards.strNotesCount == "23", "Summary cards use the master-selected all-authorized variant");
            using var vmEmpty = new AnalyticsViewModel(_ => Task.FromResult<AnalyticsSnapshot?>(new AnalyticsSnapshot(dtNow)), () => true);
            vmEmpty.EnsureLoaded(); Complete(vmEmpty.tskLoad);
            Check(new[] { vmEmpty.strNotContactedCount, vmEmpty.strNeverContactedCount, vmEmpty.strNotesCount, vmEmpty.strOrganizationsReachedCount }.All(strValue => strValue == "No data"), "Empty datasets show No data rather than fabricated zero values");
        }
        void LayoutAndStyles()
        {
            var objPage = new AnalyticsPageView { DataContext = vmAnalytics };
            var frmLayout = new Window { Width = 1400, Height = 900, Content = objPage }; frmLayout.Show(); Dispatcher.UIThread.RunJobs(); frmLayout.UpdateLayout();
            try
            {
                var arrPairs = objPage.GetVisualDescendants().OfType<Grid>().Where(grdControl => grdControl.Name is "grdAnalyticsRow1" or "grdAnalyticsRow2" or "grdAnalyticsRow4").ToArray();
                Check(arrPairs.Length == 3 && arrPairs.All(grdControl => grdControl.ColumnDefinitions.Count == 2 && Grid.GetColumn(grdControl.Children[1]) == 1), "Wide dashboard pairs use equal side-by-side columns");
                frmLayout.Width = 800; Dispatcher.UIThread.RunJobs(); frmLayout.UpdateLayout();
                Check(arrPairs.All(grdControl => grdControl.ColumnDefinitions.Count == 1 && Grid.GetRow(grdControl.Children[1]) == 1), "Narrow dashboard pairs stack at the one named width threshold");
                frmLayout.Width = 1400; Dispatcher.UIThread.RunJobs(); frmLayout.UpdateLayout();
                Check(objPage.GetVisualDescendants().OfType<CrmTextBox>().All(txtInput => !string.IsNullOrWhiteSpace(txtInput.Watermark) && !string.IsNullOrWhiteSpace(AutomationProperties.GetName(txtInput)) && txtInput.Classes.Contains("crmField") && txtInput.Height == 30), "Every textbox has a visible watermark, accessible name and shared CRM field geometry");
                Check(objPage.GetVisualDescendants().OfType<ComboBox>().All(cmbInput => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(cmbInput)) && cmbInput.Classes.Contains("crmSelector") && cmbInput.Height == 30
                    && cmbInput.Parent is StackPanel pnlParent && pnlParent.Children.OfType<TextBlock>().Any(txtLabel => !string.IsNullOrWhiteSpace(txtLabel.Text))), "Dropdowns have visible labels, accessible names and shared selector geometry");
                Check(objPage.GetVisualDescendants().OfType<CheckBox>().All(chkInput => chkInput.Classes.Contains("crmCheck") && chkInput.Height == 30)
                    && objPage.GetVisualDescendants().OfType<Button>().Where(btnInput => btnInput.Name?.StartsWith("btnAnalytics", StringComparison.Ordinal) == true).All(btnInput => btnInput.Classes.Contains("crmButton") && btnInput.Height == 30), "Header checkbox and buttons use the shared 30-point geometry");
                Check(objPage.GetVisualDescendants().OfType<Border>().Count(bdrCard => bdrCard.Classes.Contains("card")) >= 14, "Metrics, filters and table or graph panels use shared card surfaces");
                var arrCells = objPage.GetVisualDescendants().OfType<TextBlock>().Where(txtCell => txtCell.GetType().Name == "AnalyticsCell").ToArray();
                Check(arrCells.Length > 0 && arrCells.All(txtCell => txtCell.DataContext is not AnalyticsDisplayRow objRow || !objRow.arrCells.Contains(txtCell.Text ?? "")
                    || objRow.arrCells.Select((strCell, iColumn) => (strCell, iColumn)).Where(objCell => objCell.strCell == txtCell.Text && objRow.arrUntrimmedColumns.ElementAtOrDefault(objCell.iColumn))
                        .All(_ => txtCell.TextTrimming == TextTrimming.None && txtCell.TextWrapping == TextWrapping.Wrap)), "Phone and email cell properties wrap full text without an ellipsis");
                foreach (var strFile in new[] { "AnalyticsPageView.cs", "AnalyticsTableView.cs" })
                {
                    var strSource = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "src", "HippoCrm.Desktop", "Views", strFile));
                    Check(!System.Text.RegularExpressions.Regex.IsMatch(strSource, @"\b(FontSize|CornerRadius|FontFamily)\s*=|\b(Height|MinHeight|MaxHeight)\s*=\s*\d|\b(Colors|Brushes)\.|#[0-9a-fA-F]{6}"), "Analytics view source contains no literal typography, color, radius or control heights");
                }
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light; Dispatcher.UIThread.RunJobs(); frmLayout.UpdateLayout();
                var bdrMetric = objPage.GetVisualDescendants().OfType<Border>().First(bdrCard => bdrCard.Classes.Contains("card")); var objLight = (bdrMetric.Background as ISolidColorBrush)?.Color;
                Preview("analytics-light.png");
                Application.Current.RequestedThemeVariant = ThemeVariant.Dark; Dispatcher.UIThread.RunJobs(); frmLayout.UpdateLayout();
                Check(frmLayout.ActualThemeVariant == ThemeVariant.Dark && objLight != (bdrMetric.Background as ISolidColorBrush)?.Color, "Loaded dashboard card brushes follow the shared light and dark theme");
                Preview("analytics-dark.png");
                Application.Current.RequestedThemeVariant = ThemeVariant.Light; Dispatcher.UIThread.RunJobs();
                void Preview(string strFile)
                {
                    try
                    {
                        var pnlDashboard = objPage.GetVisualDescendants().OfType<StackPanel>().Single(pnlControl => pnlControl.Name == "pnlAnalyticsDashboard");
                        var dblWidth = pnlDashboard.Bounds.Width; var dblHeight = pnlDashboard.DesiredSize.Height;
                        using var objBitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(dblWidth), (int)Math.Ceiling(dblHeight)));
                        objBitmap.Render(pnlDashboard); var strFolder = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "analytics-v2-dashboard"); Directory.CreateDirectory(strFolder); objBitmap.Save(Path.Combine(strFolder, strFile));
                        Console.WriteLine("Saved fictional Analytics preview: artifacts/analytics-v2-dashboard/" + strFile);
                    }
                    catch (Exception) { Console.WriteLine("Whole-page Analytics preview is unavailable."); }
                }
            }
            finally { frmLayout.Close(); }
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
