using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using HippoCrm.Desktop.ViewModels;

namespace HippoCrm.Desktop.Views;

public sealed partial class SkillSelectionWindow : Window
{
    public SkillSelectionWindow() : this(new SkillSelectionViewModel([], () => Task.FromResult<IReadOnlyList<string>>([]))) { }
    public SkillSelectionWindow(SkillSelectionViewModel vmSelection)
    {
        DataContext = vmSelection; AvaloniaXamlLoader.Load(this);
        Opened += async (objSender, objArguments) => await vmSelection.LoadAsync();
        Closed += (objSender, objArguments) => vmSelection.Clear();
    }
    private void OnClose(object? objSender, RoutedEventArgs objArguments) => Close(null);
    private void OnSave(object? objSender, RoutedEventArgs objArguments)
    {
        if (DataContext is SkillSelectionViewModel { bLoading: false } vmSelection) Close(vmSelection.SelectedValues());
    }
}

