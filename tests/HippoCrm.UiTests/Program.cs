using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HippoCrm.Core;
using HippoCrm.Desktop;
using Microsoft.Data.Sqlite;
using HippoCrm.TestSupport;
using HippoCrm.Desktop.ViewModels;

if (args.Contains("--draft-photo-save")) { DraftPhotoSaveChecks.Run(); return; }
if (args.Contains("--enter-submit")) { EnterSubmitChecks.Run(); return; }
if (args.Contains("--analytics-tables")) { AnalyticsTableChecks.Run(); return; }
if (args.Contains("--organization-lifecycle")) { OrganizationLifecycleViewChecks.Run(); return; }
if (args.Contains("--cui-security")) { CuiPresentationChecks.Run(); return; }
if (args.Contains("--navigation-hydration")) { NavigationHydrationChecks.Run(); return; }

#if DEBUG
if (args.Contains("--photo-duplicates-ui")) { PhotoDuplicateUiChecks.Run(); return; }
if (args.Length == 2 && args[0] == "--ui-lease-probe")
{
    Environment.ExitCode = UiPersistenceChecks.ProbeLease(args[1]);
    return;
}
if (args.Contains("--ui-persistence-checks"))
{
    UiPersistenceChecks.Run();
    return;
}
#endif

if (args.Contains("--ui-development-checks"))
{
    UiDevelopmentChecks.Run();
    return;
}

if (args.Contains("--phase3-security"))
{
    PhaseThreeViewChecks.Run();
    return;
}

if (args.Contains("--phase2-security"))
{
    PhaseTwoViewChecks.Run();
    return;
}

if (args.Contains("--phase1-security"))
{
    var bAppDenied = false;
    try { new App().Initialize(); }
    catch (System.Security.Cryptography.CryptographicException) { bAppDenied = true; }
    if (!bAppDenied) throw new InvalidOperationException("Unprovisioned Avalonia initialization was accepted.");
    Console.WriteLine("PASS Direct Avalonia initialization is denied before XAML/platform initialization");
    var vmUnprovisioned = new MainWindowViewModel();
    try
    {
        vmUnprovisioned.tskInitialization.GetAwaiter().GetResult();
        if (vmUnprovisioned.strPage != "Error" || vmUnprovisioned.bCanCreate || vmUnprovisioned.lstContacts.Count != 0)
            throw new InvalidOperationException("Unprovisioned UI workspace was exposed.");
        Console.WriteLine("PASS Direct production ViewModel initialization exposes no workspace");
    }
    finally { vmUnprovisioned.CloseAsync().GetAwaiter().GetResult(); }
    return;
}

if (args.Contains("--draft-safety"))
{
    DraftSafetyChecks.Run();
    return;
}

if (args.Contains("--token-selection"))
{
    TokenSelectionChecks.Run();
    return;
}
if (args.Contains("--master-search"))
{
    MasterSearchChecks.Run();
    return;
}
if (args.Contains("--contacts-routing"))
{
    ContactRoutingChecks.Run();
    return;
}

var directory = Path.Combine(Path.GetTempPath(),"HippoCrmUiTests",Guid.NewGuid().ToString("N"));
var screenshots = Path.GetFullPath("artifacts/screenshots"); Directory.CreateDirectory(screenshots);
AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
using var objKeyStore = new EphemeralKeyStore();
var database = new Database(Path.Combine(directory,"ui.db"), objKeyStore);
var window = new MainWindow(database); window.Show();
var checks = 0;
try
{
    Layout();
    Check(window.WindowState == WindowState.Maximized, "Main window launches maximized");
    Check(double.IsNaN(window.Width) && double.IsNaN(window.Height)
        && double.IsPositiveInfinity(window.MaxWidth) && double.IsPositiveInfinity(window.MaxHeight)
        && window.MinWidth == 0 && window.MinHeight == 0, "Window dimensions do not constrain the available screen");
    Check(Text("Set up your workspace"),"First-run screen renders"); Capture("01-setup.png");
    Check(Controls<TextBox>().All(txtInput => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(txtInput))), "Setup fields expose accessible names");
    EnterPassword(Controls<SecurePasswordInput>().Single(txtInput => txtInput.Name == "txtPassword"), "Local UI test password!");
    EnterPassword(Controls<SecurePasswordInput>().Single(txtInput => txtInput.Name == "txtConfirmPassword"), "Different UI test password!");
    Click("Create workspace");
    Check(new AuthService(database).NeedsSetup && Text("Passwords do not match."), "Mismatched passwords do not create an administrator");
    EnterPassword(Controls<SecurePasswordInput>().Single(txtInput => txtInput.Name == "txtPassword"), "Local UI test password!");
    EnterPassword(Controls<SecurePasswordInput>().Single(txtInput => txtInput.Name == "txtConfirmPassword"), "Local UI test password!");
    Click("Create workspace");
    Check(Controls<HippoCrm.Desktop.Views.DashboardView>().Any(),"Administrator setup opens the dashboard"); Capture("02-overview-light.png");
    SelectDashboardTab("Analytics"); Capture("13-analytics.png");
    SelectDashboardTab("Master Search"); Capture("14-master-search.png");
    SelectDashboardTab("Overview");
    Click("Organizations"); Capture("07-organizations-lookup.png"); SelectTab("Add Organization"); Capture("08-organization-editor.png");
    Check(Controls<TextBox>().All(txtInput => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(txtInput))), "Organization fields expose accessible names");
    Check(Controls<TextBox>().Single(txtInput => txtInput.Name == "txtNewOrganizationEmail").Bounds.Width >= 400, "Organization entry fields use the available form width");
    Check(HasAlignedFieldRow("Emails", "txtNewOrganizationEmail", "btnAddOrganizationEmail")
        && HasAlignedFieldRow("Phones", "txtNewOrganizationPhone", "btnAddOrganizationPhone")
        && HasAlignedFieldRow("Addresses", "txtNewOrganizationAddress", "btnAddOrganizationAddress"),
        "Organization entry fields align with their labels and keep Add buttons beside them");
    Controls<TextBox>().Single(t => Equals(t.Tag,"Organization name *")).Text = "Northstar Services";
    Controls<TextBox>().Single(txtInput => txtInput.Name == "txtNewOrganizationEmail").Text = "office@example.test";
    var btnAddOrganizationEmail = Controls<Button>().Single(btn => btn.Name == "btnAddOrganizationEmail");
    btnAddOrganizationEmail.Command!.Execute(btnAddOrganizationEmail.CommandParameter); Layout();
    Click("Save Organization");
    var auth = new AuthService(database); var session = auth.SignInForTest("admin","Local UI test password!"); var crm = new CrmService(database,session);
    Capture("09-organization-profile.png");
    Click("Back"); Capture("15-organizations-with-record.png"); Click("View Profile");
    Check(crm.Organizations().Single() is { Name: "Northstar Services", Email: "office@example.test" },"Organization editor saves to SQLite");
    Click("Contacts"); Capture("10-contacts-lookup.png"); SelectTab("Add Contact"); Capture("11-contact-editor.png");
    Check(Controls<TextBox>().All(txtInput => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(txtInput))), "Contact fields expose accessible names");
    Check(!Controls<TabControl>().Any(tabControl => tabControl.Name == "tabContactInformation")
        && Text("Name") && Text("Contact Information") && Text("Personal Information") && Text("Extra Information"),
        "Add Contact is one page with all requested information sections");
    Check(Controls<TextBox>().Single(txtInput => txtInput.Name == "txtNewContactEmail").Bounds.Width >= 400, "Contact entry fields use the available form width");
    Check(HasAlignedFieldRow("Middle name", "txtAddContactMiddleName", "btnAddMiddleName"),
        "Add middle name sits beside its field");
    Check(Math.Abs(TopLeft(Controls<ComboBox>().Single(cboControl => cboControl.Name == "cboContactPrefix")).X
        - TopLeft(Controls<TextBlock>().Single(txtLabel => txtLabel.Text == "Prefix")).X) <= 1
        && Math.Abs(TopLeft(Controls<ComboBox>().Single(cboControl => cboControl.Name == "cboContactSuffix")).X
        - TopLeft(Controls<TextBlock>().Single(txtLabel => txtLabel.Text == "Suffix")).X) <= 1,
        "Prefix and suffix dropdowns align with their labels");
    Check(HasAlignedTextField("Street 1", "txtAddContactStreet1")
        && HasAlignedTextField("Street 2", "txtAddContactStreet2"),
        "Structured address fields align with their labels");
    var vmAddContact = (MainWindowViewModel)window.DataContext!;
    Controls<TextBox>().Single(txtInput => txtInput.Name == "txtFirstName").Text = "Alex";
    Controls<TextBox>().Single(txtInput => txtInput.Name == "txtLastName").Text = "Rivera";
    Controls<TextBox>().Single(txtInput => txtInput.Name == "txtAddContactStreet1").Text = "100 Main Street";
    Controls<TextBox>().Single(txtInput => txtInput.Name == "txtAddContactCity").Text = "Dallas";
    Controls<TextBox>().Single(txtInput => txtInput.Name == "txtAddContactState").Text = "TX";
    Controls<TextBox>().Single(txtInput => txtInput.Name == "txtAddContactZip").Text = "75201";
    Controls<TextBox>().Single(txtInput => txtInput.Name == "txtAddContactNation").Text = "United States";
    Controls<TextBox>().Single(txtInput => txtInput.Name == "txtNewContactEmail").Text = "alex@example.test";
    vmAddContact.vmNewContactEmails.cmdAdd.Execute(null);
    vmAddContact.strNewSocialMediaValue = "@alexrivera"; vmAddContact.cmdAddContactSocialMedia.Execute(null);
    vmAddContact.AddSocialMediaPlatform("ExampleNet");
    vmAddContact.strNewSocialMediaValue = "alex.example"; vmAddContact.cmdAddContactSocialMedia.Execute(null);
    vmAddContact.vmNewContactSpouse.strNewValue = "Jordan Rivera"; vmAddContact.vmNewContactSpouse.cmdAdd.Execute(null);
    vmAddContact.vmNewContactChildren.strNewValue = "Casey Rivera"; vmAddContact.vmNewContactChildren.cmdAdd.Execute(null);
    vmAddContact.vmNewContactFamily.strNewValue = "Taylor Rivera"; vmAddContact.vmNewContactFamily.cmdAdd.Execute(null);
    vmAddContact.vmNewContactFriends.strNewValue = "Morgan Lee"; vmAddContact.vmNewContactFriends.cmdAdd.Execute(null);
    Click("Save contact");
    Check(crm.Contacts().Single() is { DisplayName: "Alex Rivera", Email: "alex@example.test", Spouse: "Jordan Rivera", Children: "Casey Rivera", Family: "Taylor Rivera", Friends: "Morgan Lee" }
        && crm.Contacts().Single().Address.Contains("Home: 100 Main Street, Dallas TX 75201, United States", StringComparison.Ordinal)
        && crm.Contacts().Single().SocialMedia.Contains("LinkedIn", StringComparison.Ordinal)
        && crm.Contacts().Single().SocialMedia.Contains("ExampleNet", StringComparison.Ordinal),
        "Single-page contact form saves structured address, social media, and personal details");
    var id = crm.Contacts().Single().Id;
    Capture("03-contact-light.png");
    Click("Back"); Capture("16-contacts-with-record.png"); Click("View Profile");
    Click("Edit Profile");
    Capture("12-contact-editor-profile.png");
    SelectTab("Personal"); Check(Text("Spouse / partner"),"Personal details tab opens");
    SelectTab("Social"); Check(Text("Twitter / X"),"Social profiles tab opens");
    SelectTab("Interactions");
    Controls<TextBox>().Single(txtControl => txtControl.Name == "txtInteractionNote").Text = "Discussed upcoming availability.";
    Click("Record interaction");
    Check(crm.Interactions(id).Count == 1,"Interaction editor saves an authored note");
    Capture("04-interactions.png");
    SelectTab("Employment"); Check(Text("Employment history"),"Employment editor opens");
    Controls<ComboBox>().Single(cboControl => cboControl.Name == "cboEmployer").SelectedIndex = 0;
    Controls<TextBox>().Single(txtControl => txtControl.Name == "txtEmploymentRole").Text = "Operations specialist";
    Click("Add placement"); Check(crm.EmploymentHistory(id).Single().Role == "Operations specialist","Employment form saves a placement");
    SelectTab("Contracts"); Check(Text("Contract history"),"Contract editor opens");
    Controls<TextBox>().Single(txtControl => txtControl.Name == "txtContractValue").Text = "12345.67";
    Controls<TextBox>().Single(txtControl => txtControl.Name == "txtContractLocation").Text = "Dallas";
    Click("Add contract"); Check(crm.Contracts(id).Single().Value == 12345.67m,"Contract form saves an exact value");
    SelectTab("Profile"); Click("Archive contact"); ConfirmArchive();
    Check(crm.Contacts().Count == 0 && crm.Contacts(bArchived:true).Count == 1,"Archive action preserves the contact");
    Controls<CheckBox>().Single(c => Equals(c.Content,"Archived records")).IsChecked = true; Click("Apply filters");
    Click("View Profile"); Click("Edit Profile"); Click("Restore contact"); ConfirmArchive();
    Check(crm.Contacts().Count == 1,"Restore action returns the contact to active search");
    Controls<ToggleSwitch>().Single().IsChecked = true; Layout();
    Check(Application.Current!.RequestedThemeVariant == ThemeVariant.Dark,"Theme control enables dark mode");
    Click("Dashboard"); Capture("05-overview-dark.png");
    window.Width = 800; window.Height = 680; Click("Contacts");
    Click("View Profile"); Layout(); Capture("06-contact-compact.png");
    Check(Controls<ScrollViewer>().Any(scrControl => scrControl.Name == "scrContactProfile" && scrControl.Bounds.Width > 0),"Read-only contact profile lays out at the minimum window size");
    Click("Back"); SelectTab("Add Contact"); Layout();
    Check(Controls<TextBox>().Single(txtInput => txtInput.Name == "txtNewContactEmail").Bounds.Width >= 300, "Contact entry fields remain usable at compact window size");
    Capture("17-contact-editor-compact.png");
    Click("Lock workspace"); Check(Text("Welcome back"),"Lock action clears the authenticated workspace");
    Check(!Controls<TextBox>().Any(txtInput => txtInput.Name == "txtUsername"), "Unlock requires only a password after account creation");
    EnterPassword(Controls<SecurePasswordInput>().Single(txtInput => txtInput.Name == "txtPassword"), "Local UI test password!"); Click("Sign in");
    Check(Controls<HippoCrm.Desktop.Views.DashboardView>().Any(),"Existing administrator can sign back in");
    Console.WriteLine($"{checks} UI checks passed. Screenshots: {screenshots}");
}
finally
{
    ContactRoutingChecks.WaitForTask(((MainWindowViewModel)window.DataContext!).CloseAsync());
    window.Close(); SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory,true);
}

IEnumerable<T> Controls<T>() where T : Control => window.GetVisualDescendants().OfType<T>().Where(ctlControl => ctlControl.IsEffectivelyVisible);
bool Text(string text) => Controls<TextBlock>().Any(t => t.Text == text);
Point TopLeft(Control control) => control.TranslatePoint(new Point(0, 0), window) ?? throw new InvalidOperationException($"Could not locate {control.Name} in the window.");
bool HasAlignedFieldRow(string label, string fieldName, string buttonName)
{
    var textField = Controls<TextBox>().Single(txtInput => txtInput.Name == fieldName);
    var addButton = Controls<Button>().Single(btnControl => btnControl.Name == buttonName);
    var fieldLeft = TopLeft(textField).X;
    var labelLeft = TopLeft(Controls<TextBlock>().Single(txtLabel => txtLabel.Text == label)).X;
    var buttonGap = TopLeft(addButton).X - fieldLeft - textField.Bounds.Width;
    return Math.Abs(fieldLeft - labelLeft) <= 1 && Math.Abs(buttonGap - 8) <= 1;
}
bool HasAlignedTextField(string label, string fieldName)
{
    var textField = Controls<TextBox>().Single(txtInput => txtInput.Name == fieldName);
    var fieldLeft = TopLeft(textField).X;
    var labelLeft = TopLeft(Controls<TextBlock>().Single(txtLabel => txtLabel.Text == label)).X;
    return Math.Abs(fieldLeft - labelLeft) <= 1;
}
void Layout()
{
    var vmWorkspace = (MainWindowViewModel)window.DataContext!;
    ContactRoutingChecks.WaitForTask(vmWorkspace.tskInitialization);
    ContactRoutingChecks.WaitForTask(vmWorkspace.tskPendingOperation);
    ContactRoutingChecks.WaitForTask(vmWorkspace.tskThemeSave);
    ContactRoutingChecks.WaitForTask(((AsyncRelayCommand)vmWorkspace.cmdLock).tskExecution);
    ContactRoutingChecks.WaitForTask(((AsyncRelayCommand)vmWorkspace.vmShell.cmdGlobalBack).tskExecution);
    if (vmWorkspace.vmShell.vmCurrentPage is ContactsPageViewModel { vmEdit: { } vmEdit })
    {
        ContactRoutingChecks.WaitForTask(((AsyncRelayCommand)vmEdit.cmdSave).tskExecution);
        ContactRoutingChecks.WaitForTask(((AsyncRelayCommand)vmEdit.cmdCancel).tskExecution);
        ContactRoutingChecks.WaitForTask(((AsyncRelayCommand)vmEdit.cmdArchive).tskExecution);
    }
    if (vmWorkspace.vmShell.vmCurrentPage is ContactsPageViewModel vmContactsPage)
    {
        ContactRoutingChecks.WaitForTask(vmContactsPage.tskTabChange);
        ContactRoutingChecks.WaitForTask(vmContactsPage.tskSearch);
    }
    if (vmWorkspace.vmShell.vmCurrentPage is OrganizationsPageViewModel vmOrganizationsPage)
    {
        ContactRoutingChecks.WaitForTask(vmOrganizationsPage.tskTabChange);
        ContactRoutingChecks.WaitForTask(vmOrganizationsPage.tskSearch);
    }
    Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
}
void Click(string strLabel)
{
    Layout();
    var btnAction = Controls<Button>().Single(btnCurrent => Equals(btnCurrent.Content, strLabel)
        || btnCurrent.Content is Control ctlContent
            && ctlContent.GetVisualDescendants().OfType<TextBlock>().Any(txtContent => txtContent.Text == strLabel));
    if (btnAction.Command is null)
    {
        btnAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(); return;
    }
    if (!btnAction.Command.CanExecute(btnAction.CommandParameter))
        throw new InvalidOperationException("Bound command is unavailable: " + strLabel);
    btnAction.Command.Execute(btnAction.CommandParameter);
    Layout();
}
void ConfirmArchive()
{
    window.OwnedWindows.OfType<HippoCrm.Desktop.Views.ArchiveContactDialog>().Single()
        .FindControl<Button>("btnConfirmArchive")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Layout();
}
void SelectTab(string header) { Controls<TabControl>().Single().SelectedItem = Controls<TabControl>().Single().Items.Cast<TabItem>().Single(t => Equals(t.Header,header)); Layout(); }
void SelectDashboardTab(string header)
{
    var tcCurrent = Controls<TabControl>().Single(tcControl => tcControl.Name == "tcDashboard");
    tcCurrent.SelectedItem = tcCurrent.Items.Cast<TabItem>().Single(tiCurrent => Equals(tiCurrent.Header, header));
    Layout();
}
void Check(bool condition,string name) { if (!condition) throw new Exception("FAIL " + name + " | " + string.Join(" / ",Controls<TextBlock>().Select(t => t.Text))); checks++; Console.WriteLine("PASS " + name); }
void Capture(string name)
{
    Layout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using var frame = window.CaptureRenderedFrame();
    if (frame == null) throw new Exception("No rendered frame.");
    frame.Save(Path.Combine(screenshots,name));
}

void EnterPassword(SecurePasswordInput txtPassword, string strFictionalPassword)
{
    Layout();
    Check(txtPassword.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Any(), "Password field renders its text-entry template");
    Check(!txtPassword.IsReadOnly && txtPassword.PasswordChar == SecurePasswordInput.chrMask && txtPassword.Focus(), "Masked password field accepts keyboard focus");
    window.KeyTextInput(strFictionalPassword);
    Check(txtPassword.Text == new string(SecurePasswordInput.chrMask, strFictionalPassword.Length), "Password control stores only masking characters");
}
