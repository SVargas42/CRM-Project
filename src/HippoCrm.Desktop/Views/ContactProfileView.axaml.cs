using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace HippoCrm.Desktop.Views;

public sealed partial class ContactProfileView : UserControl
{
    public ContactProfileView() => AvaloniaXamlLoader.Load(this);

    private void OnSectionNavigate(object? objSender, RoutedEventArgs objArguments)
    {
        if (objSender is not Button { Tag: string strSection }) return;
        var scrProfile = this.FindControl<ScrollViewer>("scrContactProfile");
        var ctlSection = this.FindControl<Control>(strSection);
        if (scrProfile?.Content is not Visual visContent || ctlSection is null) return;
        var ptSection = ctlSection.TranslatePoint(default, visContent);
        if (ptSection is not { } ptTarget) return;
        // Scroll only this content column; the contents menu and shell remain stationary.
        scrProfile.Offset = new Vector(0, Math.Clamp(ptTarget.Y, 0, Math.Max(0, scrProfile.Extent.Height - scrProfile.Viewport.Height)));
    }
}
