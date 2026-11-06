using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HippoCrm.Core;
using HippoCrm.Desktop;
using HippoCrm.Desktop.ViewModels;
using HippoCrm.Desktop.Views;
using HippoCrm.TestSupport;
using Microsoft.Data.Sqlite;

internal static class TokenSelectionChecks
{
    public static void Run()
    {
        var iChecks = 0;
        var strFixtureDirectory = Path.Combine(Path.GetTempPath(), "HippoCrmTokenSelection", Guid.NewGuid().ToString("N"));
#if DEBUG
        // Use the existing isolated composition; production startup gates remain unchanged.
        UiDevelopmentChecks.Invoke("TryEnable", (object)new[] { "--ui-development" });
        using var objDatabase = Database.CreateUiDevelopmentWorkspace();
        UiDevelopmentChecks.Invoke("ConfigureWorkspaceFactory", (Func<Database>)(() => objDatabase));
#else
        using var objKeyStore = new EphemeralKeyStore();
        var objDatabase = new Database(Path.Combine(strFixtureDirectory, "tokens.db"), objKeyStore);
#endif
        AppBuilder.Configure<App>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var objAuth = new AuthService(objDatabase);
        using var objFixtureSession = objAuth.CreateAdministratorForTest("admin", "Local token test password!");
        var objUsers = new UserAdministrationService(objDatabase, objFixtureSession);
        objUsers.SetCuiAuthorization(objFixtureSession.UserId, true);
        var objCrm = new CrmService(objDatabase, objFixtureSession);
        var objCatalogOrganization = new Organization { Name = "Catalog", Tags = "CatalogTag", Services = "CatalogService" }; objCrm.Save(objCatalogOrganization);
        var objCatalogContact = new Contact { FirstName = "Catalog Contact", strTags = "ContactTag", Skills = "CatalogSkill" }; objCrm.Save(objCatalogContact);
        objCrm.Save(new Contact { FirstName = "Restricted catalog contact", Skills = "HiddenSkill", strTags = "HiddenContactTag" });
        objCrm.Save(new Organization { Name = "Restricted catalog organization", Services = "HiddenService", Tags = "HiddenOrganizationTag" });
        using (var secViewerPassword = TestPasswords.Secret("Local catalog viewer password!"))
        {
            var lngViewer = objUsers.CreateUser("catalog-viewer", secViewerPassword, UserRole.Viewer);
            objUsers.SetCuiAuthorization(lngViewer, true);
            objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, objCatalogContact.Id, true, false);
            objUsers.SetRecordAccess(lngViewer, RecordKind.Organization, objCatalogOrganization.Id, true, false);
            using var objViewer = objAuth.SignIn("catalog-viewer", secViewerPassword);
            var objCatalog = new CrmService(objDatabase, objViewer).TokenChoices();
            try
            {
                Check(objCatalog.Skills.SequenceEqual(new[] { "CatalogSkill" }) && objCatalog.Services.SequenceEqual(new[] { "CatalogService" }),
                    "Record grants expose only authorized contact skills and organization services");
                Check(objCatalog.Tags.Order().SequenceEqual(new[] { "CatalogTag", "ContactTag" }), "Both authorized record types contribute shared tags");
                Check(!objCatalog.Tags.Concat(objCatalog.Skills).Concat(objCatalog.Services).Any(strValue => strValue.StartsWith("Hidden", StringComparison.Ordinal)),
                    "Unassigned record skills, services and tags never appear in any catalog");
                Check(!objCatalog.Skills.Contains("CatalogService") && !objCatalog.Services.Contains("CatalogSkill"), "Skills and services never cross catalogs");
                Check(!objCatalog.Tags.Intersect(objCatalog.Skills.Concat(objCatalog.Services)).Any(), "Tags never cross into skill or service catalogs");
            }
            finally { CuiMemoryPolicy.Release(objCatalog); objAuth.SignOut(objViewer); }
            Check(objCatalog.Services.All(strValue => strValue is null) && objCatalog.Skills.All(strValue => strValue is null)
                && objCatalog.Tags.All(strValue => strValue is null), "Catalog release clears all three owned arrays");
        }
        // Plausible local fixtures exercise real lookup and chip layouts without touching the user's database.
        objCrm.Save(new Organization { Name = "Cedar Ridge Advisory", Tags = "Prospect, Regional", Services = "Operations" });
        objCrm.Save(new Organization { Name = "Harborline Health Group", Tags = "Preferred", Services = "Care coordination" });
        objCrm.Save(new Contact { FirstName = "Jordan", LastName = "Kim", Email = "jordan.kim@example.test",
            Phone = "+1 (312) 555-0142", strTags = "Prospect, Healthcare", Skills = "Operations, Analytics" });
        objCrm.Save(new Contact { FirstName = "Samira", LastName = "Bennett", Email = "samira.bennett@example.test",
            Phone = "+1 (415) 555-0186", strTags = "Preferred", Skills = "Care coordination" });
#if DEBUG
        var vmMain = new MainWindowViewModel() { strUsername = "admin" };
#else
        var vmMain = new MainWindowViewModel(objDatabase) { strUsername = "admin" };
#endif
        ContactRoutingChecks.WaitForTask(vmMain.tskInitialization);
        foreach (var chrValue in "Local token test password!") vmMain.secPassword.AppendChar(chrValue);
        vmMain.cmdSignIn.Execute(null); ContactRoutingChecks.WaitForTask(vmMain.tskPendingOperation);
        var frmMain = new MainWindow(vmMain); frmMain.Show();
        vmMain.fnConfirmDiscard = () => Task.FromResult(true);
        try
        {
            VerifyButtonGeometry("workspace shell");
            Check(!Visible<TextBlock>().Any(txtBlock => txtBlock.Text is "SIGNED IN" or "admin"),
                "The shell does not display the signed-in username");
            vmMain.cmdContacts.Execute(null); Layout();
            ((ContactsPageViewModel)vmMain.vmShell.vmCurrentPage).iSelectedTab = 1; Layout();
            vmMain.objContact!.FirstName = "Casey";
            vmMain.ApplyTags(true, vmMain.objContact, null, ["InitialTag"]);
            vmMain.ApplySkills(vmMain.objContact, ["InitialSkill"]); Layout();
            Click("btnManageContactTags");
            var dlgTags = frmMain.OwnedWindows.OfType<TagSelectionWindow>().Single();
            VerifyButtonGeometry("tag picker");
            Check(Math.Abs(dlgTags.FindControl<Button>("btnAddTag")!.Bounds.Height - 30) < 1,
                "Tag add action matches the shared text field height");
            var vmTags = (TagSelectionViewModel)dlgTags.DataContext!;
            Check(vmTags.lstVisible.Any(vmValue => vmValue.strValue == "CatalogTag")
                && !vmTags.lstVisible.Any(vmValue => vmValue.strValue is "CatalogSkill" or "CatalogService"), "Contact tag picker shares organization tags and excludes skills/services");
            vmTags.strSearch = "AddedTag"; vmTags.cmdAddValue.Execute(null);
            vmTags.strSearch = "addedtag"; vmTags.cmdAddValue.Execute(null);
            Check(vmTags.lstSelected.Count == 2, "Tag creation ignores case-insensitive duplicates");
            vmTags.iSelectionFilter = 1;
            Check(vmTags.lstVisible.All(vmValue => vmValue.bSelected), "Selection filter shows selected values only");
            dlgTags.FindControl<Button>("btnSaveTagSelection")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            Check(vmMain.lstContactTags.Contains("AddedTag") && vmMain.objContact.Skills == "InitialSkill", "Saving tags changes only Contact.strTags");
            Click("btnManageContactSkills");
            var dlgSkills = frmMain.OwnedWindows.OfType<SkillSelectionWindow>().Single();
            Check(Math.Abs(dlgSkills.FindControl<Button>("btnAddSkill")!.Bounds.Height - 30) < 1,
                "Skill add action matches the shared text field height");
            var vmSkills = (SkillSelectionViewModel)dlgSkills.DataContext!;
            Check(vmSkills.lstVisible.Any(vmValue => vmValue.strValue == "CatalogSkill")
                && !vmSkills.lstVisible.Any(vmValue => vmValue.strValue is "CatalogTag" or "ContactTag" or "CatalogService"), "Contact Skills picker excludes organization services and all tags");
            vmSkills.strSearch = "AddedSkill"; vmSkills.cmdAddValue.Execute(null);
            dlgSkills.FindControl<Button>("btnSaveSkillSelection")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            Check(vmMain.lstContactSkills.Contains("AddedSkill") && !vmMain.lstContactTags.Contains("AddedSkill"), "Saving skills cannot mix values into contact tags");
            Click("btnManageContactTags");
            dlgTags = frmMain.OwnedWindows.OfType<TagSelectionWindow>().Single();
            vmTags = (TagSelectionViewModel)dlgTags.DataContext!;
            vmTags.strSearch = "DiscardedTag"; vmTags.cmdAddValue.Execute(null);
            dlgTags.FindControl<Button>("btnCloseTagSelection")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            Check(!vmMain.lstContactTags.Contains("DiscardedTag"), "Close discards picker changes");
            vmMain.cmdSaveContact.Execute(null); Layout();
            var objSavedContact = objCrm.Contacts().Single(objContact => objContact.FirstName == "Casey");
            Check(objSavedContact.strTags.Contains("AddedTag") && objSavedContact.Skills.Contains("AddedSkill")
                && !objSavedContact.strTags.Contains("AddedSkill"), "Contact save persists separate tag and skill fields");
            Check(Visible<TokenChipsView>().Any(ctrChips => ctrChips.lstValues?.Contains("AddedSkill") == true), "Read-only contact profile displays skill chips");
            vmMain.cmdOrganizations.Execute(null); Layout();
            var vmOrganizations = (OrganizationsPageViewModel)vmMain.vmShell.vmCurrentPage;
            vmOrganizations.iSelectedTab = 1; Layout(); vmMain.objOrganization!.Name = "Picker Organization";
            AddOrganizationValue(true, "OrgTag"); AddOrganizationValue(false, "OrgService");
            Check(vmMain.objOrganization.Tags == "OrgTag" && vmMain.objOrganization.Services == "OrgService", "Organization pickers use Tags and Services independently");
            vmMain.cmdSaveOrganization.Execute(null); Layout();
            var objSavedOrganization = objCrm.Organizations().Single(objOrganization => objOrganization.Name == "Picker Organization");
            Check(objSavedOrganization.Tags == "OrgTag" && objSavedOrganization.Services == "OrgService", "Organization save persists separate properties");
            var lstSharedTags = vmMain.LoadTagChoicesAsync(); ContactRoutingChecks.WaitForTask(lstSharedTags);
            Check(lstSharedTags.Result.Contains("OrgTag") && lstSharedTags.Result.Contains("AddedTag"), "Saved organization and contact tags share the same choices");
            vmOrganizations.vmProfile!.cmdEdit.Execute(null); Layout();
            AddOrganizationValue(true, "EditOrgTag"); AddOrganizationValue(false, "EditOrgService");
            vmOrganizations.vmProfile!.cmdSave.Execute(null); Layout();
            objSavedOrganization = objCrm.Organizations().Single(objOrganization => objOrganization.Name == "Picker Organization");
            Check(objSavedOrganization.Tags.Contains("EditOrgTag") && objSavedOrganization.Services.Contains("EditOrgService")
                && !objSavedOrganization.Tags.Contains("EditOrgService"), "Organization Edit Profile routes both modal selections correctly");
            vmOrganizations.vmProfile!.cmdEdit.Execute(null); Layout(); Click("btnManageOrganizationTags");
            dlgTags = frmMain.OwnedWindows.OfType<TagSelectionWindow>().Single(); vmTags = (TagSelectionViewModel)dlgTags.DataContext!;
            vmMain.cmdLock.Execute(null); Layout(); ContactRoutingChecks.WaitForTask(((AsyncRelayCommand)vmMain.cmdLock).tskExecution);
            Check(!frmMain.OwnedWindows.OfType<TagSelectionWindow>().Any() && vmTags.lstSelected.Count == 0,
                "Session lock closes the picker and clears its data");
            Console.WriteLine($"{iChecks} focused tag/skill checks passed.");
        }
        finally
        {
            ContactRoutingChecks.WaitForTask(vmMain.CloseAsync()); frmMain.Close(); objAuth.SignOut(objFixtureSession);
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(strFixtureDirectory))
                foreach (var strFile in Directory.EnumerateFiles(strFixtureDirectory)) File.Delete(strFile);
            if (Directory.Exists(strFixtureDirectory)) Directory.Delete(strFixtureDirectory);
        }

        IEnumerable<T> Visible<T>() where T : Control => frmMain.GetVisualDescendants().OfType<T>().Where(ctlControl => ctlControl.IsEffectivelyVisible);
        void VerifyButtonGeometry(string strContext)
        {
            var lstMismatched = Visible<Button>().Where(btnControl => btnControl.Name != "tglDarkMode"
                && !(btnControl.Name?.StartsWith("PART_", StringComparison.Ordinal) ?? false))
                .Where(btnControl => Math.Abs(btnControl.Bounds.Height - 30) >= 1
                || btnControl.CornerRadius != new CornerRadius(6)
                || btnControl.Padding != new Thickness(11, 4)).Select(btnControl => btnControl.Name ?? btnControl.Content?.ToString() ?? "unnamed").ToArray();
            Check(lstMismatched.Length == 0, $"All {strContext} buttons share 30-point height, 6-point corners and balanced padding ({string.Join(", ", lstMismatched)})");
        }
        void Layout()
        {
            ContactRoutingChecks.WaitForTask(vmMain.tskPendingOperation);
            if (vmMain.vmShell.vmCurrentPage is ContactsPageViewModel vmContactsPage)
                ContactRoutingChecks.WaitForTask(vmContactsPage.tskTabChange);
            if (vmMain.vmShell.vmCurrentPage is OrganizationsPageViewModel vmOrganizationsPage)
                ContactRoutingChecks.WaitForTask(vmOrganizationsPage.tskTabChange);
            Dispatcher.UIThread.RunJobs(); frmMain.UpdateLayout();
            foreach (var dlgPicker in frmMain.OwnedWindows)
                if (dlgPicker.DataContext is SelectionDialogViewModel vmPicker)
                {
                    var swLoading = System.Diagnostics.Stopwatch.StartNew();
                    while (vmPicker.bLoading)
                    {
                        Dispatcher.UIThread.RunJobs(); Thread.Sleep(1);
                        if (swLoading.Elapsed > TimeSpan.FromSeconds(90)) throw new TimeoutException("Picker choices did not finish loading.");
                    }
                    dlgPicker.UpdateLayout();
                }
        }
        void Click(string strName) { Visible<Button>().Single(btnControl => btnControl.Name == strName).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(); }
        void AddOrganizationValue(bool bTags, string strValue)
        {
            Click(bTags ? "btnManageOrganizationTags" : "btnManageOrganizationServices");
            if (bTags)
            {
                var dlgSelection = frmMain.OwnedWindows.OfType<TagSelectionWindow>().Single();
                var vmSelection = (TagSelectionViewModel)dlgSelection.DataContext!;
                Check(vmSelection.lstVisible.Any(vmValue => vmValue.strValue == "AddedTag"), "Organization tag picker includes a tag saved on a contact");
                vmSelection.strSearch = strValue; vmSelection.cmdAddValue.Execute(null);
                dlgSelection.FindControl<Button>("btnSaveTagSelection")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            else
            {
                var dlgSelection = frmMain.OwnedWindows.OfType<SkillSelectionWindow>().Single();
                var vmSelection = (ServiceSelectionViewModel)dlgSelection.DataContext!;
                Check(dlgSelection.Title == "CUI — Add Services" && dlgSelection.FindControl<TextBox>("txtSkillSearch")!.Watermark == "Search services", "Organization picker has Services title and search prompt");
                Check(Avalonia.Automation.AutomationProperties.GetName(dlgSelection.FindControl<ListBox>("lstAvailableSkills")!) == "Available services", "Organization picker accessibility names say services");
                Check(vmSelection.lstVisible.Any(vmValue => vmValue.strValue == "CatalogService")
                    && !vmSelection.lstVisible.Any(vmValue => vmValue.strValue is "CatalogSkill" or "CatalogTag" or "ContactTag"), "Services picker excludes contact skills and shared tags");
                vmSelection.strSearch = strValue; vmSelection.cmdAddValue.Execute(null);
                dlgSelection.FindControl<Button>("btnSaveSkillSelection")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            Layout();
        }
        void Check(bool bCondition, string strDescription)
        { if (!bCondition) throw new InvalidOperationException("FAIL " + strDescription); iChecks++; Console.WriteLine("PASS " + strDescription); }
    }
}
