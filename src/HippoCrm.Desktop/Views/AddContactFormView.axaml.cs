using Avalonia.Controls;
using Avalonia.Interactivity;
using HippoCrm.Desktop.ViewModels;

namespace HippoCrm.Desktop.Views;

public sealed partial class AddContactFormView : UserControl
{
    public AddContactFormView() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

    private void OnAddMiddleName(object? objSender, RoutedEventArgs objArguments)
    {
        if (DataContext is MainWindowViewModel vmMain) vmMain.cmdAddMiddleName.Execute(null);
    }

    private async void OnManageTags(object? objSender, RoutedEventArgs objArguments)
    {
        if (DataContext is MainWindowViewModel vmMain) await TokenSelectionDialogs.ManageTagsAsync(this, vmMain, true);
    }

    private async void OnManageSkills(object? objSender, RoutedEventArgs objArguments)
    {
        if (DataContext is MainWindowViewModel vmMain) await TokenSelectionDialogs.ManageSkillsAsync(this, vmMain);
    }
}
