using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HippoCrm.Desktop.Views;

public sealed partial class ContactsRootView : UserControl
{
    public ContactsRootView() => AvaloniaXamlLoader.Load(this);
}

