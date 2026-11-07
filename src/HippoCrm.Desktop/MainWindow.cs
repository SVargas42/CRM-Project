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
    private static readonly Lazy<Avalonia.Media.Imaging.Bitmap> objBrandLogo = new(() =>
    {
        using var stmLogo = Avalonia.Platform.AssetLoader.Open(new Uri("avares://HippoCrm/Assets/HippoLogo.png"));
        return new Avalonia.Media.Imaging.Bitmap(stmLogo);
    });
    private readonly DispatcherTimer tmrIdle = new() { Interval = TimeSpan.FromSeconds(30) };

    private readonly Control ctrShell;
    private bool bAllowClose;
    private bool bClosingRequested;
    private bool bSidebarExpanded = true;
    private ArchiveContactDialog? frmDiscardConfirmation;

    static MainWindow()
    {
        // Class handlers also cover owned modal windows, whose input does not route through their owner.
        PointerPressedEvent.AddClassHandler<Window>(GuardWindowInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerMovedEvent.AddClassHandler<Window>(GuardWindowInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerReleasedEvent.AddClassHandler<Window>(GuardWindowInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerWheelChangedEvent.AddClassHandler<Window>(GuardWindowInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        KeyDownEvent.AddClassHandler<Window>(GuardWindowInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        KeyUpEvent.AddClassHandler<Window>(GuardWindowInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        TextInputEvent.AddClassHandler<Window>(GuardWindowInput, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public MainWindow() : this(new MainWindowViewModel()) { }
    public MainWindow(Database? objDatabase) : this(new MainWindowViewModel(objDatabase)) { }

    public MainWindow(MainWindowViewModel vmMain)
    {
        this.vmMain = vmMain;
        vmMain.fnRequestSessionCheck = () => Dispatcher.UIThread.Post(vmMain.CheckInactivity, DispatcherPriority.Send);
        DataContext = vmMain;
        vmMain.fnConfirmDiscard ??= ConfirmDiscardAsync;
        AvaloniaXamlLoader.Load(this);
        ctrShell = (Control)Content!;
#if DEBUG && HIPPOCRM_UI_DEVELOPMENT
        if (DebugUiDevelopment.bEnabled)
        {
            Title = "HippoCRM — UI DEVELOPMENT (local demo)";
            Content = null; // Detach the XAML shell before giving it a development-banner parent.
            ctrShell = DebugUiDevelopment.Mark(ctrShell);
        }
#endif
        Application.Current!.RequestedThemeVariant = vmMain.bDark ? ThemeVariant.Dark : ThemeVariant.Light;
        vmMain.PropertyChanged += OnViewModelChanged;
        Activated += (objSender, objArguments) => vmMain.CheckInactivity();

        // Commands now yield during database work, so the dispatcher can service inactivity checks.
        tmrIdle.Tick += (objSender, objArguments) => vmMain.CheckInactivity();
        tmrIdle.Start();
        Closing += OnWindowClosing;
        Closed += (objSender, objArguments) => { tmrIdle.Stop(); vmMain.fnRequestSessionCheck = null; vmMain.PropertyChanged -= OnViewModelChanged; };
        RenderPage();
    }

    private static void GuardWindowInput(Window frmInput, RoutedEventArgs objArguments)
    {
        for (Window? frmOwner = frmInput; frmOwner is not null; frmOwner = frmOwner.Owner as Window)
            if (frmOwner is MainWindow frmWorkspace)
            {
                if (!frmWorkspace.vmMain.RecordActivity()) objArguments.Handled = true;
                return;
            }
    }

    private async void OnWindowClosing(object? objSender, WindowClosingEventArgs objArguments)
    {
        if (bAllowClose) return;
        objArguments.Cancel = true;
        if (bClosingRequested || vmMain.bBusy) return;
        bClosingRequested = true;
        try
        {
            if (vmMain.bHasUnsavedChanges && !await vmMain.ConfirmDiscardAsync()) return;
            tmrIdle.Stop();
            await vmMain.CloseAsync();
            bAllowClose = true;
            Close();
        }
        catch (Exception objException) { System.Diagnostics.Debug.WriteLine($"CRM close failed: {objException.GetType().Name}"); }
        finally { bClosingRequested = false; }
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!IsVisible || frmDiscardConfirmation is not null) return false;
        var frmConfirmation = new ArchiveContactDialog("Unsaved changes",
            "You have unsaved changes. Discard them and continue?", "Discard changes", "Keep editing");
        frmDiscardConfirmation = frmConfirmation;
        try { return await frmConfirmation.ShowDialog<bool>(this); }
        finally { frmDiscardConfirmation = null; }
    }

    private void OnViewModelChanged(object? objSender, PropertyChangedEventArgs objArguments)
    {
        // A modal confirmation must never delay locking or keep its owner visible.
        if (vmMain.bLocking || vmMain.strPage == "Login") frmDiscardConfirmation?.Close(false);
        if (objArguments.PropertyName == nameof(vmMain.bDark))
            Application.Current!.RequestedThemeVariant = vmMain.bDark ? ThemeVariant.Dark : ThemeVariant.Light;
        else if (objArguments.PropertyName == nameof(vmMain.bLocking) || objArguments.PropertyName == nameof(vmMain.strPage) ||
                 (objArguments.PropertyName == nameof(vmMain.secPassword) && vmMain.strPage == "Login") ||
                 ((objArguments.PropertyName is nameof(vmMain.bSetup) or nameof(vmMain.bNeedsUsername)) && vmMain.strPage == "Login")) RenderPage();


    }

    private void OnToggleSidebar(object? objSender, RoutedEventArgs objArguments)
    {
        bSidebarExpanded = !bSidebarExpanded;
        var bExpanded = bSidebarExpanded;
        var bdrSidebar = this.FindControl<Border>("bdrSidebar")!;
        var btnToggle = this.FindControl<Button>("btnSidebarToggle")!;
        bdrSidebar.Width = bExpanded ? 232 : 88;
        bdrSidebar.Padding = bExpanded ? new Thickness(20, 24) : new Thickness(12, 24);
        this.FindControl<PathIcon>("icoSidebarToggle")!.Data = Geometry.Parse(
            bExpanded ? "M15,4 L7,12 L15,20 L17,18 L11,12 L17,6 Z"
                : "M9,4 L17,12 L9,20 L7,18 L13,12 L7,6 Z");
        ToolTip.SetTip(btnToggle, bExpanded ? "Collapse sidebar" : "Expand sidebar");
        AutomationProperties.SetName(btnToggle, bExpanded ? "Collapse sidebar" : "Expand sidebar");
        this.FindControl<Grid>("gridShell")!.ColumnDefinitions[0].Width = new GridLength(bExpanded ? 232 : 88);
        this.FindControl<StackPanel>("pnlSidebarBrandExpanded")!.IsVisible = bExpanded;
        this.FindControl<TextBlock>("txtSidebarMark")!.IsVisible = !bExpanded;
        foreach (var strName in new[] { "txtNavDashboard", "txtNavOrganizations", "txtNavContacts", "txtNavAuditLogs" })
            this.FindControl<TextBlock>(strName)!.IsVisible = bExpanded;
        foreach (var strName in new[] { "btnNavDashboard", "btnNavOrganizations", "btnNavContacts", "btnNavAuditLogs" })
        {
            var btnNavigation = this.FindControl<Button>(strName)!;
            btnNavigation.HorizontalContentAlignment = bExpanded ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            btnNavigation.Padding = new Thickness(11, 4);
        }
        var tglDarkMode = this.FindControl<ToggleSwitch>("tglDarkMode")!;
        tglDarkMode.Content = bExpanded ? "Dark mode" : "";
        tglDarkMode.HorizontalAlignment = bExpanded ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        var btnLock = this.FindControl<Button>("btnLockWorkspace")!;
        btnLock.Content = bExpanded ? "Lock workspace" : "⏻";
        ToolTip.SetTip(btnLock, bExpanded ? null : "Lock workspace");
        btnLock.HorizontalContentAlignment = HorizontalAlignment.Center;
    }

    private void RenderPage()
    {
        if (vmMain.bLocking)
        {
            foreach (var txtEditor in ctrShell.GetLogicalDescendants().OfType<TextBox>())
            { txtEditor.IsUndoEnabled = false; txtEditor.Text = ""; }
            Content = WithCuiBanner(Ui.Card(Ui.Text("Locking workspace…", "title")));
            return;
        }

        if (vmMain.strPage == "Error")
        {
            Content = WithCuiBanner(Ui.Card(Ui.Stack(Ui.Text("Unable to open the CRM", "title"), Message())));
            return;
        }
        if (vmMain.strPage == "Login") { Content = WithCuiBanner(Login()); return; }
        Content = ctrShell;
    }

    private static Control WithCuiBanner(Control ctlContent)
    {
        var pnlMarked = new DockPanel();
        var ctlBanner = new CuiBanner();
        DockPanel.SetDock(ctlBanner, Dock.Top);
        pnlMarked.Children.Add(ctlBanner); pnlMarked.Children.Add(ctlContent);
#if DEBUG && HIPPOCRM_UI_DEVELOPMENT
        if (DebugUiDevelopment.bEnabled) return DebugUiDevelopment.Mark(pnlMarked);
#endif
        return pnlMarked;
    }

    private TextBlock Message()
    {
        var txtMessage = Ui.Text("");
        AutomationProperties.SetLiveSetting(txtMessage, AutomationLiveSetting.Polite);
        txtMessage.Bind(TextBlock.TextProperty, Ui.Read(nameof(vmMain.strMessage)));
        return txtMessage;
    }

    private Control Login()
    {
        var txtUsername = Ui.Input(nameof(vmMain.strUsername));
        txtUsername.Name = "txtUsername";
        txtUsername.VerticalContentAlignment = VerticalAlignment.Center;
        var txtPassword = new SecurePasswordInput(vmMain.secPassword) { Name = "txtPassword", VerticalContentAlignment = VerticalAlignment.Center, Watermark = "Password" };
        var txtConfirmPassword = new SecurePasswordInput(vmMain.secConfirmation) { Name = "txtConfirmPassword", VerticalContentAlignment = VerticalAlignment.Center };
        // This bundled public brand asset never reads confidential record images.
        var imgLogo = new Image { Name = "imgLoginLogo", Source = objBrandLogo.Value, Stretch = Stretch.Uniform, RenderTransform = new ScaleTransform(1.12, 1.12), RenderTransformOrigin = RelativePoint.Center, IsHitTestVisible = false };
        AutomationProperties.SetName(imgLogo, "Swollen Hippo logo");
        var bdrLogo = new Border { Width = 112, Height = 112, Padding = new Thickness(0), ClipToBounds = true, CornerRadius = new CornerRadius(56), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Child = imgLogo };
        var pnlForm = Ui.Stack(bdrLogo, Ui.Text("SWOLLEN HIPPO", "muted"), Ui.Text(vmMain.bSetup ? "Set up your workspace" : "Welcome back", "title"));
        if (vmMain.bSetup || vmMain.bNeedsUsername) pnlForm.Children.Add(Ui.Field("Username", txtUsername));
        pnlForm.Children.Add(Ui.Field("Password", txtPassword));
        if (vmMain.bSetup) pnlForm.Children.Add(Ui.Field("Confirm password", txtConfirmPassword));
        var btnAuthenticate = Ui.Button(vmMain.bSetup ? "Create workspace" : "Sign in", vmMain.cmdSignIn, true);
        btnAuthenticate.Name = "btnAuthenticate";
        btnAuthenticate.HorizontalAlignment = HorizontalAlignment.Center;
        btnAuthenticate.HorizontalContentAlignment = HorizontalAlignment.Center;
        btnAuthenticate.Width = vmMain.bSetup ? 180 : 96;
        pnlForm.Children.Add(btnAuthenticate);
        pnlForm.Children.Add(Message());
        var pgrLoginWork = new ProgressBar { Name = "pgrLoginWork", IsIndeterminate = true };
        pgrLoginWork.Bind(IsVisibleProperty, Ui.Read(nameof(vmMain.bShowBusyIndicator)));
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
        var txtInput = new CrmTextBox { AcceptsReturn = bMultiline, TextWrapping = bMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = bMultiline ? 96 : 30, Height = bMultiline ? 96 : 30 };
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
    public static Border Card(Control ctlContent)
    {
        var bdrCard = new Border { Child = ctlContent, Padding = new Thickness(24), CornerRadius = new CornerRadius(12) };
        bdrCard.Classes.Add("card");
        return bdrCard;
    }
    public static Grid Columns(string strDefinitions, params Control[] arrChildren)
    {
        var grdColumns = new Grid { ColumnDefinitions = new ColumnDefinitions(strDefinitions), ColumnSpacing = 20 };
        for (var iColumn = 0; iColumn < arrChildren.Length; iColumn++) { Grid.SetColumn(arrChildren[iColumn], iColumn); grdColumns.Children.Add(arrChildren[iColumn]); }
        return grdColumns;
    }
}
