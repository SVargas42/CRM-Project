using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using HippoCrm.Core;

namespace HippoCrm.Desktop.Views;

public sealed partial class DuplicateReviewWindow : Window
{
    private readonly IReadOnlyList<DuplicateCandidate> lstMatches;
    public DuplicateReviewWindow() : this([]) { }
    public DuplicateReviewWindow(IReadOnlyList<DuplicateCandidate> lstMatches,bool bNewRecord=false)
    {
        this.lstMatches=lstMatches; AvaloniaXamlLoader.Load(this);
        this.FindControl<Button>("btnIgnoreDuplicate")!.Content=bNewRecord?"Create Separate Record":"Save Without Linking";
        var lstControl=this.FindControl<ListBox>("lstDuplicateMatches")!; lstControl.ItemsSource=lstMatches;
        lstControl.SelectionChanged+=(objSender,objArguments)=>this.FindControl<Button>("btnLinkDuplicate")!.IsEnabled=lstControl.SelectedItem is DuplicateCandidate { bCanLink:true };
        Opened+=(objSender,objArguments)=>{ Opacity=1; this.FindControl<Button>("btnCancelDuplicate")!.Focus(); };
        KeyDown+=(objSender,objArguments)=>
        {
            var enModifier=OperatingSystem.IsMacOS()?KeyModifiers.Meta:KeyModifiers.Control;
            if(objArguments.Key==Key.Enter && objArguments.KeyModifiers==enModifier) { objArguments.Handled=true; OnIgnore(this,new RoutedEventArgs()); }
            if(objArguments.Key==Key.Escape) { objArguments.Handled=true; Close(null); }
        };
        Closed+=(objSender,objArguments)=>{ lstControl.ItemsSource=null; DataContext=null; };
    }
    private void OnCancel(object? objSender,RoutedEventArgs objArguments)=>Close(null);
    private void OnIgnore(object? objSender,RoutedEventArgs objArguments)=>Close(new DuplicateDecision(DuplicateResolution.Ignore,lstMatches));
    private void OnLink(object? objSender,RoutedEventArgs objArguments)
    {
        if(this.FindControl<ListBox>("lstDuplicateMatches")!.SelectedItem is DuplicateCandidate { bCanLink:true } objTarget)
            Close(new DuplicateDecision(DuplicateResolution.Link,lstMatches,objTarget.lngId));
    }
}
