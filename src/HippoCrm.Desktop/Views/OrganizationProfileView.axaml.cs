using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using HippoCrm.Desktop.ViewModels;

namespace HippoCrm.Desktop.Views;

public sealed partial class OrganizationProfileView : UserControl
{
    public OrganizationProfileView() => AvaloniaXamlLoader.Load(this);
    private async void OnManageTags(object? objSender, RoutedEventArgs objArguments)
    { if (DataContext is OrganizationProfileViewModel vmProfile) await TokenSelectionDialogs.ManageTagsAsync(this, vmProfile.vmWorkspace, false); }
    private async void OnManageSkills(object? objSender, RoutedEventArgs objArguments)
    { if (DataContext is OrganizationProfileViewModel vmProfile) await TokenSelectionDialogs.ManageSkillsAsync(this, vmProfile.vmWorkspace, false); }

    private void OnSectionNavigate(object? objSender, RoutedEventArgs objArguments)
    {
        if (objSender is not Button { Tag: string strSection }) return;
        var scrProfile = this.FindControl<ScrollViewer>("scrOrganizationProfile");
        var ctlSection = this.FindControl<Control>(strSection);
        if (scrProfile?.Content is not Visual visContent || ctlSection?.TranslatePoint(default, visContent) is not { } ptTarget) return;
        scrProfile.Offset = new Vector(0, Math.Clamp(ptTarget.Y, 0, Math.Max(0, scrProfile.Extent.Height - scrProfile.Viewport.Height)));
    }
}
