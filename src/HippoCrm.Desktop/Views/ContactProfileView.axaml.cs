using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HippoCrm.Desktop.Views;

public sealed partial class ContactProfileView : UserControl
{
    public ContactProfileView() => AvaloniaXamlLoader.Load(this);
}

