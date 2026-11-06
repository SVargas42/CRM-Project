using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using HippoCrm.Desktop.ViewModels;

namespace HippoCrm.Desktop.Views;

public sealed partial class OrganizationEditorView : UserControl
{
    public OrganizationEditorView() => AvaloniaXamlLoader.Load(this);
    private async void OnManageTags(object? objSender, RoutedEventArgs objArguments)
    { if (DataContext is MainWindowViewModel vmMain) await TokenSelectionDialogs.ManageTagsAsync(this, vmMain, false); }
    private async void OnManageServices(object? objSender, RoutedEventArgs objArguments)
    { if (DataContext is MainWindowViewModel vmMain) await TokenSelectionDialogs.ManageServicesAsync(this, vmMain); }
}

