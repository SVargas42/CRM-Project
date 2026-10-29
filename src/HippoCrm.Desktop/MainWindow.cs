using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using HippoCrm.Core;
using HippoCrm.Desktop.ViewModels;

namespace HippoCrm.Desktop;

// Presentation only: controls, bindings, theme, and input forwarding. No SQL/authentication workflows.
public sealed class MainWindow : Window
{
    private readonly MainWindowViewModel vmMain;
    private readonly DispatcherTimer tmrIdle = new() { Interval = TimeSpan.FromSeconds(30) };
    private ContentControl ctrEditor = new();

    public MainWindow(Database? objDatabase = null) : this(new MainWindowViewModel(objDatabase)) { }

    public MainWindow(MainWindowViewModel vmMain)
    {
        this.vmMain = vmMain;
        DataContext = vmMain;
        Title = "Swollen Hippo • CRM";
        WindowState = WindowState.Maximized;
        vmMain.PropertyChanged += OnViewModelChanged;
        AddHandler(PointerPressedEvent, (objSender, objArguments) => vmMain.RecordActivity(), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, (objSender, objArguments) => vmMain.RecordActivity(), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // Kept on the dispatcher for Step 1; Step 3 will replace the scheduling mechanism.
        tmrIdle.Tick += (objSender, objArguments) => vmMain.CheckInactivity();
        tmrIdle.Start();
        Closed += (objSender, objArguments) => { tmrIdle.Stop(); vmMain.PropertyChanged -= OnViewModelChanged; vmMain.Close(); };
        RenderPage();
    }

    private void OnViewModelChanged(object? objSender, PropertyChangedEventArgs objArguments)
    {
        if (objArguments.PropertyName == nameof(vmMain.bDark))
            Application.Current!.RequestedThemeVariant = vmMain.bDark ? ThemeVariant.Dark : ThemeVariant.Light;
        else if (objArguments.PropertyName == nameof(vmMain.strPage) ||
                 (objArguments.PropertyName == nameof(vmMain.secPassword) && vmMain.strPage == "Login") ||
                 (objArguments.PropertyName == nameof(vmMain.bSetup) && vmMain.strPage == "Login")) RenderPage();
        else if (objArguments.PropertyName == nameof(vmMain.objContact) && vmMain.strPage == "Contacts") RenderContact();
        else if (objArguments.PropertyName == nameof(vmMain.objOrganization) && vmMain.strPage == "Organizations") RenderOrganization();
    }

    private void RenderPage()
    {
        if (vmMain.strPage == "Error")
        {
            Content = Ui.Card(Ui.Stack(Ui.Text("Unable to open the CRM", "title"), Message()));
            return;
        }
        if (vmMain.strPage == "Login") { Content = Login(); return; }
        var pnlNavigation = Ui.Stack(Ui.Text("SWOLLEN\nHIPPO", "section"), Ui.Text("CONTRACTOR RELATIONSHIPS", "muted"),
            Ui.Button("Overview", vmMain.cmdOverview), Ui.Button("Contacts", vmMain.cmdContacts), Ui.Button("Organizations", vmMain.cmdOrganizations));
        var txtUsername = Ui.Text(""); txtUsername.Bind(TextBlock.TextProperty, Ui.Read(nameof(vmMain.strSignedInUsername)));
        var tglTheme = new ToggleSwitch { Content = "Dark mode" };
        tglTheme.Bind(ToggleSwitch.IsCheckedProperty, Ui.Edit(nameof(vmMain.bDark)));
        pnlNavigation.Children.Add(Ui.Stack(new Separator(), Ui.Text("WORKSPACE", "muted"), txtUsername,
            tglTheme, Ui.Button("Lock workspace", vmMain.cmdLock), Ui.Text("Big Eagle Group\nLocal data · No cloud sync", "muted")));
        var ctrPage = vmMain.strPage switch
        {
            "Contacts" => Contacts(),
            "Organizations" => Organizations(),
            _ => Dashboard()
        };
        var grdMain = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(24) };
        grdMain.Children.Add(Message()); Grid.SetRow(ctrPage, 1); grdMain.Children.Add(ctrPage);
        Content = Ui.Columns("235,*", Ui.Card(pnlNavigation), grdMain);
    }

    private TextBlock Message()
    {
        var txtMessage = Ui.Text("");
        txtMessage.Bind(TextBlock.TextProperty, Ui.Read(nameof(vmMain.strMessage)));
        return txtMessage;
    }

    private Control Login()
    {
        var txtUsername = Ui.Input(nameof(vmMain.strUsername));
        txtUsername.Name = "txtUsername";
        var txtPassword = new SecurePasswordInput(vmMain.secPassword) { Name = "txtPassword", Watermark = "Password", PasswordChar = '●' };
        var txtConfirmPassword = new SecurePasswordInput(vmMain.secConfirmation) { Name = "txtConfirmPassword", PasswordChar = '●' };
        var pnlForm = Ui.Stack(Ui.Text("SWOLLEN HIPPO", "muted"), Ui.Text(vmMain.bSetup ? "Set up your workspace" : "Welcome back", "title"),
            Ui.Field("Username", txtUsername), Ui.Field("Password", txtPassword));
        if (vmMain.bSetup) pnlForm.Children.Add(Ui.Field("Confirm password", txtConfirmPassword));
        pnlForm.Children.Add(Ui.Button(vmMain.bSetup ? "Create workspace" : "Sign in", vmMain.cmdSignIn, true));
        pnlForm.Children.Add(Message()); pnlForm.Children.Add(Ui.Text("LOCAL WORKSPACE • OFFLINE", "muted"));
        txtPassword.KeyDown += (objSender, objArguments) =>
        {
            if (!vmMain.bSetup && objArguments.Key == Avalonia.Input.Key.Enter) vmMain.cmdSignIn.Execute(null);
        };
        txtConfirmPassword.KeyDown += (objSender, objArguments) =>
        {
            if (objArguments.Key == Avalonia.Input.Key.Enter) vmMain.cmdSignIn.Execute(null);
        };
        var bdrCard = Ui.Card(pnlForm); bdrCard.Width = 490;
        bdrCard.HorizontalAlignment = HorizontalAlignment.Center; bdrCard.VerticalAlignment = VerticalAlignment.Center;
        return bdrCard;
    }

    private Control Dashboard()
    {
        var objDashboard = vmMain.objDashboard!;
        Control Metric(string strLabel, int iValue) => Ui.Card(Ui.Stack(Ui.Text(iValue.ToString("N0"), "title"), Ui.Text(strLabel, "muted")));
        var pnlActivity = Ui.Stack(Ui.Text("Interaction activity", "section"));
        foreach (var objWeek in objDashboard.Activity)
            pnlActivity.Children.Add(Ui.Text($"{objWeek.Label}: {objWeek.Count}"));
        return Ui.Scroll(Ui.Stack(Ui.Text("Workspace overview", "title"),
            Ui.Columns("*,*,*", Metric("Contacts", objDashboard.Contacts), Metric("Organizations", objDashboard.Organizations), Metric("Active & available", objDashboard.Available)),
            Ui.Columns("*,*,*", Metric("Interactions · 30 days", objDashboard.Interactions30Days), Metric("Need follow-up · 30+ days", objDashboard.Overdue), Metric("Archived contacts", objDashboard.Archived)),
            Ui.Card(pnlActivity)));
    }

    private Control Organizations()
    {
        var lstOrganizations = new ListBox { MinHeight = 200 };
        lstOrganizations.Bind(ItemsControl.ItemsSourceProperty, Ui.Read(nameof(vmMain.lstOrganizations)));
        lstOrganizations.Bind(ListBox.SelectedItemProperty, Ui.Edit(nameof(vmMain.objOrganization)));
        ctrEditor = new ContentControl(); RenderOrganization();
        var btnNew = Ui.Button("+ Add organization", vmMain.cmdNewOrganization, true); btnNew.IsEnabled = vmMain.bCanCreate;
        return Ui.Page("Organizations", btnNew,
            Ui.Columns("280,*", Ui.ListPane(Ui.Stack(Ui.Field("Name, service, or tag", Ui.Input(nameof(vmMain.strSearch))), ArchiveFilter(),
                Ui.Button("Apply filters", vmMain.cmdApplyFilters)), lstOrganizations), ctrEditor));
    }

    private void RenderOrganization()
    {
        if (vmMain.objOrganization is not { } objOrganization) { ctrEditor.Content = Ui.Text("Select an organization or add one."); return; }
        var pnlForm = Ui.Stack(Ui.Text(objOrganization.Id == 0 ? "New organization" : objOrganization.Name, "section"),
            Ui.Field("Organization name *", Ui.Input("objOrganization.Name")),
            Ui.Field("Email", Ui.Input("objOrganization.Email")), Ui.Field("Phone", Ui.Input("objOrganization.Phone")),
            Ui.Field("Address / locations", Ui.Input("objOrganization.Address", true)),
            Ui.Field("Services", Ui.Input("objOrganization.Services", true)), Ui.Field("Tags (comma separated)", Ui.Input("objOrganization.Tags")),
            Ui.Field("Status", Ui.Choice<RecordStatus>("objOrganization.Status")), Ui.Button("Save organization", vmMain.cmdSaveOrganization, true));
        if (objOrganization.Id != 0) pnlForm.Children.Add(Ui.Button(objOrganization.Archived ? "Restore organization" : "Archive organization", vmMain.cmdArchiveOrganization));
        Ui.SetEditing(pnlForm, vmMain.bCanEditOrganization);
        ctrEditor.Content = Ui.Scroll(Ui.Card(pnlForm));
    }

    private Control Contacts()
    {
        var lstContacts = new ListBox { MinHeight = 200 };
        lstContacts.Bind(ItemsControl.ItemsSourceProperty, Ui.Read(nameof(vmMain.lstContacts)));
        lstContacts.Bind(ListBox.SelectedItemProperty, Ui.Edit(nameof(vmMain.objContact)));
        var cboAvailability = new ComboBox { ItemsSource = new[] { "Any availability", "Available", "Unavailable", "Unknown" } };
        cboAvailability.Bind(ComboBox.SelectedIndexProperty, Ui.Edit(nameof(vmMain.iAvailability)));
        ctrEditor = new ContentControl(); RenderContact();
        var btnNew = Ui.Button("+ Add contact", vmMain.cmdNewContact, true); btnNew.IsEnabled = vmMain.bCanCreate;
        return Ui.Page("Contacts", btnNew,
            Ui.Columns("280,*", Ui.ListPane(Ui.Stack(Ui.Field("Name, email, or phone", Ui.Input(nameof(vmMain.strSearch))),
                Ui.Field("Skill contains", Ui.Input(nameof(vmMain.strSkill))), Ui.Field("Availability", cboAvailability), ArchiveFilter(),
                Ui.Button("Apply filters", vmMain.cmdApplyFilters)), lstContacts), ctrEditor));
    }

    private CheckBox ArchiveFilter()
    {
        var chkArchived = new CheckBox { Content = "Archived records" };
        chkArchived.Bind(CheckBox.IsCheckedProperty, Ui.Edit(nameof(vmMain.bArchived)));
        return chkArchived;
    }

    private void RenderContact()
    {
        if (vmMain.objContact is not { } objContact) { ctrEditor.Content = Ui.Text("Select a contact or add someone."); return; }
        Control Field(string strLabel, string strProperty, bool bMultiline = false) => Ui.Field(strLabel, Ui.Input("objContact." + strProperty, bMultiline));
        var pnlProfile = Ui.Stack(Field("First name *", nameof(Contact.FirstName)), Field("Last name", nameof(Contact.LastName)),
            Field("Emails (one per line)", nameof(Contact.Email), true), Field("Phone numbers (one per line)", nameof(Contact.Phone), true),
            Field("Addresses (one per line)", nameof(Contact.Address), true), Field("Skills (comma separated)", nameof(Contact.Skills), true),
            Ui.Field("Status", Ui.Choice<RecordStatus>("objContact.Status")), Ui.Field("Contract availability", Ui.Choice<Availability>("objContact.Availability")));
        var pnlPersonal = Ui.Stack(Field("Spouse / partner", nameof(Contact.Spouse)), Field("Children", nameof(Contact.Children), true),
            Field("Extended family", nameof(Contact.Family), true), Field("Friends", nameof(Contact.Friends), true));
        var pnlSocial = Ui.Stack(Field("Twitter / X", nameof(Contact.Twitter)), Field("Instagram", nameof(Contact.Instagram)),
            Field("Snapchat", nameof(Contact.Snapchat)), Field("Facebook", nameof(Contact.Facebook)));
        var lstTabs = new List<TabItem>
        {
            new() { Header = "Profile", Content = Ui.Scroll(pnlProfile) },
            new() { Header = "Personal", Content = Ui.Scroll(pnlPersonal) },
            new() { Header = "Social", Content = Ui.Scroll(pnlSocial) }
        };
        if (objContact.Id != 0)
        {
            lstTabs.Add(new() { Header = "Interactions", Content = Interactions(objContact.Archived || !vmMain.bCanEditContact) });
            lstTabs.Add(new() { Header = "Employment", Content = Employment(objContact.Archived || !vmMain.bCanEditContact) });
            lstTabs.Add(new() { Header = "Contracts", Content = Contracts(objContact.Archived || !vmMain.bCanEditContact) });
        }
        var iSelectedTab = (ctrEditor.Content as Border)?.Child is Grid grdPrevious
            ? grdPrevious.Children.OfType<TabControl>().FirstOrDefault()?.SelectedIndex ?? 0 : 0;
        var tabEditor = new TabControl { ItemsSource = lstTabs, SelectedIndex = Math.Clamp(iSelectedTab, 0, lstTabs.Count - 1) };
        var pnlActions = Ui.Row(Ui.Button("Save contact", vmMain.cmdSaveContact, true));
        pnlActions.IsEnabled = vmMain.bCanEditContact;
        Ui.SetEditing(pnlProfile, vmMain.bCanEditContact); Ui.SetEditing(pnlPersonal, vmMain.bCanEditContact); Ui.SetEditing(pnlSocial, vmMain.bCanEditContact);
        if (objContact.Id != 0) pnlActions.Children.Add(Ui.Button(objContact.Archived ? "Restore contact" : "Archive contact", vmMain.cmdArchiveContact));
        var grdForm = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 12 };
        grdForm.Children.Add(Ui.Text(objContact.Id == 0 ? "New contact" : objContact.DisplayName, "section"));
        Grid.SetRow(pnlActions, 1); grdForm.Children.Add(pnlActions);
        Grid.SetRow(tabEditor, 2); grdForm.Children.Add(tabEditor);
        ctrEditor.Content = Ui.Card(grdForm);
    }

    private Control Interactions(bool bArchived)
    {
        var pnlNotes = Ui.Stack(Ui.Text("Interaction timeline", "section"));
        if (!bArchived)
        {
            var txtNote = Ui.Input(nameof(vmMain.strNote), true); txtNote.Watermark = "Record a conversation, meeting, or follow-up…";
            pnlNotes.Children.Add(Ui.Field("Interaction note", txtNote));
            pnlNotes.Children.Add(Ui.Button("Record interaction", vmMain.cmdAddInteraction, true));
        }
        foreach (var objNote in vmMain.lstInteractions)
            pnlNotes.Children.Add(Ui.Card(Ui.Stack(Ui.Text($"{objNote.CreatedAt.ToLocalTime():MMM d, yyyy · h:mm tt} / {objNote.Author}", "muted"), Ui.Text(objNote.Body))));
        if (vmMain.lstInteractions.Count == 0) pnlNotes.Children.Add(Ui.Text("No interactions recorded."));
        return Ui.Scroll(pnlNotes);
    }

    private Control Employment(bool bArchived)
    {
        var pnlEmployment = Ui.Stack(Ui.Text("Employment history", "section"));
        if (!bArchived)
        {
            var cboEmployer = new ComboBox { ItemsSource = vmMain.lstEmployers };
            cboEmployer.Bind(ComboBox.SelectedItemProperty, Ui.Edit(nameof(vmMain.objEmployer)));
            pnlEmployment.Children.Add(Ui.Stack(Ui.Field("Organization", cboEmployer), Ui.Field("Role", Ui.Input(nameof(vmMain.strRole))),
                Ui.Field("Start (YYYY-MM-DD)", Ui.Input(nameof(vmMain.strStartDate))), Ui.Field("End (optional)", Ui.Input(nameof(vmMain.strEndDate))),
                Ui.Button("Add placement", vmMain.cmdAddEmployment, true)));
        }
        foreach (var vmPlacement in vmMain.lstEmployment)
        {
            var objPlacement = vmPlacement.objEmployment;
            var pnlPlacement = Ui.Stack(Ui.Text(objPlacement.OrganizationName, "section"), Ui.Text(objPlacement.Role),
                Ui.Text($"{objPlacement.Start:yyyy-MM-dd} → {(objPlacement.End.HasValue ? objPlacement.End.Value.ToString("yyyy-MM-dd") : "Current")}"));
            if (!bArchived && !objPlacement.End.HasValue)
            {
                var txtEndDate = Ui.Input(nameof(vmPlacement.strEndDate)); txtEndDate.DataContext = vmPlacement;
                pnlPlacement.Children.Add(Ui.Field("End date (YYYY-MM-DD)", txtEndDate));
                pnlPlacement.Children.Add(Ui.Button("End placement", vmPlacement.cmdEnd));
            }
            pnlEmployment.Children.Add(Ui.Card(pnlPlacement));
        }
        if (vmMain.lstEmployment.Count == 0) pnlEmployment.Children.Add(Ui.Text("No employment history recorded."));
        return Ui.Scroll(pnlEmployment);
    }

    private Control Contracts(bool bArchived)
    {
        var pnlContracts = Ui.Stack(Ui.Text("Contract history", "section"));
        if (!bArchived)
            pnlContracts.Children.Add(Ui.Stack(Ui.Field("Date (YYYY-MM-DD)", Ui.Input(nameof(vmMain.strContractDate))),
                Ui.Field("Contract value", Ui.Input(nameof(vmMain.strContractValue))), Ui.Field("Location", Ui.Input(nameof(vmMain.strLocation))),
                Ui.Field("Description", Ui.Input(nameof(vmMain.strDescription), true)), Ui.Button("Add contract", vmMain.cmdAddContract, true)));
        foreach (var objContract in vmMain.lstContracts)
            pnlContracts.Children.Add(Ui.Card(Ui.Stack(Ui.Text($"{objContract.Date:MMM d, yyyy} · {objContract.Value:N2}", "section"), Ui.Text(objContract.Location), Ui.Text(objContract.Description))));
        if (vmMain.lstContracts.Count == 0) pnlContracts.Children.Add(Ui.Text("No contracts recorded."));
        return Ui.Scroll(pnlContracts);
    }
}

internal static class Ui
{
    public static void SetEditing(Control ctlControl, bool bCanEdit)
    {
        // Read-only text stays keyboard-focusable; only mutation controls are disabled.
        if (ctlControl is TextBox txtInput) txtInput.IsReadOnly = !bCanEdit;
        if (ctlControl is ComboBox cboChoice) cboChoice.IsEnabled = bCanEdit;
        if (ctlControl is Button btnAction) btnAction.IsEnabled = bCanEdit;
        if (ctlControl is Panel pnlContainer)
            foreach (var ctlChild in pnlContainer.Children) SetEditing(ctlChild, bCanEdit);
    }
    public static Grid Page(string strTitle, Control ctlAction, Control ctlBody)
    {
        var grdPage = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 20 };
        grdPage.Children.Add(Stack(Text(strTitle, "title"), ctlAction));
        Grid.SetRow(ctlBody, 1); grdPage.Children.Add(ctlBody);
        return grdPage;
    }
    public static Grid ListPane(Control ctlFilters, Control ctlList)
    {
        var grdPane = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 12 };
        grdPane.Children.Add(ctlFilters); Grid.SetRow(ctlList, 1); grdPane.Children.Add(ctlList);
        return grdPane;
    }
    public static Binding Read(string strPath) => new(strPath) { Mode = BindingMode.OneWay };
    public static Binding Edit(string strPath) => new(strPath) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
    public static TextBlock Text(string strValue, string? strStyle = null)
    {
        var txtBlock = new TextBlock { Text = strValue, TextWrapping = TextWrapping.Wrap };
        if (strStyle is not null) txtBlock.Classes.Add(strStyle);
        return txtBlock;
    }
    public static StackPanel Stack(params Control[] arrChildren)
    {
        var pnlStack = new StackPanel { Spacing = 12 };
        foreach (var ctlChild in arrChildren) pnlStack.Children.Add(ctlChild);
        return pnlStack;
    }
    public static StackPanel Row(params Control[] arrChildren)
    {
        var pnlRow = Stack(arrChildren); pnlRow.Orientation = Orientation.Horizontal; return pnlRow;
    }
    public static Button Button(string strLabel, ICommand cmdAction, bool bPrimary = false)
    {
        var btnAction = new Button { Content = strLabel, Command = cmdAction };
        if (bPrimary) btnAction.Classes.Add("primary");
        return btnAction;
    }
    public static TextBox Input(string strPath, bool bMultiline = false)
    {
        var txtInput = new TextBox { AcceptsReturn = bMultiline, TextWrapping = bMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = bMultiline ? 85 : 38 };
        txtInput.Bind(TextBox.TextProperty, Edit(strPath));
        return txtInput;
    }
    public static Control Field(string strLabel, Control ctlInput)
    {
        ctlInput.Tag = strLabel;
        AutomationProperties.SetName(ctlInput, strLabel);
        return Stack(Text(strLabel, "muted"), ctlInput);
    }
    public static ComboBox Choice<T>(string strPath) where T : struct, Enum
    {
        var cboChoice = new ComboBox { ItemsSource = Enum.GetValues<T>() };
        cboChoice.Bind(ComboBox.SelectedItemProperty, Edit(strPath));
        return cboChoice;
    }
    public static ScrollViewer Scroll(Control ctlContent) => new() { Content = ctlContent, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    public static Border Card(Control ctlContent) => new() { Child = ctlContent, Padding = new Thickness(20), CornerRadius = new CornerRadius(10) };
    public static Grid Columns(string strDefinitions, params Control[] arrChildren)
    {
        var grdColumns = new Grid { ColumnDefinitions = new ColumnDefinitions(strDefinitions), ColumnSpacing = 20 };
        for (var iColumn = 0; iColumn < arrChildren.Length; iColumn++) { Grid.SetColumn(arrChildren[iColumn], iColumn); grdColumns.Children.Add(arrChildren[iColumn]); }
        return grdColumns;
    }
}
