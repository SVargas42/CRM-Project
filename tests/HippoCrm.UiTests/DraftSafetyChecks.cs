using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HippoCrm.Core;
using HippoCrm.Desktop;
using HippoCrm.Desktop.ViewModels;
using HippoCrm.TestSupport;

internal static class DraftSafetyChecks
{
    internal static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var strDirectory = Path.Combine(Path.GetTempPath(), "HippoCrmDraftChecks", Guid.NewGuid().ToString("N"));
        using var objKeys = new EphemeralKeyStore();
        var objDatabase = new Database(Path.Combine(strDirectory, "drafts.db"), objKeys);
        var objAuth = new AuthService(objDatabase);
        using var objSession = objAuth.CreateAdministratorForTest("fixture", "Fictional draft test password!");
        var objCrm = new CrmService(objDatabase, objSession);
        objCrm.Save(new Organization { Name = "Fixture organization" });
        objCrm.Save(new Contact { FirstName = "First fixture" });
        objCrm.Save(new Contact { FirstName = "Second fixture" });
        var vmMain = new MainWindowViewModel(objDatabase);
        Wait(vmMain.tskInitialization);
        var frmMain = new MainWindow(vmMain);
        frmMain.Show();
        var iChecks = 0;
        try
        {
            Check(frmMain.GetVisualDescendants().OfType<TextBlock>().Any(txtControl =>
                AutomationProperties.GetLiveSetting(txtControl) == AutomationLiveSetting.Polite), "Login feedback has live-region metadata");
            Pump();
            var txtUsername = frmMain.GetVisualDescendants().OfType<TextBox>().Single(txtInput => txtInput.Name == "txtUsername");
            txtUsername.Text = "fixture";
            var txtPassword = frmMain.GetVisualDescendants().OfType<SecurePasswordInput>().Single();
            txtPassword.Focus(); frmMain.KeyTextInput("Fictional alignment sample"); Pump();
            foreach (var txtInput in new TextBox[] { txtUsername, txtPassword })
            {
                var txtPresenter = txtInput.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Single();
                var objTop = txtPresenter.TranslatePoint(new Point(0, 0), txtInput)!.Value;
                var decBottom = txtInput.Bounds.Height - objTop.Y - txtPresenter.Bounds.Height;
                Check(Math.Abs(objTop.Y - decBottom) <= 3, txtInput.Name + " renders its text vertically centered");
            }
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (var objFrame = frmMain.CaptureRenderedFrame())
            {
                Directory.CreateDirectory("artifacts/screenshots");
                objFrame?.Save("artifacts/screenshots/login-centered.png");
            }
            vmMain.secPassword.Clear();
            SignIn();
            Execute(vmMain.cmdContacts);
            var vmContacts = (ContactsPageViewModel)vmMain.vmShell.vmCurrentPage;
            vmContacts.iSelectedTab = 1; Wait(vmContacts.tskTabChange);
            Check(!vmMain.bHasUnsavedChanges, "An untouched new contact does not prompt");
            vmMain.objContact!.FirstName = "Unsaved fictional contact";
            vmMain.vmNewContactEmails.strNewValue = "pending@example.test";
            Check(vmMain.bHasUnsavedChanges, "Profile and unadded multi-value text are tracked");
            Execute(vmMain.cmdSaveContact);
            Check(vmMain.objContact.Id == 0 && vmMain.vmNewContactEmails.strNewValue == "pending@example.test"
                && vmMain.strMessage.Contains("Add or clear"), "Save rejects unadded contact details without losing them");
            vmMain.cmdOrganizations.Execute(null);
            Respond(false); Wait(vmMain.tskPendingOperation);
            Check(vmMain.strPage == "Contacts" && vmMain.objContact.FirstName == "Unsaved fictional contact"
                && vmMain.vmNewContactEmails.strNewValue == "pending@example.test", "Keep editing cancels navigation without losing fields");
            vmContacts.iSelectedTab = 0;
            Respond(false); Wait(vmContacts.tskTabChange);
            Check(vmContacts.iSelectedTab == 1 && vmMain.objContact is not null, "Declined tab switch restores Add Contact selection");
            vmContacts.iSelectedTab = 0;
            Respond(true); Wait(vmContacts.tskTabChange);
            Check(vmContacts.iSelectedTab == 0 && vmMain.objContact is null && !vmMain.bHasUnsavedChanges,
                "Confirmed tab switch clears only the abandoned draft");
            var objFirst = vmMain.lstContacts.First();
            var objSecond = vmMain.lstContacts.Last();
            Wait(vmMain.SelectContactAsync(objFirst));
            vmMain.strNote = "Unsubmitted fictional interaction";
            vmMain.strRole = "Unsubmitted placement";
            vmMain.strContractValue = "250.00"; vmMain.strLocation = "Fictional location";
            Execute(vmMain.cmdSaveContact);
            Check(vmMain.strNote == "Unsubmitted fictional interaction" && vmMain.strRole == "Unsubmitted placement"
                && vmMain.strContractValue == "250.00" && vmMain.strLocation == "Fictional location",
                "Saving a contact preserves unfinished interaction, employment and contract fields");
            Check(objCrm.Interactions(objFirst.Id).Count == 0 && objCrm.Contracts(objFirst.Id).Count == 0,
                "Profile save does not silently submit unfinished history");
            var tskSelection = vmMain.SelectContactAsync(objSecond);
            Respond(false); Wait(tskSelection);
            Check(vmMain.objContact!.Id == objFirst.Id && vmMain.strNote.Length > 0, "Declining record switch preserves the original record and note");
            tskSelection = vmMain.SelectContactAsync(objSecond);
            Respond(true); Wait(tskSelection);
            Check(vmMain.objContact!.Id == objSecond.Id && vmMain.strNote == "" && vmMain.strContractValue == "",
                "Confirmed record switch never carries history into another contact");
            vmMain.objContact.FirstName = "";
            vmMain.strNote = "Retain after invalid save";
            Execute(vmMain.cmdSaveContact);
            Check(vmMain.objContact.FirstName == "" && vmMain.strNote == "Retain after invalid save"
                && vmMain.strMessage.Length > 0, "Invalid save retains edits and provides user feedback");
            vmMain.cmdOrganizations.Execute(null); Respond(true); Wait(vmMain.tskPendingOperation);
            var vmOrganizations = (OrganizationsPageViewModel)vmMain.vmShell.vmCurrentPage;
            vmOrganizations.iSelectedTab = 1; Wait(vmOrganizations.tskTabChange);
            vmMain.objOrganization!.Name = "Unsaved organization";
            vmMain.vmNewOrganizationPhones.strNewValue = "555-0100";
            Execute(vmMain.cmdSaveOrganization);
            Check(vmMain.objOrganization.Id == 0 && vmMain.vmNewOrganizationPhones.strNewValue == "555-0100"
                && vmMain.strMessage.Contains("Add or clear"), "Save rejects unadded organization details without losing them");
            frmMain.Close(); Respond(false); Pump();
            Check(frmMain.IsVisible && vmMain.objOrganization.Name == "Unsaved organization", "Keep editing cancels ordinary window closing");
            vmMain.cmdContacts.Execute(null); Respond(false); Wait(vmMain.tskPendingOperation);
            Check(vmMain.strPage == "Organizations", "Organization drafts also block declined navigation");
            // A timed lock must dismiss the prompt and hide records without waiting for consent.
            vmMain.cmdContacts.Execute(null); Pump();
            Check(frmMain.OwnedWindows.Any(frmWindow => frmWindow.Title == "Unsaved changes"), "Navigation opens the existing styled confirmation dialog");
            typeof(MainWindowViewModel).GetField("dtLastActivity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(vmMain, DateTimeOffset.UtcNow.AddMinutes(-16));
            vmMain.CheckInactivity();
            Check(vmMain.bLocking, "Inactivity lock obscures the workspace immediately during a prompt");
            Wait(vmMain.tskPendingOperation);
            PumpUntil(() => vmMain.strPage == "Login" && !vmMain.bLocking);
            Check(vmMain.objContact is null && vmMain.objOrganization is null && !vmMain.bHasUnsavedChanges
                && !frmMain.OwnedWindows.Any(), "Lock clears draft buffers and dismisses confirmation windows");
            SignIn(); Execute(vmMain.cmdContacts);
            vmContacts = (ContactsPageViewModel)vmMain.vmShell.vmCurrentPage;
            vmContacts.iSelectedTab = 1; Wait(vmContacts.tskTabChange);
            vmMain.objContact!.FirstName = "Discard on close";
            frmMain.Close(); Respond(true);
            PumpUntil(() => !frmMain.IsVisible);
            Check(!frmMain.IsVisible && vmMain.strPage == "Login", "Confirmed closing revokes the session before closing");
            Check(objCrm.Contacts().Count == 2, "Discard and lock never persist abandoned records");
            Console.WriteLine($"{iChecks} draft/accessibility checks passed.");
        }
        finally
        {
            vmMain.fnConfirmDiscard = () => Task.FromResult(true);
            Wait(vmMain.CloseAsync()); frmMain.Close(); Pump();
            if (Directory.Exists(strDirectory)) Directory.Delete(strDirectory, true);
        }
        void SignIn()
        {
            vmMain.strUsername = "fixture";
            foreach (var chrCharacter in "Fictional draft test password!") vmMain.secPassword.AppendChar(chrCharacter);
            Execute(vmMain.cmdSignIn);
            Check(vmMain.strPage == "Overview", "Fixture signs in successfully");
        }
        void Execute(System.Windows.Input.ICommand cmdAction)
        {
            cmdAction.Execute(null);
            Wait(((AsyncRelayCommand)cmdAction).tskExecution);
        }
        void Respond(bool bDiscard)
        {
            PumpUntil(() => frmMain.OwnedWindows.Any(frmWindow => frmWindow.Title == "Unsaved changes"));
            var frmDialog = frmMain.OwnedWindows.Single(frmWindow => frmWindow.Title == "Unsaved changes");
            Check(frmDialog.FindControl<Button>("btnCancelArchive")!.IsFocused, "Confirmation defaults keyboard focus to Keep editing");
            frmDialog.FindControl<Button>(bDiscard ? "btnConfirmArchive" : "btnCancelArchive")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
        }
        void Check(bool bCondition, string strDescription)
        {
            if (!bCondition) throw new InvalidOperationException("FAIL " + strDescription);
            iChecks++; Console.WriteLine("PASS " + strDescription);
        }
        void Pump() { Dispatcher.UIThread.RunJobs(); frmMain.UpdateLayout(); }
        void PumpUntil(Func<bool> fnComplete)
        {
            var objTimeout = System.Diagnostics.Stopwatch.StartNew();
            while (!fnComplete())
            {
                Pump();
                if (objTimeout.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException("Draft check did not finish. Page=" + vmMain.strPage + "; Busy=" + vmMain.bBusy + "; Message=" + vmMain.strMessage);
                Thread.Sleep(1);
            }
            Pump();
        }
    }
    private static void Wait(Task tskWork) => ContactRoutingChecks.WaitForTask(tskWork);
}
