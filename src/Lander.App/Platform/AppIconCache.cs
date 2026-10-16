using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lander.App.Interop;
using static Lander.App.Interop.NativeConstants;

namespace Lander.App.Platform;

/// <summary>Icono de la aplicación dueña de una ventana (para las misiones), cacheado por ejecutable.</summary>
internal sealed unsafe class AppIconCache
{
    private readonly Dictionary<string, ImageSource?> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<nint, ImageSource?> _byWindow = [];

    public ImageSource? Get(long handle)
    {
        if (handle == 0)
        {
            return null;
        }

        var hwnd = (nint)handle;
        if (_byWindow.TryGetValue(hwnd, out var cached))
        {
            return cached;
        }

        Native.GetWindowThreadProcessId(hwnd, out var pid);
        var path = pid == 0 ? null : WindowScanner.GetProcessPath(pid);
        ImageSource? icon = null;
        if (path is not null && !_byPath.TryGetValue(path, out icon))
        {
            icon = Extract(path);
            _byPath[path] = icon;
        }

        if (_byWindow.Count > 64)
        {
            _byWindow.Clear();
        }

        _byWindow[hwnd] = icon;
        return icon;
    }

    private static BitmapSource? Extract(string path)
    {
        var info = default(SHFILEINFOW);
        if (Native.SHGetFileInfo(path, 0, ref info, (uint)sizeof(SHFILEINFOW), SHGFI_ICON | SHGFI_LARGEICON) == 0 || info.hIcon == 0)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
        finally
        {
            Native.DestroyIcon(info.hIcon);
        }
    }
}
