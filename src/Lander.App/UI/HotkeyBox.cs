using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lander.App.Services;
using Lander.Core.Settings;

namespace Lander.App.UI;

/// <summary>Caja que captura una combinación de teclas (al menos un modificador y una tecla).</summary>
public sealed class HotkeyBox : TextBox
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey),
        typeof(HotkeySetting),
        typeof(HotkeyBox),
        new FrameworkPropertyMetadata(HotkeySetting.Default, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).ShowCurrent()));

    public HotkeyBox()
    {
        // Mismo estilo (Fluent) que cualquier TextBox.
        SetResourceReference(StyleProperty, typeof(TextBox));
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        MinWidth = 170;
        TextAlignment = TextAlignment.Center;
        Cursor = Cursors.Hand;
        ShowCurrent();
    }

    public HotkeySetting Hotkey
    {
        get => (HotkeySetting)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        Text = "Pulsa la combinación…";
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        ShowCurrent();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Escape or Key.Tab)
        {
            Keyboard.ClearFocus();
            MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.None or Key.ImeProcessed or Key.DeadCharProcessed)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var hotkey = new HotkeySetting
        {
            Control = modifiers.HasFlag(ModifierKeys.Control),
            Alt = modifiers.HasFlag(ModifierKeys.Alt),
            Shift = modifiers.HasFlag(ModifierKeys.Shift),
            Windows = modifiers.HasFlag(ModifierKeys.Windows),
            VirtualKey = KeyInterop.VirtualKeyFromKey(key),
        };

        if (!hotkey.HasModifier)
        {
            Text = "Añade Ctrl, Alt, Mayús o Win";
            return;
        }

        Hotkey = hotkey;
        Keyboard.ClearFocus();
        MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    private void ShowCurrent() => Text = Hotkey is { } hotkey ? HotkeyText.Format(hotkey) : string.Empty;
}
