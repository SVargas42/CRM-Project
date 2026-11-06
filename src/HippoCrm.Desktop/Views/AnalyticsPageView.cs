using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Automation;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HippoCrm.Desktop.ViewModels;
namespace HippoCrm.Desktop.Views;

public sealed class AnalyticsPageView : UserControl
{
    public AnalyticsPageView()
    {
        FontSize = 16;
        var pnlPage = new StackPanel { Spacing = 12, Margin = new Thickness(6) };
        var pnlHeader = new StackPanel { Spacing = 4 };
        pnlHeader.Children.Add(new TextBlock { Text = "Analytics", FontSize = 22, FontWeight = FontWeight.SemiBold });
        var pnlActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        pnlActions.Children.Add(Text("strLoadedAt", "txtAnalyticsLoadedAt"));
        var btnRefresh = new Button { Content = "Refresh", Name = "btnAnalyticsRefresh" }; btnRefresh.Bind(Button.CommandProperty, new Binding("cmdRefresh"));
        AutomationProperties.SetName(btnRefresh, "Refresh analytics"); pnlActions.Children.Add(btnRefresh); pnlHeader.Children.Add(pnlActions);
        var chkInclude = new CheckBox { Content = "Include inactive and archived records", Name = "chkAnalyticsInclude" };
        chkInclude.Bind(CheckBox.IsCheckedProperty, new Binding("bIncludeInactive", BindingMode.TwoWay)); AutomationProperties.SetName(chkInclude, "Include inactive and archived records"); pnlHeader.Children.Add(chkInclude);
        pnlHeader.Children.Add(new TextBlock { Text = "All times are UTC. Filters apply only to the loaded snapshot.", FontSize = 15 });
        var pbLoading = new ProgressBar { IsIndeterminate = true, Height = 4 }; pbLoading.Bind(IsVisibleProperty, new Binding("bLoading")); AutomationProperties.SetName(pbLoading, "Loading analytics"); pnlHeader.Children.Add(pbLoading);
        pnlHeader.Children.Add(Text("strError", "txtAnalyticsError"));
        var btnReload = new Button { Content = "Reload" }; btnReload.Bind(Button.CommandProperty, new Binding("cmdRefresh")); btnReload.Bind(IsVisibleProperty, new Binding("bFailed")); AutomationProperties.SetName(btnReload, "Reload analytics"); pnlHeader.Children.Add(btnReload);
        pnlPage.Children.Add(pnlHeader);
        var pnlLast = Section(pnlPage, "A. Not contacted recently");
        pnlLast.Children.Add(new TextBlock { Text = "Not contacted in at least N days", FontSize = 15 });
        var txtDays = Input("strDays", "txtAnalyticsDays", "Not contacted in at least N days, 1 to 3650", 4); txtDays.Width = 150; txtDays.HorizontalAlignment = HorizontalAlignment.Left; pnlLast.Children.Add(txtDays);
        pnlLast.Children.Add(Text("strDaysError", "txtAnalyticsDaysError")); pnlLast.Children.Add(Filters("vmLastFilter", "Last")); Table(pnlLast, "vmLastContacted", "tblAnalyticsLast");
        var pnlRecent = Section(pnlPage, "B. Recent contacts"); pnlRecent.Children.Add(Filters("vmRecentFilter", "Recent")); Table(pnlRecent, "vmRecentContacts", "tblAnalyticsRecent");
        var pnlSearch = Section(pnlPage, "C. Find by skill, tag or service");
        pnlSearch.Children.Add(Input("strFindSearch", "txtAnalyticsFind", "Find matching skills, tags or services"));
        var cmbTypes = new ComboBox { Name = "cmbAnalyticsTypes", ItemsSource = new[] { "Contacts", "Organizations", "Both" } };
        cmbTypes.Bind(ComboBox.SelectedIndexProperty, new Binding("iSearchType", BindingMode.TwoWay)); AutomationProperties.SetName(cmbTypes, "Search contacts, organizations or both"); pnlSearch.Children.Add(cmbTypes);
        Table(pnlSearch, "vmSearch", "tblAnalyticsSearch"); var txtEmpty = new TextBlock { Text = "No matching skills, tags or services.", FontSize = 15 }; txtEmpty.Bind(IsVisibleProperty, new Binding("bSearchEmpty")); pnlSearch.Children.Add(txtEmpty);
        var pnlOrganization = Section(pnlPage, "D. Organization lookup");
        var txtOrganization = Input("strOrganizationSearch", "txtAnalyticsOrganization", "Search loaded organizations"); pnlOrganization.Children.Add(txtOrganization);
        var lstChoices = new ListBox { Name = "lstAnalyticsOrganizations", MaxHeight = 160, ItemTemplate = new FuncDataTemplate<AnalyticsOrganizationChoice>((_, _) => Text("strCaption", "")) };
        lstChoices.Bind(ItemsControl.ItemsSourceProperty, new Binding("lstOrganizationChoices")); lstChoices.Bind(ListBox.SelectedItemProperty, new Binding("objHighlightedOrganization", BindingMode.TwoWay)); lstChoices.Bind(IsVisibleProperty, new Binding("bOrganizationDropOpen"));
        AutomationProperties.SetName(lstChoices, "Loaded organizations in alphabetical order"); KeyboardNavigation.SetTabNavigation(lstChoices, KeyboardNavigationMode.Once); pnlOrganization.Children.Add(lstChoices);
        void Choose() { if (DataContext is AnalyticsViewModel vmAnalytics) vmAnalytics.SelectOrganization(lstChoices.SelectedItem as AnalyticsOrganizationChoice); txtOrganization.Focus(); }
        txtOrganization.KeyDown += (_, objEvent) =>
        {
            if (DataContext is not AnalyticsViewModel vmAnalytics) return;
            if (objEvent.Key == Key.Down)
            {
                vmAnalytics.bOrganizationDropOpen = true; lstChoices.SelectedIndex = 0; objEvent.Handled = true;
                // Focus a realized row after the visibility binding/layout has run.
                // ListBox itself is not the keyboard focus target for row navigation.
                Dispatcher.UIThread.Post(() =>
                {
                    if (!vmAnalytics.bOrganizationDropOpen || !vmAnalytics.bHasData || lstChoices.Items.Count == 0) return;
                    lstChoices.ScrollIntoView(0); lstChoices.UpdateLayout(); (lstChoices.ContainerFromIndex(0) as Control)?.Focus();
                });
            }
            else if (objEvent.Key == Key.Escape) { vmAnalytics.bOrganizationDropOpen = false; objEvent.Handled = true; }
        };
        lstChoices.KeyDown += (_, objEvent) =>
        {
            if (objEvent.Key == Key.Enter) { objEvent.Handled = true; Choose(); }
            else if (objEvent.Key == Key.Escape && DataContext is AnalyticsViewModel vmAnalytics) { vmAnalytics.bOrganizationDropOpen = false; txtOrganization.Focus(); objEvent.Handled = true; }
        };
        lstChoices.PointerReleased += (_, objEvent) => { if (objEvent.InitialPressMouseButton == MouseButton.Left) Choose(); };
        var txtSelect = new TextBlock { Text = "Select an organization", FontSize = 15 }; txtSelect.Bind(IsVisibleProperty, new Binding("bSelectOrganization")); pnlOrganization.Children.Add(txtSelect);
        pnlOrganization.Children.Add(Text("strSelectedOrganization", "txtAnalyticsSelectedOrganization"));
        foreach (var (strLabel, strProperty) in new[] { ("Phone", "strOrganizationPhone"), ("Email", "strOrganizationEmail"), ("Services", "strOrganizationServices"), ("Tags", "strOrganizationTags") })
        { var pnlLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; pnlLine.Children.Add(new TextBlock { Text = strLabel + ":", FontSize = 15 }); pnlLine.Children.Add(Text(strProperty, "txtAnalytics" + strLabel)); pnlOrganization.Children.Add(pnlLine); }
        Table(pnlOrganization, "vmMembers", "tblAnalyticsMembers"); var txtNoMembers = new TextBlock { Text = "No members match the current filters.", FontSize = 15 }; txtNoMembers.Bind(IsVisibleProperty, new Binding("bNoMembers")); pnlOrganization.Children.Add(txtNoMembers);
        Table(Section(pnlPage, "E. Latest contract per contact"), "vmContracts", "tblAnalyticsContracts");
        var pnlMovement = Section(pnlPage, "F. Movement"); Table(pnlMovement, "vmMovement", "tblAnalyticsMovement");
        var txtNoMovement = new TextBlock { Text = "No movement history yet. Add placements in a contact's Employment tab.", FontSize = 15 }; txtNoMovement.Bind(IsVisibleProperty, new Binding("bNoMovement")); pnlMovement.Children.Add(txtNoMovement);
        var pnlGraphs = Section(pnlPage, "Graphs (reserved for next step)"); var grdGraphs = new Grid { Name = "grdAnalyticsGraphs" }; AutomationProperties.SetName(grdGraphs, "Graphs section reserved for next step"); pnlGraphs.Children.Add(grdGraphs);
        Content = new ScrollViewer { Content = pnlPage, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }
    private static TextBlock Text(string strProperty, string strName)
    { var txtText = new TextBlock { FontSize = 15, TextWrapping = TextWrapping.Wrap }; if (strName.Length > 0) txtText.Name = strName; txtText.Bind(TextBlock.TextProperty, new Binding(strProperty) { FallbackValue = "", TargetNullValue = "" }); return txtText; }
    private static CrmTextBox Input(string strProperty, string strName, string strAccessible, int iMaximum = 512)
    {
        var txtInput = new CrmTextBox { Name = strName, FontSize = 16, MaxLength = iMaximum };
        txtInput.Bind(TextBox.TextProperty, new Binding(strProperty, BindingMode.TwoWay) { FallbackValue = "", TargetNullValue = "" });
        txtInput.Bind(IsEnabledProperty, new Binding("bHasData")); AutomationProperties.SetName(txtInput, strAccessible); return txtInput;
    }
    private static StackPanel Filters(string strProperty, string strPrefix)
    {
        var pnlFilters = new StackPanel { Spacing = 4 }; pnlFilters.Bind(DataContextProperty, new Binding(strProperty));
        var txtSearch = Input("strSearch", "txtAnalytics" + strPrefix + "Search", "Search " + strPrefix.ToLowerInvariant() + " contacts by name, organization, skills or tags");
        txtSearch.Bind(IsEnabledProperty, new Binding("bEnabled")); pnlFilters.Children.Add(txtSearch);
        var pnlChecks = new WrapPanel();
        foreach (var strStatus in new[] { "Active", "Inactive", "Retired", "Deceased" })
        {
            var chkStatus = new CheckBox { Content = strStatus, Name = "chkAnalytics" + strPrefix + strStatus, Margin = new Thickness(0, 0, 12, 0), FontSize = 15 };
            chkStatus.Bind(CheckBox.IsCheckedProperty, new Binding("b" + strStatus, BindingMode.TwoWay)); chkStatus.Bind(IsEnabledProperty, new Binding(strStatus == "Active" ? "bEnabled" : "bNonActiveEnabled")); AutomationProperties.SetName(chkStatus, strPrefix + " contacts with status " + strStatus); pnlChecks.Children.Add(chkStatus);
        }
        pnlFilters.Children.Add(pnlChecks);
        var txtHint = new TextBlock { Text = "Turn on 'Include inactive and archived records' above to see these.", FontSize = 15 }; txtHint.Bind(IsVisibleProperty, new Binding("bShowHint")); pnlFilters.Children.Add(txtHint); return pnlFilters;
    }
    private static StackPanel Section(StackPanel pnlPage, string strTitle)
    {
        var pnlSection = new StackPanel { Spacing = 4 };
        pnlSection.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 3), Child = new TextBlock { Text = strTitle, FontSize = 18, FontWeight = FontWeight.SemiBold } });
        pnlPage.Children.Add(pnlSection); return pnlSection;
    }
    private static void Table(StackPanel pnlSection, string strProperty, string strName)
    { var objTable = new AnalyticsTableView { Name = strName }; objTable.Bind(DataContextProperty, new Binding(strProperty)); pnlSection.Children.Add(objTable); }
}
