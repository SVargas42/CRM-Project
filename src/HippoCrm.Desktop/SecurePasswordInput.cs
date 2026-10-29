using System.Security;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HippoCrm.Desktop;

// TextBox stores only bullets. Secrets are never bound through its immutable Text/undo properties.
// Clipboard and drag/drop editing are blocked so plaintext cannot bypass secure input handling.
public sealed class SecurePasswordInput : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);
    private readonly SecureString secPassword;

    public SecurePasswordInput(SecureString secPassword)
    {
        this.secPassword = secPassword;
        // An editable TextBox provides the native text-input client; tunnel handlers below
        // consume committed text before the base editor can retain it in Text.
        IsReadOnly = false; IsUndoEnabled = false; PasswordChar = '●';
        DragDrop.SetAllowDrop(this, false);
        CopyingToClipboard += BlockClipboard;
        CuttingToClipboard += BlockClipboard;
        PastingFromClipboard += BlockClipboard;
        Text = new string('●', secPassword.Length);
        AddHandler(TextInputEvent, OnSecretTextInput, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnSecretKeyDown, RoutingStrategies.Tunnel);
    }

    private static void BlockClipboard(object? objSender, RoutedEventArgs objArguments) => objArguments.Handled = true;

    private void OnSecretTextInput(object? objSender, TextInputEventArgs objArguments)
    {
        objArguments.Handled = true;
        if (string.IsNullOrEmpty(objArguments.Text)) return;
        var iPosition = RemoveSelection();
        // The framework owns the transient TextInput string; this control never retains it.
        foreach (var chrCharacter in objArguments.Text)
        {
            if (char.IsControl(chrCharacter) || secPassword.Length >= 256) continue;
            secPassword.InsertAt(iPosition++, chrCharacter);
        }
        RenderMask(iPosition);
    }

    private void OnSecretKeyDown(object? objSender, KeyEventArgs objArguments)
    {
        if (objArguments.Key is not (Key.Back or Key.Delete)) return;
        objArguments.Handled = true;
        var bSelected = SelectionStart != SelectionEnd;
        var iPosition = RemoveSelection();
        if (!bSelected && objArguments.Key == Key.Back && iPosition > 0) secPassword.RemoveAt(--iPosition);
        else if (!bSelected && objArguments.Key == Key.Delete && iPosition < secPassword.Length) secPassword.RemoveAt(iPosition);
        RenderMask(iPosition);
    }

    private int RemoveSelection()
    {
        var iStart = Math.Clamp(Math.Min(SelectionStart, SelectionEnd), 0, secPassword.Length);
        var iEnd = Math.Clamp(Math.Max(SelectionStart, SelectionEnd), iStart, secPassword.Length);
        for (var iCharacter = iStart; iCharacter < iEnd; iCharacter++) secPassword.RemoveAt(iStart);
        return iStart;
    }

    private void RenderMask(int iPosition)
    {
        Text = new string('●', secPassword.Length);
        SelectionStart = iPosition; SelectionEnd = iPosition; CaretIndex = iPosition;
    }
}
