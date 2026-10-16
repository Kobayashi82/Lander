using System.Runtime.InteropServices;
using Lander.App.Interop;
using Lander.Core.Terrain;
using static Lander.App.Interop.NativeConstants;

namespace Lander.App.Platform;

/// <summary>Enumera los monitores con su área de trabajo y su escalado DPI.</summary>
internal static unsafe class MonitorService
{
    [ThreadStatic]
    private static List<nint>? _handles;

    public static List<MonitorInfo> GetMonitors()
    {
        _handles = [];
        Native.EnumDisplayMonitors(0, 0, &OnMonitor, 0);

        var monitors = new List<MonitorInfo>(_handles.Count);
        foreach (var handle in _handles)
        {
            var info = new MONITORINFOEXW { cbSize = (uint)sizeof(MONITORINFOEXW) };
            if (!Native.GetMonitorInfo(handle, ref info))
            {
                continue;
            }

            var dpi = Native.GetDpiForMonitor(handle, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 ? dpiX : 96u;
            monitors.Add(new MonitorInfo(
                monitors.Count,
                info.rcMonitor.ToPixelRect(),
                info.rcWork.ToPixelRect(),
                dpi / 96.0,
                (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
        }

        _handles = null;

        // Orden estable: el principal primero y luego de izquierda a derecha.
        return monitors
            .OrderByDescending(m => m.IsPrimary)
            .ThenBy(m => m.Bounds.Left)
            .ThenBy(m => m.Bounds.Top)
            .Select((m, i) => m with { Index = i })
            .ToList();
    }

    [UnmanagedCallersOnly]
    private static int OnMonitor(nint monitor, nint hdc, RECT* rect, nint data)
    {
        _handles?.Add(monitor);
        return 1;
    }
}
