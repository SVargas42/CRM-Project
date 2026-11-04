using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HippoCrm.Desktop;

/// <summary>A consistent, compact text entry control used throughout the CRM.</summary>
public class CrmTextBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);

    public CrmTextBox()
    {
        MinHeight = 30;
        Height = 30;
        Padding = new Thickness(11, 4);
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
    }
}
