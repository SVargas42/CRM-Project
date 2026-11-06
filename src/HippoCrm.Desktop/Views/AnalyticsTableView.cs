using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HippoCrm.Desktop.ViewModels;
namespace HippoCrm.Desktop.Views;

// Direct release notifications clear cells before containers can detach or recycle.
// A queued binding update alone can leave text on an already detached TextBlock.
internal sealed class AnalyticsCell : TextBlock
{
    protected override Type StyleKeyOverride => typeof(TextBlock);
    private readonly WeakReference<AnalyticsDisplayRow> wrRow;
    private readonly int iColumn;
    private bool bSubscribed;
    internal AnalyticsCell(AnalyticsDisplayRow objRow, int iColumn)
    {
        wrRow = new(objRow); this.iColumn = iColumn;
        var bUntrimmed = objRow.arrUntrimmedColumns.ElementAtOrDefault(iColumn);
        TextTrimming = bUntrimmed ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        TextWrapping = bUntrimmed ? TextWrapping.Wrap : TextWrapping.NoWrap;
        Subscribe(); AttachedToVisualTree += (_, _) => Subscribe(); DetachedFromVisualTree += (_, _) => Clear();
    }
    private void Subscribe()
    {
        if (!wrRow.TryGetTarget(out var objRow)) return;
        if (!bSubscribed) { objRow.PropertyChanged += OnRowChanged; bSubscribed = true; }
        Text = objRow.arrCells[iColumn] ?? "";
    }
    private void OnRowChanged(object? objSender, PropertyChangedEventArgs objEvent)
    { if (objEvent.PropertyName == nameof(AnalyticsDisplayRow.arrCells)) Subscribe(); }
    private void Clear()
    {
        if (bSubscribed && wrRow.TryGetTarget(out var objRow)) objRow.PropertyChanged -= OnRowChanged;
        bSubscribed = false; Text = "";
    }
}

public sealed class AnalyticsTableView : UserControl
{
    internal static void ApplyViewport(ListBox lstTable) => lstTable.Bind(MaxHeightProperty, new ViewportHeight(lstTable.GetResourceObservable("CrmTableRowHeight")));
    private const int iVisibleRows = 8;
    private sealed class ViewportHeight(IObservable<object?> objSource) : IObservable<object?>
    {
        public IDisposable Subscribe(IObserver<object?> objObserver) => objSource.Subscribe(new Scale(objObserver));
        private sealed class Scale(IObserver<object?> objObserver) : IObserver<object?>
        {
            public void OnNext(object? objValue) { if (objValue is double dblRow) objObserver.OnNext(dblRow * iVisibleRows); }
            public void OnError(Exception objError) => objObserver.OnError(objError);
            public void OnCompleted() => objObserver.OnCompleted();
        }
    }
    private static readonly FuncDataTemplate<AnalyticsDisplayRow> objRowTemplate = new((objRow, _) =>
    {
        var grdRow = RowGrid(objRow?.arrCells.Length ?? 1);
        if (objRow is not null) for (var iColumn = 0; iColumn < objRow.arrCells.Length; iColumn++)
        { var txtCell = new AnalyticsCell(objRow, iColumn); Grid.SetColumn(txtCell, iColumn); grdRow.Children.Add(txtCell); }
        return grdRow;
    });
    private readonly StackPanel pnlContent = new() { Spacing = 4 };
    public AnalyticsTableView() { Content = pnlContent; DataContextChanged += (_, _) => Build(); }
    private static Grid RowGrid(int iColumns) => new()
    { ColumnDefinitions = new ColumnDefinitions(string.Join(',', Enumerable.Repeat("*", iColumns))), ColumnSpacing = 8, Margin = new Thickness(4, 2) };
    private void Build()
    {
        pnlContent.Children.Clear();
        if (DataContext is not AnalyticsTableViewModel vmTable) return;
        var txtCount = new TextBlock { Name = "txtAnalyticsRowCount", Classes = { "muted" } };
        txtCount.Bind(TextBlock.TextProperty, new Binding("strCount") { FallbackValue = "", TargetNullValue = "" });
        AutomationProperties.SetLiveSetting(txtCount, AutomationLiveSetting.Polite);
        var grdHeaders = RowGrid(vmTable.arrHeaders.Length);
        for (var iColumn = 0; iColumn < vmTable.arrHeaders.Length; iColumn++)
        {
            var txtHeader = new TextBlock { Text = vmTable.arrHeaders[iColumn], Classes = { "fieldLabel" }, TextTrimming = TextTrimming.CharacterEllipsis };
            AutomationProperties.SetName(txtHeader, vmTable.arrHeaders[iColumn]); Grid.SetColumn(txtHeader, iColumn); grdHeaders.Children.Add(txtHeader);
        }
        var lstTable = new ListBox { Name = "lstAnalyticsRows", Classes = { "crmTable" }, ItemTemplate = objRowTemplate, SelectionMode = SelectionMode.Single };
        // The viewport follows the shared row geometry instead of a view-local height.
        ApplyViewport(lstTable);
        lstTable.Bind(ItemsControl.ItemsSourceProperty, new Binding("lstRows"));
        AutomationProperties.SetName(lstTable, vmTable.strTitle); KeyboardNavigation.SetTabNavigation(lstTable, KeyboardNavigationMode.Once);
        var styRows = new Style(objSelector => objSelector.OfType<ListBoxItem>());
        styRows.Setters.Add(new Setter(AutomationProperties.NameProperty, new Binding("strSummary") { FallbackValue = "", TargetNullValue = "" }));
        styRows.Setters.Add(new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        styRows.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(2)));
        lstTable.Styles.Add(styRows);
        lstTable.ContainerClearing += (_, objEvent) => objEvent.Container.SetCurrentValue(AutomationProperties.NameProperty, "");
        var txtError = new TextBlock { Classes = { "muted" } };
        async Task Open(AnalyticsDisplayRow? objRow)
        { try { await vmTable.ActivateAsync(objRow); } catch (OperationCanceledException) { } catch (Exception) { txtError.Text = "This profile could not be opened. Please try again."; } }
        if (vmTable.bCanActivate)
        {
            lstTable.PointerReleased += async (_, objEvent) =>
            {
                if (objEvent.InitialPressMouseButton != MouseButton.Left) return;
                var objItem = (objEvent.Source as Visual)?.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() ?? objEvent.Source as ListBoxItem;
                if (objItem?.DataContext is AnalyticsDisplayRow objRow) await Open(objRow);
            };
            lstTable.KeyDown += async (_, objEvent) =>
            { if (objEvent.Key is Key.Enter or Key.Space) { objEvent.Handled = true; await Open(lstTable.SelectedItem as AnalyticsDisplayRow); } };
        }
        pnlContent.Children.Add(txtCount); pnlContent.Children.Add(grdHeaders); pnlContent.Children.Add(lstTable); pnlContent.Children.Add(txtError);
    }
}
