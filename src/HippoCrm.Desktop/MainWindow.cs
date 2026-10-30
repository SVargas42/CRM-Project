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
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using HippoCrm.Core;
using HippoCrm.Desktop.ViewModels;
using HippoCrm.Desktop.Views;

namespace HippoCrm.Desktop;

// Presentation only: controls, bindings, theme, and input forwarding. No SQL/authentication workflows.
public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel vmMain;
    private readonly DispatcherTimer tmrIdle = new() { Interval = TimeSpan.FromSeconds(30) };
    private ContentControl ctrEditor = new();
    private readonly Control ctrShell;
    private bool bAllowClose;

    public MainWindow() : this(new MainWindowViewModel()) { }
    public MainWindow(Database? objDatabase) : this(new MainWindowViewModel(objDatabase)) { }

    public MainWindow(MainWindowViewModel vmMain)
    {
        this.vmMain = vmMain;
        DataContext = vmMain;
        AvaloniaXamlLoader.Load(this);
        ctrShell = (Control)Content!;
        Application.Current!.RequestedThemeVariant = vmMain.bDark ? ThemeVariant.Dark : ThemeVariant.Light;
        vmMain.PropertyChanged += OnViewModelChanged;
        AddHandler(PointerPressedEvent, (objSender, objArguments) => vmMain.RecordActivity(), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, (objSender, objArguments) => vmMain.RecordActivity(), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(Avalonia.Controls.Primitives.SelectingItemsControl.SelectionChangedEvent, OnRecordSelectionChanged);
        // Commands now yield during database work, so the dispatcher can service inactivity checks.
        tmrIdle.Tick += (objSender, objArguments) => vmMain.CheckInactivity();
        tmrIdle.Start();
        Closing += OnWindowClosing;
        Closed += (objSender, objArguments) => { tmrIdle.Stop(); vmMain.PropertyChanged -= OnViewModelChanged; };
        RenderPage();
    }

    private async void OnWindowClosing(object? objSender, WindowClosingEventArgs objArguments)
    {
        if (bAllowClose) return;
        objArguments.Cancel = true;
        tmrIdle.Stop();
        try { await vmMain.CloseAsync(); }
        catch (Exception objException) { System.Diagnostics.Debug.WriteLine($"CRM close failed: {objException.GetType().Name}"); }
        finally { bAllowClose = true; Close(); }
    }

    private void OnViewModelChanged(object? objSender, PropertyChangedEventArgs objArguments)
    {
        if (objArguments.PropertyName == nameof(vmMain.bDark))
            Application.Current!.RequestedThemeVariant = vmMain.bDark ? ThemeVariant.Dark : ThemeVariant.Light;
        else if (objArguments.PropertyName == nameof(vmMain.bLocking) || objArguments.PropertyName == nameof(vmMain.strPage) ||
                 (objArguments.PropertyName == nameof(vmMain.secPassword) && vmMain.strPage == "Login") ||
                 (objArguments.PropertyName == nameof(vmMain.bSetup) && vmMain.strPage == "Login")) RenderPage();

        else if (objArguments.PropertyName == nameof(vmMain.objOrganization) && vmMain.strPage == "Organizations") RenderOrganization();
    }

    private void RenderPage()
    {
        if (vmMain.bLocking)
        {
            Content = Ui.Card(Ui.Text("Locking workspace…", "title"));
            return;
        }
        if (vmMain.strPage is "Login" or "Error") ctrEditor.Content = null;
        if (vmMain.strPage == "Error")
        {
            Content = Ui.Card(Ui.Stack(Ui.Text("Unable to open the CRM", "title"), Message()));
            return;
        }
        if (vmMain.strPage == "Login") { Content = Login(); return; }
        Content = ctrShell;
    }

    // View bootstrap only: preserve the existing authorized record forms inside routed page hosts.
    private void OnOrganizationsHostLoaded(object? objSender, RoutedEventArgs objArguments)
    {
        if (objSender is ContentControl ctrHost)
        {
            ctrHost.DataContext = vmMain;
            ctrHost.Content = Organizations();
            ctrHost.GetLogicalDescendants().OfType<ListBox>().Single()
                .Bind(ListBox.SelectedItemProperty, Ui.Read(nameof(vmMain.objOrganization)));
        }
    }

    private TextBlock Message()
    {
        var txtMessage = Ui.Text("");
        txtMessage.Bind(TextBlock.TextProperty, Ui.Read(nameof(vmMain.strMessage)));
        return txtMessage;
    }

    private void OnRecordSelectionChanged(object? objSender, SelectionChangedEventArgs objArguments)
    {
        // Routed legacy lists receive selection one-way; user changes are forwarded explicitly.
        if (objArguments.Source is ListBox lstRecords && ReferenceEquals(lstRecords.DataContext, vmMain))
        {
            if (vmMain.vmShell.bContacts) vmMain.vmShell.SelectContact(lstRecords.SelectedItem as Contact);
            else if (vmMain.vmShell.bOrganizations) vmMain.vmShell.SelectOrganization(lstRecords.SelectedItem as Organization);
        }
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
        var pgrLoginWork = new ProgressBar { Name = "pgrLoginWork", IsIndeterminate = true };
        pgrLoginWork.Bind(IsVisibleProperty, Ui.Read(nameof(vmMain.bBusy)));
        pnlForm.Children.Add(pgrLoginWork);
        txtPassword.KeyDown += (objSender, objArguments) =>
        {
            if (!vmMain.bSetup && objArguments.Key == Avalonia.Input.Key.Enter) vmMain.cmdSignIn.Execute(null);
        };
        txtConfirmPassword.KeyDown += (objSender, objArguments) =>
        {
            if (objArguments.Key == Avalonia.Input.Key.Enter) vmMain.cmdSignIn.Execute(null);
        };
        var bdrCard = Ui.Card(pnlForm); bdrCard.Width = 490;
        bdrCard.Bind(IsEnabledProperty, Ui.Read("!bBusy"));
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
        var pnlForm = Ui.Stack(new ProfileHeader { DataContext = new ProfileHeaderViewModel(vmMain, false) },
            Ui.Text(objOrganization.Id == 0 ? "New organization" : objOrganization.Name, "section"),
            Ui.Field("Organization name *", Ui.Input("objOrganization.Name")),
            Ui.Field("Email", Ui.Input("objOrganization.Email")), Ui.Field("Phone", Ui.Input("objOrganization.Phone")),
            Ui.Field("Address / locations", Ui.Input("objOrganization.Address", true)),
            Ui.Field("Services", Ui.Input("objOrganization.Services", true)), Ui.Field("Tags (comma separated)", Ui.Input("objOrganization.Tags")),
            Ui.Field("Status", Ui.Choice<RecordStatus>("objOrganization.Status")), Ui.Button("Save organization", vmMain.cmdSaveOrganization, true));
        if (objOrganization.Id != 0) pnlForm.Children.Add(Ui.Button(objOrganization.Archived ? "Restore organization" : "Archive organization", vmMain.cmdArchiveOrganization));
        Ui.SetEditing(pnlForm, vmMain.bCanEditOrganization);
        ctrEditor.Content = Ui.Scroll(Ui.Card(pnlForm));
    }

    private CheckBox ArchiveFilter()
    {
        var chkArchived = new CheckBox { Content = "Archived records" };
        chkArchived.Bind(CheckBox.IsCheckedProperty, Ui.Edit(nameof(vmMain.bArchived)));
        return chkArchived;
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
