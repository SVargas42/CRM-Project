using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HippoCrm.Desktop.Views;

public sealed partial class OrganizationEditorView : UserControl
{
    public OrganizationEditorView() => AvaloniaXamlLoader.Load(this);
}

