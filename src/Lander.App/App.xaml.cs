using System.Windows;
using System.Windows.Threading;
using Lander.App.Engine;
using Lander.App.Platform;
using Lander.App.Services;
using Lander.App.UI;
using Lander.Core.Game;
using Lander.Core.Settings;

namespace Lander.App;

/// <summary>
/// Punto de entrada. No hay ventana principal: el lander vive en su propia capa y el resto se maneja
/// desde el icono de la bandeja.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Los recursos se liberan en OnExit, el final del ciclo de vida de la aplicación.")]
public partial class App : Application
{
    private SingleInstance? _instance;
    private SettingsService? _settings;
    private EngineHost? _engine;
    private TrayIcon? _tray;
    private TrayMenu? _menu;
    private SettingsWindow? _settingsWindow;
    private EngineStatus _status = new(ControlMode.Auto, false, 1, true);
    private bool _startWithWindows;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Tema Fluent de Windows 11, claro u oscuro según el sistema.
        ThemeMode = ThemeMode.System;

        // Herramientas de desarrollo: generar el icono o una hoja con todo el arte.
        if (e.Args is ["--export-icon", var iconPath, ..])
        {
            IconFactory.SaveIco(iconPath, 16, 20, 24, 32, 40, 48, 64, 256);
            Shutdown();
            return;
        }

        if (e.Args is ["--preview", var previewPath, ..])
        {
            PreviewSheet.Save(previewPath);
            Shutdown();
            return;
        }

        if (e.Args is ["--preview-hero", var heroPath, ..])
        {
            PreviewSheet.SaveHero(heroPath);
            Shutdown();
            return;
        }

        if (e.Args is ["--preview-settings", var settingsFolder, ..])
        {
            _settings = SettingsService.Load();
            PreviewSheet.SaveSettingsPages(settingsFolder, _settings, this);
            Shutdown();
            return;
        }

        if (e.Args.Contains("--exit"))
        {
            SingleInstance.RequestExit();
            Shutdown();
            return;
        }

        _instance = SingleInstance.TryAcquire(
            () => Dispatcher.BeginInvoke(ShowSettings),
            () => Dispatcher.BeginInvoke(Shutdown));
        if (_instance is null)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Excepción no controlada.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Tarea con excepción no observada.", args.Exception);
            args.SetObserved();
        };

        Log.Info($"Lander {typeof(App).Assembly.GetName().Version} iniciado en {Environment.OSVersion}.");
        Log.Debug($"Hilo de la interfaz: {Interop.Native.GetCurrentThreadId()}");

        _settings = SettingsService.Load();
        _startWithWindows = _settings.Current.StartWithWindows;
        StartupRegistration.Apply(_startWithWindows);

        _tray = new TrayIcon();
        _tray.Activated += () => _settings.Update(s => s.Paused = !s.Paused);
        _tray.MenuRequested += ShowMenu;

        _menu = new TrayMenu(_settings, () => _status, _tray.Handle);
        _menu.ToggleManualRequested += () => _engine?.ToggleManual();
        _menu.MissionRequested += () => _engine?.OfferMission();
        _menu.RaceRequested += repeat => _engine?.StartRace(repeat);
        _menu.AbandonRaceRequested += () => _engine?.AbandonRace();
        _menu.SettingsRequested += ShowSettings;
        _menu.ExitRequested += Shutdown;

        _status = _status with { Paused = _settings.Current.Paused, Level = _settings.Progress.Level };
        UpdateTrayIcon();

        _engine = new EngineHost(_settings.Current.Clone(), _settings.Progress.Clone(), Dispatcher) {
            DemoMode = e.Args.Contains("--demo") || e.Args.Contains("--demo-manual") || e.Args.Contains("--demo-race"),
            DemoPilot = e.Args.Contains("--demo-manual") || e.Args.Contains("--demo-race") ? new DemoPilot() : null,
            DemoRace = e.Args.Contains("--demo-race"),
            SoftwareRendering = e.Args.Contains("--software"),
#if DEBUG
            Statistics = true,
#else
            Statistics = e.Args.Contains("--stats"),
#endif
        };
        _engine.StatusChanged += OnEngineStatus;
        if (!_engine.DemoMode)
        {
            // En modo demostración no se guarda nada del progreso.
            _engine.ProgressChanged += progress => _settings.SetProgress(progress);
        }

        _engine.ContextMenuRequested += ShowMenu;
        _engine.Resumed += () => _settings.Update(s => s.Paused = false);
        _engine.Notification += (title, message) => _tray?.ShowBalloon(title, message);
        _settings.Changed += OnSettingsChanged;
        _engine.Start();

        if (e.Args.Contains("--settings"))
        {
            ShowSettings();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_engine is not null)
        {
            _engine.Stop();
            if (!_engine.DemoMode && _engine.FinalProgress is { } progress)
            {
                _settings?.SetProgress(progress);
            }

            _engine.Dispose();
        }

        _settings?.Flush();
        _tray?.Dispose();
        _instance?.Dispose();
        Log.Info("Lander cerrado.");
        Log.Shutdown();
        base.OnExit(e);
    }

    internal EngineHost? Engine => _engine;

    internal SettingsService? Settings => _settings;

    internal EngineStatus Status => _status;

    private void OnSettingsChanged(LanderSettings settings)
    {
        _engine?.ApplySettings(settings);
        if (settings.StartWithWindows != _startWithWindows)
        {
            _startWithWindows = settings.StartWithWindows;
            StartupRegistration.Apply(_startWithWindows);
        }

        _status = _status with { Paused = settings.Paused };
        UpdateTrayIcon();
    }

    private void OnEngineStatus(EngineStatus status)
    {
        var changedIcon = status.Paused != _status.Paused || status.Mode != _status.Mode;
        _status = status;
        if (changedIcon)
        {
            UpdateTrayIcon();
        }

        _settingsWindow?.RefreshStatus(status);
    }

    private void UpdateTrayIcon()
    {
        if (_tray is null || _settings is null)
        {
            return;
        }

        var size = Math.Max(16, Interop.Native.GetSystemMetricsForDpi(Interop.NativeConstants.SM_CXSMICON, Interop.Native.GetDpiForSystem()));
        var icon = IconFactory.CreateIconHandle(IconFactory.RenderLander(size, paused: _status.Paused));
        var state = _status.Paused ? " (en pausa)" : _status.Mode == ControlMode.Manual ? " (pilotando)" : string.Empty;
        _tray.Update(icon, "Lander" + state);
    }

    private void ShowMenu() => _menu?.Show();

    private void ShowSettings()
    {
        if (_settings is null)
        {
            return;
        }

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settings, this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }

        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        _settingsWindow.Activate();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Error en la interfaz.", e.Exception);
        e.Handled = true;
    }
}
