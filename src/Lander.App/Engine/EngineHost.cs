using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Lander.App.Audio;
using Lander.App.Interop;
using Lander.App.Platform;
using Lander.App.Rendering;
using Lander.App.Rendering.Skins;
using Lander.App.Services;
using Lander.Core;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Missions;
using Lander.Core.Physics;
using Lander.Core.Progression;
using Lander.Core.Racing;
using Lander.Core.Settings;
using Lander.Core.Terrain;
using static Lander.App.Interop.NativeConstants;

namespace Lander.App.Engine;

/// <summary>Estado que la interfaz necesita conocer (menú de la bandeja).</summary>
internal sealed record EngineStatus(ControlMode Mode, bool Paused, int Level, bool HotkeyAvailable, bool Racing = false, bool CanRepeatRace = false, double? RaceRecord = null);

/// <summary>
/// El motor de Lander en su propio hilo: lee el escritorio, avanza la simulación con paso fijo, dibuja
/// el lander en su ventana por capas y reproduce los sonidos. Duerme casi del todo cuando el lander
/// está quieto. Todo lo que llega de la interfaz pasa por <see cref="Post"/>.
/// </summary>
internal sealed unsafe class EngineHost : IDisposable
{
    private const double IdleInterval = 0.25;
    private const double AttentiveInterval = 1.0 / 20;
    private const double AwayInterval = 0.5;
    private const int MaxStepsPerTick = 20;
    private const double WindowSizeFactor = 5.8;

    private readonly Dispatcher _ui;
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly PilotProgress _progress;
    private Thread? _thread;
    private volatile bool _running;
    private LanderSettings _settings;

    // Estado del hilo del motor ------------------------------------------------------------------
    private readonly HashSet<nint> _ownWindows = [];
    private readonly HashSet<nint> _knownWindows = [];
    private readonly Dictionary<nint, PixelRect> _windowBounds = [];
    private readonly List<(nint Handle, double Due)> _pendingWindows = [];
    private readonly List<GameEvent> _events = [];
    private readonly List<MarkerSpec> _markerSpecs = [];
    private readonly Sensors _sensors = new();
    private readonly SystemStatus _system = new();
    private readonly LanderScene _scene = new();

    private LanderGame _game = null!;
    private LayeredWindow _window = null!;
    private FramePresenter _presenter = null!;
    private ParticleSystem _particles = null!;
    private MarkerLayer _markers = null!;
    private WindowScanner _scanner = null!;
    private AppIconCache _icons = null!;
    private WinEventWatcher? _winEvents;
    private HotkeyManager _hotkeys = null!;
    private RecycleBinWatcher? _recycleBin;
    private VirtualDesktopKeeper? _desktops;
    private bool _desktopCheck = true;
    private AudioEngine _audio = null!;
    private LanderSkin _skin = SkinCatalog.Classic;
    private nint _timer;

    private List<MonitorInfo> _monitors = [];
    private World _world = World.Empty;
    private bool _terrainDirty = true;
    private bool _monitorsDirty;
    private double _lastScan;
    private double _nextSafetyScan;
    private double _nextKnownPrune;
    private double _burstUntil;
    private bool _tickSoon;

    private double _lastTick;
    private double _accumulator;
    private Vec2 _previousPosition;
    private double _previousAngle;
    private Vec2? _lastCursor;
    private int _lastSignature;
    private bool _forceRender = true;
    private double _nextTopmost;
    private double _lastMarkerRender;

    private bool _pressed;
    private bool _dragging;
    private Vec2 _pressPoint;
    private bool _hidden;
    private bool _hotkeyAvailable;
    private bool _rawInputRegistered;
    private double _wheelEnergy;
    private double _gustCooldown;
    private double _recycleCheckAt = double.MaxValue;
    private double _notificationCooldown;
    private int _pushedVersion = -1;
    private int _pushedFlightBucket;
    private double _nextProgressPush;
    private int _demoRaces;
    private double _nextDemoRace = 5;

    // Contadores de diagnóstico (se registran en compilaciones Debug).
    private int _statTicks;
    private int _statRenders;
    private int _statScans;
    private int _statEvents;
    private double _nextStatLog;
    private readonly Dictionary<string, int> _statSources = [];
    private long _statRenderTicks;
    private long _statScanTicks;

    public EngineHost(LanderSettings settings, PilotProgress progress, Dispatcher ui)
    {
        _settings = settings;
        _progress = progress;
        _ui = ui;
    }

    /// <summary>Cambia el modo o el estado de pausa (en el hilo de la interfaz).</summary>
    public event Action<EngineStatus>? StatusChanged;

    /// <summary>Instantánea del progreso para guardarla (en el hilo de la interfaz).</summary>
    public event Action<PilotProgress>? ProgressChanged;

    /// <summary>Clic derecho sobre el lander (en el hilo de la interfaz).</summary>
    public event Action? ContextMenuRequested;

    /// <summary>Tomar el control ha sacado al lander de la pausa (en el hilo de la interfaz).</summary>
    public event Action? Resumed;

    /// <summary>Aviso para mostrar en la bandeja: título y texto (en el hilo de la interfaz).</summary>
    public event Action<string, string>? Notification;

    /// <summary>
    /// Modo demostración (<c>--demo</c>): simula a alguien delante del PC y vuela mucho más a menudo.
    /// Útil para grabar vídeos y para probar el comportamiento.
    /// </summary>
    public bool DemoMode { get; init; }

    /// <summary>Piloto simulado para el modo manual (<c>--demo-manual</c>).</summary>
    public DemoPilot? DemoPilot { get; init; }

    /// <summary>El piloto simulado corre una carrera tras otra (<c>--demo-race</c>).</summary>
    public bool DemoRace { get; init; }

    /// <summary>Progreso final, disponible tras <see cref="Stop"/>.</summary>
    public PilotProgress? FinalProgress { get; private set; }

    private double Now => _clock.Elapsed.TotalSeconds;

    private double FrameInterval => _system.PowerSaver ? 1.0 / 30 : 1.0 / 60;

    public void Start()
    {
        _running = true;
        _thread = new Thread(Run) { IsBackground = true, Name = "Lander engine" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Ejecuta una acción en el hilo del motor.</summary>
    public void Post(Action action)
    {
        _commands.Enqueue(action);
        _wake.Set();
    }

    public void ApplySettings(LanderSettings settings) => Post(() => OnSettingsChanged(settings.Clone()));

    public void ToggleManual() => Post(ToggleMode);

    public void OfferMission() => Post(() =>
    {
        if (!_game.OfferMission())
        {
            RaiseNotification("Sin misiones ahora", "Ya hay una misión o una carrera en curso, o no hay sitio para una.");
        }
    });

    /// <summary>Empieza una carrera por un circuito nuevo o repite el último para batir el récord.</summary>
    public void StartRace(bool repeat = false) => Post(() => BeginRace(repeat));

    public void AbandonRace() => Post(() =>
    {
        _game.AbandonRace();
        DrainGameEvents();
    });

    public void ResetProgress() => Post(() =>
    {
        _game.ResetProgress();
        _skin = ResolveSkin(_settings.Skin);
        _forceRender = true;
        PushProgress(Now, force: true);
        PublishStatus();
    });

    public void Stop()
    {
        if (_thread is null)
        {
            return;
        }

        _running = false;
        _wake.Set();
        if (!_thread.Join(TimeSpan.FromSeconds(3)))
        {
            Log.Warn("El motor no terminó a tiempo.");
        }

        _thread = null;
    }

    public void Dispose()
    {
        Stop();
        _wake.Dispose();
    }

    // Hilo del motor ------------------------------------------------------------------------------

    private void Run()
    {
        try
        {
            Initialize();
            Loop();
        }
        catch (Exception ex)
        {
            Log.Error("El motor se ha detenido por un error.", ex);
            RaiseNotification("Lander se ha detenido", "Ha ocurrido un error inesperado. Consulta el registro para más detalles.");
        }
        finally
        {
            FinalProgress = _progress.Clone();
            Cleanup();
        }
    }

    private void Initialize()
    {
        _window = new LayeredWindow("Lander", clickThrough: true) { MessageHandler = OnWindowMessage };
        _ownWindows.Add(_window.Handle);
        _presenter = new FramePresenter();
        _particles = new ParticleSystem();
        _markers = new MarkerLayer(_ownWindows);
        _scanner = new WindowScanner(_ownWindows);
        _icons = new AppIconCache();
        _winEvents = new WinEventWatcher();
        _winEvents.WindowEvent += OnWindowEvent;
        _hotkeys = new HotkeyManager(_window.Handle);
        _hotkeyAvailable = _hotkeys.RegisterToggle(_settings.ToggleHotkey);
        if (!_hotkeyAvailable)
        {
            RaiseNotification("Atajo no disponible", $"Otra aplicación ya usa {HotkeyText.Format(_settings.ToggleHotkey)}. Puedes cambiarlo en Ajustes.");
        }

        try
        {
            _recycleBin = new RecycleBinWatcher(_window.Handle);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or System.Runtime.InteropServices.COMException)
        {
            Log.Warn("No se puede vigilar la papelera.", ex);
        }

        _audio = new AudioEngine();
        ApplyAudio();
        _desktops = new VirtualDesktopKeeper();

        _timer = Native.CreateWaitableTimerEx(0, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
        if (_timer == 0)
        {
            _timer = Native.CreateWaitableTimerEx(0, null, 0, TIMER_ALL_ACCESS);
        }

        var now = Now;
        _lastTick = now;
        _monitors = MonitorService.GetMonitors();
        _system.Update(now, _monitors, _ownWindows);
        RebuildWorld(now);

        _game = new LanderGame(BuildGameOptions(_settings), _progress, new GameRandom());
        _game.Initialize(_world);
        if (_settings.Paused)
        {
            _game.SetPaused(true);
        }

        _skin = ResolveSkin(_settings.Skin);
        _previousPosition = _game.Body.Position;
        _previousAngle = _game.Body.Angle;
        _pushedVersion = _game.ProgressVersion;
        DrainGameEvents();
        PublishStatus();
        Log.Debug($"Hilo del motor: {Native.GetCurrentThreadId()}");
        Log.Info($"Motor iniciado: {_monitors.Count} monitor(es), {_world.Platforms.Count} plataformas.");
    }

    private void Loop()
    {
        var nextTick = Now;
        while (_running)
        {
            PumpMessages();
            RunCommands();
            if (!_running)
            {
                break;
            }

            if (_tickSoon)
            {
                _tickSoon = false;
                nextTick = Math.Min(nextTick, _lastTick + FrameInterval);
            }

            var now = Now;
            if (now >= nextTick)
            {
                var interval = Tick(now);
                nextTick = interval <= FrameInterval + 1e-6 ? nextTick + interval : now + interval;
                if (nextTick <= now)
                {
                    nextTick = now + interval * 0.5;
                }
            }

            var wait = nextTick - Now;
            if (wait > 0.0005)
            {
                Wait(wait);
            }
        }
    }

    private void Wait(double seconds)
    {
        var due = -(long)(seconds * 10_000_000);
        Native.SetWaitableTimer(_timer, due, 0, 0, 0, false);
        var handles = stackalloc nint[2];
        handles[0] = _timer;
        handles[1] = _wake.SafeWaitHandle.DangerousGetHandle();
        _ = Native.MsgWaitForMultipleObjectsEx(2, handles, INFINITE, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
    }

    private static void PumpMessages()
    {
        while (Native.PeekMessage(out var message, 0, 0, 0, PM_REMOVE))
        {
            Native.TranslateMessage(message);
            Native.DispatchMessage(message);
        }
    }

    private void RunCommands()
    {
        while (_commands.TryDequeue(out var command))
        {
            try
            {
                command();
            }
            catch (Exception ex)
            {
                Log.Error("Error al ejecutar una orden en el motor.", ex);
            }
        }
    }

    /// <summary>Un ciclo del motor. Devuelve cuánto esperar hasta el siguiente.</summary>
    private double Tick(double now)
    {
        var elapsed = Math.Clamp(now - _lastTick, 0, 0.25);
        _statTicks++;
        LogStats(now);
        _lastTick = now;
        _gustCooldown -= elapsed;

        if (_monitorsDirty)
        {
            _monitorsDirty = false;
            _monitors = MonitorService.GetMonitors();
            _terrainDirty = true;
            _forceRender = true;
        }

        _system.Update(now, _monitors, _ownWindows);
        UpdateSensors(elapsed);

        var supportMoving = _game.Body.Support is { } support && support.Velocity.LengthSquared > 1;
        var minScanInterval = now < _burstUntil || supportMoving ? 0 : IsActive(now) ? 1.0 / 20 : IdleInterval;
        if ((_terrainDirty && now - _lastScan >= minScanInterval) || now >= _nextSafetyScan)
        {
            RebuildWorld(now);
        }

        if (_desktopCheck)
        {
            _desktopCheck = false;
            _desktops?.Follow(_ownWindows, Native.GetForegroundWindow());
        }

        ProcessPendingWindows(now);
        CheckRecycleBin(now);
        PruneKnownWindows(now);

        if (UpdateFullscreen())
        {
            _audio.SetEngine(0);
            return IdleInterval;
        }

        UpdateWind(elapsed);
        StepSimulation(elapsed);
        UpdateEffects(now, elapsed);
        UpdateInteraction();
        Render(now);
        UpdateMarkers(now);
        PushProgress(now);

        if (now >= _nextTopmost)
        {
            _nextTopmost = now + 5;
            _window.BringToTop();
        }

        if (IsUserAway() && _game.Body.IsLanded && !_game.Autopilot.IsFlightActive)
        {
            return AwayInterval;
        }

        return IsActive(now) ? FrameInterval : IsAttentive() ? AttentiveInterval : IdleInterval;
    }

    /// <summary>Nadie mira: el usuario lleva un rato sin tocar nada y no está pilotando.</summary>
    private bool IsUserAway() =>
        _sensors.UserIdleSeconds > 60 && _game.Mode == ControlMode.Auto && !_pressed && !_dragging;

    private bool IsActive(double now) =>
        _game.Mode == ControlMode.Manual || _game.NeedsHighFrequency || _particles.Count > 0
        || _pressed || _dragging || now < _burstUntil || _markers.HasVisible;

    private bool IsAttentive() =>
        _game.Visual.Bubble is not null
        || (_sensors.Cursor is { } cursor && Vec2.Distance(cursor, _game.Body.Position) < _game.Body.Height * 6);

    private void StepSimulation(double elapsed)
    {
        if (_game.IsPaused && !_game.NeedsHighFrequency && !_dragging)
        {
            _accumulator = 0;
            return;
        }

        _accumulator += elapsed;
        if (DemoPilot is not null && _game.Mode == ControlMode.Auto && !_game.IsPaused && _game.Time > 3)
        {
            _game.SetMode(ControlMode.Manual, ModeChangeReason.User);
        }

        if (DemoRace && _game.Mode == ControlMode.Manual && _game.Races.Current is null && _game.Body.IsLanded && _game.Time > _nextDemoRace)
        {
            _nextDemoRace = _game.Time + 10;
            // Una carrera tras otra: la mitad de las veces repite el circuito para intentar batir el récord.
            _demoRaces++;
            BeginRace(repeat: _demoRaces % 2 == 0 && _game.Races.CanRepeat);
        }

        var manual = _game.Mode == ControlMode.Manual;
        var controls = !manual ? default : DemoPilot?.Next(_game, _world) ?? HotkeyManager.ReadControls();
        var steps = 0;
        while (_accumulator >= LanderGame.TimeStep && steps < MaxStepsPerTick)
        {
            _previousPosition = _game.Body.Position;
            _previousAngle = _game.Body.Angle;
            if (_dragging && _sensors.Cursor is { } cursor)
            {
                _game.Drag(cursor);
            }

            _game.Step(_world, _sensors, controls);
            _accumulator -= LanderGame.TimeStep;
            steps++;
        }

        if (steps == MaxStepsPerTick)
        {
            _accumulator = 0;
        }

        DrainGameEvents();
    }

    private void DrainGameEvents()
    {
        _events.Clear();
        _game.DrainEvents(_events);
        foreach (var gameEvent in _events)
        {
            HandleGameEvent(gameEvent);
        }
    }

    private void RebuildWorld(double now)
    {
        _statScans++;
        var scanStart = Stopwatch.GetTimestamp();
        var windows = _scanner.Scan();
        _statScanTicks += Stopwatch.GetTimestamp() - scanStart;
        _windowBounds.Clear();
        foreach (var window in windows)
        {
            _knownWindows.Add((nint)window.Handle);
            _windowBounds[(nint)window.Handle] = window.Bounds;
        }

        _world = TerrainBuilder.Build(_monitors, windows, new TerrainOptions { LanderHeightDip = _settings.LanderSize }, now);
        _terrainDirty = false;
        _lastScan = now;
        _nextSafetyScan = now + 2;
    }

    private void UpdateSensors(double elapsed)
    {
        if (Native.GetCursorPos(out var point))
        {
            var cursor = new Vec2(point.X, point.Y);
            if (_lastCursor is { } last && elapsed > 0)
            {
                var moved = cursor - last;
                _sensors.CursorVelocity = MathUtil.Damp(_sensors.CursorVelocity, moved / elapsed, 12, elapsed);
                _sensors.CursorStillSeconds = moved.LengthSquared < 4 ? _sensors.CursorStillSeconds + elapsed : 0;
            }

            _lastCursor = cursor;
            _sensors.Cursor = cursor;
        }

        _sensors.UserIdleSeconds = DemoMode ? 0 : SystemStatus.UserIdleSeconds();
        _sensors.BatteryLow = _system.BatteryLow;
        _sensors.PowerSaver = _system.PowerSaver;
        _sensors.DarkMode = _system.DarkMode;
        _sensors.QuietMode = _system.QuietMode;
        _sensors.FullscreenMonitors = _system.FullscreenMonitors;
    }

    /// <summary>
    /// Con una aplicación a pantalla completa en su monitor, el lander se muda a otro monitor libre; si
    /// no hay ninguno (o es un juego exclusivo o una presentación), aterriza y se esconde.
    /// </summary>
    private bool UpdateFullscreen()
    {
        var monitor = SystemStatus.MonitorIndexAt(_monitors, _game.Body.Position);
        var hide = _system.ExclusiveFullscreen || _system.FullscreenMonitors.Contains(monitor);
        if (hide && !_system.ExclusiveFullscreen && !_dragging && FindFreeGround() is { } ground)
        {
            EndPress();
            _game.Relocate(ground);
            DrainGameEvents();
            _particles.Clear();
            _previousPosition = _game.Body.Position;
            _forceRender = true;
            hide = false;
            Log.Info($"Pantalla completa en el monitor {monitor}: el lander se muda al monitor {ground.MonitorIndex}.");
        }

        if (hide)
        {
            if (!_hidden)
            {
                _hidden = true;
                EndPress();
                _game.SnapToSurface();
                DrainGameEvents();
                _window.Hide();
                _markers.HideAll();
                _particles.Clear();
                _hotkeys.SetGameKeys(false);
                SetRawInput(false);
                Log.Info("Pantalla completa: el lander se esconde.");
            }

            return true;
        }

        if (_hidden)
        {
            _hidden = false;
            _forceRender = true;
            _accumulator = 0;
            _previousPosition = _game.Body.Position;
        }

        return false;
    }

    [Conditional("DEBUG")]
    private void CountEventSource(uint eventType, nint hwnd)
    {
        var key = $"0x{eventType:X4} {WindowScanner.GetClassName(hwnd)} «{WindowScanner.GetTitle(hwnd)}»";
        _statSources[key] = _statSources.GetValueOrDefault(key) + 1;
    }

    [Conditional("DEBUG")]
    private void LogStats(double now)
    {
        if (now < _nextStatLog)
        {
            return;
        }

        var span = 10.0;
        Log.Debug($"Motor (10 s): {_statTicks / span:0.#} ciclos/s, {_statRenders / span:0.#} dibujos/s, {_statScans / span:0.#} escaneos/s, {_statEvents / span:0.#} eventos/s, autopiloto {_game?.Autopilot.State}.");
        Log.Debug($"   eventos en bruto: {WinEventWatcher.RawCount / span:0} /s");
        WinEventWatcher.RawCount = 0;
        if (_statRenders > 0 || _statScans > 0)
        {
            Log.Debug($"   dibujo medio {Stopwatch.GetElapsedTime(0, _statRenderTicks).TotalMilliseconds / Math.Max(1, _statRenders):0.00} ms, escaneo medio {Stopwatch.GetElapsedTime(0, _statScanTicks).TotalMilliseconds / Math.Max(1, _statScans):0.00} ms");
        }

        _statRenderTicks = _statScanTicks = 0;
        foreach (var (source, count) in _statSources.OrderByDescending(p => p.Value).Take(3))
        {
            Log.Debug($"   {count} × {source}");
        }

        _statSources.Clear();
        _statTicks = _statRenders = _statScans = _statEvents = 0;
        _nextStatLog = now + span;
    }

    /// <summary>Suelo de un monitor sin nada a pantalla completa (el principal primero).</summary>
    private Core.Terrain.Platform? FindFreeGround()
    {
        foreach (var monitor in _world.Monitors)
        {
            if (!_system.FullscreenMonitors.Contains(monitor.Index) && _world.GroundOf(monitor.Index) is { } ground)
            {
                return ground;
            }
        }

        return null;
    }

    private void UpdateEffects(double now, double elapsed)
    {
        var body = _game.Body;
        var visual = _game.Visual;
        var scale = body.Scale;

        if (body.State != BodyState.Destroyed && visual.MainFlame > 0.03)
        {
            var exhaust = -body.Up;
            var nozzle = BodyToWorld(body, _skin.Nozzle);
            _particles.Exhaust(nozzle, exhaust, body.Velocity, visual.MainFlame, scale, _settings.Trail, now, elapsed);

            var below = _world.PlatformBelow(body.Position.X, body.FeetY - 1);
            if (below is not null)
            {
                var height = below.Y - body.FeetY;
                var reach = body.Height * 1.6;
                if (height < reach)
                {
                    _particles.GroundDust(new Vec2(body.Position.X, below.Y), visual.MainFlame * (1 - height / reach), scale, elapsed);
                }
            }
        }

        if (body.State != BodyState.Destroyed && body.Damage > 0.45)
        {
            _particles.DamageWisp(body.Position, body.Damage, scale, elapsed);
        }

        _particles.Update(elapsed);

        var engineLevel = !_sensors.QuietMode && body.State != BodyState.Destroyed ? visual.MainFlame : 0;
        _audio.SetEngine(engineLevel);
    }

    private void HandleGameEvent(GameEvent gameEvent)
    {
        var scale = _game.Body.Scale;
        var position = gameEvent.Position;
        var feet = new Vec2(position.X, position.Y + LanderBody.BaseHalfHeight * scale);
        switch (gameEvent.Kind)
        {
            case GameEventKind.TookOff:
                _particles.DustBurst(feet, 0.25, scale);
                break;

            case GameEventKind.Touchdown:
            {
                var landing = gameEvent.Landing!;
                var intensity = Math.Clamp(landing.VerticalSpeed / (170 * scale), 0, 1);
                _particles.DustBurst(feet, 0.25 + intensity, scale);
                if (landing.Quality == LandingQuality.Hard)
                {
                    _particles.Sparks(feet, new Vec2(0, -1), 10, scale);
                    Play(Sound.HardThud);
                }
                else
                {
                    Play(Sound.LegClick, 0.6);
                }

                if (gameEvent.Manual && landing.Quality == LandingQuality.Perfect)
                {
                    Play(Sound.PerfectChime);
                    _particles.Sparkles(position, 26 * scale, Gfx.Rgb(0xFFD23F), 10, scale);
                }

                break;
            }

            case GameEventKind.Crashed:
                _particles.Explosion(position, scale, _skin.DebrisColors);
                Play(Sound.Explosion);
                break;

            case GameEventKind.Respawned:
                _particles.Sparkles(position, 30 * scale, Gfx.Rgb(0x7DE3FF), 16, scale);
                Play(Sound.Respawn);
                break;

            case GameEventKind.SupportLost:
            case GameEventKind.Launched:
            case GameEventKind.Toppled:
            case GameEventKind.Startled:
                Play(Sound.Pop, 0.6);
                break;

            case GameEventKind.Slipped:
                _particles.Sparks(feet, new Vec2(-Math.Sign(_game.Body.Velocity.X), -0.4), 3, scale);
                Play(Sound.Scrape, 0.6);
                break;

            case GameEventKind.HitCeiling:
                _particles.Sparks(position - new Vec2(0, 24 * scale), new Vec2(0, 1), 5, scale);
                Play(Sound.HardThud, 0.35);
                break;

            case GameEventKind.HitWall:
                _particles.Sparks(position, new Vec2(-Math.Sign(_game.Body.Velocity.X), 0), 4, scale);
                Play(Sound.HardThud, 0.35);
                break;

            case GameEventKind.Boosted:
                Play(Sound.Boost);
                break;

            case GameEventKind.LegsDeployed when _game.Body.IsFlying:
                Play(Sound.LegClick, 0.5);
                break;

            case GameEventKind.FuelEmpty:
                Play(Sound.Pop);
                break;

            case GameEventKind.ModeChanged:
                _hotkeys.SetGameKeys(gameEvent.Mode == ControlMode.Manual && !_hidden && DemoPilot is null);
                Play(Sound.Pop, 0.7);
                PublishStatus();
                break;

            case GameEventKind.Celebrated:
                _particles.Confetti(position - new Vec2(0, 20 * scale), scale);
                Play(Sound.PerfectChime, 0.7);
                break;

            case GameEventKind.LevelUp:
                _particles.Confetti(position - new Vec2(0, 20 * scale), scale, 40);
                Play(Sound.Fanfare);
                var unlocked = gameEvent.Unlocks.Count > 0
                    ? "Desbloqueado: " + string.Join(", ", gameEvent.Unlocks.Select(u => u.Name)) + "."
                    : "Sigue aterrizando para desbloquear más cosas.";
                RaiseNotification($"¡Nivel {gameEvent.Level}!", unlocked);
                PublishStatus();
                break;

            case GameEventKind.MissionOffered:
            case GameEventKind.MissionStarted:
                Play(Sound.Pop, 0.8);
                break;

            case GameEventKind.MissionCompleted:
                _particles.Confetti(position - new Vec2(0, 20 * scale), scale, 36);
                Play(Sound.Fanfare);
                break;

            case GameEventKind.CrystalCollected:
                _particles.Sparkles(position, 22 * scale, Gfx.Rgb(0x5CF2FF), 14, scale);
                Play(Sound.Crystal);
                break;

            case GameEventKind.RaceStarted:
                Play(Sound.Pop, 0.8);
                Log.Info($"Carrera: {gameEvent.Race!.Checkpoints.Count} balizas, tiempo de referencia {Race.FormatTime(gameEvent.Race.ParSeconds)} s.");
                PublishStatus();
                break;

            case GameEventKind.RaceCheckpoint when gameEvent.Magnitude > 0:
                _particles.Sparkles(position, 24 * scale, Gfx.Rgb(0x60CDFF), 12, scale);
                Play(Sound.Crystal, 0.8);
                break;

            case GameEventKind.RaceFinished:
            {
                var race = gameEvent.Race!;
                _particles.Confetti(position - new Vec2(0, 20 * scale), scale, 44);
                Play(Sound.Fanfare);
                Log.Info($"Carrera terminada en {Race.FormatTime(race.Elapsed)} s (referencia {Race.FormatTime(race.ParSeconds)} s, penalización {race.Penalties:0} s): {race.Medal}{(race.IsNewRecord ? ", récord" : string.Empty)}.");
                PublishStatus();
                break;
            }

            case GameEventKind.RaceFailed:
                Play(Sound.Pop, 0.6);
                Log.Info($"Carrera perdida: {gameEvent.Race!.Failure}.");
                PublishStatus();
                break;

            case GameEventKind.AchievementUnlocked:
                var achievement = gameEvent.Achievement!;
                Play(Sound.PerfectChime, 0.8);
                Log.Info($"Logro: {achievement.Name}.");
                RaiseNotification($"Logro: {achievement.Name}", achievement.Description);
                break;
        }
    }

    private void Play(Sound sound, double gain = 1)
    {
        if (!_sensors.QuietMode)
        {
            _audio.Play(sound, gain);
        }
    }

    // Ratón ------------------------------------------------------------------------------------------

    private void UpdateInteraction()
    {
        var body = _game.Body;
        var over = false;
        if (_sensors.Cursor is { } cursor && body.State != BodyState.Destroyed && _window.IsVisible)
        {
            var local = Rotate(cursor - body.Position, -body.Angle);
            over = Math.Abs(local.X) < body.HalfWidth * 1.1 && local.Y > -body.HalfHeight && local.Y < body.HalfHeight;

            if (_pressed && !_dragging && Vec2.Distance(cursor, _pressPoint) > 5)
            {
                _dragging = true;
                _game.BeginDrag(_pressPoint);
            }
        }

        _window.SetClickThrough(!((_settings.AllowDragging && over) || _pressed || _dragging));

        var near = _sensors.Cursor is { } c && Vec2.Distance(c, body.Position) < body.Height * 6;
        SetRawInput(near && !_hidden);
    }

    private void EndPress()
    {
        if (_dragging)
        {
            _game.EndDrag();
        }

        _dragging = false;
        _pressed = false;
    }

    private nint? OnWindowMessage(uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WM_HOTKEY:
                OnHotkey((int)wParam);
                return 0;

            case WM_LBUTTONDOWN when _settings.AllowDragging:
                if (Native.GetCursorPos(out var point))
                {
                    _pressPoint = new Vec2(point.X, point.Y);
                    _pressed = true;
                    Native.SetCapture(_window.Handle);
                    _tickSoon = true;
                }

                return 0;

            case WM_LBUTTONUP:
            {
                var wasDragging = _dragging;
                var wasPressed = _pressed;
                if (Native.GetCapture() == _window.Handle)
                {
                    Native.ReleaseCapture();
                }

                EndPress();
                if (wasPressed && !wasDragging)
                {
                    _game.Poke();
                    Play(Sound.Pop, 0.5);
                }

                return 0;
            }

            case WM_CAPTURECHANGED:
                EndPress();
                return 0;

            case WM_LBUTTONDBLCLK:
                ToggleMode();
                return 0;

            case WM_RBUTTONUP:
                _ui.BeginInvoke(() => ContextMenuRequested?.Invoke());
                return 0;

            case WM_SETCURSOR when _settings.AllowDragging:
                Native.SetCursor(Native.LoadCursor(0, _dragging ? IDC_ARROW : IDC_HAND));
                return 1;

            case WM_SETTINGCHANGE:
                _system.InvalidateTheme();
                _monitorsDirty = true;
                _tickSoon = true;
                return null;

            case WM_DISPLAYCHANGE:
                _monitorsDirty = true;
                _tickSoon = true;
                return null;

            case WM_DPICHANGED:
                return 0;

            case WM_POWERBROADCAST:
                _system.InvalidatePower();
                return null;

            case WM_INPUT:
                OnRawInput(lParam);
                return null;

            case RecycleBinWatcher.Message:
                _recycleCheckAt = Now + 0.5;
                return 0;
        }

        return null;
    }

    private void BeginRace(bool repeat)
    {
        if (_hidden)
        {
            RaiseNotification("Ahora no", "Hay algo a pantalla completa. Prueba otra vez cuando termine.");
            return;
        }

        var wasPaused = _game.IsPaused;
        var result = _game.StartRace(repeat);
        DrainGameEvents();
        _tickSoon = true;
        if (result != RaceStartResult.Started)
        {
            Log.Info($"No se puede empezar la carrera: {result}.");
        }

        switch (result)
        {
            case RaceStartResult.Started when wasPaused && !_game.IsPaused:
                _ui.BeginInvoke(() => Resumed?.Invoke());
                break;
            case RaceStartResult.NoCircuit:
                RaiseNotification("No hay circuito", "Hacen falta al menos dos ventanas abiertas y a la vista para poner las balizas.");
                break;
            case RaceStartResult.CircuitGone:
                RaiseNotification("El circuito ya no está", "Alguna de sus ventanas se ha cerrado o está tapada. Empieza una carrera nueva.");
                break;
            case RaceStartResult.NotReady:
                RaiseNotification("Ahora no", "El lander no puede salir en este momento.");
                break;
        }
    }

    /// <summary>Alterna entre automático y manual; tomar el control saca al lander de la pausa.</summary>
    private void ToggleMode()
    {
        var wasPaused = _game.IsPaused;
        _game.ToggleMode();
        DrainGameEvents();
        if (wasPaused && !_game.IsPaused)
        {
            _ui.BeginInvoke(() => Resumed?.Invoke());
        }
    }

    private void OnHotkey(int id)
    {
        if (_hidden)
        {
            return;
        }

        if (id == HotkeyManager.ToggleId)
        {
            ToggleMode();
        }
        else if (id == HotkeyManager.EscapeId && _game.Mode == ControlMode.Manual)
        {
            _game.SetMode(ControlMode.Auto, ModeChangeReason.User);
        }

        DrainGameEvents();
        _tickSoon = true;
    }

    // Viento: scroll rápido cerca del lander ---------------------------------------------------------

    private void SetRawInput(bool enabled)
    {
        if (enabled == _rawInputRegistered)
        {
            return;
        }

        var device = new RAWINPUTDEVICE
        {
            usUsagePage = 0x01,
            usUsage = 0x02,
            dwFlags = enabled ? RIDEV_INPUTSINK : RIDEV_REMOVE,
            hwndTarget = enabled ? _window.Handle : 0,
        };

        if (Native.RegisterRawInputDevices(&device, 1, (uint)sizeof(RAWINPUTDEVICE)))
        {
            _rawInputRegistered = enabled;
        }
    }

    private void OnRawInput(nint handle)
    {
        uint size = 0;
        var headerSize = (uint)sizeof(RAWINPUTHEADER);
        Native.GetRawInputData(handle, RID_INPUT, null, ref size, headerSize);
        if (size == 0 || size > 256)
        {
            return;
        }

        var buffer = stackalloc byte[(int)size];
        if (Native.GetRawInputData(handle, RID_INPUT, buffer, ref size, headerSize) == uint.MaxValue)
        {
            return;
        }

        var header = (RAWINPUTHEADER*)buffer;
        if (header->dwType != RIM_TYPEMOUSE)
        {
            return;
        }

        var mouse = (RAWMOUSE*)(buffer + sizeof(RAWINPUTHEADER));
        if ((mouse->usButtonFlags & (RI_MOUSE_WHEEL | RI_MOUSE_HWHEEL)) != 0)
        {
            _wheelEnergy += Math.Abs((short)mouse->usButtonData) / 120.0;
        }
    }

    private void UpdateWind(double elapsed)
    {
        _wheelEnergy *= Math.Exp(-elapsed * 2.5);
        if (_wheelEnergy < 3.5 || _gustCooldown > 0 || _sensors.Cursor is not { } cursor)
        {
            return;
        }

        var body = _game.Body;
        var direction = Math.Sign(body.Position.X - cursor.X);
        if (direction == 0)
        {
            direction = 1;
        }

        var strength = 260 + 45 * Math.Min(_wheelEnergy, 12);
        _game.Notify(new WindGust(new Vec2(direction * strength, -strength * 0.15), 0.6));
        _wheelEnergy = 0;
        _gustCooldown = 1.2;
    }

    // Sucesos de Windows ----------------------------------------------------------------------------

    private void OnWindowEvent(uint eventType, nint hwnd)
    {
        if (_ownWindows.Contains(hwnd) || !WindowScanner.IsTopLevel(hwnd))
        {
            return;
        }

        // Muchas ventanas (widgets, superposiciones) avisan de movimientos constantemente sin moverse:
        // solo importan las que forman terreno y cuyo rectángulo ha cambiado de verdad.
        var isTerrain = _windowBounds.TryGetValue(hwnd, out var known);
        if (eventType == EVENT_OBJECT_LOCATIONCHANGE
            && (!isTerrain || (WindowScanner.TryGetBounds(hwnd, out var bounds) && bounds.ToPixelRect() == known)))
        {
            return;
        }

        if (!isTerrain && eventType is not (EVENT_OBJECT_SHOW or EVENT_SYSTEM_FOREGROUND or EVENT_SYSTEM_MINIMIZEEND or EVENT_OBJECT_UNCLOAKED))
        {
            return;
        }

        _statEvents++;
        CountEventSource(eventType, hwnd);
        var now = Now;
        _terrainDirty = true;
        _tickSoon = true;
        switch (eventType)
        {
            case EVENT_OBJECT_LOCATIONCHANGE:
                _windowBounds[hwnd] = WindowScanner.TryGetBounds(hwnd, out var moved) ? moved.ToPixelRect() : known;
                _burstUntil = now + 0.35;
                break;

            case EVENT_OBJECT_SHOW:
                CheckNotification(hwnd, now);
                if (!_knownWindows.Contains(hwnd))
                {
                    _pendingWindows.Add((hwnd, now + 0.4));
                }

                break;

            case EVENT_SYSTEM_FOREGROUND:
                _system.InvalidateFullscreen();
                _desktopCheck = true;
                break;

            default:
                _burstUntil = Math.Max(_burstUntil, now + 0.2);
                break;
        }
    }

    /// <summary>Las notificaciones del sistema son ventanas del shell con «notificación» en el título.</summary>
    private void CheckNotification(nint hwnd, double now)
    {
        if (!_settings.ReactToNotifications || now < _notificationCooldown || _sensors.QuietMode)
        {
            return;
        }

        if (WindowScanner.GetClassName(hwnd) != "Windows.UI.Core.CoreWindow")
        {
            return;
        }

        var title = WindowScanner.GetTitle(hwnd);
        Log.Debug($"Ventana del shell visible: «{title}»");
        if (title.Contains("notific", StringComparison.OrdinalIgnoreCase))
        {
            _notificationCooldown = now + 4;
            _game.Notify(new NotificationArrived());
            DrainGameEvents();
        }
    }

    private void ProcessPendingWindows(double now)
    {
        for (var i = _pendingWindows.Count - 1; i >= 0; i--)
        {
            var (handle, due) = _pendingWindows[i];
            if (now < due)
            {
                continue;
            }

            _pendingWindows.RemoveAt(i);
            if (_scanner.TryDescribe(handle, out _))
            {
                _knownWindows.Add(handle);
                _game.Notify(new WindowOpened(handle));
            }
        }
    }

    private void PruneKnownWindows(double now)
    {
        if (now < _nextKnownPrune)
        {
            return;
        }

        _nextKnownPrune = now + 60;
        _knownWindows.RemoveWhere(h => !Native.IsWindow(h));
    }

    private void CheckRecycleBin(double now)
    {
        if (now < _recycleCheckAt || _recycleBin is null)
        {
            return;
        }

        _recycleCheckAt = double.MaxValue;
        if (!_recycleBin.CheckEmptied())
        {
            return;
        }

        var location = DesktopIconLocator.FindRecycleBin();
        if (location is null && _world.PrimaryMonitor is { } primary)
        {
            location = new Vec2(primary.WorkArea.Left + 50, primary.WorkArea.Top + 50);
        }

        if (location is { } target)
        {
            Log.Info($"Papelera vaciada (icono en {target}).");
            _game.Notify(new RecycleBinEmptied(target));
            DrainGameEvents();
        }
    }

    // Dibujo -----------------------------------------------------------------------------------------

    private void Render(double now)
    {
        var body = _game.Body;
        var visual = _game.Visual;
        var alpha = Math.Clamp(_accumulator / LanderGame.TimeStep, 0, 1);
        var position = body.State == BodyState.Held || body.IsLanded
            ? body.Position
            : Vec2.Lerp(_previousPosition, body.Position, alpha);
        var angle = _previousAngle + MathUtil.WrapAngle(body.Angle - _previousAngle) * alpha;

        var size = Math.Max(120, (int)Math.Ceiling(WindowSizeFactor * body.Height / 2) * 2);
        var x = (int)Math.Round(position.X) - size / 2;
        var y = (int)Math.Round(position.Y) - size / 2;

        // En reposo, el tiempo de las animaciones avanza a saltos para no redibujar sin necesidad.
        var lively = IsActive(now);
        var away = IsUserAway() && body.IsLanded;
        var animationTime = away ? Math.Floor(now) : lively ? now : Math.Floor(now * 4) / 4;
        var bubbleAge = away ? Math.Floor(visual.BubbleAge * 2) / 2 : lively || visual.IsAnimating ? visual.BubbleAge : Math.Floor(visual.BubbleAge * 4) / 4;
        var blink = BlinkAt(now);

        _scene.Position = position;
        _scene.Angle = angle;
        _scene.Scale = body.Scale;
        _scene.Visible = body.State != BodyState.Destroyed;
        _scene.Opacity = visual.Opacity;
        _scene.Skin = _skin;
        _scene.Dark = _sensors.DarkMode;
        _scene.Time = animationTime;
        _scene.TopLimit = (_world.MonitorAt(position) ?? _world.NearestMonitor(position))?.Bounds.Top ?? double.MinValue;
        _scene.Pose = new LanderPose
        {
            LegsDeploy = body.LegsDeploy,
            Expression = visual.Expression,
            Look = visual.Look,
            Blink = blink,
            Night = _sensors.DarkMode,
            Time = animationTime,
            Damage = body.Damage,
            MainFlame = visual.MainFlame,
            LeftJet = visual.LeftJet,
            RightJet = visual.RightJet,
            BoostFlash = visual.BoostFlash,
            CarryingPackage = visual.CarryingPackage,
        };
        _scene.Bubble = visual.Bubble;
        _scene.BubbleAge = bubbleAge;
        _scene.BubbleIcon = visual.Bubble is { Icon: BubbleIcon.App } bubble ? _icons.Get(bubble.AppHandle) : null;
        _scene.ShowHud = visual.ShowHud;
        _scene.Fuel = body.FuelFraction;
        _scene.Damage = body.Damage;
        _scene.Mission = _game.Missions.Current;
        _scene.MissionIcon = _scene.Mission is { Kind: MissionKind.LandOnApp } mission ? _icons.Get(mission.AppHandle) : null;
        _scene.Popups = visual.Popups;
        var race = _game.Races.Current;
        _scene.Race = race;
        var raceSignature = race is null ? 0 : HashCode.Combine(race.Status, race.Next, race.Medal, (int)(race.Elapsed * 10), (int)(race.ResultAge * 4));

        var signature = HashCode.Combine(
            raceSignature,
            HashCode.Combine(x, y, size, (int)(angle * 300), (int)(body.LegsDeploy * 40), visual.Expression, (int)(visual.Look.X * 20), (int)(visual.Look.Y * 20)),
            HashCode.Combine(blink > 0.5, (int)(visual.MainFlame * 50), (int)(visual.Opacity * 40), visual.Bubble, (int)(bubbleAge * 30), visual.Popups.Count, (int)(body.FuelFraction * 60)),
            HashCode.Combine(_sensors.DarkMode ? (int)(animationTime * 4) : 0, visual.ShowHud, _skin, body.State, (int)(body.Damage * 20), _scene.Mission?.Status, (int)((_scene.Mission?.TimeLeft ?? 0) * 2)));

        if (!_forceRender && signature == _lastSignature && _particles.Count == 0 && (away || !visual.IsAnimating))
        {
            return;
        }

        _forceRender = false;
        _lastSignature = signature;
        var window = new Rect(x, y, size, size);
        _statRenders++;
        var renderStart = Stopwatch.GetTimestamp();
        _presenter.Present(_window, x, y, size, size, dc => SceneRenderer.Draw(dc, _scene, _particles, window));
        _statRenderTicks += Stopwatch.GetTimestamp() - renderStart;
    }

    /// <summary>Parpadeo: unas 13 veces por minuto, a intervalos irregulares.</summary>
    private static double BlinkAt(double time)
    {
        const double period = 4.6;
        var cycle = Math.Floor(time / period);
        var offset = (Math.Sin(cycle * 12.9898) * 43758.5453 % 1 + 1) % 1 * 2.5;
        var phase = time - cycle * period - offset;
        return phase is >= 0 and < 0.16 ? 1 : 0;
    }

    private void UpdateMarkers(double now)
    {
        _markerSpecs.Clear();
        var body = _game.Body;
        if (_game.Mode == ControlMode.Manual && _game.Races.Current is { IsActive: true } race)
        {
            AddRaceMarkers(race, body);
        }
        else if (_game.Mode == ControlMode.Manual && _game.Missions.Current is { Status: MissionStatus.Active } mission)
        {
            var scale = body.Scale;
            switch (mission.Kind)
            {
                case MissionKind.CollectCrystals:
                    foreach (var crystal in mission.Crystals)
                    {
                        _markerSpecs.Add(new MarkerSpec(MarkerKind.Crystal, crystal, scale));
                    }

                    break;

                case MissionKind.LandOnApp:
                    var target = _world.Platforms.Where(p => p.AppId == mission.AppId).MaxBy(p => p.Width);
                    if (target is not null)
                    {
                        _markerSpecs.Add(new MarkerSpec(MarkerKind.Beacon, new Vec2(target.CenterX, target.Y), scale, BubbleIcon.App, _icons.Get(target.OwnerId)));
                    }

                    break;

                case MissionKind.Delivery:
                    if (_world.GroundOf(mission.TargetMonitor) is { } ground)
                    {
                        _markerSpecs.Add(new MarkerSpec(MarkerKind.Beacon, new Vec2(ground.CenterX, ground.Y), scale, BubbleIcon.Package));
                    }

                    break;

                case MissionKind.RoundTrip when mission.Stage != 1 && !body.IsOnBase:
                    if (_world.NearestGround(body.Position) is { } home)
                    {
                        var x = Math.Clamp(body.Position.X, home.Left + 60 * scale, home.Right - 60 * scale);
                        _markerSpecs.Add(new MarkerSpec(MarkerKind.Beacon, new Vec2(x, home.Y), scale, BubbleIcon.Home));
                    }

                    break;
            }
        }

        if (_markerSpecs.Count == 0)
        {
            if (_markers.HasVisible)
            {
                _markers.HideAll();
            }

            return;
        }

        if (now - _lastMarkerRender >= 1.0 / 30)
        {
            _lastMarkerRender = now;
            _markers.Render(_markerSpecs, now, _sensors.DarkMode);
        }
    }

    /// <summary>La próxima baliza con su número (o la casa, si toca volver) y, más tenue, la siguiente.</summary>
    private void AddRaceMarkers(Race race, LanderBody body)
    {
        var scale = body.Scale;
        var from = body.Position;
        for (var i = race.Next; i < race.Checkpoints.Count && i <= race.Next + 1; i++)
        {
            var checkpoint = race.Checkpoints[i];
            if (checkpoint.IsBase && body.IsOnBase)
            {
                break;
            }

            if (RaceSystem.TargetPoint(_world, checkpoint, from, 60 * scale) is not { } point)
            {
                continue;
            }

            var label = checkpoint.IsBase ? null : (i + 1).ToString(System.Globalization.CultureInfo.CurrentCulture);
            var markerScale = i == race.Next ? scale : scale * 0.8;
            _markerSpecs.Add(new MarkerSpec(MarkerKind.Beacon, point, markerScale, BubbleIcon.Home, Label: label, Alpha: i == race.Next ? 1 : 0.45));
            from = point;
        }
    }

    // Ajustes, estado y progreso ----------------------------------------------------------------------

    private void OnSettingsChanged(LanderSettings settings)
    {
        var previous = _settings;
        _settings = settings;
        _game.ApplyOptions(BuildGameOptions(settings));

        if (previous.ToggleHotkey != settings.ToggleHotkey)
        {
            _hotkeyAvailable = _hotkeys.RegisterToggle(settings.ToggleHotkey);
            if (!_hotkeyAvailable)
            {
                RaiseNotification("Atajo no disponible", $"Otra aplicación ya usa {HotkeyText.Format(settings.ToggleHotkey)}.");
            }
        }

        if (Math.Abs(previous.LanderSize - settings.LanderSize) > 0.01)
        {
            _terrainDirty = true;
        }

        if (settings.Paused != _game.IsPaused)
        {
            _game.SetPaused(settings.Paused);
            DrainGameEvents();
        }

        _skin = ResolveSkin(settings.Skin);
        ApplyAudio();
        _forceRender = true;
        _tickSoon = true;
        PublishStatus();
    }

    private GameOptions BuildGameOptions(LanderSettings settings)
    {
        var options = settings.ToGameOptions();
        return DemoMode ? options with { Activity = Math.Max(options.Activity, 1) * 6 } : options;
    }

    private LanderSkin ResolveSkin(string id) =>
        Unlocks.IsUnlocked(id, _progress.Level) ? SkinCatalog.Get(id) : SkinCatalog.Classic;

    private void ApplyAudio()
    {
        _audio.Volume = _settings.Volume;
        _audio.SetEnabled(_settings.SoundEnabled);
    }

    private void PublishStatus()
    {
        var races = _game?.Races;
        var status = new EngineStatus(
            _game?.Mode ?? ControlMode.Auto,
            _game?.IsPaused ?? _settings.Paused,
            _progress.Level,
            _hotkeyAvailable,
            races?.IsActive ?? false,
            races?.CanRepeat ?? false,
            races?.LastCircuitRecord);
        _ui.BeginInvoke(() => StatusChanged?.Invoke(status));
    }

    private void PushProgress(double now, bool force = false)
    {
        var flightBucket = (int)(_progress.FlightSeconds / 30);
        var changed = _game.ProgressVersion != _pushedVersion || flightBucket != _pushedFlightBucket;
        if (!force && (!changed || now < _nextProgressPush))
        {
            return;
        }

        _pushedVersion = _game.ProgressVersion;
        _pushedFlightBucket = flightBucket;
        _nextProgressPush = now + 5;
        var snapshot = _progress.Clone();
        _ui.BeginInvoke(() => ProgressChanged?.Invoke(snapshot));
    }

    private void RaiseNotification(string title, string message) =>
        _ui.BeginInvoke(() => Notification?.Invoke(title, message));

    private void Cleanup()
    {
        try
        {
            SetRawInput(false);
            _hotkeys?.Dispose();
            _winEvents?.Dispose();
            _recycleBin?.Dispose();
            _desktops?.Dispose();
            _audio?.Dispose();
            _markers?.Dispose();
            _window?.Dispose();
            if (_timer != 0)
            {
                Native.CloseHandle(_timer);
            }

            Dispatcher.CurrentDispatcher.InvokeShutdown();
        }
        catch (Exception ex)
        {
            Log.Warn("Error al cerrar el motor.", ex);
        }
    }

    private static Vec2 BodyToWorld(LanderBody body, Point local)
    {
        var scaled = new Vec2(local.X * body.Scale, local.Y * body.Scale);
        return body.Position + Rotate(scaled, body.Angle);
    }

    private static Vec2 Rotate(Vec2 v, double angle)
    {
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        return new Vec2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }
}
