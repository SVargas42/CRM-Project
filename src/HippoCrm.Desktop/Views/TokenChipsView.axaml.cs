using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HippoCrm.Desktop.Views;

public sealed partial class TokenChipsView : UserControl
{
    public static readonly StyledProperty<IEnumerable<string>?> lstValuesProperty =
        AvaloniaProperty.Register<TokenChipsView, IEnumerable<string>?>(nameof(lstValues));
    public IEnumerable<string>? lstValues { get => GetValue(lstValuesProperty); set => SetValue(lstValuesProperty, value); }
    public TokenChipsView() => AvaloniaXamlLoader.Load(this);
}
