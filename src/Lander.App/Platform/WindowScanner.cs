using System.IO;
using System.Runtime.InteropServices;
using Lander.App.Interop;
using Lander.Core.Terrain;
using static Lander.App.Interop.NativeConstants;

namespace Lander.App.Platform;

/// <summary>
/// Recorre las ventanas de nivel superior en orden Z y se queda con las que forman terreno: visibles,
/// no minimizadas, no ocultas en otro escritorio virtual y que no sean partes del propio shell.
/// </summary>
internal sealed unsafe class WindowScanner
{
    private const int MinWidth = 80;
    private const int MinHeight = 40;

    /// <summary>Clases del shell y ventanas emergentes que nunca son terreno.</summary>
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
        "Xaml_WindowedPopupClass", "ForegroundStaging", "MultitaskingViewFrame", "TaskListThumbnailWnd",
        "Windows.Internal.Shell.TabProxyWindow", "Shell_InputSwitchTopLevelWindow", "EdgeUiInputTopWndClass",
        "ApplicationManager_ImmersiveShellWindow", "Internet Explorer_Hidden", "tooltips_class32", "#32768",
        "SysShadow", "IME", "MSCTFIME UI", "Chrome_WidgetWin_2", "LanderOverlay",
    };

    [ThreadStatic]
    private static List<nint>? _enumerated;

    private readonly Dictionary<uint, string?> _processNames = [];
    private readonly HashSet<nint> _ownWindows;

    public WindowScanner(HashSet<nint> ownWindows)
    {
        _ownWindows = ownWindows;
    }

    /// <summary>Ventanas visibles que forman terreno, de la más alta a la más baja en orden Z.</summary>
    public List<WindowInfo> Scan()
    {
        var handles = EnumerateTopLevel();
        var result = new List<WindowInfo>(32);
        foreach (var hwnd in handles)
        {
            if (TryDescribe(hwnd, out var info))
            {
                result.Add(info);
            }
        }

        if (_processNames.Count > 512)
        {
            _processNames.Clear();
        }

        return result;
    }

    /// <summary>Describe una ventana si cuenta como terreno.</summary>
    public bool TryDescribe(nint hwnd, out WindowInfo info)
    {
        info = null!;
        if (_ownWindows.Contains(hwnd) || !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd))
        {
            return false;
        }

        var exStyle = (uint)(long)Native.GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        var style = (uint)(long)Native.GetWindowLongPtr(hwnd, GWL_STYLE);

        // Capas que dejan pasar el ratón (superposiciones de otras apps) y ventanas de herramientas.
        if ((exStyle & WS_EX_TRANSPARENT) != 0 && (exStyle & WS_EX_LAYERED) != 0)
        {
            return false;
        }

        if ((exStyle & WS_EX_TOOLWINDOW) != 0 && (exStyle & WS_EX_APPWINDOW) == 0)
        {
            return false;
        }

        if (IsCloaked(hwnd))
        {
            return false;
        }

        var className = GetClassName(hwnd);
        if (className is null || IgnoredClasses.Contains(className))
        {
            return false;
        }

        var hasCaption = (style & WS_CAPTION) == WS_CAPTION;
        var owner = Native.GetWindow(hwnd, GW_OWNER);
        if (owner != 0 && !hasCaption && (exStyle & WS_EX_APPWINDOW) == 0)
        {
            return false;
        }

        if (!hasCaption && GetTitleLength(hwnd) == 0)
        {
            return false;
        }

        if (!TryGetBounds(hwnd, out var bounds) || bounds.Right - bounds.Left < MinWidth || bounds.Bottom - bounds.Top < MinHeight)
        {
            return false;
        }

        info = new WindowInfo(hwnd, bounds.ToPixelRect(), Native.IsZoomed(hwnd), GetProcessName(hwnd));
        return true;
    }

    public static bool IsCloaked(nint hwnd)
    {
        int cloaked = 0;
        return Native.DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, &cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    /// <summary>Límites reales de la ventana según DWM (sin bordes invisibles ni sombras).</summary>
    public static bool TryGetBounds(nint hwnd, out RECT bounds)
    {
        RECT rect;
        if (Native.DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, &rect, (uint)sizeof(RECT)) == 0)
        {
            bounds = rect;
            return true;
        }

        return Native.GetWindowRect(hwnd, out bounds);
    }

    public static string? GetClassName(nint hwnd)
    {
        var buffer = stackalloc char[256];
        var length = Native.GetClassName(hwnd, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : null;
    }

    public static string GetTitle(nint hwnd)
    {
        var buffer = stackalloc char[256];
        var length = Native.GetWindowText(hwnd, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    public static bool IsTopLevel(nint hwnd) => Native.GetAncestor(hwnd, GA_ROOT) == hwnd;

    /// <summary>Nombre del proceso dueño (en minúsculas, sin extensión).</summary>
    public string? GetProcessName(nint hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            return null;
        }

        if (_processNames.TryGetValue(pid, out var cached))
        {
            return cached;
        }

        var path = GetProcessPath(pid);
        var name = path is null ? null : Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        _processNames[pid] = name;
        return name;
    }

    public static string? GetProcessPath(uint pid)
    {
        var process = Native.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
        {
            return null;
        }

        try
        {
            var buffer = stackalloc char[1024];
            uint size = 1024;
            return Native.QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            Native.CloseHandle(process);
        }
    }

    private static int GetTitleLength(nint hwnd)
    {
        var buffer = stackalloc char[2];
        return Native.GetWindowText(hwnd, buffer, 2);
    }

    private static List<nint> EnumerateTopLevel()
    {
        _enumerated = new List<nint>(256);
        Native.EnumWindows(&OnWindow, 0);
        var list = _enumerated;
        _enumerated = null;
        return list;
    }

    [UnmanagedCallersOnly]
    private static int OnWindow(nint hwnd, nint lParam)
    {
        _enumerated?.Add(hwnd);
        return 1;
    }
}
