using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using HippoCrm.Desktop.ViewModels;

namespace HippoCrm.Desktop.Views;

public sealed partial class EditOrganizationView : UserControl
{
    public EditOrganizationView() => AvaloniaXamlLoader.Load(this);
    private async void OnManageTags(object? objSender, RoutedEventArgs objArguments)
    { if (DataContext is OrganizationProfileViewModel vmProfile) await TokenSelectionDialogs.ManageTagsAsync(this, vmProfile.vmWorkspace, false); }
    private async void OnManageServices(object? objSender, RoutedEventArgs objArguments)
    { if (DataContext is OrganizationProfileViewModel vmProfile) await TokenSelectionDialogs.ManageServicesAsync(this, vmProfile.vmWorkspace); }
}
