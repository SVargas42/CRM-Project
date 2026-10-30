using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using HippoCrm.Desktop.ViewModels;

namespace HippoCrm.Desktop.Views;

public sealed partial class EditContactView : UserControl
{
    public EditContactView() => AvaloniaXamlLoader.Load(this);

    private async void OnArchiveRequested(object? objSender, RoutedEventArgs objArguments)
    {
        if (DataContext is not EditContactViewModel vmEdit || vmEdit.vmWorkspace.bBusy || vmEdit.vmWorkspace.bLocking
            || !vmEdit.vmWorkspace.bCanEditContact || TopLevel.GetTopLevel(this) is not Window frmOwner) return;
        var dlgArchive = new ArchiveContactDialog(vmEdit.strArchiveAction);
        if (await dlgArchive.ShowDialog<bool>(frmOwner) && ReferenceEquals(DataContext, vmEdit))
            vmEdit.cmdArchive.Execute(null);
    }
}

