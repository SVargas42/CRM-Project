using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using HippoCrm.Desktop.ViewModels;

namespace HippoCrm.Desktop.Views;

// One template for every Analytics table. ListBox supplies native arrow/Tab behavior.
public sealed class AnalyticsTableView : UserControl
{
    private static readonly FuncDataTemplate<AnalyticsDisplayRow> objRowTemplate = new((objRow, _) =>
    {
        var grdRow = RowGrid(objRow?.arrCells.Length ?? 1);
        for (var iColumn = 0; iColumn < (objRow?.arrCells.Length ?? 0); iColumn++)
        {
            var txtCell = Cell(); Grid.SetColumn(txtCell, iColumn);
            // Recycled/detached containers can lose their binding path before the queued
            // release notification arrives. Explicit empty fallbacks prevent stale text.
            txtCell.Bind(TextBlock.TextProperty, new Binding($"arrCells[{iColumn}]") { FallbackValue = "", TargetNullValue = "" });
            grdRow.Children.Add(txtCell);
        }
        return grdRow;
    });
    private readonly StackPanel pnlContent = new() { Spacing = 6 };
    public AnalyticsTableView()
    {
        Content = pnlContent;
        DataContextChanged += (_, _) => Build();
    }
    private static Grid RowGrid(int iColumns) => new()
    {
        ColumnDefinitions = new ColumnDefinitions(string.Join(',', Enumerable.Repeat("*", iColumns))),
        ColumnSpacing = 12, Margin = new Thickness(8, 4)
    };
    private static TextBlock Cell() => new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private void Build()
    {
        pnlContent.Children.Clear();
        if (DataContext is not AnalyticsTableViewModel vmTable) return;
        var txtTitle = new TextBlock { Text = vmTable.strTitle, FontWeight = FontWeight.SemiBold, FontSize = 18 };
        var txtCount = new TextBlock { Name = "txtAnalyticsRowCount" }; txtCount.Bind(TextBlock.TextProperty, new Binding("strCount") { FallbackValue = "", TargetNullValue = "" });
        AutomationProperties.SetLiveSetting(txtCount, AutomationLiveSetting.Polite);
        var grdHeaders = RowGrid(vmTable.arrHeaders.Length);
        for (var iColumn = 0; iColumn < vmTable.arrHeaders.Length; iColumn++)
        {
            var txtHeader = Cell(); txtHeader.Text = vmTable.arrHeaders[iColumn]; txtHeader.FontWeight = FontWeight.SemiBold;
            AutomationProperties.SetName(txtHeader, vmTable.arrHeaders[iColumn]);
            Grid.SetColumn(txtHeader, iColumn); grdHeaders.Children.Add(txtHeader);
        }
        var lstTable = new ListBox
        {
            Name = "lstAnalyticsRows", MinHeight = 50, MaxHeight = 250,
            ItemTemplate = objRowTemplate, SelectionMode = SelectionMode.Single
        };
        lstTable.Bind(ItemsControl.ItemsSourceProperty, new Binding("lstRows"));
        // Read-only rows use normal ListBox selection for keyboard focus; only the roster
        // transfers selection into the view model to filter the members table.
        if (vmTable.bSelectable) lstTable.Bind(ListBox.SelectedItemProperty, new Binding("objSelectedRow", BindingMode.TwoWay));
        AutomationProperties.SetName(lstTable, vmTable.strTitle);
        KeyboardNavigation.SetTabNavigation(lstTable, KeyboardNavigationMode.Once);
        var styRows = new Style(objSelector => objSelector.OfType<ListBoxItem>());
        styRows.Setters.Add(new Setter(AutomationProperties.NameProperty, new Binding("strSummary") { FallbackValue = "", TargetNullValue = "" }));
        styRows.Setters.Add(new Setter(Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
        styRows.Setters.Add(new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        lstTable.Styles.Add(styRows);
        pnlContent.Children.Add(txtTitle); pnlContent.Children.Add(txtCount); pnlContent.Children.Add(grdHeaders); pnlContent.Children.Add(lstTable);
    }
}
