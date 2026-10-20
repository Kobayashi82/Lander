using System.Diagnostics;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using Lander.App.Rendering;
using Lander.App.Rendering.Gpu;
using Lander.App.Services;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Missions;
using Lander.Core.Physics;
using Vortice.Direct2D1;

namespace Lander.App.Engine;

/// <summary>
/// Dibujo del lander. Por defecto va por GPU (Direct2D + DirectComposition) al ritmo del compositor; si el
/// equipo no puede, o con <c>--software</c>, se dibuja por CPU en la ventana por capas como antes.
/// </summary>
internal sealed unsafe partial class EngineHost
{
    /// <summary>Tope de fotogramas por segundo en monitores de refresco muy alto.</summary>
    private const double MaxFrameRate = 165;

    private const int MaxGpuFailures = 3;

    private DrawingGroup _frameDrawing = null!;
    private GpuDevice? _gpu;
    private CompositionOverlay? _overlay;
    private DrawingReplayer? _replayer;
    private VsyncClock? _vsync;
    private Action<ID2D1DeviceContext, Matrix3x2>? _replay;
    private bool _vsyncFired;
    private bool _smooth;
    private int _gpuFailures;
    private (int X, int Y, int Size, int Angle) _hitMask = (int.MinValue, 0, 0, 0);
    private double _antenna;
    private double _antennaVelocity;
    private Vec2 _lastVelocity;

    /// <summary>Dibujo por CPU aunque haya GPU (<c>--software</c>).</summary>
    public bool SoftwareRendering { get; init; }

    private bool UseGpu => _overlay is not null;

    private bool UseVsync => _vsync is not null;

    private double MinFrameInterval => _system.PowerSaver ? 1.0 / 30 : 1.0 / MaxFrameRate;

    private bool LanderVisible => UseGpu ? _overlay!.IsVisible : _window.IsVisible;

    private void InitializeGraphics()
    {
        if (SoftwareRendering)
        {
            Log.Info("Render por CPU (--software).");
            return;
        }

        _gpu = GpuDevice.TryCreate();
        if (_gpu is null)
        {
            Log.Info("Sin GPU utilizable: render por CPU.");
            return;
        }

        try
        {
            _overlay = new CompositionOverlay(_gpu, _ownWindows);
            _overlay.SetMonitors(_monitors);
            _frameDrawing = new DrawingGroup();
            _replayer = new DrawingReplayer(_gpu.Factory);
            _replay = (context, origin) => _replayer!.Replay(context, _frameDrawing, origin);
            _vsync = new VsyncClock(_gpu);

            // La ventana por capas ya no dibuja: solo hace de diana invisible para el ratón.
            _window.Hide();
            _forceRender = true;

            // La captura del contenido de las ventanas reutiliza el mismo dispositivo.
            _content.SetRenderDevice(_gpu.Direct3D);
        }
        catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or InvalidOperationException or InvalidCastException or EntryPointNotFoundException)
        {
            Log.Warn("No se pudo preparar el render por GPU; se dibujará por CPU.", ex);
            DisposeGraphics();
        }
    }

    private void DisposeGraphics()
    {
        _content?.SetRenderDevice(null);
        _vsync?.Dispose();
        _vsync = null;
        _replayer?.Dispose();
        _replayer = null;
        _replay = null;
        _overlay?.Dispose();
        _overlay = null;
        _gpu?.Dispose();
        _gpu = null;
        _smooth = false;
    }

    private void OnMonitorsChanged()
    {
        try
        {
            _overlay?.SetMonitors(_monitors);
        }
        catch (SharpGen.Runtime.SharpGenException ex)
        {
            OnGpuFailure(ex);
        }
    }

    /// <summary>
    /// La GPU se ha perdido (controlador actualizado, cambio de tarjeta, sesión remota…): se vuelve a
    /// crear todo; si sigue fallando, se pasa a dibujar por CPU.
    /// </summary>
    private void OnGpuFailure(Exception ex)
    {
        _gpuFailures++;
        Log.Warn($"Fallo del render por GPU ({_gpuFailures}/{MaxGpuFailures}).", ex);
        DisposeGraphics();
        _forceRender = true;
        if (_gpuFailures < MaxGpuFailures)
        {
            InitializeGraphics();
        }
        else
        {
            Log.Warn("Demasiados fallos de la GPU: se dibuja por CPU hasta reiniciar Lander.");
        }
    }

    private void SetVsync(bool enabled)
    {
        if (_vsync is not null)
        {
            _vsync.Enabled = enabled;
        }
    }

    private void HideLander()
    {
        _window.Hide();
        _overlay?.Hide();
        _hitMask = (int.MinValue, 0, 0, 0);
    }

    private void BringLanderToTop()
    {
        _overlay?.BringToTop();

        // La diana del ratón, por encima de la capa de dibujo.
        _window.BringToTop();
    }

    private void Render(double now)
    {
        var body = _game.Body;
        var visual = _game.Visual;

        // Entre dos pasos de física (60 por segundo) se interpola, también posado sobre una ventana que se
        // mueve; solo un salto brusco (reaparecer, mudarse de monitor) se dibuja tal cual.
        var alpha = Math.Clamp(_accumulator / LanderGame.TimeStep, 0, 1);
        var jumped = (body.Position - _previousPosition).LengthSquared > body.Height * body.Height * 4;
        var position = body.State == BodyState.Held || jumped ? body.Position : Vec2.Lerp(_previousPosition, body.Position, alpha);
        var angle = jumped ? body.Angle : _previousAngle + MathUtil.WrapAngle(body.Angle - _previousAngle) * alpha;

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
            LowerLeftJet = visual.LowerLeftJet,
            LowerRightJet = visual.LowerRightJet,
            BoostFlash = visual.BoostFlash,
            CarryingPackage = visual.CarryingPackage,
        };
        ApplyAnimations(position, body, visual, now);
        _scene.Bubble = visual.Bubble;
        _scene.BubbleAge = bubbleAge;
        _scene.BubbleIcon = visual.Bubble is { Icon: BubbleIcon.App } bubble ? _icons.Get(bubble.AppHandle) : null;
        _scene.ShowHud = visual.ShowHud;
        _scene.Fuel = body.FuelFraction;
        _scene.Damage = body.Damage;
        _scene.LegsDown = _game.ManualLegsDown;
        _scene.Mission = _game.Missions.Current;
        _scene.MissionIcon = _scene.Mission is { Kind: MissionKind.LandOnApp } mission ? _icons.Get(mission.AppHandle) : null;
        _scene.Popups = visual.Popups;
        var race = _game.Races.Current;
        _scene.Race = race;
        var raceSignature = race is null ? 0 : HashCode.Combine(race.Status, race.Next, race.Medal, (int)(race.Elapsed * 10), (int)(race.ResultAge * 4));

        // Por GPU el lander se dibuja con precisión de subpíxel: también cuenta como cambio.
        var subpixel = UseGpu ? HashCode.Combine((int)(position.X * 8), (int)(position.Y * 8)) : 0;
        var animation = HashCode.Combine((int)(_scene.Pose.Compress * 20), (int)(_scene.Pose.Yawn * 20), (int)(_scene.Pose.AntennaSway * 100),
            (int)(_scene.Offset.X * 10), (int)(_scene.Offset.Y * 10), (int)(_scene.Tilt * 300), (int)(_scene.ShadowStrength * 30), (int)(_scene.GroundGlow * 30) * 37 + (int)(_scene.LandingLight * 30));
        var signature = HashCode.Combine(
            raceSignature,
            subpixel,
            animation,
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
        if (UseGpu)
        {
            PresentGpu(x, y, size, window);
        }
        else
        {
            _presenter.Present(_window, x, y, size, size, dc => SceneRenderer.Draw(dc, _scene, _particles, window));
        }

        _statRenderTicks += Stopwatch.GetTimestamp() - renderStart;
    }

    /// <summary>
    /// Animaciones puramente visuales: suspensión al posarse, estirarse, bostezar, saltito de alegría,
    /// saludo, meneo, vibración con el motor a tope, antena con inercia y la sombra sobre la superficie.
    /// </summary>
    private void ApplyAnimations(Vec2 position, LanderBody body, LanderVisual visual, double now)
    {
        static double Progress(double remaining, double duration) => remaining > 0 ? 1 - remaining / duration : 0;
        static double Envelope(double remaining, double duration) => remaining > 0 ? Math.Sin(Math.PI * Progress(remaining, duration)) : 0;

        var stretch = Envelope(visual.StretchTime, LanderVisual.StretchDuration);
        var yawn = Envelope(visual.YawnTime, LanderVisual.YawnDuration);
        var compress = visual.Squash * 3.2 - stretch * 2.4 - yawn * 0.9 + visual.CrouchLevel * 3;

        var hop = 9 * Envelope(visual.BounceTime, LanderVisual.BounceDuration);
        var tilt = 0.0;
        if (visual.WaveTime > 0)
        {
            var p = Progress(visual.WaveTime, LanderVisual.WaveDuration);
            tilt += Math.Sin(p * Math.PI * 4) * 0.22 * (1 - p * 0.6);
        }

        if (visual.WiggleTime > 0)
        {
            var p = Progress(visual.WiggleTime, LanderVisual.WiggleDuration);
            tilt += Math.Sin(p * Math.PI * 6) * 0.1 * (1 - p);
            hop += Math.Abs(Math.Sin(p * Math.PI * 3)) * 2.5 * (1 - p);
        }

        // Con el motor fuerte, la nave vibra un poco (nada que se note en la física).
        var shake = visual.MainFlame > 0.35 ? (visual.MainFlame - 0.35) / 0.65 : 0;
        var jitter = new Vec2(Math.Sin(now * 97) * 0.35, Math.Cos(now * 83) * 0.25) * shake;

        _scene.Offset = new Vec2(jitter.X, jitter.Y - hop);
        _scene.Tilt = tilt;
        _scene.Pose = _scene.Pose with { Compress = compress, Yawn = Math.Pow(yawn, 0.7), AntennaSway = _antenna };

        // Sombra y resplandor sobre la superficie de debajo.
        var feet = position.Y + body.HalfHeight + compress * body.Scale * 0.2;
        _scene.ShadowY = null;
        _scene.ShadowStrength = 0;
        _scene.GroundGlow = 0;
        var lightTarget = 0.0;
        if (body.State != BodyState.Destroyed && _world.PlatformBelow(position.X, feet - 2) is { } below)
        {
            var gap = Math.Max(0, below.Y - feet);
            var reach = body.Height * 2.2;
            if (gap < reach)
            {
                _scene.ShadowY = below.Y;
                _scene.ShadowStrength = 1 - gap / reach;
                _scene.GroundGlow = visual.MainFlame * Math.Clamp(1 - gap / (body.Height * 1.6), 0, 1);
            }

            // De noche enciende el faro al bajar hacia donde se va a posar (o al pilotarlo a mano).
            var approaching = _game.Mode == ControlMode.Manual || _game.Autopilot.State == Core.Autopilot.AutopilotState.Landing;
            if (_sensors.DarkMode && body.IsFlying && approaching && gap > body.Height * 0.15 && gap < body.Height * 2.3)
            {
                lightTarget = Math.Clamp((body.Height * 2.3 - gap) / (body.Height * 0.6), 0, 1);
                _scene.LightSurfaceY = below.Y;
            }
        }

        // El faro se enciende y se apaga con suavidad (y se apaga solo al posarse).
        var dt = Math.Clamp(now - _lightClock, 0, 0.1);
        _lightClock = now;
        _scene.LandingLight = MathUtil.Damp(_scene.LandingLight, lightTarget, lightTarget > _scene.LandingLight ? 6 : 4, dt);
        if (_scene.LandingLight < 0.01 && lightTarget <= 0)
        {
            _scene.LandingLight = 0;
        }
    }

    private double _lightClock;

    /// <summary>El faro aún se está encendiendo o apagando.</summary>
    private bool LightFading => _scene.LandingLight > 0;

    /// <summary>Muelle de la antena: se inclina con las aceleraciones laterales y vuelve oscilando.</summary>
    private void UpdateAntenna(LanderBody body, double elapsed)
    {
        if (elapsed <= 0 || elapsed > 0.1)
        {
            // Tras una pausa larga (o al reaparecer) no hay inercia que valga.
            _antenna = 0;
            _antennaVelocity = 0;
            _lastVelocity = body.Velocity;
            return;
        }

        var acceleration = (body.Velocity - _lastVelocity) / elapsed;
        _lastVelocity = body.Velocity;
        var lateral = acceleration.X * Math.Cos(body.Angle) + acceleration.Y * Math.Sin(body.Angle);
        var push = Math.Clamp(-lateral / (body.Scale * 900), -1.5, 1.5) - body.AngularVelocity * 0.05;
        for (var t = 0.0; t < elapsed; t += 1.0 / 240)
        {
            var dt = Math.Min(1.0 / 240, elapsed - t);
            _antennaVelocity += (-170 * _antenna - 6 * _antennaVelocity + push * 40) * dt;
            _antenna = Math.Clamp(_antenna + _antennaVelocity * dt, -0.5, 0.5);
        }

        if (Math.Abs(_antenna) < 0.002 && Math.Abs(_antennaVelocity) < 0.02)
        {
            _antenna = 0;
            _antennaVelocity = 0;
        }
    }

    private bool AntennaMoving => _antenna != 0 || _antennaVelocity != 0;

    /// <summary>Bocanada de un propulsor: <paramref name="outward"/> es hacia dónde sale el gas.</summary>
    private void EmitRcs(int jet, System.Windows.Point local, Vec2 outward, double strength, double elapsed)
    {
        if (strength < 0.12)
        {
            return;
        }

        var body = _game.Body;
        var scale = body.Scale;
        var at = BodyToWorld(body, local) + outward * (2.5 * scale);
        _particles.Rcs(jet, at, outward, body.Velocity, strength, scale, elapsed);
    }

    private void PresentGpu(int x, int y, int size, Rect window)
    {
        var recordStart = Stopwatch.GetTimestamp();
        using (var dc = _frameDrawing.Open())
        {
            SceneRenderer.Draw(dc, _scene, _particles, window);
        }

        _statRecordTicks += Stopwatch.GetTimestamp() - recordStart;

        try
        {
            _overlay!.Present(x, y, size, size, _replay!, _system.FullscreenMonitors);
        }
        catch (SharpGen.Runtime.SharpGenException ex)
        {
            OnGpuFailure(ex);
        }
    }

    /// <summary>
    /// Por GPU, la ventana por capas solo es una diana invisible con la forma del lander: aparece cuando el
    /// cursor se acerca para que se pueda coger, hacer clic o doble clic.
    /// </summary>
    private void UpdateHitTarget(bool near)
    {
        if (!UseGpu)
        {
            return;
        }

        var body = _game.Body;
        var wanted = _settings.AllowDragging && !_hidden && body.State != BodyState.Destroyed && _overlay!.IsVisible
            && (near || _pressed || _dragging);
        if (!wanted)
        {
            if (_window.IsVisible && !_pressed && !_dragging)
            {
                _window.Hide();
                _hitMask = (int.MinValue, 0, 0, 0);
            }

            return;
        }

        var size = Math.Max(16, (int)Math.Ceiling(body.Height * 1.5));
        var x = (int)Math.Round(body.Position.X) - size / 2;
        var y = (int)Math.Round(body.Position.Y) - size / 2;
        var key = (x, y, size, (int)(body.Angle * 50));
        if (key == _hitMask && _window.IsVisible)
        {
            return;
        }

        _hitMask = key;
        var cos = Math.Cos(-body.Angle);
        var sin = Math.Sin(-body.Angle);
        var centerX = body.Position.X - x;
        var centerY = body.Position.Y - y;
        var halfWidth = body.HalfWidth * 1.1;
        var halfHeight = body.HalfHeight;
        _window.PresentMask(x, y, size, size, (column, row) =>
        {
            var dx = column + 0.5 - centerX;
            var dy = row + 0.5 - centerY;
            var localX = dx * cos - dy * sin;
            var localY = dx * sin + dy * cos;
            return Math.Abs(localX) < halfWidth && Math.Abs(localY) < halfHeight;
        });
    }

    private const double BlinkPeriod = 4.6;
    private const double BlinkLength = 0.16;

    /// <summary>Parpadeo: unas 13 veces por minuto, a intervalos irregulares.</summary>
    private static double BlinkAt(double time)
    {
        var cycle = Math.Floor(time / BlinkPeriod);
        var phase = time - BlinkStart(cycle);
        return phase is >= 0 and < BlinkLength ? 1 : 0;
    }

    /// <summary>Próximo instante en que el ojo se cierra o se abre (para despertar justo entonces).</summary>
    private static double NextBlinkEdge(double time)
    {
        var cycle = Math.Floor(time / BlinkPeriod);
        for (var k = 0; k < 3; k++)
        {
            var start = BlinkStart(cycle + k);
            if (start > time)
            {
                return start;
            }

            if (start + BlinkLength > time)
            {
                return start + BlinkLength;
            }
        }

        return time + BlinkPeriod;
    }

    private static double BlinkStart(double cycle)
    {
        var offset = (Math.Sin(cycle * 12.9898) * 43758.5453 % 1 + 1) % 1 * 2.5;
        return cycle * BlinkPeriod + offset;
    }
}
