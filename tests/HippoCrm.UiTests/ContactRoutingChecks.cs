using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
        AppBuilder.Configure<App>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var strFixtureDirectory = Path.Combine(Path.GetTempPath(), "HippoCrmContactRouting", Guid.NewGuid().ToString("N"));
        using var objKeyStore = new EphemeralKeyStore();
        var objDatabase = new Database(Path.Combine(strFixtureDirectory, "routing.db"), objKeyStore);
        var objAuthentication = new AuthService(objDatabase);
        using var objFixtureSession = objAuthentication.CreateAdministratorForTest("admin", "Local routing test password!");
        var objCrm = new CrmService(objDatabase, objFixtureSession);
        var lngOrganizationId = objCrm.Save(new Organization { Name = "Routing Organization" });
        var lngSecondOrganizationId = objCrm.Save(new Organization { Name = "Second Organization" });
        objCrm.Save(new Contact { FirstName = "Alex", LastName = "Rivera", lngOrganizationId = lngOrganizationId,
            Email = "local@example.test", Phone = "555-0100" });
        // Downgrade only this disposable fixture to the preceding schema, then exercise the actual migration.
        using (var dbFixture = (SqliteConnection)typeof(Database).GetMethod("Open",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(objDatabase, null)!)
        using (var cmdFixture = dbFixture.CreateCommand())
        {
            cmdFixture.CommandText = "DROP TABLE contact_organizations; ALTER TABLE contacts DROP COLUMN tags; UPDATE schema_version SET version=3;";
            cmdFixture.ExecuteNonQuery();
        }
        _ = new Database(objDatabase.Path, objKeyStore);
        var objMigratedContact = objCrm.Contacts().Single();
        objDatabase.SaveDarkTheme(true);
        var vmMain = new MainWindowViewModel(objDatabase) { strUsername = "admin" };
        WaitForTask(vmMain.tskInitialization);
        var bNotificationOffUiThread = false;
        vmMain.PropertyChanged += (objSender, objArguments) => bNotificationOffUiThread |= !Dispatcher.UIThread.CheckAccess();
        foreach (var chrCharacter in "Local routing test password!") vmMain.secPassword.AppendChar(chrCharacter);
        vmMain.cmdSignIn.Execute(null);
        WaitForTask(vmMain.tskPendingOperation);
        var frmMain = new MainWindow(vmMain);
        frmMain.Show();
        var iChecks = 0;
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
            vmMain.bDark = false; WaitForTask(vmMain.tskThemeSave);
            var vmReopened = new MainWindowViewModel(objDatabase);
            WaitForTask(vmReopened.tskInitialization);
            Check(!vmReopened.bDark && !objDatabase.ReadDarkTheme(), "Light theme persists across workspace reopening");
            WaitForTask(vmReopened.CloseAsync());
            vmMain.bDark = true; vmMain.bDark = false; vmMain.bDark = true;
            WaitForTask(vmMain.tskThemeSave);
            Check(objDatabase.ReadDarkTheme(), "Rapid theme changes persist the final selection in order");
            Check(objMigratedContact.lstOrganizationIds.SequenceEqual(new[] { lngOrganizationId }) && objMigratedContact.strTags == "",
                "Version 3 migration preserves the existing organization assignment");
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
                vmMain.cmdApplyFilters.Execute(null);
                Check(ReferenceEquals(tskFirstSearch, vmMain.tskPendingOperation), "Busy commands cannot queue duplicate searches");
            }
            finally { semWork.Release(); }
            Layout();
            Check(vmContacts.iSelectedTab == 0 && !vmContacts.bProfile, "Contacts opens Lookup");
            Check(vmContacts.lstRows.Single().strOrganization == "Routing Organization", "Lookup resolves authorized organization name");
            Check(Visible<Button>().Any(btnControl => Equals(btnControl.Content, "View Profile")), "Lookup renders profile action");
            Check(Visible<Button>().Single(btnControl => Equals(btnControl.Content, "View Profile")).IsEffectivelyEnabled,
                "Profile action re-enables when asynchronous loading completes");
            Search("  RIVERA  ");
            Check(vmContacts.lstRows.Single().strName == "Alex Rivera", "Typing filters contact names case-insensitively and trims surrounding spaces");
            Search("LOCAL@EXAMPLE.TEST");
            Check(vmContacts.lstRows.Count == 1, "Typing searches contact email addresses");
            Search("555-0100");
            Check(vmContacts.lstRows.Count == 1, "Typing searches contact phone numbers");
            Search("Alix");
            Check(vmContacts.lstRows.Single().strName == "Alex Rivera", "Soundex matches a phonetically equivalent contact first name");
            Search("Rivara");
            Check(vmContacts.lstRows.Single().strName == "Alex Rivera", "Soundex matches a phonetically equivalent contact last name");
            Search("123456");
            Check(vmContacts.lstRows.Count == 0, "Numeric searches do not create empty-code phonetic matches");
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
            vmContacts.lstRows.Single().cmdViewProfile.Execute(null); Layout();
            Check(vmContacts.bProfile && vmMain.objContact?.DisplayName == "Alex Rivera", "View Profile routes the selected contact");
            Check(Visible<ContactProfileView>().Any() && Visible<Image>().Any(imgControl => imgControl.Name == "imgContactProfile"), "Profile renders its image placeholder");
            Check(Visible<Border>().Any(brdControl => brdControl.Name == "brdOrgLogoPlaceholder" && brdControl.Width == 64 && brdControl.Height == 64)
                && Visible<TextBlock>().Any(txtControl => txtControl.Text == "No Logo"), "Profile shows a dedicated organization logo placeholder");
            Check(Visible<Button>().Any(btnControl => btnControl.Name == "btnNavContacts"), "Global sidebar remains visible in profile");
            var vmOpenProfile = vmContacts.vmProfile;
            vmMain.strNote = "Local routing fixture note";
            vmMain.cmdAddInteraction.Execute(null); Layout();
            Check(ReferenceEquals(vmOpenProfile, vmContacts.vmProfile) && vmMain.lstInteractions.Count == 1,
                "History refresh preserves the current profile route");
            vmContacts.vmProfile!.cmdBack.Execute(null); Layout();
            Check(!vmContacts.bProfile && vmContacts.iSelectedTab == 0, "Back restores Lookup");
            vmContacts.iSelectedTab = 1; Layout();
            Check(vmMain.objContact is { Id: 0 }, "Add Contact starts a new draft");
            Check(!Visible<TextBox>().Any(txtControl => txtControl.Name == "txtContactSearch"), "Add Contact has no search controls");
            Visible<TextBox>().Single(txtControl => txtControl.Name == "txtFirstName").Text = "New Contact";
            Check(!Visible<CheckBox>().Any(chkControl => chkControl.Name == "chkContactOrganization") && !vmMain.bOrganizationLookup,
                "Organization picker starts compact without checkboxes or search");
            AddOrganization("Routing", lngOrganizationId);
            Check(vmMain.lstAddedOrganizations.Count == 1 && !vmMain.bOrganizationLookup,
                "Confirm adds an organization chip and collapses the lookup");
            Check(!vmMain.lstAvailableOrganizations.Any(vmOption => vmOption.lngOrganizationId == lngOrganizationId),
                "Assigned organizations cannot be added twice");
            AddOrganization("Sekond", lngSecondOrganizationId);
            Visible<TextBox>().Single(txtControl => txtControl.Name == "txtContactTags").Text = "Priority, Logistics, priority";
            Check(vmMain.objContact!.lstOrganizationIds.Order().SequenceEqual(new[] { lngOrganizationId, lngSecondOrganizationId }.Order()),
                "Compact organization picker updates both assignments");
            var btnSaveContact = Visible<Button>().Single(btnControl => btnControl.Name == "btnSaveContact");
            Check(btnSaveContact.Parent is StackPanel pnlEditor && ReferenceEquals(pnlEditor.Children.Last(), btnSaveContact),
                "Save contact is the final control in the form");
            vmMain.cmdSaveContact.Execute(null); Layout();
            Check(vmContacts.bProfile && objCrm.Contacts().Any(objContact => objContact.FirstName == "New Contact"
                && objContact.lstOrganizationIds.Count == 2 && objContact.lstTags.SequenceEqual(new[] { "Priority", "Logistics" })),
                "Save persists multiple organizations and deduplicated tags and opens the profile");
            vmContacts.vmProfile!.cmdBack.Execute(null); Layout();
            Search("lOgIsTiCs");
            Check(vmContacts.lstRows.Single().strName == "New Contact", "Typing searches assigned contact tags");
            vmMain.cmdApplyFilters.Execute(null); Layout();
            Check(vmContacts.lstRows.Single().strName == "New Contact", "Applying database filters preserves the current text query");
            Search("");
            Check(vmContacts.lstRows.Count == 2, "Clearing a tag query restores both contacts");
            var vmNewRow = vmContacts.lstRows.Single(vmRow => vmRow.strName == "New Contact");
            Check(vmNewRow.strOrganization.Contains("Routing Organization") && vmNewRow.strOrganization.Contains("Second Organization"),
                "Lookup displays all assigned organization names");
            Check(vmNewRow.strAllTags == "Priority, Logistics" && vmNewRow.lstTags.Count == 2,
                "Lookup exposes all tags for badges and tooltip");
            Check(Visible<Border>().Where(bdrControl => bdrControl.Name == "bdrContactTags")
                .Any(bdrControl => Equals(ToolTip.GetTip(bdrControl), "Priority, Logistics") && bdrControl.ClipToBounds),
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
            // Removing a chip refreshes the item controls, so capture commands before detaching them.
            var arrRemoveCommands = Visible<Button>().Where(btnControl => btnControl.Name == "btnRemoveContactOrganization")
                .Select(btnControl => btnControl.Command!).ToArray();
            foreach (var cmdRemove in arrRemoveCommands) cmdRemove.Execute(null);
            Layout();
            Check(vmMain.lstAddedOrganizations.Count == 0, "Remove actions clear assignment chips");
            vmMain.cmdSaveContact.Execute(null); Layout();
            Check(objCrm.Contacts().Single(objContact => objContact.Id == objSavedContact.Id).lstOrganizationIds.Count == 0,
                "All organization assignments can be removed and saved");
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
            Check(!Visible<ContactProfileView>().Any(), "Locked shell hides contact profile");
            Check(!bNotificationOffUiThread, "Async workflows deliver bound state notifications on the UI thread");
            vmMain.bDark = false;
            WaitForTask(vmMain.CloseAsync());
            Check(!objDatabase.ReadDarkTheme(), "Closing flushes the final pending theme preference");
            Console.WriteLine($"{iChecks} focused Contacts checks passed.");
        }
        finally
        {
            WaitForTask(vmMain.CloseAsync()); frmMain.Close();
            objAuthentication.SignOut(objFixtureSession);
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(strFixtureDirectory))
            {
                foreach (var strFixtureFile in Directory.EnumerateFiles(strFixtureDirectory)) File.Delete(strFixtureFile);
                Directory.Delete(strFixtureDirectory);
            }
        }

        IEnumerable<T> Visible<T>() where T : Control => frmMain.GetVisualDescendants().OfType<T>().Where(ctlControl => ctlControl.IsEffectivelyVisible);
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
            try { WaitForTask(vmMain.tskOrganizationSearch); }
            catch (OperationCanceledException) { /* Superseded autocomplete requests are expected to be canceled. */ }
            if (vmMain.vmShell.vmCurrentPage is ContactsPageViewModel vmContactsPage) WaitForTask(vmContactsPage.tskSearch);
            Dispatcher.UIThread.RunJobs(); frmMain.UpdateLayout();
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
