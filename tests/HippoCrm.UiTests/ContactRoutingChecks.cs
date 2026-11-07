using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HippoCrm.Core;
using HippoCrm.Desktop;
using HippoCrm.Desktop.ViewModels;
using HippoCrm.Desktop.Views;
using HippoCrm.Desktop.Utilities;
using HippoCrm.TestSupport;
using Microsoft.Data.Sqlite;

internal static class ContactRoutingChecks
{
    public static void Run()
    {
        var typDevelopment = typeof(App).Assembly.GetType("HippoCrm.Desktop.DebugUiDevelopment")!;
        var enNonPublicStatic = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        typDevelopment.GetMethod("TryEnable", enNonPublicStatic)!.Invoke(null, [new[] { "--ui-development" }]);
        var strFixtureRoot = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
            "HippoCrmContactRouting", Guid.NewGuid().ToString("N"));
        var strFixtureDirectory = Path.Combine(strFixtureRoot, "UiDevelopment");
        using var objKeyStore = new EphemeralKeyStore();
        var objDatabase = Database.OpenUiDevelopmentWorkspace(strFixtureDirectory, objKeyStore);
        typDevelopment.GetMethod("ConfigureWorkspaceFactory", enNonPublicStatic)!
            .Invoke(null, [new Func<Database>(() => objDatabase)]);
        AppBuilder.Configure<App>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var objAuthentication = new AuthService(objDatabase);
        using var objFixtureSession = objAuthentication.CreateAdministratorForTest("admin", "Local routing test password!");
        new UserAdministrationService(objDatabase, objFixtureSession).SetCuiAuthorization(objFixtureSession.UserId, true);
        var objCrm = new CrmService(objDatabase, objFixtureSession);
        var lngOrganizationId = objCrm.Save(new Organization { Name = "Routing Organization", Phone = "+1 (202) 555-0200", Services = "Local logistics" });
        var lngSecondOrganizationId = objCrm.Save(new Organization { Name = "Second Organization" });
        objCrm.Save(new Contact { FirstName = "Alex", LastName = "Rivera", lngOrganizationId = lngOrganizationId,
            Email = "local@example.test", Phone = "+1 (202) 555-0100" });
        if (!OperatingSystem.IsMacOS())
        {
            // Downgrade only this disposable fixture to the preceding schema, then exercise the actual migration.
            using var dbFixture = (SqliteConnection)typeof(Database).GetMethod("Open",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(objDatabase, null)!;
            using var cmdFixture = dbFixture.CreateCommand();
            cmdFixture.CommandText = "DROP TABLE contact_organizations; ALTER TABLE contacts DROP COLUMN tags; ALTER TABLE contacts DROP COLUMN middle_names; ALTER TABLE contacts DROP COLUMN prefix; ALTER TABLE contacts DROP COLUMN suffix; ALTER TABLE contacts DROP COLUMN social_media; UPDATE schema_version SET version=3;";
            cmdFixture.ExecuteNonQuery();
            _ = new Database(objDatabase.Path, objKeyStore);
        }
        var objMigratedContact = objCrm.Contacts().Single();
        using (var secViewerPassword = TestPasswords.Secret("Local viewer routing password!"))
            new UserAdministrationService(objDatabase, objFixtureSession).CreateUser("viewer", secViewerPassword, UserRole.Viewer);
        objDatabase.SaveDarkTheme(true);
        var vmMain = new MainWindowViewModel() { strAccount = "admin" };
        WaitForTask(vmMain.tskInitialization);
        var frmMain = new MainWindow(vmMain);
        var iChecks = 0;
        var hashButtonLayouts = new HashSet<string>(StringComparer.Ordinal);
        var hashTextBoxLayouts = new HashSet<string>(StringComparer.Ordinal);
        frmMain.Show(); Layout();
        Check(vmMain.bNeedsUsername && Visible<TextBox>().Any(txtControl => txtControl.Name == "txtAccount"),
            "Multi-account unlock asks for the username as well as the password");
        var btnAuthenticate = Visible<Button>().Single(btnControl => btnControl.Name == "btnAuthenticate");
        Check(Math.Abs(btnAuthenticate.Bounds.Height - 30) < 1 && Math.Abs(btnAuthenticate.Bounds.Width - 96) < 1,
            "Sign-in action is compact and aligned to the shared control height");
        var btnSidebarToggle = frmMain.FindControl<Button>("btnSidebarToggle")!;
        var objExpandedArrow = frmMain.FindControl<PathIcon>("icoSidebarToggle")!.Data;
        btnSidebarToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
        Check(frmMain.FindControl<Border>("bdrSidebar")!.Width == 88
            && !frmMain.FindControl<TextBlock>("txtNavContacts")!.IsVisible
            && !ReferenceEquals(frmMain.FindControl<PathIcon>("icoSidebarToggle")!.Data, objExpandedArrow),
            "Sidebar collapses to an icon rail and reverses its chevron");
        btnSidebarToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
        Check(frmMain.FindControl<Border>("bdrSidebar")!.Width == 232
            && frmMain.FindControl<TextBlock>("txtNavContacts")!.IsVisible,
            "Sidebar expands and restores its labels");
        var bNotificationOffUiThread = false;
        vmMain.PropertyChanged += (objSender, objArguments) => bNotificationOffUiThread |= !Dispatcher.UIThread.CheckAccess();
        foreach (var chrCharacter in "Local routing test password!") vmMain.secPassword.AppendChar(chrCharacter);
        vmMain.cmdSignIn.Execute(null);
        WaitForTask(vmMain.tskPendingOperation);
        Layout();
        Check(!Visible<TextBlock>().Any(txtBlock => txtBlock.Text is "SIGNED IN" or "admin"),
            "The signed-in username is not displayed in the workspace");
        vmMain.fnConfirmDiscard = () => Task.FromResult(true);
        try
        {
            foreach (var (strName, strExpected) in new[]
            {
                ("Robert", "R163"), ("Rupert", "R163"), ("Rubin", "R150"), ("Ashcraft", "A261"),
                ("Ashcroft", "A261"), ("Pfister", "P236"), ("Tymczak", "T522"), ("Honeyman", "H555"),
                ("A", "A000"), ("BWB", "B000"), ("BAB", "B100"), ("  robert  ", "R163")
            }) Check(SoundexHelper.GetSoundexCode(strName) == strExpected, "Soundex vector: " + strName);
            Check(SoundexHelper.GetSoundexCode(null) == "" && SoundexHelper.GetSoundexCode("   ") == ""
                && SoundexHelper.GetSoundexCode("123--") == "", "Inputs without English letters have no phonetic match code");
            Check(vmMain.bDark && Application.Current!.RequestedThemeVariant == ThemeVariant.Dark,
                "Startup restores the saved dark theme explicitly");
            Check(!vmMain.vmShell.bShowGlobalBack && !Visible<Button>().Any(btnControl => btnControl.Name == "btnGlobalBack"),
                "Global Back is hidden on the dashboard");
            vmMain.bDark = false; WaitForTask(vmMain.tskThemeSave);
            var vmReopened = new MainWindowViewModel();
            WaitForTask(vmReopened.tskInitialization);
            Check(!vmReopened.bDark && !objDatabase.ReadDarkTheme(), "Light theme persists across workspace reopening");
            WaitForTask(vmReopened.CloseAsync());
            vmMain.bDark = true; vmMain.bDark = false; vmMain.bDark = true;
            WaitForTask(vmMain.tskThemeSave);
            Check(objDatabase.ReadDarkTheme(), "Rapid theme changes persist the final selection in order");
            Check(objMigratedContact.lstOrganizationIds.SequenceEqual(new[] { lngOrganizationId }) && objMigratedContact.strTags == "",
                "Version 3 migration preserves the existing organization assignment");
            vmMain.vmShell.cmdOrganizations.Execute(null); Layout();
            var vmOrganizations = (OrganizationsPageViewModel)vmMain.vmShell.vmCurrentPage;
            Check(vmOrganizations.iSelectedTab == 0 && !vmOrganizations.bProfile && vmOrganizations.lstRows.Count == 2,
                "Organizations opens its Lookup tab with authorized records");
            Check(!vmMain.bCanGoToNextLookupPage
                && !Visible<Button>().Single(btnControl => AutomationProperties.GetName(btnControl) == "Next organizations page").IsEnabled,
                "Organizations lookup disables Next when there is no additional page");
            vmMain.cmdNextLookupPage.Execute(null); Layout();
            Check(vmMain.strLookupPageScope.StartsWith("Page 1", StringComparison.Ordinal),
                "Organizations cannot be advanced to an empty second page");
            Check(!vmMain.vmShell.bShowGlobalBack, "Global Back is hidden on Organizations tabs");
            var txtOrganizationSearch = Visible<TextBox>().Single(txtControl => txtControl.Name == "txtOrganizationLookupSearch");
            var cboOrganizationStatus = Visible<ComboBox>().Single(cboControl => cboControl.Name == "cboOrganizationVisibilityFilter");
            Check(Math.Abs(txtOrganizationSearch.Bounds.Width - cboOrganizationStatus.Bounds.Width) <= 1,
                $"Organization lookup text and status controls use equal widths ({txtOrganizationSearch.Bounds.Width} vs {cboOrganizationStatus.Bounds.Width})");
            Check(Math.Abs(txtOrganizationSearch.Bounds.Height - cboOrganizationStatus.Bounds.Height) < 1,
                $"Organization lookup fields use equal heights ({txtOrganizationSearch.Bounds.Height} vs {cboOrganizationStatus.Bounds.Height})");
            Check(Math.Abs(txtOrganizationSearch.Bounds.Height - 30) < 1,
                $"New CRM text fields use the shared 30-point desktop height ({txtOrganizationSearch.Bounds.Height})");
            Visible<TextBox>().Single(txtControl => txtControl.Name == "txtOrganizationLookupSearch").Text = "Rooting"; Layout();
            Check(vmOrganizations.lstRows.Single().strName == "Routing Organization", "Organization Lookup supports Soundex search");
            vmOrganizations.strSearchQuery = "+1 (202) 555-0200"; Layout();
            Check(vmOrganizations.lstRows.Single().strName == "Routing Organization", "Organization Lookup supports phone text search");
            Check(vmOrganizations.strFilterSummary.Contains("Filters applied", StringComparison.Ordinal)
                && vmOrganizations.strFilterSummary.Contains("Search: +1 (202) 555-0200", StringComparison.Ordinal),
                "Organization lookup summarizes its active search filter");
            Check(Visible<TextBlock>().Single(txtControl => txtControl.Name == "txtOrganizationFilterSummary").Text == vmOrganizations.strFilterSummary,
                "Organization filter summary is visible on the lookup page");
            vmOrganizations.strSearchQuery = ""; vmOrganizations.iVisibilityFilter = 1; Layout();
            Check(vmOrganizations.lstRows.Count == 0, "Organization status filter excludes other statuses");
            Check(vmOrganizations.bShowEmptyState
                && Visible<Border>().Any(bdrControl => bdrControl.Name == "bdrOrganizationEmptyState"),
                "Organization lookup explains when filters return no results");
            vmOrganizations.iVisibilityFilter = 0; Layout();
            Check(!vmOrganizations.bShowEmptyState, "Organization empty state clears when results return");
            vmOrganizations.lstRows.Single(vmRow => vmRow.strName == "Routing Organization").cmdViewProfile.Execute(null); Layout();
            Check(vmOrganizations.bProfile && vmMain.objOrganization?.Id == lngOrganizationId, "View Profile routes the selected organization");
            Check(Visible<Button>().Any(btnControl => btnControl.Name == "btnGlobalBack")
                && !Visible<OrganizationProfileView>().Single().GetVisualDescendants().OfType<Button>().Any(btnControl => Equals(btnControl.Content, "Back")),
                "Organization profile uses only the global header Back button");
            Check(Visible<OrganizationProfileView>().Any() && Visible<Border>().Any(brdControl =>
                brdControl.Name == "brdOrganizationLogoPlaceholder" && brdControl.Width == 88 && brdControl.Height == 88),
                "Organization profile renders its styled logo placeholder");
            Check(Visible<Button>().Any(btnControl => btnControl.Name == "btnNavOrganizations"), "Global sidebar remains visible in organization profile");
            GlobalBack();
            Check(!vmOrganizations.bProfile && vmOrganizations.iSelectedTab == 0, "Organization Back restores its Lookup tab");
            vmOrganizations.iSelectedTab = 1; Layout();
            Check(vmMain.objOrganization is { Id: 0 } && !Visible<TextBox>().Any(txtControl => txtControl.Name == "txtOrganizationLookupSearch"),
                "Add Organization starts a draft without search controls");
            Check(!vmMain.vmShell.bShowGlobalBack, "Global Back stays hidden on Add Organization");
            Visible<TextBox>().Single(txtControl => txtControl.Name == "txtOrganizationName").Text = "Beacon Systems";
            Visible<TextBox>().Single(txtControl => txtControl.Name == "txtNewOrganizationPhone").Text = "+1 (202) 555-0300";
            Visible<Button>().Single(btnControl => btnControl.Name == "btnAddOrganizationPhone").Command!.Execute(null);
            vmMain.ApplyServices(vmMain.objOrganization, ["Field support"]);
            Check(!Visible<ComboBox>().Any(cboControl => cboControl.Name == "cboOrganizationStatus"), "Organization form has no stored-status selector");
            var btnSaveOrganization = Visible<Button>().Single(btnControl => btnControl.Name == "btnSaveOrganization");
            Check(btnSaveOrganization.Parent is StackPanel pnlOrganizationEditor && ReferenceEquals(pnlOrganizationEditor.Children.Last(), btnSaveOrganization),
                "Save Organization is at the bottom of its form");
            btnSaveOrganization.Command!.Execute(null); Layout();
            Check(vmOrganizations.bProfile && objCrm.Organizations().Any(objOrganization => objOrganization.Name == "Beacon Systems"
                && objOrganization.Phone == "+1 (202) 555-0300" && objOrganization.Status == RecordStatus.Active),
                "Organization save persists through the existing service and opens the profile");
            GlobalBack();
            vmOrganizations.iVisibilityFilter = 0; vmOrganizations.strSearchQuery = "Beacon Systems"; Layout();
            Check(vmOrganizations.lstRows.Single().strName == "Beacon Systems", "Active organization lookup includes the new record");
            vmOrganizations.lstRows.Single().cmdViewProfile.Execute(null); Layout();
            vmMain.cmdArchiveOrganization.Execute(null); Layout();
            Check(!vmOrganizations.bProfile && objCrm.Organizations(bArchived: true).Any(objOrganization => objOrganization.Name == "Beacon Systems"),
                "Organization archive preserves the record and returns to Lookup");
            vmMain.bArchived = true; vmMain.cmdApplyFilters.Execute(null); Layout();
            Check(vmOrganizations.lstRows.Single().strName == "Beacon Systems", "Archived organization filter loads matching records");
            vmOrganizations.lstRows.Single().cmdViewProfile.Execute(null); Layout();
            vmMain.cmdArchiveOrganization.Execute(null); Layout();
            Check(objCrm.Organizations().Any(objOrganization => objOrganization.Name == "Beacon Systems"), "Organization restore reuses the existing authorized command");
            vmMain.vmShell.cmdContacts.Execute(null); Layout();
            var vmContacts = (ContactsPageViewModel)vmMain.vmShell.vmCurrentPage;
            var semWork = (SemaphoreSlim)typeof(MainWindowViewModel).GetField("semDatabaseWork",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(vmMain)!;
            semWork.Wait();
            try
            {
                vmMain.cmdApplyFilters.Execute(null);
                var tskFirstSearch = vmMain.tskPendingOperation;
                var bHeartbeat = false;
                Dispatcher.UIThread.Post(() => bHeartbeat = true); Dispatcher.UIThread.RunJobs();
                Check(bHeartbeat && vmMain.bBusy && !tskFirstSearch.IsCompleted, "UI dispatcher remains responsive while database work is pending");
                Thread.Sleep(250); Dispatcher.UIThread.RunJobs();
                Check(Visible<TextBlock>().Any(txtControl => txtControl.Name == "txtWorkspaceBusy")
                    && Visible<ProgressBar>().Any(), "Pending database work displays a visible loading status");
                vmMain.cmdApplyFilters.Execute(null);
                Check(ReferenceEquals(tskFirstSearch, vmMain.tskPendingOperation), "Busy commands cannot queue duplicate searches");
            }
            finally { semWork.Release(); }
            Layout();
            Check(vmContacts.iSelectedTab == 0 && !vmContacts.bProfile, "Contacts opens Lookup");
            Check(!vmMain.bCanGoToNextLookupPage
                && !Visible<Button>().Single(btnControl => AutomationProperties.GetName(btnControl) == "Next contacts page").IsEnabled,
                "Contacts lookup disables Next when there is no additional page");
            vmMain.cmdNextLookupPage.Execute(null); Layout();
            Check(vmMain.strLookupPageScope.StartsWith("Page 1", StringComparison.Ordinal),
                "Contacts cannot be advanced to an empty second page");
            Check(!vmMain.vmShell.bShowGlobalBack, "Global Back is hidden on Contacts tabs");
            var txtContactSearch = Visible<TextBox>().Single(txtControl => txtControl.Name == "txtContactSearch");
            var txtContactSkill = Visible<TextBox>().Single(txtControl => txtControl.Name == "txtContactSkill");
            var cboContactAvailability = Visible<ComboBox>().Single(cboControl => cboControl.Name == "cboContactAvailability");
            Check(new[] { txtContactSearch.Bounds.Width, txtContactSkill.Bounds.Width, cboContactAvailability.Bounds.Width }
                    .Max() - new[] { txtContactSearch.Bounds.Width, txtContactSkill.Bounds.Width, cboContactAvailability.Bounds.Width }.Min() <= 1
                && Math.Abs(txtContactSearch.Bounds.Height - txtContactSkill.Bounds.Height) <= 1
                && Math.Abs(txtContactSearch.Bounds.Height - cboContactAvailability.Bounds.Height) <= 1,
                $"Contact lookup fields use equal widths and heights (search {txtContactSearch.Bounds.Width}x{txtContactSearch.Bounds.Height}, skill {txtContactSkill.Bounds.Width}x{txtContactSkill.Bounds.Height}, availability {cboContactAvailability.Bounds.Width}x{cboContactAvailability.Bounds.Height})");
            Check(vmContacts.lstRows.Single().strOrganization == "Routing Organization", "Lookup resolves authorized organization name");
            Check(Visible<Button>().Any(btnControl => Equals(btnControl.Content, "View Profile")), "Lookup renders profile action");
            Check(Visible<Button>().Single(btnControl => Equals(btnControl.Content, "View Profile")).IsEffectivelyEnabled,
                "Profile action re-enables when asynchronous loading completes");
            Search("  RIVERA  ");
            Check(vmContacts.lstRows.Single().strName == "Alex Rivera", "Typing filters contact names case-insensitively and trims surrounding spaces");
            Check(vmContacts.strFilterSummary.Contains("Filters applied", StringComparison.Ordinal)
                && vmContacts.strFilterSummary.Contains("Search: RIVERA", StringComparison.Ordinal),
                "Contact lookup summarizes the active search filter");
            Check(Visible<TextBlock>().Single(txtControl => txtControl.Name == "txtContactFilterSummary").Text == vmContacts.strFilterSummary,
                "Contact filter summary is visible on the lookup page");
            Search("LOCAL@EXAMPLE.TEST");
            Check(vmContacts.lstRows.Count == 1, "Typing searches contact email addresses");
            Search("+1 (202) 555-0100");
            Check(vmContacts.lstRows.Count == 1, "Typing searches contact phone numbers");
            Search("Alix");
            Check(vmContacts.lstRows.Single().strName == "Alex Rivera", "Soundex matches a phonetically equivalent contact first name");
            Search("Rivara");
            Check(vmContacts.lstRows.Single().strName == "Alex Rivera", "Soundex matches a phonetically equivalent contact last name");
            Search("123456");
            Check(vmContacts.lstRows.Count == 0, "Numeric searches do not create empty-code phonetic matches");
            Check(vmContacts.bShowEmptyState && Visible<Border>().Any(bdrControl => bdrControl.Name == "bdrContactEmptyState"),
                "Contact lookup explains when a search returns no results");
            Search("no such contact");
            Check(vmContacts.lstRows.Count == 0, "A query without matches clears lookup results");
            Search("' OR 1=1 --");
            Check(vmContacts.lstRows.Count == 0, "Malformed search text is treated literally");
            var txtLookupSearch = Visible<TextBox>().Single(txtControl => txtControl.Name == "txtContactSearch");
            txtLookupSearch.Text = "Alex"; txtLookupSearch.Text = "no match"; txtLookupSearch.Text = "Rivera";
            Layout();
            Check(vmContacts.lstRows.Single().strName == "Alex Rivera", "Rapid typing retains only the latest query results");
            Search("");
            Check(vmContacts.lstRows.Count == 1, "Clearing the search restores all currently authorized filter results");
            Check(!vmContacts.bShowEmptyState, "Contact empty state clears when results return");
            vmContacts.lstRows.Single().cmdViewProfile.Execute(null); Layout();
            Check(vmContacts.bProfile && vmMain.objContact?.DisplayName == "Alex Rivera", "View Profile routes the selected contact");
            Check(Visible<Button>().Any(btnControl => btnControl.Name == "btnGlobalBack")
                && !Visible<ContactProfileView>().Single().GetVisualDescendants().OfType<Button>().Any(btnControl => Equals(btnControl.Content, "Back")),
                "Contact profile uses only the global header Back button");
            Check(Visible<ContactProfileView>().Any() && Visible<Image>().Any(imgControl => imgControl.Name == "imgContactProfile"), "Profile renders its image placeholder");
            Check(Visible<Border>().Any(brdControl => brdControl.Name == "brdOrgLogoPlaceholder" && brdControl.Width == 64 && brdControl.Height == 64)
                && Visible<TextBlock>().Any(txtControl => txtControl.Text == "No Logo"), "Profile shows a dedicated organization logo placeholder");
            Check(Visible<Button>().Any(btnControl => btnControl.Name == "btnNavContacts"), "Global sidebar remains visible in profile");
            var vReadOnlyProfile = Visible<ContactProfileView>().Single();
            Check(!vReadOnlyProfile.GetVisualDescendants().Any(ctlControl => ctlControl is TextBox or ComboBox or TabControl),
                "Contact profile is one read-only page without edit inputs or tabs");
            var scrReadOnlyProfile = vReadOnlyProfile.FindControl<ScrollViewer>("scrContactProfile")!;
            var brdContents = vReadOnlyProfile.FindControl<Border>("brdProfileContents")!;
            var ptContentsTop = brdContents.Bounds.Top;
            vReadOnlyProfile.FindControl<Button>("btnGoNotes")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            Check(scrReadOnlyProfile.Offset.Y > 0 && brdContents.Bounds.Top == ptContentsTop,
                "Contents menu scrolls the contact sections while remaining fixed");
            vmContacts.vmProfile!.cmdEdit.Execute(null); Layout();
            Check(vmContacts.bEditing && Visible<EditContactView>().Any() && !Visible<ContactProfileView>().Any(),
                "Edit Profile opens a separate edit route inside the shell");
            Visible<TextBox>().Single(txtControl => txtControl.Name == "txtFirstName").Text = "Discarded draft";
            vmContacts.vmEdit!.cmdCancel.Execute(null); Layout();
            Check(vmContacts.bProfile && vmMain.objContact!.FirstName == "Alex" && objCrm.Contacts().Single().FirstName == "Alex",
                "Cancel discards contact-field edits and returns to the read-only profile");
            vmContacts.vmProfile!.cmdEdit.Execute(null); Layout();
            Visible<TextBox>().Single(txtControl => txtControl.Name == "txtFirstName").Text = "Discard via global Back";
            GlobalBack();
            Check(vmContacts.bProfile && vmMain.objContact!.FirstName == "Alex" && vmMain.vmShell.bShowGlobalBack,
                "Global Back from editing discards the draft and returns to the read-only profile");
            vmContacts.vmProfile!.cmdEdit.Execute(null); Layout();
            Visible<TextBox>().Single(txtControl => txtControl.Name == "txtFirstName").Text = "";
            Visible<TextBox>().Single(txtControl => txtControl.Name == "txtLastName").Text = "";
            vmContacts.vmEdit!.cmdSave.Execute(null); Layout();
            Check(vmContacts.bEditing, "An invalid save stays in the Edit Profile view");
            Visible<TextBox>().Single(txtControl => txtControl.Name == "txtFirstName").Text = "Alex";
            Visible<TextBox>().Single(txtControl => txtControl.Name == "txtLastName").Text = "Rivera";
            Visible<Button>().Single(btnControl => btnControl.Name == "btnRemovePhone").Command!.Execute(null); Layout();
            Visible<TextBox>().Single(txtControl => txtControl.Name == "txtNewPhone").Text = "+1 (202) 555-0199";
            Visible<Button>().Single(btnControl => btnControl.Name == "btnAddPhone").Command!.Execute(null);
            vmContacts.vmEdit!.cmdSave.Execute(null); Layout();
            Check(vmContacts.bProfile && objCrm.Contacts().Single().Phone == "+1 (202) 555-0199",
                "Save persists the edited draft and returns to the read-only profile");
            var vmOpenProfile = vmContacts.vmProfile;
            vmMain.strNote = "Local routing fixture note";
            vmMain.cmdAddInteraction.Execute(null); Layout();
            Check(ReferenceEquals(vmOpenProfile, vmContacts.vmProfile) && vmMain.lstInteractions.Count == 1,
                "History refresh preserves the current profile route");
            GlobalBack();
            Check(!vmContacts.bProfile && vmContacts.iSelectedTab == 0, "Back restores Lookup");
            vmContacts.iSelectedTab = 1; Layout();
            WaitForTask(vmContacts.tskTabChange); Layout();
            Check(vmMain.objContact is { Id: 0 }, "Add Contact starts a new draft");
            Check(!vmMain.bBusy, "Preparing a new contact does not queue redundant database loading");
            Check(vmMain.bCanEditContact, "An administrator can edit a new contact draft without a database permission round trip");
            Check(!vmMain.vmShell.bShowGlobalBack, "Global Back stays hidden on Add Contact");
            Check(!Visible<TextBox>().Any(txtControl => txtControl.Name == "txtContactSearch"), "Add Contact has no search controls");
            var txtFirstName = Visible<TextBox>().Single(txtControl => txtControl.Name == "txtFirstName");
            var txtLastName = Visible<TextBox>().Single(txtControl => txtControl.Name == "txtLastName");
            var cboPrefix = Visible<ComboBox>().Single(cboControl => cboControl.Name == "cboContactPrefix");
            var cboSuffix = Visible<ComboBox>().Single(cboControl => cboControl.Name == "cboContactSuffix");
            var pnlNameFields = Visible<StackPanel>().Single(pnlControl => pnlControl.Name == "pnlAddContactName");
            var arrNameSections = new[] { "pnlPrefixName", "pnlFirstName", "pnlMiddleNames", "pnlLastName", "pnlSuffixName" }
                .Select(strName => Visible<StackPanel>().Single(pnlControl => pnlControl.Name == strName)).ToArray();
            Check(txtFirstName is CrmTextBox && txtLastName is CrmTextBox
                && Math.Abs(txtFirstName.Bounds.Height - 30) < 1
                && Math.Abs(txtLastName.Bounds.Height - 30) < 1
                && Math.Abs(txtFirstName.Bounds.Width - txtLastName.Bounds.Width) <= 1
                && txtFirstName.Bounds.Width >= 350
                && txtFirstName.Bounds.Width <= 420
                && cboPrefix.Bounds.Width <= 220 && cboSuffix.Bounds.Width <= 220,
                $"Name fields use equal, bounded 30-point proportions ({txtFirstName.Bounds.Width}x{txtFirstName.Bounds.Height}, {txtLastName.Bounds.Width}x{txtLastName.Bounds.Height})");
            var arrNameSectionPositions = arrNameSections.Select(pnlSection => pnlNameFields.Children.IndexOf(pnlSection)).ToArray();
            Check(arrNameSections.All(pnlSection => ReferenceEquals(pnlSection.Parent, pnlNameFields))
                && pnlNameFields.Children[0] is TextBlock { Text: "Name" }
                && arrNameSectionPositions.SequenceEqual(Enumerable.Range(1, arrNameSections.Length))
                && arrNameSections.All(pnlSection => Math.Abs(pnlSection.Bounds.Left - arrNameSections[0].Bounds.Left) <= 1),
                "Name fields follow Prefix, First name, Middle name, Last name, Suffix in one left-aligned column");
            var lstMiddleNameTextBoxes = Visible<TextBox>().Where(txtControl => txtControl.Name == "txtAddContactMiddleName").ToArray();
            Check(lstMiddleNameTextBoxes.Length == 1, "Add Contact starts with one middle name field");
            var btnAddMiddleName = Visible<Button>().Single(btnControl => btnControl.Name == "btnAddMiddleName");
            btnAddMiddleName.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            lstMiddleNameTextBoxes = Visible<TextBox>().Where(txtControl => txtControl.Name == "txtAddContactMiddleName").ToArray();
            Check(lstMiddleNameTextBoxes.Length == 2, "Add middle name creates another editable field");
            lstMiddleNameTextBoxes[0].Text = "James"; lstMiddleNameTextBoxes[1].Text = "Robert";
            Visible<TextBox>().Single(txtControl => txtControl.Name == "txtFirstName").Text = "New Contact";
            Visible<TextBox>().Single(txtControl => txtControl.Name == "txtLastName").Text = "Rivera";
            Check(!Visible<CheckBox>().Any(chkControl => chkControl.Name == "chkContactOrganization") && !vmMain.bOrganizationLookup,
                "Organization picker starts compact without checkboxes or search");
            AddOrganization("Routing", lngOrganizationId);
            Check(vmMain.lstAddedOrganizations.Count == 1 && !vmMain.bOrganizationLookup,
                "Confirm adds an organization chip and collapses the lookup");
            Check(!vmMain.lstAvailableOrganizations.Any(vmOption => vmOption.lngOrganizationId == lngOrganizationId),
                "Assigned organizations cannot be added twice");
            AddOrganization("Sekond", lngSecondOrganizationId);
            vmMain.ApplyTags(true, vmMain.objContact, null, ["Priority", "Logistics", "priority"]);
            Check(vmMain.objContact!.lstOrganizationIds.Order().SequenceEqual(new[] { lngOrganizationId, lngSecondOrganizationId }.Order()),
                "Compact organization picker updates both assignments");
            var btnSaveContact = Visible<Button>().Single(btnControl => btnControl.Name == "btnSaveContact");
            Check(btnSaveContact.Parent is StackPanel pnlEditor && ReferenceEquals(pnlEditor.Children.Last(), btnSaveContact),
                "Save contact is the final control in the form");
            vmMain.cmdSaveContact.Execute(null); Layout();
            Check(vmContacts.bProfile && objCrm.Contacts().Any(objContact => objContact.FirstName == "New Contact" && objContact.LastName == "Rivera"
                && objContact.DisplayMiddleNames == "James Robert" && objContact.DisplayName == "New Contact James Robert Rivera"
                && objContact.lstOrganizationIds.Count == 2 && objContact.lstTags.SequenceEqual(new[] { "Priority", "Logistics" })),
                "Save persists middle names, multiple organizations and deduplicated tags and opens the profile");
            GlobalBack();
            Search("lOgIsTiCs");
            Check(vmContacts.lstRows.Single().strName == "New Contact James Robert Rivera", "Typing searches assigned contact tags and displays all name parts");
            vmMain.cmdApplyFilters.Execute(null); Layout();
            Check(vmContacts.lstRows.Single().strName == "New Contact James Robert Rivera", "Applying database filters preserves the current text query");
            Search("");
            Check(vmContacts.lstRows.Count == 2, "Clearing a tag query restores both contacts");
            var vmNewRow = vmContacts.lstRows.Single(vmRow => vmRow.strName == "New Contact James Robert Rivera");
            Check(vmNewRow.strOrganization.Contains("Routing Organization") && vmNewRow.strOrganization.Contains("Second Organization"),
                "Lookup displays all assigned organization names");
            Check(vmNewRow.strAllTags == "Priority, Logistics" && vmNewRow.lstTags.Count == 2,
                "Lookup exposes all tags for badges and tooltip");
            Check(Visible<Border>().Where(bdrControl => bdrControl.Name == "bdrContactTags")
                .Any(bdrControl => bdrControl.ClipToBounds && AutomationProperties.GetName(bdrControl) == "Priority, Logistics"),
                "Clipped tag container retains a tooltip containing all tags");
            var objSavedContact = objCrm.Contacts().Single(objContact => objContact.FirstName == "New Contact");
            objSavedContact.strTags = new string('x', 65);
            var bInvalidTagRejected = false;
            try { objCrm.Save(objSavedContact); } catch (ArgumentException) { bInvalidTagRejected = true; }
            Check(bInvalidTagRejected && objCrm.Contacts().Single(objContact => objContact.Id == objSavedContact.Id).strTags == "Priority, Logistics",
                "Invalid tags are rejected without changing the saved contact");
            objSavedContact.strTags = "Priority, Logistics";
            objSavedContact.lstOrganizationIds.Add(long.MaxValue);
            var bInvalidOrganizationRejected = false;
            try { objCrm.Save(objSavedContact); } catch (UnauthorizedAccessException) { bInvalidOrganizationRejected = true; }
            Check(bInvalidOrganizationRejected && objCrm.Contacts().Single(objContact => objContact.Id == objSavedContact.Id).lstOrganizationIds.Count == 2,
                "An unauthorized assignment leaves existing organizations intact");
            vmNewRow.cmdViewProfile.Execute(null); Layout();
            vmContacts.vmProfile!.cmdEdit.Execute(null); Layout();
            // Removing a chip refreshes the item controls, so capture commands before detaching them.
            var arrRemoveCommands = Visible<Button>().Where(btnControl => btnControl.Name == "btnRemoveContactOrganization")
                .Select(btnControl => btnControl.Command!).ToArray();
            foreach (var cmdRemove in arrRemoveCommands) cmdRemove.Execute(null);
            Layout();
            Check(vmMain.lstAddedOrganizations.Count == 0, "Remove actions clear assignment chips");
            vmContacts.vmEdit!.cmdSave.Execute(null); Layout();
            Check(objCrm.Contacts().Single(objContact => objContact.Id == objSavedContact.Id).lstOrganizationIds.Count == 0,
                "All organization assignments can be removed and saved");
            vmContacts.vmProfile!.cmdEdit.Execute(null); Layout();
            var btnArchiveContact = Visible<Button>().Single(btnControl => btnControl.Name == "btnArchiveContact");
            Check(btnArchiveContact.Parent is StackPanel pnlEdit && ReferenceEquals(pnlEdit.Children.Last(), btnArchiveContact),
                "Archive is the final action in Edit Profile");
            btnArchiveContact.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            var dlgArchive = frmMain.OwnedWindows.OfType<ArchiveContactDialog>().Single();
            dlgArchive.FindControl<Button>("btnCancelArchive")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            Check(vmContacts.bEditing && objCrm.Contacts().Any(objContact => objContact.Id == objSavedContact.Id),
                "Canceling archive confirmation leaves the contact active");
            btnArchiveContact.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            dlgArchive = frmMain.OwnedWindows.OfType<ArchiveContactDialog>().Single();
            dlgArchive.FindControl<Button>("btnConfirmArchive")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            Check(!vmContacts.bEditing && objCrm.Contacts(bArchived: true).Any(objContact => objContact.Id == objSavedContact.Id),
                "Confirming archive preserves the record and exits editing");
            vmMain.bArchived = true; vmMain.cmdApplyFilters.Execute(null); Layout();
            vmContacts.lstRows.Single().cmdViewProfile.Execute(null); Layout();
            vmContacts.vmProfile!.cmdEdit.Execute(null); Layout();
            Visible<Button>().Single(btnControl => btnControl.Name == "btnArchiveContact").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            frmMain.OwnedWindows.OfType<ArchiveContactDialog>().Single().FindControl<Button>("btnConfirmArchive")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            vmMain.vmShell.cmdContacts.Execute(null); Layout();
            vmContacts.lstRows.Single(vmRow => vmRow.strName == "New Contact James Robert Rivera").cmdViewProfile.Execute(null); Layout();
            vmMain.vmShell.cmdContacts.Execute(null); Layout();
            Check(!vmMain.bCanGoToNextLookupPage && !Visible<Button>().Single(btnControl => btnControl.Content?.ToString() == "Next").IsEnabled,
                "Contacts lookup disables Next when fewer than one page of results exists");
            vmMain.cmdNextLookupPage.Execute(null); Layout();
            Check(vmMain.strLookupPageScope.StartsWith("Page 1", StringComparison.Ordinal), "Invoking Next cannot advance a single-page contact result set");
            foreach (var iIndex in Enumerable.Range(1, CuiMemoryPolicy.iPageSize))
                objCrm.Save(new Contact { FirstName = $"PageCheck{iIndex:000}", LastName = "Fixture" });
            vmContacts.strSearchQuery = "PageCheck"; vmMain.strSearch = "PageCheck";
            vmMain.cmdApplyFilters.Execute(null); Layout();
            Check(vmContacts.lstRows.Count == CuiMemoryPolicy.iPageSize && !vmMain.bCanGoToNextLookupPage,
                "An exact 128-result contact set has no phantom second page");
            objCrm.Save(new Contact { FirstName = "PageCheck129", LastName = "Fixture" });
            vmMain.cmdApplyFilters.Execute(null); Layout();
            Check(vmContacts.lstRows.Count == CuiMemoryPolicy.iPageSize && vmMain.bCanGoToNextLookupPage,
                "Contacts lookup enables Next when a 129th match exists");
            vmMain.cmdNextLookupPage.Execute(null); Layout();
            Check(vmMain.strLookupPageScope.StartsWith("Page 2", StringComparison.Ordinal) && vmContacts.lstRows.Count == 1
                && !vmMain.bCanGoToNextLookupPage && vmMain.bCanGoToPreviousLookupPage,
                "The final contact page shows its one result and disables Next");
            vmMain.cmdPreviousLookupPage.Execute(null); Layout();
            Check(vmMain.strLookupPageScope.StartsWith("Page 1", StringComparison.Ordinal) && vmContacts.lstRows.Count == CuiMemoryPolicy.iPageSize,
                "Previous returns to the first contact page");
            vmContacts.strSearchQuery = "no PageCheck match"; vmMain.strSearch = "no PageCheck match";
            vmMain.cmdApplyFilters.Execute(null); Layout();
            Check(vmContacts.lstRows.Count == 0 && !vmMain.bCanGoToNextLookupPage && vmMain.strLookupPageScope.StartsWith("Page 1", StringComparison.Ordinal),
                "An empty contact search stays on page one with no forward navigation");

            foreach (var iIndex in Enumerable.Range(1, CuiMemoryPolicy.iPageSize))
                objCrm.Save(new Organization { Name = $"PageCheckOrg{iIndex:000}" });
            vmMain.cmdOrganizations.Execute(null); Layout();
            var vmOrganizationsPage = (OrganizationsPageViewModel)vmMain.vmShell.vmCurrentPage;
            vmOrganizationsPage.strSearchQuery = "PageCheckOrg"; vmMain.strSearch = "PageCheckOrg";
            vmMain.cmdApplyFilters.Execute(null); Layout();
            Check(vmOrganizationsPage.lstRows.Count == CuiMemoryPolicy.iPageSize && !vmMain.bCanGoToNextLookupPage,
                "An exact 128-result organization set has no phantom second page");
            objCrm.Save(new Organization { Name = "PageCheckOrg129" });
            vmMain.cmdApplyFilters.Execute(null); Layout();
            Check(vmOrganizationsPage.lstRows.Count == CuiMemoryPolicy.iPageSize && vmMain.bCanGoToNextLookupPage,
                "Organizations lookup enables Next when a 129th match exists");
            vmMain.cmdNextLookupPage.Execute(null); Layout();
            Check(vmMain.strLookupPageScope.StartsWith("Page 2", StringComparison.Ordinal) && vmOrganizationsPage.lstRows.Count == 1
                && !vmMain.bCanGoToNextLookupPage && vmMain.bCanGoToPreviousLookupPage,
                "The final organization page shows its one result and disables Next");
            vmMain.cmdPreviousLookupPage.Execute(null); Layout();
            Check(vmMain.strLookupPageScope.StartsWith("Page 1", StringComparison.Ordinal) && vmOrganizationsPage.lstRows.Count == CuiMemoryPolicy.iPageSize,
                "Previous returns to the first organization page");
            vmOrganizationsPage.strSearchQuery = "no PageCheckOrg match"; vmMain.strSearch = "no PageCheckOrg match";
            vmMain.cmdApplyFilters.Execute(null); Layout();
            Check(vmOrganizationsPage.lstRows.Count == 0 && !vmMain.bCanGoToNextLookupPage && vmMain.strLookupPageScope.StartsWith("Page 1", StringComparison.Ordinal),
                "An empty organization search stays on page one with no forward navigation");

            vmMain.vmShell.cmdContacts.Execute(null); Layout();
            vmContacts = (ContactsPageViewModel)vmMain.vmShell.vmCurrentPage;
            semWork.Wait();
            try
            {
                vmMain.cmdApplyFilters.Execute(null);
                vmMain.vmShell.cmdLock.Execute(null);
                Dispatcher.UIThread.RunJobs(); frmMain.UpdateLayout();
                Check(vmMain.bLocking && !Visible<ContactProfileView>().Any(),
                    "Lock immediately obscures the profile while a database operation is pending");
            }
            finally { semWork.Release(); }
            Layout();
            Check(vmContacts.vmProfile is null && vmContacts.lstRows.Count == 0, "Lock clears Contacts subrouting state");
            Check(vmOrganizations.vmProfile is null && vmOrganizations.lstRows.Count == 0, "Lock clears Organizations subrouting state");
            Check(!Visible<ContactProfileView>().Any(), "Locked shell hides contact profile");
            Check(!bNotificationOffUiThread, "Async workflows deliver bound state notifications on the UI thread");
            vmMain.bDark = false;
            WaitForTask(vmMain.CloseAsync());
            Layout();
            Check(vmMain.bNeedsUsername && Visible<TextBox>().Any(txtControl => txtControl.Name == "txtAccount"),
                "Re-locking a multi-account workspace restores the username field");
            Check(!objDatabase.ReadDarkTheme(), "Closing flushes the final pending theme preference");
            Console.WriteLine($"{iChecks} focused Contacts checks passed.");
        }
        finally
        {
            WaitForTask(vmMain.CloseAsync()); frmMain.Close();
            objAuthentication.SignOut(objFixtureSession);
            typDevelopment.GetMethod("DisposeWorkspace", enNonPublicStatic)!.Invoke(null, null);
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(strFixtureDirectory))
            {
                foreach (var strFixtureFile in Directory.EnumerateFiles(strFixtureDirectory)) File.Delete(strFixtureFile);
                Directory.Delete(strFixtureDirectory);
            }
            if (Directory.Exists(strFixtureRoot)) Directory.Delete(strFixtureRoot);
        }

        IEnumerable<T> Visible<T>() where T : Control => frmMain.GetVisualDescendants().OfType<T>().Where(ctlControl => ctlControl.IsEffectivelyVisible);
        void GlobalBack()
        {
            var btnBack = Visible<Button>().Single(btnControl => btnControl.Name == "btnGlobalBack");
            btnBack.Command!.Execute(null); Layout();
        }
        void Search(string strQuery)
        {
            Visible<TextBox>().Single(txtControl => txtControl.Name == "txtContactSearch").Text = strQuery;
            Layout();
        }
        void AddOrganization(string strSearch, long lngOrganization)
        {
            vmMain.cmdShowOrganizationLookup.Execute(null); Layout();
            var acbOrganizationSearch = Visible<AutoCompleteBox>().Single(acbControl => acbControl.Name == "acbOrganizationSearch");
            var txtOrganizationInput = acbOrganizationSearch.GetVisualDescendants().OfType<TextBox>().Single();
            txtOrganizationInput.Focus();
            txtOrganizationInput.Text = strSearch;
            Layout();
            Check(vmMain.lstAvailableOrganizations.Count == 1 && vmMain.lstAvailableOrganizations[0].lngOrganizationId == lngOrganization,
                "Search filters available organizations locally");
            var arrMatches = acbOrganizationSearch.ItemsSource!.Cast<OrganizationOptionViewModel>().ToArray();
            Check(acbOrganizationSearch.IsDropDownOpen && arrMatches.Length == 1 && arrMatches[0].lngOrganizationId == lngOrganization,
                "Typing opens autocomplete suggestions with the matching organization");
            acbOrganizationSearch.SelectedItem = arrMatches[0]; Layout();
            Check(vmMain.objOrganizationToAdd?.lngOrganizationId == lngOrganization, "Selecting an autocomplete result stages the organization");
            acbOrganizationSearch.IsDropDownOpen = false;
            var btnConfirm = Visible<Button>().Single(btnControl => btnControl.Name == "btnConfirmContactOrganization");
            Check(btnConfirm.IsEffectivelyEnabled, "Confirm enables for an available organization");
            btnConfirm.Command!.Execute(null); Layout();
        }
        void Layout()
        {
            WaitForTask(vmMain.tskPendingOperation);
            WaitForTask(vmMain.tskThemeSave);
            WaitForTask(((AsyncRelayCommand)vmMain.cmdLock).tskExecution);
            WaitForTask(((AsyncRelayCommand)vmMain.vmShell.cmdGlobalBack).tskExecution);
            if (vmMain.vmShell.vmCurrentPage is ContactsPageViewModel { vmEdit: { } vmEdit })
            {
                WaitForTask(((AsyncRelayCommand)vmEdit.cmdSave).tskExecution);
                WaitForTask(((AsyncRelayCommand)vmEdit.cmdCancel).tskExecution);
                WaitForTask(((AsyncRelayCommand)vmEdit.cmdArchive).tskExecution);
            }
            try { WaitForTask(vmMain.tskOrganizationSearch); }
            catch (OperationCanceledException) { /* Superseded autocomplete requests are expected to be canceled. */ }
            if (vmMain.vmShell.vmCurrentPage is ContactsPageViewModel vmContactsPage)
            { WaitForTask(vmContactsPage.tskTabChange); WaitForTask(vmContactsPage.tskSearch); }
            if (vmMain.vmShell.vmCurrentPage is OrganizationsPageViewModel vmOrganizationsPage)
            { WaitForTask(vmOrganizationsPage.tskTabChange); WaitForTask(vmOrganizationsPage.tskSearch); }
            Dispatcher.UIThread.RunJobs(); frmMain.UpdateLayout(); CheckVisibleButtonGeometry();
        }
        void CheckVisibleButtonGeometry()
        {
            var arrVisibleTextBoxes = Visible<TextBox>().ToArray();
            var strTextBoxSignature = string.Join("|", arrVisibleTextBoxes.Select(txtControl =>
                $"{txtControl.Name ?? "unnamed"}:{txtControl.Bounds.Width:0}"));
            if (hashTextBoxLayouts.Add(strTextBoxSignature))
                Check(arrVisibleTextBoxes.All(txtControl => txtControl.Bounds.Width <= 760),
                    $"Text fields stay within the 760-point maximum on layout {hashTextBoxLayouts.Count}");
            var lstActionButtons = Visible<Button>().Where(btnControl => btnControl is not CheckBox and not RadioButton and not ToggleButton
                && btnControl.Name != "tglDarkMode"
                && !(btnControl.Name?.StartsWith("PART_", StringComparison.Ordinal) ?? false)).ToArray();
            var strSignature = string.Join("|", lstActionButtons.Select(btnControl => btnControl.Name ?? btnControl.Content?.ToString() ?? "unnamed")
                .Order(StringComparer.Ordinal));
            if (!hashButtonLayouts.Add(strSignature)) return;
            var lstMismatched = lstActionButtons.Where(btnControl => Math.Abs(btnControl.Bounds.Height - 30) >= 1
                || btnControl.CornerRadius != new CornerRadius(6)
                || btnControl.Padding != new Thickness(11, 4))
                .Select(btnControl => btnControl.Name ?? btnControl.Content?.ToString() ?? "unnamed").ToArray();
            Check(lstMismatched.Length == 0, $"All controls share consistent button sizing on layout {hashButtonLayouts.Count} ({string.Join(", ", lstMismatched)})");
        }
        void Check(bool bCondition, string strDescription)
        {
            if (!bCondition) throw new InvalidOperationException("FAIL " + strDescription);
            iChecks++; Console.WriteLine("PASS " + strDescription);
        }
    }

    internal static void WaitForTask(Task tskPending)
    {
        var swTimeout = System.Diagnostics.Stopwatch.StartNew();
        while (!tskPending.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            if (swTimeout.Elapsed > TimeSpan.FromSeconds(90)) throw new TimeoutException("The local test operation did not finish.");
            Thread.Sleep(1);
        }
        tskPending.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
    }
}
