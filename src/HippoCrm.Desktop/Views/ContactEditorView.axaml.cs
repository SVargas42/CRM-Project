using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HippoCrm.Desktop.Views;

public sealed partial class ContactEditorView : UserControl
{
    public ContactEditorView() => AvaloniaXamlLoader.Load(this);
}

