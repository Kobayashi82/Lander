using System.Runtime.InteropServices;
using Lander.App.Interop;
using Lander.App.Services;
using Lander.Core.Game;
using Lander.Core.Settings;
using static Lander.App.Interop.NativeConstants;

namespace Lander.App.Platform;

/// <summary>
/// Atajo global para tomar el control (RegisterHotKey, no un hook de teclado). En modo manual registra
/// también las flechas, Espacio, Esc, A, D y G: así esas teclas no llegan a otras ventanas y nunca se escribe
/// por accidente; el resto del teclado sigue funcionando con normalidad.
/// </summary>
internal sealed class HotkeyManager(nint hwnd) : IDisposable
{
    public const int ToggleId = 1;
    public const int EscapeId = 15;

    private const int KeyA = 0x41;
    private const int KeyD = 0x44;
    private const int KeyG = 0x47;

    private static readonly (int Id, int Key)[] GameKeys =
    [
        (10, VK_UP), (11, VK_DOWN), (12, VK_LEFT), (13, VK_RIGHT), (14, VK_SPACE), (EscapeId, VK_ESCAPE),
        (16, KeyA), (17, KeyD), (18, KeyG),
    ];

    private bool _toggleRegistered;
    private bool _gameKeysRegistered;

    /// <summary>Registra el atajo para alternar el control. Devuelve false si otra aplicación ya lo usa.</summary>
    public bool RegisterToggle(HotkeySetting hotkey)
    {
        if (_toggleRegistered)
        {
            Native.UnregisterHotKey(hwnd, ToggleId);
            _toggleRegistered = false;
        }

        var modifiers = MOD_NOREPEAT
            | (hotkey.Control ? MOD_CONTROL : 0)
            | (hotkey.Alt ? MOD_ALT : 0)
            | (hotkey.Shift ? MOD_SHIFT : 0)
            | (hotkey.Windows ? MOD_WIN : 0);

        _toggleRegistered = Native.RegisterHotKey(hwnd, ToggleId, modifiers, (uint)hotkey.VirtualKey);
        if (!_toggleRegistered)
        {
            Log.Warn($"No se pudo registrar el atajo (error {Marshal.GetLastPInvokeError()}).");
        }

        return _toggleRegistered;
    }

    public void SetGameKeys(bool enabled)
    {
        if (enabled == _gameKeysRegistered)
        {
            return;
        }

        foreach (var (id, key) in GameKeys)
        {
            if (enabled)
            {
                if (!Native.RegisterHotKey(hwnd, id, 0, (uint)key))
                {
                    Log.Warn($"La tecla 0x{key:X2} ya está en uso por otra aplicación.");
                }
            }
            else
            {
                Native.UnregisterHotKey(hwnd, id);
            }
        }

        _gameKeysRegistered = enabled;
    }

    /// <summary>Teclas del juego pulsadas ahora mismo (estado físico, aunque estén registradas).</summary>
    public static ManualControls ReadControls() => new(
        IsDown(VK_UP), IsDown(VK_DOWN), IsDown(VK_LEFT), IsDown(VK_RIGHT), IsDown(VK_SPACE),
        SlideLeft: IsDown(KeyA), SlideRight: IsDown(KeyD), Legs: IsDown(KeyG));

    public void Dispose()
    {
        SetGameKeys(false);
        if (_toggleRegistered)
        {
            Native.UnregisterHotKey(hwnd, ToggleId);
            _toggleRegistered = false;
        }
    }

    private static bool IsDown(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;
}
