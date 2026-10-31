using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using HippoCrm.Desktop.ViewModels;

namespace HippoCrm.Desktop.Views;

public sealed partial class ContactEditorView : UserControl
{
    public static readonly StyledProperty<EditContactViewModel?> vmEditFieldsProperty =
        AvaloniaProperty.Register<ContactEditorView, EditContactViewModel?>(nameof(vmEditFields));
    public static readonly DirectProperty<ContactEditorView, bool> bEditingMultipleValuesProperty =
        AvaloniaProperty.RegisterDirect<ContactEditorView, bool>(nameof(bEditingMultipleValues), ctlEditor => ctlEditor.bEditingMultipleValues);
    public EditContactViewModel? vmEditFields { get => GetValue(vmEditFieldsProperty); set => SetValue(vmEditFieldsProperty, value); }
    public bool bEditingMultipleValues => vmEditFields is not null;
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs objArguments)
    {
        base.OnPropertyChanged(objArguments);
        if (objArguments.Property == vmEditFieldsProperty)
            RaisePropertyChanged(bEditingMultipleValuesProperty, objArguments.OldValue is not null, objArguments.NewValue is not null);
    }
    public ContactEditorView() => AvaloniaXamlLoader.Load(this);
    private async void OnManageTags(object? objSender, RoutedEventArgs objArguments)
    { if (DataContext is MainWindowViewModel vmMain) await TokenSelectionDialogs.ManageTagsAsync(this, vmMain, true); }
    private async void OnManageSkills(object? objSender, RoutedEventArgs objArguments)
    { if (DataContext is MainWindowViewModel vmMain) await TokenSelectionDialogs.ManageSkillsAsync(this, vmMain, true); }
}

