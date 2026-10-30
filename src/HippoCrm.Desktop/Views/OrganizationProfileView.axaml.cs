using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HippoCrm.Desktop.Views;

public sealed partial class OrganizationProfileView : UserControl
{
    public OrganizationProfileView() => AvaloniaXamlLoader.Load(this);
}

