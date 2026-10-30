using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HippoCrm.Desktop.Views;

public sealed partial class OrganizationsRootView : UserControl
{
    public OrganizationsRootView() => AvaloniaXamlLoader.Load(this);
}

