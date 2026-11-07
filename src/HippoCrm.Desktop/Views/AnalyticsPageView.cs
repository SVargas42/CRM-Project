using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;
using System.ComponentModel;
using HippoCrm.Desktop.ViewModels;
namespace HippoCrm.Desktop.Views;

public sealed class AnalyticsPageView : UserControl
{
    public const double dblPanelStackThreshold = 1100;
    private readonly List<Grid> lstPairs = [];
    private readonly Expander expContracts = new() { Name = "expAnalyticsContracts" };
    private readonly Expander expMovement = new() { Name = "expAnalyticsMovement" };
    private AnalyticsViewModel? vmObserved;
    public AnalyticsPageView()
    {
        // Native macOS scrollbars can overlay the viewport; keep content clear of that gutter.
        var pnlPage = new StackPanel { Name = "pnlAnalyticsDashboard", Spacing = 12, Margin = new Thickness(0, 12, 24, 16) };
        var grdHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 12 };
        grdHeader.Children.Add(new TextBlock { Text = "Analytics", Classes = { "pageTitle" }, VerticalAlignment = VerticalAlignment.Center });
        var txtLoaded = Text("strLoadedAt", "txtAnalyticsLoadedAt", "muted"); Grid.SetColumn(txtLoaded, 1); grdHeader.Children.Add(txtLoaded);
        var btnRefresh = ActionButton("Refresh", "btnAnalyticsRefresh", "primary"); btnRefresh.Bind(Button.CommandProperty, new Binding("cmdRefresh")); Grid.SetColumn(btnRefresh, 2); grdHeader.Children.Add(btnRefresh);
        var chkInclude = new CheckBox { Name = "chkAnalyticsInclude", Content = "Include inactive and archived records", Classes = { "crmCheck" } };
        chkInclude.Bind(CheckBox.IsCheckedProperty, new Binding("bIncludeInactive", BindingMode.TwoWay)); AutomationProperties.SetName(chkInclude, "Include inactive and archived records"); Grid.SetColumn(chkInclude, 3); grdHeader.Children.Add(chkInclude); pnlPage.Children.Add(grdHeader);
        pnlPage.Children.Add(new TextBlock { Text = "All times are UTC. Lists reflect the loaded snapshot. Cards and charts follow the inclusion control; status and text filters apply to tables.", Classes = { "muted" } });
        var pbLoading = new ProgressBar { IsIndeterminate = true }; pbLoading.Bind(IsVisibleProperty, new Binding("bLoading")); AutomationProperties.SetName(pbLoading, "Loading analytics"); pnlPage.Children.Add(pbLoading);
        pnlPage.Children.Add(Text("strError", "txtAnalyticsError", "muted"));
        var btnReload = ActionButton("Reload", "btnAnalyticsReload", "inlineAction"); btnReload.Bind(Button.CommandProperty, new Binding("cmdRefresh")); btnReload.Bind(IsVisibleProperty, new Binding("bFailed")); pnlPage.Children.Add(btnReload);

        var pnlFilters = new StackPanel { Spacing = 8, Name = "pnlAnalyticsFilters" };
        var grdFilters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 12 };
        var txtList = Input("strListFilter", "txtAnalyticsListFilter", "Filter lists by name, organization, phone or email", "fieldSearch");
        grdFilters.Children.Add(Labeled("Filter lists by name, organization, phone or email", txtList));
        var cmbStatus = new ComboBox { Name = "cmbAnalyticsStatus", Classes = { "crmSelector" } };
        foreach (var strStatus in new[] { "Active", "Inactive", "Retired", "Deceased", "All statuses" })
        {
            var objItem = new ComboBoxItem { Content = strStatus };
            if (strStatus != "Active") objItem.Bind(IsEnabledProperty, new Binding("bOtherStatusesEnabled"));
            cmbStatus.Items.Add(objItem);
        }
        cmbStatus.Bind(ComboBox.SelectedIndexProperty, new Binding("iStatus", BindingMode.TwoWay)); AutomationProperties.SetName(cmbStatus, "Status for every contact table");
        var pnlStatus = Labeled("Status", cmbStatus); Grid.SetColumn(pnlStatus, 1); grdFilters.Children.Add(pnlStatus);
        var cmbPeriod = new ComboBox { Name = "cmbAnalyticsPeriod", Classes = { "crmSelector" } }; cmbPeriod.Bind(ItemsControl.ItemsSourceProperty, new Binding("lstPeriods")); cmbPeriod.Bind(ComboBox.SelectedIndexProperty, new Binding("iPeriod", BindingMode.TwoWay)); AutomationProperties.SetName(cmbPeriod, "Period for notes and organizations reached");
        var pnlPeriod = Labeled("Period", cmbPeriod); Grid.SetColumn(pnlPeriod, 2); grdFilters.Children.Add(pnlPeriod); pnlFilters.Children.Add(grdFilters);
        var txtHint = new TextBlock { Name = "txtAnalyticsStatusHint", Text = "Selecting another status automatically includes inactive and archived records.", Classes = { "muted" } }; txtHint.Bind(IsVisibleProperty, new Binding("bShowStatusHint")); pnlFilters.Children.Add(txtHint); pnlPage.Children.Add(Card(pnlFilters));

        var grdCards = new UniformGrid { Name = "grdAnalyticsCards", Columns = 4 };
        var pnlDue = new StackPanel { Spacing = 8 }; pnlDue.Children.Add(Text("strNotContactedTitle", "txtCardDueTitle", "fieldLabel")); pnlDue.Children.Add(Text("strNotContactedCount", "txtCardDueValue", "title"));
        var txtDays = Input("strDays", "txtAnalyticsDays", "Not contacted in at least N days, 1 to 3650", "fieldCompact", 4);
        txtDays.HorizontalAlignment = HorizontalAlignment.Left;
        pnlDue.Children.Add(Labeled("Not contacted in at least N days", txtDays));
        pnlDue.Children.Add(Text("strDaysError", "txtAnalyticsDaysError", "muted")); CardNotes(pnlDue); grdCards.Children.Add(Card(pnlDue, true));
        var pnlNever = Metric("Never contacted", "strNeverContactedCount", "txtCardNeverValue"); CardNotes(pnlNever); grdCards.Children.Add(Card(pnlNever, true));
        var pnlNotes = Metric("Notes logged", "strNotesCount", "txtCardNotesValue"); pnlNotes.Children.Add(Text("strContactsReached", "txtCardContactsReached", "muted")); grdCards.Children.Add(Card(pnlNotes, true));
        var pnlReached = Metric("Organizations reached", "strOrganizationsReachedCount", "txtCardReachedValue");  grdCards.Children.Add(Card(pnlReached, true)); pnlPage.Children.Add(grdCards);

        pnlPage.Children.Add(Pair(Panel("Not contacted recently", Table("vmLastContacted", "tblAnalyticsLast")), Graph("Notes in the last seven UTC days", "grdGraphNotes", "lstActivityBars", "Scope follows the include-archived control. Counts are interactions, not unique people."), "grdAnalyticsRow1"));
        pnlPage.Children.Add(Pair(Panel("Recent contacts", Table("vmRecentContacts", "tblAnalyticsRecent")), Graph("Active contacts per organization", "grdGraphContactsPerOrg", "lstOrganizationBars", "Up to eight loaded organizations; active, unarchived contacts only."), "grdAnalyticsRow2"));
        var pnlFind = new StackPanel { Spacing = 8 };
        var grdFind = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 }; grdFind.Children.Add(Labeled("Skill, tag or service", Input("strFindSearch", "txtAnalyticsFind", "Skill, tag or service", "fieldSearch")));
        var cmbTypes = new ComboBox { Name = "cmbAnalyticsTypes", Classes = { "crmSelector" }, ItemsSource = new[] { "Contacts", "Organizations", "Both" } }; cmbTypes.Bind(ComboBox.SelectedIndexProperty, new Binding("iSearchType", BindingMode.TwoWay)); AutomationProperties.SetName(cmbTypes, "Search contacts, organizations or both"); var pnlTypes = Labeled("Find in", cmbTypes); Grid.SetColumn(pnlTypes, 1); grdFind.Children.Add(pnlTypes); pnlFind.Children.Add(grdFind);
        pnlFind.Children.Add(Table("vmSearch", "tblAnalyticsSearch")); var txtEmpty = new TextBlock { Text = "No matching skills, tags or services.", Classes = { "muted" } }; txtEmpty.Bind(IsVisibleProperty, new Binding("bSearchEmpty")); pnlFind.Children.Add(txtEmpty); pnlPage.Children.Add(Panel("Find by skill, tag or service", pnlFind));

        var pnlOrganization = new StackPanel { Spacing = 8 };
        var txtOrganization = Input("strOrganizationSearch", "txtAnalyticsOrganization", "Search loaded organizations", "fieldSearch"); pnlOrganization.Children.Add(Labeled("Organization", txtOrganization));
        var lstChoices = new ListBox { Name = "lstAnalyticsOrganizations", Classes = { "crmTable" }, ItemTemplate = new FuncDataTemplate<AnalyticsOrganizationChoice>((_, _) => Text("strCaption", "", "")) };
        // The dropdown shares the table viewport calculation; it is a list, not a fixed-height field.
        AnalyticsTableView.ApplyViewport(lstChoices);
        lstChoices.Bind(ItemsControl.ItemsSourceProperty, new Binding("lstOrganizationChoices")); lstChoices.Bind(ListBox.SelectedItemProperty, new Binding("objHighlightedOrganization", BindingMode.TwoWay)); lstChoices.Bind(IsVisibleProperty, new Binding("bOrganizationDropOpen")); AutomationProperties.SetName(lstChoices, "Loaded organizations in alphabetical order"); KeyboardNavigation.SetTabNavigation(lstChoices, KeyboardNavigationMode.Once); pnlOrganization.Children.Add(lstChoices);
        void Choose() { if (DataContext is AnalyticsViewModel vmAnalytics) vmAnalytics.SelectOrganization(lstChoices.SelectedItem as AnalyticsOrganizationChoice); txtOrganization.Focus(); }
        txtOrganization.KeyDown += (_, objEvent) =>
        {
            if (DataContext is not AnalyticsViewModel vmAnalytics) return;
            if (objEvent.Key == Key.Down)
            {
                vmAnalytics.bOrganizationDropOpen = true; lstChoices.SelectedIndex = 0; objEvent.Handled = true;
                Dispatcher.UIThread.Post(() => { if (!vmAnalytics.bOrganizationDropOpen || !vmAnalytics.bHasData || lstChoices.Items.Count == 0) return; lstChoices.ScrollIntoView(0); lstChoices.UpdateLayout(); (lstChoices.ContainerFromIndex(0) as Control)?.Focus(); });
            }
            else if (objEvent.Key == Key.Escape) { vmAnalytics.bOrganizationDropOpen = false; objEvent.Handled = true; }
        };
        lstChoices.KeyDown += (_, objEvent) =>
        { if (objEvent.Key == Key.Enter) { objEvent.Handled = true; Choose(); } else if (objEvent.Key == Key.Escape && DataContext is AnalyticsViewModel vmAnalytics) { vmAnalytics.bOrganizationDropOpen = false; txtOrganization.Focus(); objEvent.Handled = true; } };
        lstChoices.PointerReleased += (_, objEvent) => { if (objEvent.InitialPressMouseButton == MouseButton.Left) Choose(); };
        var txtSelect = new TextBlock { Text = "Select an organization", Classes = { "muted" } }; txtSelect.Bind(IsVisibleProperty, new Binding("bSelectOrganization")); pnlOrganization.Children.Add(txtSelect); pnlOrganization.Children.Add(Text("strSelectedOrganization", "txtAnalyticsSelectedOrganization", "fieldLabel"));
        foreach (var (strLabel, strProperty) in new[] { ("Phone", "strOrganizationPhone"), ("Email", "strOrganizationEmail"), ("Services", "strOrganizationServices"), ("Tags", "strOrganizationTags") })
        { var pnlLine = new StackPanel { Spacing = 4 }; pnlLine.Children.Add(new TextBlock { Text = strLabel, Classes = { "fieldLabel" } }); pnlLine.Children.Add(Text(strProperty, "txtAnalytics" + strLabel, "")); pnlOrganization.Children.Add(pnlLine); }
        pnlOrganization.Children.Add(Table("vmMembers", "tblAnalyticsMembers")); var txtNoMembers = new TextBlock { Text = "No members match the current filters.", Classes = { "muted" } }; txtNoMembers.Bind(IsVisibleProperty, new Binding("bNoMembers")); pnlOrganization.Children.Add(txtNoMembers);
        pnlPage.Children.Add(Pair(Panel("Organization lookup", pnlOrganization), Graph("Contacts by lifecycle", "grdGraphCounts", "lstLifecycleBars", "Retained records remain in storage. Archived records are included only when selected."), "grdAnalyticsRow4"));
        expContracts.Header = new TextBlock { Text = "Latest contract per contact", Classes = { "section" } }; expContracts.Bind(Expander.IsExpandedProperty, new Binding("bContractsExpanded", BindingMode.TwoWay)); AutomationProperties.SetName(expContracts, "Latest contract per contact"); pnlPage.Children.Add(Card(expContracts));
        expMovement.Header = new TextBlock { Text = "Movement", Classes = { "section" } }; expMovement.Bind(Expander.IsExpandedProperty, new Binding("bMovementExpanded", BindingMode.TwoWay)); AutomationProperties.SetName(expMovement, "Movement"); pnlPage.Children.Add(Card(expMovement));
        Content = new ScrollViewer { Content = pnlPage, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        SizeChanged += (_, objEvent) => ArrangePairs(objEvent.NewSize.Width);
        DataContextChanged += (_, _) => { if (vmObserved is not null) vmObserved.PropertyChanged -= OnPresentationChanged; vmObserved = DataContext as AnalyticsViewModel; if (vmObserved is not null) vmObserved.PropertyChanged += OnPresentationChanged; UpdateExpanders(); };
    }
    private void OnPresentationChanged(object? objSender, PropertyChangedEventArgs objEvent)
    { if (objEvent.PropertyName is nameof(AnalyticsViewModel.bHasData) or nameof(AnalyticsViewModel.bContractsExpanded) or nameof(AnalyticsViewModel.bMovementExpanded)) UpdateExpanders(); }
    private void UpdateExpanders()
    {
        if (vmObserved?.bContractsExpanded == true && vmObserved.bHasData) expContracts.Content ??= Table("vmContracts", "tblAnalyticsContracts"); else expContracts.Content = null;
        if (vmObserved?.bMovementExpanded == true && vmObserved.bHasData)
        {
            if (expMovement.Content is null) { var pnlMovement = new StackPanel { Spacing = 8 }; pnlMovement.Children.Add(Table("vmMovement", "tblAnalyticsMovement")); var txtEmpty = new TextBlock { Text = "No movement history yet. Add placements in a contact's Employment tab.", Classes = { "muted" } }; txtEmpty.Bind(IsVisibleProperty, new Binding("bNoMovement")); pnlMovement.Children.Add(txtEmpty); expMovement.Content = pnlMovement; }
        }
        else expMovement.Content = null;
    }
    private Grid Pair(Control objLeft, Control objRight, string strName)
    { var grdPair = new Grid { Name = strName, ColumnSpacing = 16, RowSpacing = 16 }; grdPair.Children.Add(objLeft); grdPair.Children.Add(objRight); lstPairs.Add(grdPair); return grdPair; }
    private void ArrangePairs(double dblWidth)
    {
        foreach (var grdPair in lstPairs)
        {
            var bStack = dblWidth < dblPanelStackThreshold;
            grdPair.ColumnDefinitions = new ColumnDefinitions(bStack ? "*" : "*,*"); grdPair.RowDefinitions = new RowDefinitions(bStack ? "Auto,Auto" : "Auto");
            Grid.SetColumn(grdPair.Children[1], bStack ? 0 : 1); Grid.SetRow(grdPair.Children[1], bStack ? 1 : 0);
        }
    }
    private static Border Card(Control objChild, bool bMetric = false) => new() { Classes = { "card" }, Padding = new Thickness(12), Margin = bMetric ? new Thickness(0, 0, 8, 0) : default, Child = objChild };
    private static Border Panel(string strTitle, Control objContent)
    {
        var pnlSection = new StackPanel { Spacing = 12 };
        var bdrTitle = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 8), Child = new TextBlock { Text = strTitle, Classes = { "section" } } };
        bdrTitle.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("CardStrokeColorDefaultBrush")); pnlSection.Children.Add(bdrTitle); pnlSection.Children.Add(objContent); return Card(pnlSection);
    }
    private static Border Graph(string strTitle,string strName,string strItems,string strHint)
    {
        var pnlChart=new StackPanel { Spacing=10 };
        pnlChart.Children.Add(new TextBlock { Text=strHint,TextWrapping=Avalonia.Media.TextWrapping.Wrap,Classes={"muted"} });
        var txtScope=Text("strChartScope","","muted"); if(strItems!="lstOrganizationBars") pnlChart.Children.Add(txtScope);
        var lstBars=new ItemsControl { Name=strName+"Bars" };
        lstBars.Bind(ItemsControl.ItemsSourceProperty,new Binding(strItems));
        lstBars.ItemTemplate=new FuncDataTemplate<AnalyticsBarRow>((_,_)=>
        {
            var pnlRow=new StackPanel { Spacing=4,Margin=new Thickness(0,0,0,8) };
            var grdLabel=new Grid { ColumnDefinitions=new ColumnDefinitions("*,Auto"),ColumnSpacing=10 };
            grdLabel.Children.Add(Text("strLabel","","")); var txtCount=Text("strCount","","");Grid.SetColumn(txtCount,1);grdLabel.Children.Add(txtCount);pnlRow.Children.Add(grdLabel);
            var pgrBar=new ProgressBar { Minimum=0,Maximum=100,Classes={"analyticsTrendBar"},Focusable=false };
            pgrBar.Bind(RangeBase.ValueProperty,new Binding("dblPercent"));pgrBar.Bind(ProgressBar.ForegroundProperty,new DynamicResourceExtension("TacticalAccentBrush"));
            pgrBar.Bind(AutomationProperties.NameProperty,new Binding("strAccessible"));pnlRow.Children.Add(pgrBar);return pnlRow;
        });
        pnlChart.Children.Add(lstBars);
        return Panel(strTitle,new Grid { Name=strName,Children={pnlChart} });
    }
    private static StackPanel Metric(string strTitle, string strProperty, string strName) => new() { Spacing = 8, Children = { new TextBlock { Text = strTitle, Classes = { "fieldLabel" } }, Text(strProperty, strName, "title") } };
    private static void CardNotes(StackPanel pnlCard)
    {
        var txtCap = new TextBlock { Text = "from loaded rows", Classes = { "muted" } }; txtCap.Bind(IsVisibleProperty, new Binding("bContactCardsCapped")); pnlCard.Children.Add(txtCap);
        var txtIgnore = new TextBlock { Text = "Cards ignore the text filter.", Classes = { "muted" } }; txtIgnore.Bind(IsVisibleProperty, new Binding("bCardsIgnoreText")); pnlCard.Children.Add(txtIgnore);
    }
    private static StackPanel Labeled(string strLabel, Control objInput) => new() { Spacing = 6, Children = { new TextBlock { Text = strLabel, Classes = { "fieldLabel" } }, objInput } };
    private static Button ActionButton(string strText, string strName, string strClass) { var btnButton = new Button { Name = strName, Content = strText, Classes = { "crmButton" } }; btnButton.Classes.Add(strClass); AutomationProperties.SetName(btnButton, strText + " analytics"); return btnButton; }
    private static TextBlock Text(string strProperty, string strName, string strClass)
    { var txtText = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }; if (strName.Length > 0) txtText.Name = strName; if (strClass.Length > 0) txtText.Classes.Add(strClass); txtText.Bind(TextBlock.TextProperty, new Binding(strProperty) { FallbackValue = "", TargetNullValue = "" }); return txtText; }
    private static CrmTextBox Input(string strProperty, string strName, string strLabel, string strClass, int iMaximum = 512)
    { var txtInput = new CrmTextBox { Name = strName, Watermark = strLabel, MaxLength = iMaximum, Classes = { "crmField" } }; txtInput.ClearValue(HeightProperty); txtInput.ClearValue(MinHeightProperty); txtInput.Classes.Add(strClass); txtInput.Bind(TextBox.TextProperty, new Binding(strProperty, BindingMode.TwoWay) { FallbackValue = "", TargetNullValue = "" }); txtInput.Bind(IsEnabledProperty, new Binding("bHasData")); AutomationProperties.SetName(txtInput, strLabel); return txtInput; }
    private static AnalyticsTableView Table(string strProperty, string strName) { var objTable = new AnalyticsTableView { Name = strName }; objTable.Bind(DataContextProperty, new Binding(strProperty)); return objTable; }
}
