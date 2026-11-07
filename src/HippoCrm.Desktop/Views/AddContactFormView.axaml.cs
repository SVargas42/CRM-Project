using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using HippoCrm.Desktop.ViewModels;

namespace HippoCrm.Desktop.Views;

public sealed partial class AddContactFormView : UserControl
{
    public AddContactFormView() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

    private void OnAddMiddleName(object? objSender, RoutedEventArgs objArguments)
    {
        if (DataContext is MainWindowViewModel vmMain) vmMain.cmdAddMiddleName.Execute(null);
    }

    private async void OnAddSocialMediaPlatform(object? objSender, RoutedEventArgs objArguments)
    {
        if (DataContext is not MainWindowViewModel vmMain || TopLevel.GetTopLevel(this) is not Window frmOwner) return;

        var txtPlatform = new TextBox { Watermark = "Platform name", MinWidth = 300 };
        AutomationProperties.SetName(txtPlatform, "New social media platform");
        var btnAdd = new Button { Content = "Add platform", IsDefault = true };
        var frmAddPlatform = new Window
        {
            Title = "Add social media platform",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Padding = new Thickness(24),
            Content = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = "Enter the platform name to add it to the dropdown." },
                    txtPlatform,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            new Button { Content = "Cancel", IsCancel = true },
                            btnAdd
                        }
                    }
                }
            }
        };

        btnAdd!.Click += (_, _) => frmAddPlatform.Close(txtPlatform.Text?.Trim());
        var strPlatform = await frmAddPlatform.ShowDialog<string?>(frmOwner);
        if (!string.IsNullOrWhiteSpace(strPlatform)) vmMain.AddSocialMediaPlatform(strPlatform);
    }

    private async void OnManageTags(object? objSender, RoutedEventArgs objArguments)
    {
        if (DataContext is MainWindowViewModel vmMain) await TokenSelectionDialogs.ManageTagsAsync(this, vmMain, true);
    }

    private async void OnManageSkills(object? objSender, RoutedEventArgs objArguments)
    {
        if (DataContext is MainWindowViewModel vmMain) await TokenSelectionDialogs.ManageSkillsAsync(this, vmMain);
    }
}
