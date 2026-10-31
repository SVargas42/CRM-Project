using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace HippoCrm.Desktop.Views;

public sealed partial class ArchiveContactDialog : Window
{
    public ArchiveContactDialog() : this("Archive contact") { }
    public ArchiveContactDialog(string strAction) : this("Confirm contact archive status",
        strAction + "? The contact record and its history will be retained.", "Confirm", "Cancel") { }

    public ArchiveContactDialog(string strTitle, string strMessage, string strConfirm, string strCancel)
    {
        AvaloniaXamlLoader.Load(this);
        Title = strTitle;
        this.FindControl<TextBlock>("txtArchiveMessage")!.Text = strMessage;
        this.FindControl<Button>("btnConfirmArchive")!.Content = strConfirm;
        this.FindControl<Button>("btnCancelArchive")!.Content = strCancel;
        Opened += (objSender, objArguments) => this.FindControl<Button>("btnCancelArchive")!.Focus();
    }

    private void OnCancel(object? objSender, RoutedEventArgs objArguments) => Close(false);
    private void OnConfirm(object? objSender, RoutedEventArgs objArguments) => Close(true);
}

