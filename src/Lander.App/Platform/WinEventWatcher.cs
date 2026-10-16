using System.Runtime.InteropServices;
using Lander.App.Interop;
using Lander.App.Services;
using static Lander.App.Interop.NativeConstants;

namespace Lander.App.Platform;

/// <summary>
/// Escucha (fuera de proceso, sin inyectar nada) los eventos de ventanas: aparecen, se mueven,
/// se minimizan, se cierran o cambian de escritorio virtual. Las notificaciones llegan por el bucle de
/// mensajes del hilo que crea el observador.
/// </summary>
internal sealed unsafe class WinEventWatcher : IDisposable
{
    [ThreadStatic]
    private static WinEventWatcher? _current;

    private readonly List<nint> _hooks = [];

    /// <summary>Llamadas recibidas en total (diagnóstico).</summary>
    [ThreadStatic]
    public static int RawCount;

    public WinEventWatcher()
    {
        _current = this;
        Hook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND);
        Hook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND);
        Hook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND);
        Hook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_HIDE);
        Hook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE);
        Hook(EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED);
    }

    /// <summary>Evento de una ventana (tipo de evento, ventana).</summary>
    public event Action<uint, nint>? WindowEvent;

    public void Dispose()
    {
        foreach (var hook in _hooks)
        {
            Native.UnhookWinEvent(hook);
        }

        _hooks.Clear();
        if (_current == this)
        {
            _current = null;
        }
    }

    private void Hook(uint min, uint max)
    {
        var hook = Native.SetWinEventHook(min, max, 0, &OnEvent, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (hook != 0)
        {
            _hooks.Add(hook);
        }
        else
        {
            Log.Warn($"SetWinEventHook falló para 0x{min:X4}-0x{max:X4}.");
        }
    }

    [UnmanagedCallersOnly]
    private static void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        RawCount++;
        if (hwnd == 0 || idObject != OBJID_WINDOW || idChild != CHILDID_SELF)
        {
            return;
        }

        try
        {
            _current?.WindowEvent?.Invoke(eventType, hwnd);
        }
        catch (Exception ex)
        {
            Log.Error("Error al procesar un evento de ventana.", ex);
        }
    }
}
