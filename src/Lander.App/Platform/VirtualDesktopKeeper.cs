using System.Runtime.InteropServices;
using Lander.App.Interop;
using Lander.App.Services;

namespace Lander.App.Platform;

/// <summary>
/// Mantiene las ventanas del lander en el escritorio virtual activo: cuando el usuario cambia de
/// escritorio, las mueve al nuevo (IVirtualDesktopManager, API documentada de Windows 10/11).
/// </summary>
internal sealed class VirtualDesktopKeeper : IDisposable
{
    private static readonly Guid ManagerClsid = new("AA509086-5CA9-4C25-8F95-589D3C07B48A");

    private readonly IVirtualDesktopManager? _manager;

    public VirtualDesktopKeeper()
    {
        try
        {
            if (Type.GetTypeFromCLSID(ManagerClsid) is { } type)
            {
                _manager = Activator.CreateInstance(type) as IVirtualDesktopManager;
            }
        }
        catch (COMException ex)
        {
            Log.Warn("Escritorios virtuales no disponibles.", ex);
        }
    }

    /// <summary>
    /// Si alguna de <paramref name="windows"/> no está en el escritorio activo, las mueve al escritorio de
    /// <paramref name="reference"/> (normalmente la ventana en primer plano).
    /// </summary>
    public void Follow(IEnumerable<nint> windows, nint reference)
    {
        if (_manager is null || reference == 0)
        {
            return;
        }

        try
        {
            if (_manager.GetWindowDesktopId(reference, out var desktop) != 0 || desktop == Guid.Empty)
            {
                return;
            }

            foreach (var hwnd in windows)
            {
                if (_manager.IsWindowOnCurrentVirtualDesktop(hwnd, out var onCurrent) == 0 && onCurrent == 0)
                {
                    _manager.MoveWindowToDesktop(hwnd, ref desktop);
                }
            }
        }
        catch (COMException)
        {
            // Ventanas del sistema sin escritorio: se ignora.
        }
    }

    public void Dispose()
    {
        if (_manager is not null)
        {
            Marshal.ReleaseComObject(_manager);
        }
    }

    [ComImport]
    [Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(nint topLevelWindow, out int onCurrentDesktop);

        [PreserveSig]
        int GetWindowDesktopId(nint topLevelWindow, out Guid desktopId);

        [PreserveSig]
        int MoveWindowToDesktop(nint topLevelWindow, ref Guid desktopId);
    }
}
