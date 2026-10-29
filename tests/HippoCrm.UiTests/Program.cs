using Avalonia;
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
    Controls<TextBox>().Single(txtInput => txtInput.Name == "txtUsername").Text = "admin";
    EnterPassword(Controls<SecurePasswordInput>().Single(txtInput => txtInput.Name == "txtPassword"), "Local UI test password!");
    EnterPassword(Controls<SecurePasswordInput>().Single(txtInput => txtInput.Name == "txtConfirmPassword"), "Different UI test password!");
    Click("Create workspace");
    Check(new AuthService(database).NeedsSetup && Text("Passwords do not match."), "Mismatched passwords do not create an administrator");
    EnterPassword(Controls<SecurePasswordInput>().Single(txtInput => txtInput.Name == "txtPassword"), "Local UI test password!");
    EnterPassword(Controls<SecurePasswordInput>().Single(txtInput => txtInput.Name == "txtConfirmPassword"), "Local UI test password!");
    Click("Create workspace");
    Check(Text("Workspace overview"),"Administrator setup opens the dashboard"); Capture("02-overview-light.png");
    Click("Organizations"); Click("+ Add organization");
    Controls<TextBox>().Single(t => Equals(t.Tag,"Organization name *")).Text = "Northstar Services";
    Controls<TextBox>().Single(t => Equals(t.Tag,"Email")).Text = "office@example.test";
    Click("Save organization");
    var auth = new AuthService(database); var session = auth.SignInForTest("admin","Local UI test password!"); var crm = new CrmService(database,session);
    Check(crm.Organizations().Single().Name == "Northstar Services","Organization editor saves to SQLite");
    Click("Contacts"); Click("+ Add contact");
    Controls<TextBox>().Single(t => Equals(t.Tag,"First name *")).Text = "Alex";
    Controls<TextBox>().Single(t => Equals(t.Tag,"Last name")).Text = "Rivera";
    Click("Save contact");
    Check(crm.Contacts().Single().DisplayName == "Alex Rivera","Contact editor saves to SQLite");
    var id = crm.Contacts().Single().Id;
    Capture("03-contact-light.png");
    SelectTab("Personal"); Check(Text("Spouse / partner"),"Personal details tab opens");
    SelectTab("Social"); Check(Text("Twitter / X"),"Social profiles tab opens");
    SelectTab("Interactions");
    Controls<TextBox>().Single(t => t.Watermark?.Contains("Record a conversation") == true).Text = "Discussed upcoming availability.";
    Click("Record interaction");
    Check(crm.Interactions(id).Count == 1,"Interaction editor saves an authored note");
    Capture("04-interactions.png");
    SelectTab("Employment"); Check(Text("Employment history"),"Employment editor opens");
    Controls<ComboBox>().Single(c => Equals(c.Tag,"Organization")).SelectedIndex = 0;
    Controls<TextBox>().Single(t => Equals(t.Tag,"Role")).Text = "Operations specialist";
    Click("Add placement"); Check(crm.EmploymentHistory(id).Single().Role == "Operations specialist","Employment form saves a placement");
    SelectTab("Contracts"); Check(Text("Contract history"),"Contract editor opens");
    Controls<TextBox>().Single(t => Equals(t.Tag,"Contract value")).Text = "12345.67";
    Controls<TextBox>().Single(t => Equals(t.Tag,"Location")).Text = "Dallas";
    Click("Add contract"); Check(crm.Contracts(id).Single().Value == 12345.67m,"Contract form saves an exact value");
    SelectTab("Profile"); Click("Archive contact");
    Check(crm.Contacts().Count == 0 && crm.Contacts(bArchived:true).Count == 1,"Archive action preserves the contact");
    Controls<CheckBox>().Single(c => Equals(c.Content,"Archived records")).IsChecked = true; Click("Apply filters");
    Controls<ListBox>().Single().SelectedIndex = 0; Layout(); Click("Restore contact");
    Check(crm.Contacts().Count == 1,"Restore action returns the contact to active search");
    Controls<ToggleSwitch>().Single().IsChecked = true; Layout();
    Check(Application.Current!.RequestedThemeVariant == ThemeVariant.Dark,"Theme control enables dark mode");
    Click("Overview"); Capture("05-overview-dark.png");
    window.Width = 1080; window.Height = 700; Click("Contacts");
    Controls<ListBox>().Single().SelectedIndex = 0; Layout(); Capture("06-contact-compact.png");
    Check(Controls<TabControl>().Single().Bounds.Width > 0,"Contact editor lays out at the minimum window size");
    Click("Lock workspace"); Check(Text("Welcome back"),"Lock action clears the authenticated workspace");
    Controls<TextBox>().Single(txtInput => txtInput.Name == "txtUsername").Text = "admin";
    EnterPassword(Controls<SecurePasswordInput>().Single(txtInput => txtInput.Name == "txtPassword"), "Local UI test password!"); Click("Sign in");
    Check(Text("Workspace overview"),"Existing administrator can sign back in");
    Console.WriteLine($"{checks} UI checks passed. Screenshots: {screenshots}");
}
finally
{
    window.Close(); SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory,true);
}

IEnumerable<T> Controls<T>() where T : Control => window.GetVisualDescendants().OfType<T>();
bool Text(string text) => Controls<TextBlock>().Any(t => t.Text == text);
void Layout() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
void Click(string strLabel)
{
    Layout();
    var btnAction = Controls<Button>().Single(btnCurrent => Equals(btnCurrent.Content, strLabel));
    if (btnAction.Command is null || !btnAction.Command.CanExecute(btnAction.CommandParameter))
        throw new InvalidOperationException("Bound command is unavailable: " + strLabel);
    btnAction.Command.Execute(btnAction.CommandParameter);
    Layout();
}
void SelectTab(string header) { Controls<TabControl>().Single().SelectedItem = Controls<TabControl>().Single().Items.Cast<TabItem>().Single(t => Equals(t.Header,header)); Layout(); }
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
    Check(!txtPassword.IsReadOnly && txtPassword.PasswordChar == '●' && txtPassword.Focus(), "Masked password field accepts keyboard focus");
    window.KeyTextInput(strFictionalPassword);
    Check(txtPassword.Text == new string('●', strFictionalPassword.Length), "Password control stores only masking characters");
}
