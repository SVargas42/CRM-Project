using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HippoCrm.Desktop;

// Opt-in behavior for single-line fields paired with an Add action. Existing
// command validation is shared with the button; Enter never saves a whole form.
public sealed class EnterSubmit : AvaloniaObject
{
    public static readonly AttachedProperty<ICommand?> CommandProperty=AvaloniaProperty.RegisterAttached<EnterSubmit,TextBox,ICommand?>("Command");
    public static readonly AttachedProperty<object?> CommandParameterProperty=AvaloniaProperty.RegisterAttached<EnterSubmit,TextBox,object?>("CommandParameter");
    private static readonly AttachedProperty<bool> HeldProperty=AvaloniaProperty.RegisterAttached<EnterSubmit,TextBox,bool>("Held");
    public static ICommand? GetCommand(AvaloniaObject objTarget)=>objTarget.GetValue(CommandProperty);
    public static void SetCommand(AvaloniaObject objTarget,ICommand? cmdValue)=>objTarget.SetValue(CommandProperty,cmdValue);
    public static object? GetCommandParameter(AvaloniaObject objTarget)=>objTarget.GetValue(CommandParameterProperty);
    public static void SetCommandParameter(AvaloniaObject objTarget,object? objValue)=>objTarget.SetValue(CommandParameterProperty,objValue);
    static EnterSubmit()
    {
        CommandProperty.Changed.AddClassHandler<TextBox>((txtInput,objArguments)=>
        {
            if(objArguments.OldValue is ICommand && objArguments.NewValue is ICommand) return;
            txtInput.RemoveHandler(InputElement.KeyDownEvent,OnKeyDown);
            txtInput.KeyUp-=OnKeyUp; txtInput.LostFocus-=OnLostFocus;
            txtInput.SetValue(HeldProperty,false);
            if(objArguments.NewValue is not ICommand) return;
            txtInput.AddHandler(InputElement.KeyDownEvent,OnKeyDown,RoutingStrategies.Tunnel);
            txtInput.KeyUp+=OnKeyUp; txtInput.LostFocus+=OnLostFocus;
        });
    }
    private static void OnKeyDown(object? objSender,KeyEventArgs objArguments)
    {
        if(objSender is not TextBox txtInput || objArguments.Key!=Key.Enter || objArguments.KeyModifiers!=KeyModifiers.None || txtInput.AcceptsReturn) return;
        var cmdAdd=GetCommand(txtInput); if(cmdAdd is null) return;
        // Consume the event before executing so a default dialog button cannot
        // also submit it. Hold state suppresses keyboard auto-repeat.
        objArguments.Handled=true;
        if(txtInput.GetValue(HeldProperty)) return;
        txtInput.SetValue(HeldProperty,true);
        if(txtInput.IsReadOnly || !txtInput.IsEffectivelyEnabled) return;
        var objParameter=GetCommandParameter(txtInput);
        if(cmdAdd.CanExecute(objParameter)) cmdAdd.Execute(objParameter);
    }
    private static void OnKeyUp(object? objSender,KeyEventArgs objArguments)
    { if(objSender is TextBox txtInput && objArguments.Key==Key.Enter) txtInput.SetValue(HeldProperty,false); }
    private static void OnLostFocus(object? objSender,RoutedEventArgs objArguments)
    { if(objSender is TextBox txtInput) txtInput.SetValue(HeldProperty,false); }
}
