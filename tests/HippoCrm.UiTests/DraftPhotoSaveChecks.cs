using System.Reflection;
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
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

internal static class DraftPhotoSaveChecks
{
    internal static void Run()
    {
        var iChecks = 0;
        UiDevelopmentChecks.Invoke("TryEnable", (object)new[] { "--ui-development" });
        UiDevelopmentChecks.Invoke("ConfigureWorkspaceFactory", (Func<Database>)Database.CreateUiDevelopmentWorkspace);
        AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var vmMain = new MainWindowViewModel(); Complete(vmMain.tskInitialization);
        var frmMain = new MainWindow(vmMain); frmMain.Show();
        try
        {
            foreach (var chrValue in "FICTIONAL draft picture password!") { vmMain.secPassword.AppendChar(chrValue); vmMain.secConfirmation.AppendChar(chrValue); }
            Execute(vmMain.cmdSignIn);
            var objDatabase = (Database)typeof(MainWindowViewModel).GetField("objDatabase", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vmMain)!;
            using var secPassword = TestPasswords.Secret("FICTIONAL draft picture password!");
            using var objSession = new AuthService(objDatabase).SignIn(secPassword);
            var objCrm = new CrmService(objDatabase, objSession);
            Execute(vmMain.cmdContacts);
            var vmContacts = (ContactsPageViewModel)vmMain.vmShell.vmCurrentPage;
            vmContacts.iSelectedTab = 1; Complete(vmContacts.tskTabChange); Layout();
            Check(vmMain.objContact is { Id: 0 } && vmMain.bCanUploadContactImage, "Real Add Contact tab opens an editable draft with photo selection enabled");
            Check(Visible<ProfilePicturePanel>().Single().GetVisualDescendants().OfType<Button>().Single().IsEffectivelyEnabled, "Add Contact contains an enabled photo picker");
            var arrInput = Picture(); Complete(vmMain.ImportProfileImageAsync(true, ctCancellation => Task.FromResult<byte[]?>(arrInput)));
            var arrPending = vmMain.arrContactImage!;
            Check(arrInput.All(bytValue => bytValue == 0) && arrPending.Length > 0 && vmMain.bHasUnsavedChanges, "Draft photo is sanitized, previewed and tracked without saving a record");
            Check(objCrm.Contacts(enStatus: null).Count == 0, "Choosing a draft photo does not create an empty contact");
            Complete(vmMain.ImportProfileImageAsync(true, ctCancellation => Task.FromResult<byte[]?>(null)));
            Check(ReferenceEquals(arrPending, vmMain.arrContactImage), "Canceling selection preserves the draft photo");
            Complete(vmMain.ImportProfileImageAsync(true, ctCancellation => Task.FromResult<byte[]?>(new byte[] { 1, 2, 3 })));
            Check(ReferenceEquals(arrPending, vmMain.arrContactImage), "Malformed replacement preserves the valid draft photo");
            vmMain.objContact!.FirstName = "FICTIONAL draft"; vmMain.objContact.LastName = "Contact";
            vmMain.vmNewContactEmails.strNewValue = "invalid-email"; Save("btnSaveContact");
            Check(vmMain.objContact.Id == 0 && vmMain.vmNewContactEmails.strNewValue == "invalid-email" && vmMain.arrContactImage is not null, "Invalid pending email blocks Save without losing draft fields or photo");
            Check(Visible<TextBlock>().Any(txtValue => txtValue.Text == vmMain.strMessage) && vmMain.strMessage.Length > 0, "Save feedback is available inside the current form");
            vmMain.vmNewContactEmails.strNewValue = "person@EXAMPLE.TEST";
            vmMain.vmNewContactPhones.strNewValue = "+44 20 7946 0958";
            vmMain.vmNewContactSpouse.strNewValue = "FICTIONAL partner";
            vmMain.objContact.Status = RecordStatus.Inactive;
            Save("btnSaveContact");
            Check(vmMain.objContact is { Id: > 0, Status: RecordStatus.Inactive }, $"Save opens the newly saved Inactive contact even when lookup defaults to Active (id={vmMain.objContact?.Id}, status={vmMain.objContact?.Status}, message={vmMain.strMessage})");
            var lngContact = vmMain.objContact!.Id;
            var objContact = objCrm.ContactRecord(lngContact);
            Check(objContact.Email == "person@example.test" && objContact.Phone == "+442079460958" && objContact.Spouse == "FICTIONAL partner", "Save validates and includes typed email, international phone and personal details without separate Add clicks");
            using (var objPhoto = new ProfileImageService(objDatabase, objSession).ReadContactPicture(lngContact))
                Check(objPhoto is { arrPng.Length: > 0 }, "Draft contact photo is saved to its authorized encrypted-workspace BLOB");
            Check(arrPending.All(bytValue => bytValue == 0) && !vmMain.bHasPendingImage, "Committed draft preview is cleared and pending ownership is released");
            Execute(vmMain.cmdOrganizations);
            var vmOrganizations = (OrganizationsPageViewModel)vmMain.vmShell.vmCurrentPage;
            vmOrganizations.iSelectedTab = 1; Complete(vmOrganizations.tskTabChange); Layout();
            Check(vmMain.bCanUploadOrganizationImage && Visible<ProfilePicturePanel>().Single().GetVisualDescendants().OfType<Button>().Single().IsEffectivelyEnabled, "New organization has an enabled logo picker");
            vmMain.objOrganization!.Name = "FICTIONAL draft company";
            vmMain.vmNewOrganizationEmails.strNewValue = "office@EXAMPLE.TEST";
            var arrLogo = LocalImageImport.ReadAsync(Path.Combine(Directory.GetCurrentDirectory(), "src", "HippoCrm.Desktop", "Assets", "HippoLogo.png")).GetAwaiter().GetResult();
            Complete(vmMain.ImportProfileImageAsync(false, ctCancellation => Task.FromResult<byte[]?>(arrLogo)));
            Save("btnSaveOrganization");
            Check(vmMain.objOrganization is { Id: > 0 } && objCrm.OrganizationRecord(vmMain.objOrganization.Id).Email == "office@example.test", "Organization Save includes pending validated details");
            using (var objLogo = new ProfileImageService(objDatabase, objSession).ReadOrganizationLogo(vmMain.objOrganization!.Id))
                Check(objLogo is { arrPng.Length: > 0 } && arrLogo.All(bytValue => bytValue == 0), "Draft company logo is saved and original input cleared");
            Execute(vmMain.cmdContacts); Execute(vmMain.cmdNewContact);
            Complete(vmMain.ImportProfileImageAsync(true, ctCancellation => Task.FromResult<byte[]?>(Picture())));
            var arrLocked = vmMain.arrContactImage!;
            Execute(vmMain.cmdLock);
            Check(vmMain.arrContactImage is null && !vmMain.bHasPendingImage && arrLocked.All(bytValue => bytValue == 0), "Lock discards and wipes an unsaved draft photo");
            Console.WriteLine($"{iChecks} draft photo and save checks passed.");
        }
        finally { Complete(vmMain.CloseAsync()); frmMain.Close(); UiDevelopmentChecks.Invoke("DisposeWorkspace"); }
        void Check(bool bCondition, string strMessage) { if (!bCondition) throw new InvalidOperationException("FAIL " + strMessage); iChecks++; Console.WriteLine("PASS " + strMessage); }
        void Save(string strName) { Layout(); var btnSave = Visible<Button>().Single(btnValue => btnValue.Name == strName); if (!btnSave.IsEffectivelyEnabled || btnSave.Command?.CanExecute(btnSave.CommandParameter) != true) throw new InvalidOperationException("Save control is not executable."); btnSave.Command.Execute(btnSave.CommandParameter); Complete(vmMain.tskPendingOperation); Layout(); }
        IEnumerable<T> Visible<T>() where T : Control => frmMain.GetVisualDescendants().OfType<T>().Where(ctlValue => ctlValue.IsEffectivelyVisible);
        void Layout() { Dispatcher.UIThread.RunJobs(); frmMain.UpdateLayout(); }
        void Execute(System.Windows.Input.ICommand cmdAction) => UiDevelopmentChecks.Execute(cmdAction);
        void Complete(Task tskWork) => UiDevelopmentChecks.Complete(tskWork);
    }
    private static byte[] Picture() { using var imgPicture = new Image<Rgba32>(2, 2); using var stmPicture = new MemoryStream(); imgPicture.SaveAsPng(stmPicture); return stmPicture.ToArray(); }
}