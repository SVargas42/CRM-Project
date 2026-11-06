using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HippoCrm.Desktop.Views;

public sealed partial class CuiBanner : UserControl
{
    public CuiBanner() => AvaloniaXamlLoader.Load(this);
}
