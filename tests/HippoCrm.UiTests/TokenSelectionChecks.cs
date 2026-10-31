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
        AppBuilder.Configure<App>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var strFixtureDirectory = Path.Combine(Path.GetTempPath(), "HippoCrmTokenSelection", Guid.NewGuid().ToString("N"));
        using var objKeyStore = new EphemeralKeyStore();
        var objDatabase = new Database(Path.Combine(strFixtureDirectory, "tokens.db"), objKeyStore);
        var objAuth = new AuthService(objDatabase);
        using var objFixtureSession = objAuth.CreateAdministratorForTest("admin", "Local token test password!");
        var objCrm = new CrmService(objDatabase, objFixtureSession);
        objCrm.Save(new Organization { Name = "Catalog", Tags = "CatalogTag", Services = "CatalogSkill" });
        objCrm.Save(new Contact { FirstName = "Catalog Contact", strTags = "ContactTag", Skills = "ContactSkill" });
        var vmMain = new MainWindowViewModel(objDatabase) { strUsername = "admin" };
        ContactRoutingChecks.WaitForTask(vmMain.tskInitialization);
        foreach (var chrValue in "Local token test password!") vmMain.secPassword.AppendChar(chrValue);
        vmMain.cmdSignIn.Execute(null); ContactRoutingChecks.WaitForTask(vmMain.tskPendingOperation);
        var frmMain = new MainWindow(vmMain); frmMain.Show();
        vmMain.fnConfirmDiscard = () => Task.FromResult(true);
        var iChecks = 0;
        try
        {
            vmMain.cmdContacts.Execute(null); Layout();
            ((ContactsPageViewModel)vmMain.vmShell.vmCurrentPage).iSelectedTab = 1; Layout();
            vmMain.objContact!.FirstName = "Casey";
            vmMain.ApplyTags(true, vmMain.objContact, null, ["InitialTag"]);
            vmMain.ApplySkills(true, vmMain.objContact, null, ["InitialSkill"]); Layout();
            Click("btnManageContactTags");
            var dlgTags = frmMain.OwnedWindows.OfType<TagSelectionWindow>().Single();
            var vmTags = (TagSelectionViewModel)dlgTags.DataContext!;
            Check(vmTags.lstVisible.Any(vmValue => vmValue.strValue == "CatalogTag")
                && !vmTags.lstVisible.Any(vmValue => vmValue.strValue == "CatalogSkill"), "Tag window loads only the tag catalog");
            vmTags.strSearch = "AddedTag"; vmTags.cmdAddValue.Execute(null);
            vmTags.strSearch = "addedtag"; vmTags.cmdAddValue.Execute(null);
            Check(vmTags.lstSelected.Count == 2, "Tag creation ignores case-insensitive duplicates");
            vmTags.iSelectionFilter = 1;
            Check(vmTags.lstVisible.All(vmValue => vmValue.bSelected), "Selection filter shows selected values only");
            dlgTags.FindControl<Button>("btnSaveTagSelection")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            Check(vmMain.lstContactTags.Contains("AddedTag") && vmMain.objContact.Skills == "InitialSkill", "Saving tags changes only Contact.strTags");
            Click("btnManageContactSkills");
            var dlgSkills = frmMain.OwnedWindows.OfType<SkillSelectionWindow>().Single();
            var vmSkills = (SkillSelectionViewModel)dlgSkills.DataContext!;
            Check(vmSkills.lstVisible.Any(vmValue => vmValue.strValue == "CatalogSkill")
                && !vmSkills.lstVisible.Any(vmValue => vmValue.strValue == "CatalogTag"), "Skill window loads only the skill catalog");
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
            AddOrganizationValue(true, "OrgTag"); AddOrganizationValue(false, "OrgSkill");
            Check(vmMain.objOrganization.Tags == "OrgTag" && vmMain.objOrganization.Services == "OrgSkill", "Organization pickers use Tags and Services independently");
            vmMain.cmdSaveOrganization.Execute(null); Layout();
            var objSavedOrganization = objCrm.Organizations().Single(objOrganization => objOrganization.Name == "Picker Organization");
            Check(objSavedOrganization.Tags == "OrgTag" && objSavedOrganization.Services == "OrgSkill", "Organization save persists separate properties");
            vmOrganizations.vmProfile!.cmdEdit.Execute(null); Layout();
            AddOrganizationValue(true, "EditOrgTag"); AddOrganizationValue(false, "EditOrgSkill");
            vmOrganizations.vmProfile!.cmdSave.Execute(null); Layout();
            objSavedOrganization = objCrm.Organizations().Single(objOrganization => objOrganization.Name == "Picker Organization");
            Check(objSavedOrganization.Tags.Contains("EditOrgTag") && objSavedOrganization.Services.Contains("EditOrgSkill")
                && !objSavedOrganization.Tags.Contains("EditOrgSkill"), "Organization Edit Profile routes both modal selections correctly");
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
            foreach (var strFile in Directory.EnumerateFiles(strFixtureDirectory)) File.Delete(strFile);
            Directory.Delete(strFixtureDirectory);
        }

        IEnumerable<T> Visible<T>() where T : Control => frmMain.GetVisualDescendants().OfType<T>().Where(ctlControl => ctlControl.IsEffectivelyVisible);
        void Layout()
        {
            ContactRoutingChecks.WaitForTask(vmMain.tskPendingOperation);
            if (vmMain.vmShell.vmCurrentPage is ContactsPageViewModel vmContactsPage)
                ContactRoutingChecks.WaitForTask(vmContactsPage.tskTabChange);
            if (vmMain.vmShell.vmCurrentPage is OrganizationsPageViewModel vmOrganizationsPage)
                ContactRoutingChecks.WaitForTask(vmOrganizationsPage.tskTabChange);
            Dispatcher.UIThread.RunJobs(); frmMain.UpdateLayout();
        }
        void Click(string strName) { Visible<Button>().Single(btnControl => btnControl.Name == strName).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(); }
        void AddOrganizationValue(bool bTags, string strValue)
        {
            Click(bTags ? "btnManageOrganizationTags" : "btnManageOrganizationSkills");
            if (bTags)
            {
                var dlgSelection = frmMain.OwnedWindows.OfType<TagSelectionWindow>().Single();
                var vmSelection = (TagSelectionViewModel)dlgSelection.DataContext!;
                vmSelection.strSearch = strValue; vmSelection.cmdAddValue.Execute(null);
                dlgSelection.FindControl<Button>("btnSaveTagSelection")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            else
            {
                var dlgSelection = frmMain.OwnedWindows.OfType<SkillSelectionWindow>().Single();
                var vmSelection = (SkillSelectionViewModel)dlgSelection.DataContext!;
                vmSelection.strSearch = strValue; vmSelection.cmdAddValue.Execute(null);
                dlgSelection.FindControl<Button>("btnSaveSkillSelection")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            Layout();
        }
        void Check(bool bCondition, string strDescription)
        { if (!bCondition) throw new InvalidOperationException("FAIL " + strDescription); iChecks++; Console.WriteLine("PASS " + strDescription); }
    }
}
