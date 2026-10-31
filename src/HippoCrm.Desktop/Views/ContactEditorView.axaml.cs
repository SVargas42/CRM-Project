using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using HippoCrm.Desktop.ViewModels;

namespace HippoCrm.Desktop.Views;

public sealed partial class ContactEditorView : UserControl
{
    public ContactEditorView() => AvaloniaXamlLoader.Load(this);
    private async void OnManageTags(object? objSender, RoutedEventArgs objArguments)
    { if (DataContext is MainWindowViewModel vmMain) await TokenSelectionDialogs.ManageTagsAsync(this, vmMain, true); }
    private async void OnManageSkills(object? objSender, RoutedEventArgs objArguments)
    { if (DataContext is MainWindowViewModel vmMain) await TokenSelectionDialogs.ManageSkillsAsync(this, vmMain, true); }
}

