using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace HippoCrm.Desktop.Views;

public sealed partial class ArchiveContactDialog : Window
{
    public ArchiveContactDialog() : this("Archive contact") { }
    public ArchiveContactDialog(string strAction)
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<TextBlock>("txtArchiveMessage")!.Text = strAction + "? The contact record and its history will be retained.";
        Opened += (objSender, objArguments) => this.FindControl<Button>("btnCancelArchive")!.Focus();
    }

    private void OnCancel(object? objSender, RoutedEventArgs objArguments) => Close(false);
    private void OnConfirm(object? objSender, RoutedEventArgs objArguments) => Close(true);
}

