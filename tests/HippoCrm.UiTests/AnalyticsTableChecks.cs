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
        throw new InvalidOperationException("Use the isolated Debug fixture for Analytics table checks.");
#endif
    }
#if DEBUG
    private static void RunDebug()
    {
        var iChecks = 0;
        UiDevelopmentChecks.Invoke("TryEnable", (object)new[] { "--ui-development" });
        using var objDatabase = Database.CreateUiDevelopmentWorkspace();
        UiDevelopmentChecks.Invoke("ConfigureWorkspaceFactory", (Func<Database>)(() => objDatabase));
        AppBuilder.Configure<App>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        using var secPassword = TestPasswords.Secret("FICTIONAL analytics tables password!");
        var objAuth = new AuthService(objDatabase);
        using var objAdmin = objAuth.CreateAdministrator("analytics-tables-owner", secPassword);
        var objUsers = new UserAdministrationService(objDatabase, objAdmin); objUsers.SetCuiAuthorization(objAdmin.UserId, true);
        var objCrm = new CrmService(objDatabase, objAdmin);
        var objFirst = new Organization { Name = "Fixture first" }; objCrm.Save(objFirst);
        var objSecond = new Organization { Name = "Fixture archived" }; objCrm.Save(objSecond);
        var objThird = new Organization { Name = "Fixture third" }; objCrm.Save(objThird);
        var objHidden = new Organization { Name = "PRIVATE_ORGANIZATION_CANARY" }; objCrm.Save(objHidden);
        var lstContacts = new List<Contact>();
        foreach (var strName in new[] { "Fixture first", "Fixture never", "Fixture inactive", "Fixture retired", "Fixture deceased", "Fixture redacted", "PRIVATE_CONTACT_CANARY" })
        {
            var objContact = new Contact { FirstName = strName, Skills = strName == "Fixture never" ? "Logistics" : "heavy equipment, Planning",
                lstOrganizationIds = strName == "Fixture redacted" ? [objHidden.Id] : [objFirst.Id] };
            objCrm.Save(objContact); lstContacts.Add(objContact);
            objCrm.AddContract(objContact.Id, new DateOnly(2026, 1, 1), 123.45m, "", "");
        }
        foreach (var iContact in new[] { 0, 2, 3, 4, 6 }) objCrm.AddInteraction(lstContacts[iContact].Id, "FICTIONAL fixture note");
        foreach (var iContact in new[] { 0, 2 })
        {
            objCrm.AddEmployment(lstContacts[iContact].Id, objFirst.Id, "Fixture role", new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
            objCrm.AddEmployment(lstContacts[iContact].Id, objSecond.Id, "Fixture role", new DateOnly(2025, 1, 1), null);
        }
        objCrm.AddEmployment(lstContacts[5].Id, objFirst.Id, "Fixture role", new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
        objCrm.AddEmployment(lstContacts[5].Id, objHidden.Id, "Fixture role", new DateOnly(2025, 1, 1), null);
        foreach (var (iIndex, enStatus) in new[] { (2, RecordStatus.Inactive), (3, RecordStatus.Retired), (4, RecordStatus.Deceased) })
            objCrm.SetRecordStatus(RecordKind.Contact, lstContacts[iIndex].Id, lstContacts[iIndex].Revision, enStatus);
        objCrm.SetArchived(RecordKind.Organization, objSecond.Id, objSecond.Revision, true);
        var lngViewer = objUsers.CreateUser("analytics-tables-viewer", secPassword, UserRole.Viewer); objUsers.SetCuiAuthorization(lngViewer, true);
        foreach (var objContact in lstContacts.Take(6)) objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, objContact.Id, true, false);
        foreach (var objOrganization in new[] { objFirst, objSecond, objThird }) objUsers.SetRecordAccess(lngViewer, RecordKind.Organization, objOrganization.Id, true, false);
        var vmMain = new MainWindowViewModel(); Complete(vmMain.tskInitialization);
        vmMain.strUsername = "analytics-tables-viewer";
        foreach (var chrValue in "FICTIONAL analytics tables password!") vmMain.secPassword.AppendChar(chrValue);
        vmMain.cmdSignIn.Execute(null); Complete(vmMain.tskPendingOperation);
        var frmMain = new MainWindow(vmMain); frmMain.Show();
        var vmDashboard = (DashboardViewModel)vmMain.vmShell.vmCurrentPage;
        var vmAnalytics = vmDashboard.vmAnalytics;
        var lstRenderedTables = new List<AnalyticsTableView>();
        var lstRenderedInputs = new List<TextBox>();
        var lstRenderedCells = new List<TextBlock>();
        var lstRenderedRows = new List<ListBoxItem>();
        var lstRenderedStatusLabels = new List<TextBlock>();
        var iCountLabelChecks = 0;
        AnalyticsTableViewModel[] Tables() => [vmAnalytics.vmPeriodDetail, vmAnalytics.vmPeriods, vmAnalytics.vmContracts, vmAnalytics.vmLastContacted,
            vmAnalytics.vmSkills, vmAnalytics.vmOrganizations, vmAnalytics.vmMembers, vmAnalytics.vmMovement];
        try
        {
            var lngBefore = AuditCount(objDatabase);
            vmDashboard.iSelectedTab = 1; Complete(vmAnalytics.tskLoad); Layout();
            var lngLoadReads = AuditCount(objDatabase) - lngBefore;
            Check(lngLoadReads is >= 5 and <= 12 && vmAnalytics.objSnapshot is not null, "Analytics tables load one authorized snapshot with bounded surrounding audits");
            Console.WriteLine($"Analytics tables AccessVerified per load: {lngLoadReads}.");
            lngBefore = AuditCount(objDatabase);
            foreach (var vmTable in Tables()) vmTable.PropertyChanged += (_, objEvent) =>
            {
                if (objEvent.PropertyName == nameof(AnalyticsTableViewModel.strCount) && vmAnalytics.objSnapshot is not null)
                    CountLabel(vmTable);
            };
            ComparisonChecks();
            Check(vmAnalytics.vmPeriods.lstRows.Count == 6 && vmAnalytics.vmContracts.lstRows.Count == 3 && vmAnalytics.strInactiveContacts == "0"
                && vmAnalytics.strActiveContacts == "3" && vmAnalytics.strArchivedOrganizations == "0", "Combined tables and cards default to Active only");
            Check(vmAnalytics.vmPeriodDetail.lstRows[0].arrCells[1] == "1 / 0 (n/a)" && vmAnalytics.vmPeriodDetail.lstRows[0].arrCells[4] == "1.00",
                "Selected 24-hour detail computes guarded n/a and notes-per-day in the view model");
            vmAnalytics.bIncludeInactive = true; Layout();
            Check(vmAnalytics.strInactiveContacts == "1" && vmAnalytics.strRetiredContacts == "1" && vmAnalytics.strDeceasedContacts == "1"
                && vmAnalytics.strArchivedOrganizations == "1" && vmAnalytics.vmPeriodDetail.lstRows[0].arrCells[1] == "4 / 0 (n/a)",
                "Include checkbox changes total cards and the summary variant");
            vmAnalytics.iPeriod = 1; Layout();
            Check(vmAnalytics.vmPeriodDetail.lstRows[0].arrCells[0] == "7 days" && vmAnalytics.vmPeriodDetail.lstRows[0].arrCells[4] == "0.57",
                "Period selector changes detail and computes the seven-day average");
            Check(vmAnalytics.vmContracts.lstRows.All(objRow => objRow.arrCells[3] == "$123.45" && objRow.arrCells[5] == "Not tracked yet"),
                "Contract cells convert cents to dollars and label untracked current contracts");
            Render("Combined", vmAnalytics.vmPeriods, vmAnalytics.vmContracts);
            vmDashboard.iSelectedAnalyticsTab = 1; Layout();
            Check(vmAnalytics.vmLastContacted.lstRows.Count == 2 && vmAnalytics.vmLastContacted.lstRows.All(objRow => objRow.arrCells[3] == "Never" && objRow.arrCells[4] == "n/a")
                && vmAnalytics.vmLastContacted.strCount == "Showing 2 of 6 loaded (6 total)", "Days filter retains never-contacted rows and reports filtered, loaded and true counts");
            foreach (var strBad in new[] { "", "abc", "-1", "0", "3651", "12.5", " 1", "١" })
            {
                vmAnalytics.strDays = strBad; Layout();
                Check(vmAnalytics.bDaysError && vmAnalytics.strDaysError == "Enter a whole number from 1 to 3650." && vmAnalytics.vmLastContacted.lstRows.Count == 0,
                    "Days input rejects a malformed or out-of-range value without fallback");
            }
            foreach (var strGood in new[] { "1", "3650" })
            { vmAnalytics.strDays = strGood; Check(!vmAnalytics.bDaysError && vmAnalytics.vmLastContacted.lstRows.Count == 2, "Days input accepts its inclusive limits"); }
            vmAnalytics.strSkillSearch = "equipment"; Layout();
            Check(vmAnalytics.vmSkills.lstRows.Count == 2 && vmAnalytics.vmSkills.strCount == "Showing 2 of 6 loaded (6 total)", "Skill search finds a fragment in a multi-word skill");
            Check(SoundexHelper.GetSoundexCode("hevy") == SoundexHelper.GetSoundexCode("heavy") && SoundexHelper.GetSoundexCode("heavy").Length > 0,
                "Phonetic fixture computes matching per-word Soundex codes");
            vmAnalytics.strSkillSearch = "hevy"; Layout();
            Check(vmAnalytics.vmSkills.lstRows.Count == 2 && !vmAnalytics.vmSkills.lstRows.Any(objRow => objRow.arrCells.Contains("PRIVATE_CONTACT_CANARY")),
                "Skill phonetics match individual words without revealing a hidden contact");
            vmAnalytics.strSkillSearch = "zzzz-no-match"; Layout();
            Check(vmAnalytics.bNoSkills && vmAnalytics.vmSkills.lstRows.Count == 0 && vmAnalytics.vmSkills.strCount == "Showing 0 of 6 loaded (6 total)"
                && VisibleText("No skills match the selected filters."), "Unmatched skills show an explicit empty message without looser fallback");
            vmAnalytics.strSkillSearch = "";
            vmAnalytics.bInactive = true; vmAnalytics.bRetired = true; vmAnalytics.bDeceased = true; Layout();
            Check(vmAnalytics.vmSkills.lstRows.Count == 6 && vmAnalytics.vmSkills.strCount == "Showing 6 of 6 loaded (6 total)"
                && vmAnalytics.vmContracts.lstRows.Count == 6 && vmAnalytics.vmContracts.strCount == "Showing 6 of 6 loaded (6 total)",
                "All four contact status choices update skill and contract tables and labels");
            vmAnalytics.bActive = false; Layout();
            Check(vmAnalytics.vmLastContacted.lstRows.Count == 0 && vmAnalytics.vmSkills.lstRows.Count == 3 && vmAnalytics.vmContracts.lstRows.Count == 3,
                "Contact status choices also filter LastContacted alongside its days threshold");
            vmAnalytics.bActive = true; vmAnalytics.bInactive = false; vmAnalytics.bRetired = false; vmAnalytics.bDeceased = false; Layout();
            Render("Contact", vmAnalytics.vmLastContacted, vmAnalytics.vmSkills);
            Check(frmMain.GetVisualDescendants().OfType<CrmTextBox>().Where(txtInput => txtInput.Name is "txtAnalyticsDays" or "txtAnalyticsSkill")
                .All(txtInput => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(txtInput)) && !txtInput.IsUndoEnabled),
                "Analytics text inputs have accessible names and no plaintext undo buffers");
            vmDashboard.iSelectedAnalyticsTab = 2; Layout();
            Check(vmAnalytics.vmOrganizations.lstRows.Count == 2 && vmAnalytics.bSelectOrganization && VisibleText("Select an organization"),
                "Organization tab defaults to Active and prompts for a selection");
            vmAnalytics.vmOrganizations.objSelectedRow = vmAnalytics.vmOrganizations.lstRows.Single(objRow => objRow.lngId == objFirst.Id); Layout();
            Check(vmAnalytics.vmMembers.lstRows.Count == 2 && !vmAnalytics.bSelectOrganization
                && vmAnalytics.vmMembers.strCount == "Showing 2 of 5 loaded (5 total)", "Organization selection filters loaded authorized members in memory");
            Check(VisibleText("Showing contacts with status: Active"), "Organization tab visibly reports the shared contact status selection");
            vmAnalytics.bInactive = true; Layout();
            Check(vmAnalytics.vmMembers.lstRows.Count == 3 && vmAnalytics.vmMovement.lstRows.Count == 4,
                "Contact status choices apply independently to member and movement lists");
            Check(VisibleText("Showing contacts with status: Active, Inactive"), "Organization status summaries track the same checkbox state");
            vmAnalytics.bInactive = false;
            Check(vmAnalytics.vmMovement.lstRows.Count == 2 && vmAnalytics.vmMovement.lstRows.All(objRow => objRow.lngId == lstContacts[0].Id)
                && vmAnalytics.vmMovement.lstRows.Any(objRow => objRow.arrCells[1].EndsWith(" (Archived)", StringComparison.Ordinal)),
                "Movement shows authorized multi-placement contacts and identifies archived organizations");
            vmAnalytics.iOrganizationLifecycle = 1; Layout();
            Check(vmAnalytics.vmOrganizations.lstRows.Count == 1 && vmAnalytics.vmOrganizations.strCount == "Showing 1 of 3 loaded (3 total)"
                && vmAnalytics.bSelectOrganization && vmAnalytics.vmMembers.lstRows.Count == 0,
                "Archived choice filters the flag alone and clears a selection outside that choice");
            vmAnalytics.bActive = false; Layout();
            Check(vmAnalytics.bNoMovement && VisibleText("No movement history yet. Add placements in a contact's Employment tab."),
                "Movement has the requested explicit empty message");
            vmAnalytics.bActive = true; vmAnalytics.iOrganizationLifecycle = 0;
            vmAnalytics.vmOrganizations.objSelectedRow = vmAnalytics.vmOrganizations.lstRows.Single(objRow => objRow.lngId == objFirst.Id); Layout();
            Render("Organization", vmAnalytics.vmOrganizations, vmAnalytics.vmMembers, vmAnalytics.vmMovement);
            Check(AuditCount(objDatabase) == lngBefore, "Every filter, period, status and organization selection above makes zero protected reads");

            foreach (var vmTable in Tables())
            {
                // Roster arrow navigation changes the real selection; restore a populated
                // organization before exercising the non-empty members keyboard contract.
                if (ReferenceEquals(vmTable, vmAnalytics.vmMembers))
                {
                    vmAnalytics.vmOrganizations.objSelectedRow = vmAnalytics.vmOrganizations.lstRows.Single(objRow => objRow.lngId == objFirst.Id);
                    Layout(); Check(vmTable.lstRows.Count > 1, "Members keyboard fixture selects an organization with multiple visible members");
                }
                Keyboard(vmTable);
            }
            vmAnalytics.vmOrganizations.objSelectedRow = vmAnalytics.vmOrganizations.lstRows.Single(objRow => objRow.lngId == objThird.Id); Layout();
            Check(vmAnalytics.vmMembers.lstRows.Count == 0 && vmAnalytics.bNoMembers && VisibleText("No members match the selected contact statuses."),
                "Selecting an organization without members displays an explicit empty-members message");
            Keyboard(vmAnalytics.vmMembers, bEmpty: true);
            vmAnalytics.vmOrganizations.objSelectedRow = vmAnalytics.vmOrganizations.lstRows.Single(objRow => objRow.lngId == objFirst.Id); Layout();
            Check(AuditCount(objDatabase) == lngBefore, "Table keyboard navigation and roster selection make zero protected reads");
            Check(iCountLabelChecks > 100, "Exact filtered, loaded and total labels are checked on each table count notification after filter changes");
            // Hold nondefault input and data-bound controls from every subtab to test
            // detached as well as currently visible presentation objects after locking.
            vmAnalytics.strDays = "123"; vmAnalytics.strSkillSearch = "equipment";
            for (var iTab = 0; iTab < 3; iTab++) { vmDashboard.iSelectedAnalyticsTab = iTab; Layout(); }
            var lstRowsBeforeLock = Tables().SelectMany(vmTable => vmTable.lstRows).ToArray();
            Check(lstRenderedInputs.Any(txtInput => txtInput.Name == "txtAnalyticsDays" && txtInput.Text == "123")
                && lstRenderedInputs.Any(txtInput => txtInput.Name == "txtAnalyticsSkill" && txtInput.Text == "equipment"),
                "Lock fixture holds nondefault days and skill-search text in rendered inputs");
            vmMain.cmdLock.Execute(null); Complete(((AsyncRelayCommand)vmMain.cmdLock).tskExecution); Layout();
            Check(vmAnalytics.objSnapshot is null && vmAnalytics.vmPeriods.lstRows.Count == 0 && vmAnalytics.vmSkills.lstRows.Count == 0
                && vmAnalytics.vmOrganizations.lstRows.Count == 0 && vmAnalytics.vmContracts.lstRows.Count == 0
                && lstRowsBeforeLock.All(objRow => string.IsNullOrWhiteSpace(objRow.strSummary.Replace(";", ""))) && vmAnalytics.strSkillSearch == "",
                "Lock clears snapshot, every displayed row projection and skill-search text");
            Check(Tables().All(vmTable => vmTable.lstRows.Count == 0 && vmTable.objSelectedRow is null && vmTable.strCount == "")
                && lstRowsBeforeLock.All(objRow => objRow.arrCells.All(string.IsNullOrEmpty))
                && vmAnalytics.strDays == "" && vmAnalytics.strDaysError == "" && vmAnalytics.strShownContactStatuses == ""
                && new[] { vmAnalytics.strActiveContacts, vmAnalytics.strInactiveContacts, vmAnalytics.strRetiredContacts,
                    vmAnalytics.strDeceasedContacts, vmAnalytics.strActiveOrganizations, vmAnalytics.strArchivedOrganizations }.All(string.IsNullOrEmpty),
                "Lock clears all eight table models, cells, dynamic labels, selection, days and search presentation");
            Console.WriteLine($"Analytics lock diagnostic: retainedTablesWithRows={lstRenderedTables.Distinct().Count(objTable => objTable.GetVisualDescendants().OfType<ListBox>().Any(lstTable => lstTable.Items.Count != 0 || lstTable.SelectedItem is not null))}, retainedCounts={lstRenderedTables.Distinct().SelectMany(objTable => objTable.GetVisualDescendants().OfType<TextBlock>()).Count(txtText => txtText.Name == "txtAnalyticsRowCount" && !string.IsNullOrEmpty(txtText.Text))}, retainedCells={lstRenderedCells.Distinct().Count(txtText => !string.IsNullOrEmpty(txtText.Text))}, retainedSummaries={lstRenderedRows.Distinct().Count(objRow => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(objRow)))}, retainedInputs={lstRenderedInputs.Distinct().Count(txtInput => !string.IsNullOrEmpty(txtInput.Text))}, retainedLabels={lstRenderedStatusLabels.Distinct().Count(txtText => !string.IsNullOrEmpty(txtText.Text))}.");
            Check(lstRenderedTables.Distinct().All(objTable => objTable.GetVisualDescendants().OfType<ListBox>()
                    .All(lstTable => lstTable.Items.Count == 0 && lstTable.SelectedItem is null)
                && objTable.GetVisualDescendants().OfType<TextBlock>().Where(txtText => txtText.Name == "txtAnalyticsRowCount").All(txtText => string.IsNullOrEmpty(txtText.Text)))
                && lstRenderedCells.All(txtText => string.IsNullOrEmpty(txtText.Text))
                && lstRenderedRows.All(objRow => string.IsNullOrWhiteSpace(AutomationProperties.GetName(objRow)))
                && lstRenderedInputs.All(txtInput => string.IsNullOrEmpty(txtInput.Text))
                && lstRenderedStatusLabels.All(txtText => string.IsNullOrEmpty(txtText.Text)),
                "Lock clears realized and retained table controls, row summaries, cells, count labels, inputs and status summaries");
            Console.WriteLine($"{iChecks} Analytics table checks passed.");
        }
        finally { Complete(vmMain.CloseAsync()); frmMain.Close(); objAuth.SignOut(objAdmin); }

        void Layout()
        {
            Dispatcher.UIThread.RunJobs(); frmMain.UpdateLayout();
            lstRenderedTables.AddRange(frmMain.GetVisualDescendants().OfType<AnalyticsTableView>());
            lstRenderedInputs.AddRange(frmMain.GetVisualDescendants().OfType<TextBox>().Where(txtInput => txtInput.Name is "txtAnalyticsDays" or "txtAnalyticsSkill"));
            lstRenderedCells.AddRange(frmMain.GetVisualDescendants().OfType<TextBlock>().Where(txtCell => txtCell.DataContext is AnalyticsDisplayRow));
            lstRenderedRows.AddRange(frmMain.GetVisualDescendants().OfType<ListBoxItem>().Where(objRow => objRow.DataContext is AnalyticsDisplayRow));
            lstRenderedStatusLabels.AddRange(frmMain.GetVisualDescendants().OfType<TextBlock>().Where(txtText => txtText.Name is "txtAnalyticsMemberStatuses" or "txtAnalyticsMovementStatuses"
                or "txtAnalyticsActiveContacts" or "txtAnalyticsInactiveContacts" or "txtAnalyticsRetiredContacts" or "txtAnalyticsDeceasedContacts" or "txtAnalyticsActiveOrganizations" or "txtAnalyticsArchivedOrganizations"));
        }
        void CountLabel(AnalyticsTableViewModel vmTable)
        {
            var objData = vmAnalytics.objSnapshot!;
            var (iLoaded, lngTotal) = ReferenceEquals(vmTable, vmAnalytics.vmPeriods) || ReferenceEquals(vmTable, vmAnalytics.vmPeriodDetail) ? (6, 6L)
                : ReferenceEquals(vmTable, vmAnalytics.vmContracts) ? (objData.LatestContracts.Count, objData.LatestContractsTotal)
                : ReferenceEquals(vmTable, vmAnalytics.vmLastContacted) ? (objData.LastContacted.Count, objData.LastContactedTotal)
                : ReferenceEquals(vmTable, vmAnalytics.vmSkills) ? (objData.SkillRoster.Count, objData.SkillRosterTotal)
                : ReferenceEquals(vmTable, vmAnalytics.vmOrganizations) ? (objData.OrganizationRoster.Count, objData.OrganizationRosterTotal)
                : ReferenceEquals(vmTable, vmAnalytics.vmMembers) ? (objData.OrganizationMembers.Count, objData.OrganizationMembersTotal)
                : (objData.Movement.Count, objData.MovementTotal);
            if (vmTable.strCount != $"Showing {vmTable.lstRows.Count} of {iLoaded} loaded ({lngTotal} total)")
                throw new InvalidOperationException("FAIL exact Analytics count label after filter change");
            iCountLabelChecks++;
        }
        void ComparisonChecks()
        {
            // Numeric-only presentation fixtures exercise real display formatting without
            // backdating or altering records, Core services, or the protected workspace.
            foreach (var (lngNow, strExpected) in new[] { (3L, "3 / 2 (50.0%)"), (1L, "1 / 2 (-50.0%)") })
            {
                var dtNow = DateTimeOffset.UtcNow;
                var objNumeric = new AnalyticsSnapshot(dtNow);
                objNumeric.PeriodSummaries.Add(new("24 hours", true, false, dtNow.AddDays(-1), dtNow, lngNow, 0, 0));
                objNumeric.PeriodSummaries.Add(new("24 hours", true, true, dtNow.AddDays(-2), dtNow.AddDays(-1), 2, 0, 0));
                using var vmNumeric = new AnalyticsViewModel(_ => Task.FromResult<AnalyticsSnapshot?>(objNumeric), () => true);
                vmNumeric.EnsureLoaded(); Complete(vmNumeric.tskLoad);
                Check(vmNumeric.vmPeriodDetail.lstRows[0].arrCells[1] == strExpected
                    && vmNumeric.vmPeriods.lstRows[0].arrCells[1] == strExpected,
                    "Nonzero previous value formats the signed percentage change in detail and comparison tables");
            }
        }
        bool VisibleText(string strText) => frmMain.GetVisualDescendants().OfType<TextBlock>().Any(txtText => txtText.Text == strText && txtText.IsEffectivelyVisible);
        void Render(string strTab, params AnalyticsTableViewModel[] arrTables)
        {
            Layout();
            foreach (var vmTable in arrTables)
            {
                var lstControl = frmMain.GetVisualDescendants().OfType<ListBox>().Single(lstControl => lstControl.DataContext == vmTable && lstControl.IsEffectivelyVisible);
                Check(lstControl.Items.Count == vmTable.lstRows.Count && AutomationProperties.GetName(lstControl) == vmTable.strTitle,
                    strTab + " renders an accessible snapshot-backed table");
            }
        }
        void Keyboard(AnalyticsTableViewModel vmTable, bool bEmpty = false)
        {
            var btnBefore = new Button { Content = "Before table" }; var btnAfter = new Button { Content = "After table" };
            var objTable = new AnalyticsTableView { DataContext = vmTable };
            var frmKeyboard = new Window { Width = 1200, Height = 500, Content = new StackPanel { Children = { btnBefore, objTable, btnAfter } } };
            frmKeyboard.Show(); frmKeyboard.Activate(); Dispatcher.UIThread.RunJobs(); frmKeyboard.UpdateLayout();
            try
            {
                var lstControl = objTable.GetVisualDescendants().OfType<ListBox>().Single();
                btnBefore.Focus(); Press(PhysicalKey.Tab);
                if (ReferenceEquals(vmTable, vmAnalytics.vmMembers))
                {
                    var objFocused = frmKeyboard.FocusManager!.GetFocusedElement();
                    Console.WriteLine($"Analytics focus diagnostic: rosterRows={vmAnalytics.vmOrganizations.lstRows.Count}, selectedMemberRows={vmAnalytics.vmMembers.lstRows.Count}, focusedType={objFocused?.GetType().Name ?? "none"}, focusedName={(objFocused as Control)?.Name ?? "unnamed"}.");
                }
                if (bEmpty)
                {
                    Check(lstControl.Items.Count == 0 && ReferenceEquals(frmKeyboard.FocusManager!.GetFocusedElement(), btnAfter),
                        "Empty members table does not trap forward Tab navigation");
                    frmKeyboard.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Shift); frmKeyboard.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
                    Dispatcher.UIThread.RunJobs();
                    Check(ReferenceEquals(frmKeyboard.FocusManager!.GetFocusedElement(), btnBefore), "Empty members table does not trap backward Tab navigation");
                    return;
                }
                Check(InTable(), "Tab enters " + vmTable.strTitle);
                if (lstControl.Items.Count > 1)
                {
                    lstControl.SelectedIndex = 0; Dispatcher.UIThread.RunJobs(); frmKeyboard.UpdateLayout();
                    ((ListBoxItem)lstControl.ContainerFromIndex(0)!).Focus(); Press(PhysicalKey.ArrowDown);
                    Check(lstControl.SelectedIndex == 1 && ReferenceEquals(frmKeyboard.FocusManager!.GetFocusedElement(), lstControl.ContainerFromIndex(1)),
                        "Arrow key moves row focus in " + vmTable.strTitle);
                    var objItem = (ListBoxItem)lstControl.ContainerFromIndex(1)!;
                    Check(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(objItem)), "Row container summarizes " + vmTable.strTitle);
                }
                Press(PhysicalKey.Tab);
                Check(ReferenceEquals(frmKeyboard.FocusManager!.GetFocusedElement(), btnAfter), "Tab leaves " + vmTable.strTitle);
                void Press(PhysicalKey enKey) { frmKeyboard.KeyPressQwerty(enKey, RawInputModifiers.None); frmKeyboard.KeyReleaseQwerty(enKey, RawInputModifiers.None); Dispatcher.UIThread.RunJobs(); frmKeyboard.UpdateLayout(); }
                bool InTable() => frmKeyboard.FocusManager!.GetFocusedElement() is Control objFocused
                    && (objFocused == lstControl || objFocused.GetVisualAncestors().Contains(lstControl));
            }
            finally { frmKeyboard.Close(); }
        }
        void Check(bool bCondition, string strDescription)
        { if (!bCondition) throw new InvalidOperationException("FAIL " + strDescription); iChecks++; Console.WriteLine("PASS " + strDescription); }
    }
    private static long AuditCount(Database objDatabase)
    {
        using var dbConnection = (SqliteConnection)typeof(Database).GetMethod("Open", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(objDatabase, null)!;
        using var cmdCount = dbConnection.CreateCommand(); cmdCount.CommandText = "SELECT count(*) FROM audit_log WHERE action='AccessVerified'";
        return Convert.ToInt64(cmdCount.ExecuteScalar());
    }
    private static void Complete(Task tskWork) => UiDevelopmentChecks.Complete(tskWork);
#endif
}
