using System.Windows.Interop;
using Lander.App.Interop;
using Lander.App.Services;
using static Lander.App.Interop.NativeConstants;

namespace Lander.App.UI;

/// <summary>
/// Icono en el área de notificación (Shell_NotifyIcon con NOTIFYICON_VERSION_4). Se vuelve a añadir
/// solo si el Explorador se reinicia.
/// </summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    private const uint CallbackMessage = WM_APP + 1;
    private const uint IconId = 1;

    private readonly HwndSource _source;
    private readonly uint _taskbarCreated;
    private nint _icon;
    private string _tooltip = "Lander";
    private bool _added;

    public TrayIcon()
    {
        var parameters = new HwndSourceParameters("LanderTray")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)WS_POPUP),
            ExtendedWindowStyle = (int)WS_EX_TOOLWINDOW,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
    }

    /// <summary>Clic izquierdo (o Intro/Espacio sobre el icono).</summary>
    public event Action? Activated;

    /// <summary>Clic derecho o tecla de menú.</summary>
    public event Action? MenuRequested;

    public nint Handle => _source.Handle;

    /// <summary>Muestra o actualiza el icono. Se queda con <paramref name="icon"/> y libera el anterior.</summary>
    public void Update(nint icon, string tooltip)
    {
        var previous = _icon;
        _icon = icon;
        _tooltip = tooltip;
        var data = CreateData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        if (!_added)
        {
            _added = Native.Shell_NotifyIcon(NIM_ADD, ref data);
            if (_added)
            {
                data.uVersion = NOTIFYICON_VERSION_4;
                Native.Shell_NotifyIcon(NIM_SETVERSION, ref data);
            }
            else
            {
                Log.Warn("No se pudo añadir el icono a la bandeja.");
            }
        }
        else
        {
            Native.Shell_NotifyIcon(NIM_MODIFY, ref data);
        }

        if (previous != 0 && previous != icon)
        {
            Native.DestroyIcon(previous);
        }
    }

    /// <summary>Aviso discreto (Windows lo muestra como notificación, sin sonido).</summary>
    public void ShowBalloon(string title, string message)
    {
        if (!_added)
        {
            return;
        }

        var data = CreateData(NIF_INFO);
        Copy(title, data.szInfoTitle, 64);
        Copy(message, data.szInfo, 256);
        data.dwInfoFlags = NIIF_INFO | NIIF_NOSOUND;
        Native.Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = CreateData(0);
            Native.Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }

        if (_icon != 0)
        {
            Native.DestroyIcon(_icon);
            _icon = 0;
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private NOTIFYICONDATAW CreateData(uint flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _source.Handle,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };
        Copy(_tooltip, data.szTip, 128);
        return data;
    }

    private static void Copy(string text, char* destination, int capacity)
    {
        var length = Math.Min(text.Length, capacity - 1);
        for (var i = 0; i < length; i++)
        {
            destination[i] = text[i];
        }

        destination[length] = '\0';
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == (int)CallbackMessage)
        {
            switch ((uint)(lParam.ToInt64() & 0xFFFF))
            {
                case NIN_SELECT:
                case NIN_KEYSELECT:
                    Activated?.Invoke();
                    break;
                case WM_CONTEXTMENU:
                    MenuRequested?.Invoke();
                    break;
            }

            handled = true;
        }
        else if (message == (int)_taskbarCreated && _taskbarCreated != 0)
        {
            // El Explorador se ha reiniciado: hay que volver a añadir el icono.
            _added = false;
            Update(_icon, _tooltip);
        }

        return 0;
    }
}
