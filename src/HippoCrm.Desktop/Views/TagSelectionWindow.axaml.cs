using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using HippoCrm.Desktop.ViewModels;

namespace HippoCrm.Desktop.Views;

public sealed partial class TagSelectionWindow : Window
{
    public TagSelectionWindow() : this(new TagSelectionViewModel([], () => Task.FromResult<IReadOnlyList<string>>([]))) { }
    public TagSelectionWindow(TagSelectionViewModel vmSelection)
    {
        DataContext = vmSelection; AvaloniaXamlLoader.Load(this);
        Opened += async (objSender, objArguments) => await vmSelection.LoadAsync();
        Closed += (objSender, objArguments) => vmSelection.Clear();
    }
    private void OnClose(object? objSender, RoutedEventArgs objArguments) => Close(null);
    private void OnSave(object? objSender, RoutedEventArgs objArguments)
    {
        if (DataContext is TagSelectionViewModel { bLoading: false } vmSelection) Close(vmSelection.SelectedValues());
    }
}

