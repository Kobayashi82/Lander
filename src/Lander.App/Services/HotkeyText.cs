using System.Windows.Input;
using Lander.Core.Settings;

namespace Lander.App.Services;

/// <summary>Texto legible de un atajo de teclado, como «Ctrl+Alt+L».</summary>
internal static class HotkeyText
{
    public static string Format(HotkeySetting hotkey)
    {
        var parts = new List<string>(5);
        if (hotkey.Control)
        {
            parts.Add("Ctrl");
        }

        if (hotkey.Alt)
        {
            parts.Add("Alt");
        }

        if (hotkey.Shift)
        {
            parts.Add("Mayús");
        }

        if (hotkey.Windows)
        {
            parts.Add("Win");
        }

        parts.Add(KeyName(hotkey.VirtualKey));
        return string.Join("+", parts);
    }

    public static string KeyName(int virtualKey)
    {
        var key = KeyInterop.KeyFromVirtualKey(virtualKey);
        return key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (key - Key.NumPad0),
            Key.Space => "Espacio",
            Key.Return => "Intro",
            Key.Escape => "Esc",
            Key.Back => "Retroceso",
            Key.Delete => "Supr",
            Key.Insert => "Insert",
            Key.Home => "Inicio",
            Key.End => "Fin",
            Key.PageUp => "Re Pág",
            Key.PageDown => "Av Pág",
            Key.Up => "↑",
            Key.Down => "↓",
            Key.Left => "←",
            Key.Right => "→",
            Key.None => $"0x{virtualKey:X2}",
            _ => key.ToString(),
        };
    }
}
