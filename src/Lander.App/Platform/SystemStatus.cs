using System.IO;
using Lander.App.Interop;
using Lander.Core.Geometry;
using Lander.Core.Terrain;
using Microsoft.Win32;
using static Lander.App.Interop.NativeConstants;

namespace Lander.App.Platform;

/// <summary>
/// Estado del sistema que afecta al lander: inactividad, batería, ahorro de energía, modo oscuro,
/// «No molestar» y aplicaciones a pantalla completa. Las consultas caras se hacen con poca frecuencia.
/// </summary>
internal sealed class SystemStatus
{
    private const double PowerInterval = 30;
    private const double NotificationStateInterval = 2;

    private double _nextPowerCheck;
    private double _nextNotificationCheck;
    private bool _themeDirty = true;

    public bool BatteryLow { get; private set; }

    public bool PowerSaver { get; private set; }

    public bool DarkMode { get; private set; }

    /// <summary>Notificaciones silenciadas (No molestar, presentación).</summary>
    public bool QuietMode { get; private set; }

    /// <summary>Juego DirectX exclusivo o modo presentación: se oculta en todos los monitores.</summary>
    public bool ExclusiveFullscreen { get; private set; }

    public HashSet<int> FullscreenMonitors { get; } = [];

    /// <summary>Segundos desde la última pulsación o movimiento de ratón en todo el sistema.</summary>
    public static double UserIdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = 8 };
        if (!Native.GetLastInputInfo(ref info))
        {
            return 0;
        }

        return unchecked((uint)Environment.TickCount - info.dwTime) / 1000.0;
    }

    /// <summary>Marca el tema para releerlo (WM_SETTINGCHANGE).</summary>
    public void InvalidateTheme() => _themeDirty = true;

    public void InvalidatePower() => _nextPowerCheck = 0;

    public void Update(double now, IReadOnlyList<MonitorInfo> monitors, HashSet<nint> ownWindows)
    {
        if (_themeDirty)
        {
            _themeDirty = false;
            DarkMode = ReadDarkMode();
        }

        if (now >= _nextPowerCheck)
        {
            _nextPowerCheck = now + PowerInterval;
            ReadPower();
        }

        if (now >= _nextNotificationCheck)
        {
            _nextNotificationCheck = now + NotificationStateInterval;
            ReadNotificationState();
            DetectFullscreen(monitors, ownWindows);
        }
    }

    /// <summary>Fuerza a revisar la pantalla completa (cambio de ventana activa).</summary>
    public void InvalidateFullscreen() => _nextNotificationCheck = 0;

    private void ReadPower()
    {
        if (!Native.GetSystemPowerStatus(out var status))
        {
            return;
        }

        var onBattery = status.ACLineStatus == 0;
        BatteryLow = onBattery && status.BatteryLifePercent != 255 && status.BatteryLifePercent < 20;
        PowerSaver = status.SystemStatusFlag == 1;
    }

    private void ReadNotificationState()
    {
        var quiet = false;
        var exclusive = false;
        if (Native.SHQueryUserNotificationState(out var state) == 0)
        {
            quiet = state is QUNS_QUIET_TIME or QUNS_PRESENTATION_MODE;
            exclusive = state is QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;
        }

        QuietMode = quiet || NotificationsDisabled();
        ExclusiveFullscreen = exclusive;
    }

    /// <summary>
    /// Ventana activa que ocupa un monitor entero (vídeo, juego en ventana sin bordes, presentación).
    /// Las ventanas maximizadas ocupan el área de trabajo, no el monitor entero, así que no cuentan.
    /// </summary>
    private void DetectFullscreen(IReadOnlyList<MonitorInfo> monitors, HashSet<nint> ownWindows)
    {
        FullscreenMonitors.Clear();
        var foreground = Native.GetForegroundWindow();
        if (foreground == 0 || ownWindows.Contains(foreground) || foreground == Native.GetShellWindow()
            || foreground == Native.GetDesktopWindow())
        {
            return;
        }

        var className = WindowScanner.GetClassName(foreground);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow")
        {
            return;
        }

        if (!Native.GetWindowRect(foreground, out var rect))
        {
            return;
        }

        foreach (var monitor in monitors)
        {
            var bounds = monitor.Bounds;
            if (rect.Left <= bounds.Left && rect.Top <= bounds.Top && rect.Right >= bounds.Right && rect.Bottom >= bounds.Bottom)
            {
                FullscreenMonitors.Add(monitor.Index);
            }
        }
    }

    private static bool ReadDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Notificaciones desactivadas por completo en Configuración.</summary>
    private static bool NotificationsDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings");
            return key?.GetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED") is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Monitor bajo un punto (para saber si el lander está en uno a pantalla completa).</summary>
    public static int MonitorIndexAt(IReadOnlyList<MonitorInfo> monitors, Vec2 point)
    {
        foreach (var monitor in monitors)
        {
            if (monitor.Bounds.Contains(point))
            {
                return monitor.Index;
            }
        }

        return -1;
    }
}
