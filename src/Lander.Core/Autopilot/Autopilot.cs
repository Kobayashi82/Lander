using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Terrain;

namespace Lander.Core.Autopilot;

/// <summary>Los 7 estados del modo automático.</summary>
public enum AutopilotState
{
    Resting,
    Choosing,
    Flying,
    Landing,
    Sleeping,
    Dodging,
    ReturningToBase,
}

/// <summary>Por qué vuela el lander.</summary>
public enum FlightPurpose
{
    Explore,
    Investigate,
    Follow,
    Dodge,
    ReturnToBase,
    Recover,
    Celebrate,
    Hop,
}

public sealed class Personality
{
    /// <summary>Baja con cada vuelo y sube descansando. Con poca energía vuela más despacio y más cerca.</summary>
    public double Energy { get; set; } = 0.85;

    /// <summary>Sube cuando aparece una ventana nueva.</summary>
    public double Curiosity { get; set; } = 0.3;

    /// <summary>0 = sigue al cursor, 1 = huye de él.</summary>
    public double Shyness { get; set; } = 0.5;
}

/// <summary>Todo lo que el piloto automático necesita en cada paso (objeto reutilizable).</summary>
public sealed class AutopilotContext
{
    public World World { get; set; } = World.Empty;

    public LanderBody Body { get; set; } = new();

    public PhysicsConfig Physics { get; set; } = new();

    public Sensors Sensors { get; set; } = new();

    public LanderVisual Visual { get; set; } = new();

    public double Time { get; set; }

    public double Dt { get; set; }

    public double Activity { get; set; } = 1;

    /// <summary>En pausa o en «No molestar»: termina lo que esté haciendo y no inicia vuelos nuevos.</summary>
    public bool HoldStill { get; set; }

    /// <summary>Señales para la capa de juego (celebraciones, sobresaltos...).</summary>
    public List<GameEventKind> Signals { get; } = [];
}

/// <summary>
/// La IA del modo automático. Igual que el jugador, solo produce <see cref="ThrustInput"/>.
/// </summary>
public sealed class Autopilot(IRandom random)
{
    public const double IdleSleepSeconds = 300;
    public const double ChoosingDuration = 1.2;

    private const double MaxFlightSeconds = 30;
    private const double NoveltySeconds = 90;

    private readonly Dictionary<long, double> _newWindows = [];

    private Destination? _destination;
    private FlightPurpose _purpose;
    private bool _sleepAfterLanding;
    private bool _airborneSeen;
    private double _stateTime;
    private double _restDuration = 8;
    private double _flightTime;
    private double _cruiseY;
    private Vec2? _waypoint;
    private double _waypointHold;
    private bool _celebrated;
    private long? _investigateOwner;
    private double _investigateAt;
    private Vec2? _lookOverride;
    private double _lookOverrideTime;
    private double _glanceTimer = 5;
    private Vec2? _glance;
    private double _dodgeCooldown;
    private double _followCooldown;
    private double _sleepBubbleTimer;
    private bool _hasFlown;

    public AutopilotState State { get; private set; } = AutopilotState.Resting;

    public FlightPurpose Purpose => _purpose;

    public Destination? Destination => _destination;

    public Personality Personality { get; } = new();

    public bool IsFlightActive => State is AutopilotState.Choosing or AutopilotState.Flying or AutopilotState.Landing
        or AutopilotState.Dodging or AutopilotState.ReturningToBase;

    public ThrustInput Update(AutopilotContext ctx)
    {
        var dt = ctx.Dt;
        _stateTime += dt;
        _dodgeCooldown -= dt;
        _followCooldown -= dt;
        _lookOverrideTime -= dt;
        UpdatePersonality(dt);
        PruneNovelty(ctx.Time);

        var body = ctx.Body;
        if (body.State is BodyState.Destroyed or BodyState.Held)
        {
            return ThrustInput.None;
        }

        // En el aire sin un vuelo planeado: se ha caído, lo han lanzado o soltado.
        if (!body.IsLanded && State is AutopilotState.Resting or AutopilotState.Sleeping or AutopilotState.Choosing)
        {
            StartRecovery(ctx, startled: true);
        }

        return State switch
        {
            AutopilotState.Resting => UpdateResting(ctx),
            AutopilotState.Sleeping => UpdateSleeping(ctx),
            AutopilotState.Choosing => UpdateChoosing(ctx),
            AutopilotState.Landing => UpdateLanding(ctx),
            _ => UpdateCruise(ctx),
        };
    }

    /// <summary>Reacciona a un suceso del sistema.</summary>
    public void Notify(SystemEvent systemEvent, AutopilotContext ctx)
    {
        var body = ctx.Body;
        switch (systemEvent)
        {
            case WindowOpened opened:
                Personality.Curiosity = Math.Min(1, Personality.Curiosity + 0.3);
                _newWindows[opened.Handle] = ctx.Time;
                if (State == AutopilotState.Resting && ctx.World.SegmentsOf(opened.Handle) is { Count: > 0 } segments)
                {
                    var widest = segments.MaxBy(s => s.Width)!;
                    _lookOverride = new Vec2(widest.CenterX, widest.Y);
                    _lookOverrideTime = 2.5;
                    ctx.Visual.Flash(Expression.Curious, 2);
                    _investigateOwner = opened.Handle;
                    _investigateAt = ctx.Time + random.Range(1.5, 4);
                }

                break;

            case NotificationArrived:
                if (State == AutopilotState.Sleeping)
                {
                    break;
                }

                ctx.Visual.Flash(Expression.Scared, 1.2);
                ctx.Signals.Add(GameEventKind.Startled);
                if (State == AutopilotState.Resting && body.IsLanded && !ctx.HoldStill && body.Support is { } support
                    && ctx.World.FindSegment(support.OwnerId, body.Position.X) is { } here)
                {
                    ctx.Visual.ShowBubble(new Bubble(BubbleIcon.Exclamation, 1.2));
                    StartFlight(ctx, Destination.On(here, body.Position.X), FlightPurpose.Hop, AutopilotState.Flying);
                }

                break;

            case RecycleBinEmptied emptied:
                if (State != AutopilotState.Resting || ctx.HoldStill)
                {
                    break;
                }

                var waypoint = ClampToScreen(ctx, emptied.Location + new Vec2(0, -body.Height * 1.6), body.Height * 1.4);
                if (DestinationPicker.PickBelow(ctx.World, body, waypoint) is { } below)
                {
                    _waypoint = waypoint;
                    _waypointHold = 1.8;
                    BeginChoosing(ctx, below, FlightPurpose.Celebrate, BubbleIcon.Star, waypoint);
                }

                break;
        }
    }

    /// <summary>La simulación avisa de un aterrizaje en modo automático.</summary>
    public void OnTouchdown(LandingResult landing, AutopilotContext ctx)
    {
        switch (landing.Quality)
        {
            case LandingQuality.Perfect when _purpose is FlightPurpose.Explore or FlightPurpose.Investigate
                && random.Chance(0.15):
                ctx.Visual.Flash(Expression.Happy, 2);
                ctx.Visual.CelebrateTime = 1;
                ctx.Signals.Add(GameEventKind.Celebrated);
                break;
            case LandingQuality.Hard:
                ctx.Visual.Flash(Expression.Scared, 1.5);
                break;
        }
    }

    /// <summary>Se devuelve el control a la IA (fin del modo manual o reaparición).</summary>
    public void Resume(AutopilotContext ctx)
    {
        _waypoint = null;
        _sleepAfterLanding = false;
        if (ctx.Body.IsFlying)
        {
            StartRecovery(ctx, startled: false);
        }
        else
        {
            EnterResting(ctx, NewRestDuration(ctx));
        }
    }

    /// <summary>Aterriza en la plataforma más cercana por debajo (pausa, pantalla completa).</summary>
    public void LandNow(AutopilotContext ctx)
    {
        _waypoint = null;
        _sleepAfterLanding = false;
        if (ctx.Body.IsFlying)
        {
            StartRecovery(ctx, startled: false);
        }
        else if (State != AutopilotState.Sleeping)
        {
            EnterResting(ctx, NewRestDuration(ctx));
        }
    }

    private ThrustInput UpdateResting(AutopilotContext ctx)
    {
        var body = ctx.Body;
        var sensors = ctx.Sensors;
        ctx.Visual.SetBaseExpression(Personality.Energy < 0.25 ? Expression.Tired : Expression.Neutral);
        UpdateGaze(ctx);

        if (ctx.HoldStill)
        {
            return ThrustInput.None;
        }

        if (sensors.UserIdleSeconds >= IdleSleepSeconds)
        {
            if (body.IsOnBase)
            {
                EnterSleeping(ctx);
            }
            else
            {
                StartReturnToBase(ctx, sleep: true, BubbleIcon.Sleep);
            }

            return ThrustInput.None;
        }

        if (!body.IsOnBase)
        {
            if (sensors.BatteryLow)
            {
                StartReturnToBase(ctx, sleep: false, BubbleIcon.Battery);
                return ThrustInput.None;
            }

            if (body.FuelFraction < 0.25)
            {
                StartReturnToBase(ctx, sleep: false, BubbleIcon.Fuel);
                return ThrustInput.None;
            }

            if (body.Damage > 0.5)
            {
                StartReturnToBase(ctx, sleep: false, BubbleIcon.Wrench);
                return ThrustInput.None;
            }
        }

        if (TryReactToCursor(ctx))
        {
            return ThrustInput.None;
        }

        if (_investigateOwner is { } owner && ctx.Time >= _investigateAt)
        {
            _investigateOwner = null;
            if (CanExplore(ctx) && random.Chance(0.35 + 0.5 * Personality.Curiosity)
                && owner != body.Support?.OwnerId
                && DestinationPicker.PickOnOwner(ctx.World, body, owner, random) is { } investigate)
            {
                BeginChoosing(ctx, investigate, FlightPurpose.Investigate, BubbleIcon.Question);
                return ThrustInput.None;
            }
        }

        if (_stateTime >= _restDuration)
        {
            if (CanExplore(ctx) && DestinationPicker.PickExploration(ctx.World, body, BuildQuery(ctx), random) is { } next)
            {
                BeginChoosing(ctx, next, FlightPurpose.Explore, BubbleIcon.Arrow);
            }
            else
            {
                _restDuration = _stateTime + random.Range(15, 30);
            }
        }

        return ThrustInput.None;
    }

    private ThrustInput UpdateSleeping(AutopilotContext ctx)
    {
        var visual = ctx.Visual;
        visual.SetBaseExpression(Expression.Asleep);
        visual.LookTarget = null;

        _sleepBubbleTimer -= ctx.Dt;
        if (_sleepBubbleTimer <= 0)
        {
            visual.ShowBubble(new Bubble(BubbleIcon.Sleep, 3.5));
            _sleepBubbleTimer = random.Range(10, 18);
        }

        if (ctx.Sensors.UserIdleSeconds < 2)
        {
            visual.SetBaseExpression(Expression.Neutral);
            visual.Flash(Expression.Tired, 2.5);
            visual.ClearBubble();
            EnterResting(ctx, random.Range(20, 45));
        }

        return ThrustInput.None;
    }

    private ThrustInput UpdateChoosing(AutopilotContext ctx)
    {
        if (_destination is null || !_destination.TryResolve(ctx.World, ctx.Body, out var target, out _))
        {
            _waypoint = null;
            EnterResting(ctx, random.Range(5, 12));
            return ThrustInput.None;
        }

        ctx.Visual.LookTarget = _waypoint ?? target;
        if (_stateTime >= ChoosingDuration)
        {
            var state = _purpose switch
            {
                FlightPurpose.ReturnToBase => AutopilotState.ReturningToBase,
                FlightPurpose.Dodge => AutopilotState.Dodging,
                _ => AutopilotState.Flying,
            };
            StartFlight(ctx, _destination, _purpose, state);
        }

        return ThrustInput.None;
    }

    private ThrustInput UpdateCruise(AutopilotContext ctx)
    {
        var body = ctx.Body;
        var scale = body.Scale;
        var height = body.Height;
        _flightTime += ctx.Dt;

        if (body.IsFlying)
        {
            _airborneSeen = true;
        }
        else if (body.IsLanded)
        {
            if (_airborneSeen)
            {
                OnArrived(ctx);
                return ThrustInput.None;
            }

            // Despegue: motor a tope y nave recta.
            return new ThrustInput(Throttle: 1, Stabilize: true);
        }

        if (_flightTime > MaxFlightSeconds && _purpose != FlightPurpose.Recover)
        {
            _waypoint = null;
            Retarget(ctx, DestinationPicker.PickBelow(ctx.World, body, body.Position), FlightPurpose.Recover);
        }

        if (_destination is null || !_destination.TryResolve(ctx.World, body, out var target, out _))
        {
            Replan(ctx);
            if (_destination is null || !_destination.TryResolve(ctx.World, body, out target, out _))
            {
                // Nada a la vista: descender despacio y estable.
                return FlightGuidance.TrackVelocity(body, ctx.Physics, new Vec2(0, 40 * scale));
            }
        }

        if (_waypoint is { } waypoint)
        {
            return FlyToWaypoint(ctx, waypoint);
        }

        ctx.Visual.LookTarget = target;
        var dx = target.X - body.Position.X;

        if (Math.Abs(dx) < height * 0.5 && Math.Abs(body.Velocity.X) < 45 * scale && body.Position.Y <= target.Y + height * 0.2)
        {
            State = AutopilotState.Landing;
            _stateTime = 0;
            return UpdateLanding(ctx);
        }

        var energyFactor = 0.55 + 0.45 * Personality.Energy;
        var cruiseSpeed = (_purpose == FlightPurpose.Dodge ? 260 : 230) * scale * energyFactor;
        var vx = Math.Sign(dx) * Math.Min(cruiseSpeed, MathUtil.BrakingSpeed(dx, 70 * scale));
        if (_flightTime < 0.45 && _purpose is not (FlightPurpose.Recover or FlightPurpose.Dodge))
        {
            vx = 0;
        }

        var dy = _cruiseY - body.Position.Y;
        var vy = Math.Sign(dy) * Math.Min(170 * scale, MathUtil.BrakingSpeed(dy, 90 * scale));

        var retractLegs = _flightTime > 0.6 && body.Position.Y < target.Y - height * 4 && Math.Abs(dx) > height * 3;
        return Steer(ctx, new Vec2(vx, vy), retractLegs);
    }

    private ThrustInput FlyToWaypoint(AutopilotContext ctx, Vec2 waypoint)
    {
        var body = ctx.Body;
        var scale = body.Scale;
        ctx.Visual.LookTarget = waypoint + new Vec2(0, body.Height);

        var delta = waypoint - body.Position;
        if (delta.Length < body.Height * 0.6 && body.Velocity.Length < 50 * scale)
        {
            if (!_celebrated)
            {
                _celebrated = true;
                ctx.Visual.CelebrateTime = 2;
                ctx.Visual.Flash(Expression.Happy, 2.5);
                ctx.Visual.ShowBubble(new Bubble(BubbleIcon.Heart, 1.8));
                ctx.Signals.Add(GameEventKind.Celebrated);
            }

            _waypointHold -= ctx.Dt;
            if (_waypointHold <= 0)
            {
                _waypoint = null;
                if (_destination is not null && _destination.TryResolve(ctx.World, body, out var target, out _))
                {
                    _cruiseY = Math.Min(body.Position.Y, target.Y) - body.Height * 0.5;
                }
            }
        }

        var vx = Math.Sign(delta.X) * Math.Min(230 * scale, MathUtil.BrakingSpeed(delta.X, 70 * scale));
        var vy = Math.Sign(delta.Y) * Math.Min(170 * scale, MathUtil.BrakingSpeed(delta.Y, 90 * scale));
        return Steer(ctx, new Vec2(vx, vy));
    }

    private ThrustInput UpdateLanding(AutopilotContext ctx)
    {
        var body = ctx.Body;
        var scale = body.Scale;
        var height = body.Height;

        if (body.IsLanded)
        {
            if (_airborneSeen)
            {
                OnArrived(ctx);
                return ThrustInput.None;
            }

            return new ThrustInput(Throttle: 1, Stabilize: true);
        }

        _airborneSeen = true;
        if (_destination is null || !_destination.TryResolve(ctx.World, body, out var target, out _))
        {
            Replan(ctx);
            State = _purpose == FlightPurpose.ReturnToBase ? AutopilotState.ReturningToBase : AutopilotState.Flying;
            return ThrustInput.None;
        }

        ctx.Visual.LookTarget = target + new Vec2(0, height);
        var dx = target.X - body.Position.X;
        var heightAbove = target.Y - body.Position.Y;

        // La plataforma ha subido por encima: volver a ganar altura.
        if (heightAbove < -height * 0.3)
        {
            _cruiseY = target.Y - height * 1.5;
            State = _purpose == FlightPurpose.ReturnToBase ? AutopilotState.ReturningToBase : AutopilotState.Flying;
            return UpdateCruise(ctx);
        }

        // Como un piloto de verdad: centrarse, estabilizar en estacionario y solo entonces bajar despacio.
        var vxNow = Math.Abs(body.Velocity.X);
        double vx;
        double vy;
        double maxTilt;
        if (Math.Abs(dx) > height * 0.9)
        {
            vx = Math.Clamp(dx * 1.2, -60 * scale, 60 * scale);
            vy = 0;
            maxTilt = 0.35;
        }
        else if (heightAbove > height * 1.5)
        {
            vx = Math.Clamp(dx, -40 * scale, 40 * scale);
            vy = Math.Clamp(MathUtil.BrakingSpeed(heightAbove - height * 1.2, 55 * scale), 28 * scale, 160 * scale);
            maxTilt = 0.3;
        }
        else if (Math.Abs(dx) > height * 0.3 || vxNow > 20 * scale || Math.Abs(body.Angle) > 0.12)
        {
            vx = Math.Clamp(dx, -25 * scale, 25 * scale);
            vy = 0;
            maxTilt = 0.2;
        }
        else
        {
            vx = Math.Clamp(dx, -12 * scale, 12 * scale);
            vy = 28 * scale;
            maxTilt = 0.08;
        }

        return Steer(ctx, new Vec2(vx, vy), retractLegs: false, maxTilt);
    }

    /// <summary>
    /// Pide al controlador la velocidad deseada. Solo se posa en su destino y únicamente en la fase de
    /// aterrizaje: en crucero pasa por delante de todas las ventanas, aunque el destino se le cruce.
    /// </summary>
    private ThrustInput Steer(AutopilotContext ctx, Vec2 velocity, bool retractLegs = false, double maxTilt = FlightGuidance.DefaultMaxTilt) =>
        FlightGuidance.TrackVelocity(ctx.Body, ctx.Physics, velocity, retractLegs, maxTilt) with
        {
            LandOn = State == AutopilotState.Landing && _destination is not null ? _destination.OwnerId : ThrustInput.GroundOnly,
        };

    private void OnArrived(AutopilotContext ctx)
    {
        var body = ctx.Body;
        Personality.Energy = Math.Max(0, Personality.Energy - Math.Min(0.25, 0.03 + _flightTime * 0.008));
        _waypoint = null;

        if (_purpose == FlightPurpose.ReturnToBase && !body.IsOnBase)
        {
            // Se posó por el camino: lo vuelve a intentar enseguida.
            EnterResting(ctx, random.Range(1.5, 3));
            return;
        }

        if (_sleepAfterLanding && body.IsOnBase)
        {
            EnterSleeping(ctx);
            return;
        }

        EnterResting(ctx, NewRestDuration(ctx));
    }

    private bool TryReactToCursor(AutopilotContext ctx)
    {
        var body = ctx.Body;
        var sensors = ctx.Sensors;
        if (sensors.Cursor is not { } cursor)
        {
            return false;
        }

        var distance = Vec2.Distance(cursor, body.Position);
        var height = body.Height;

        if (distance < height * 1.8 && sensors.CursorVelocity.Length > 60 * body.Scale && _dodgeCooldown <= 0)
        {
            _dodgeCooldown = 6;
            if (random.NextDouble() < Personality.Shyness)
            {
                var dodge = DestinationPicker.PickDodge(ctx.World, body, cursor, random)
                    ?? DestinationPicker.PickExploration(ctx.World, body, BuildQuery(ctx), random);
                if (dodge is not null)
                {
                    ctx.Visual.Flash(Expression.Scared, 1);
                    StartFlight(ctx, dodge, FlightPurpose.Dodge, AutopilotState.Dodging);
                    return true;
                }
            }

            ctx.Visual.Flash(Expression.Happy, 1.5);
            return false;
        }

        // Un lander poco tímido se acerca a donde se ha quedado quieto el cursor.
        if (Personality.Shyness < 0.4 && _followCooldown <= 0 && sensors.CursorStillSeconds > 2.5
            && distance > height * 3 && distance < 700 * body.Scale && CanExplore(ctx))
        {
            _followCooldown = 25;
            if (random.Chance((0.4 - Personality.Shyness) * 2))
            {
                var margin = DestinationPicker.LandingMargin(body);
                var below = ctx.World.PlatformBelow(cursor.X, cursor.Y, margin);
                if (below is not null && !ctx.Sensors.FullscreenMonitors.Contains(below.MonitorIndex))
                {
                    var x = Math.Clamp(cursor.X, below.Left + margin, below.Right - margin);
                    BeginChoosing(ctx, Destination.On(below, x), FlightPurpose.Follow, BubbleIcon.Heart);
                    return true;
                }
            }
        }

        return false;
    }

    private void UpdateGaze(AutopilotContext ctx)
    {
        var body = ctx.Body;
        var visual = ctx.Visual;

        if (_lookOverride is { } point && _lookOverrideTime > 0)
        {
            visual.LookTarget = point;
            return;
        }

        if (ctx.Sensors.Cursor is { } cursor && Vec2.Distance(cursor, body.Position) < body.Height * 6)
        {
            visual.LookTarget = cursor;
            return;
        }

        // Miradas ocasionales alrededor.
        _glanceTimer -= ctx.Dt;
        if (_glanceTimer <= 0)
        {
            if (_glance is null && random.Chance(0.7))
            {
                _glance = body.Position + new Vec2(random.Range(-1, 1), random.Range(-0.6, 0.3)) * body.Height * 3;
                _glanceTimer = random.Range(1, 2);
            }
            else
            {
                _glance = null;
                _glanceTimer = random.Range(4, 10);
            }
        }

        visual.LookTarget = _glance;
    }

    private void BeginChoosing(AutopilotContext ctx, Destination destination, FlightPurpose purpose, BubbleIcon icon, Vec2? lookAt = null)
    {
        _destination = destination;
        _purpose = purpose;
        State = AutopilotState.Choosing;
        _stateTime = 0;

        var body = ctx.Body;
        var target = lookAt ?? (destination.TryResolve(ctx.World, body, out var center, out _) ? center : body.Position);
        var direction = Math.Atan2(target.Y - body.Position.Y, target.X - body.Position.X);
        ctx.Visual.LookTarget = target;
        ctx.Visual.ShowBubble(new Bubble(icon, ChoosingDuration + 0.8, direction));
        ctx.Visual.Flash(purpose == FlightPurpose.Investigate ? Expression.Curious : Expression.Focused, ChoosingDuration + 0.5);
    }

    private void StartFlight(AutopilotContext ctx, Destination? destination, FlightPurpose purpose, AutopilotState state)
    {
        _destination = destination;
        _purpose = purpose;
        State = state;
        _stateTime = 0;
        _flightTime = 0;
        _celebrated = false;
        _airborneSeen = ctx.Body.IsFlying;
        _hasFlown = true;
        if (purpose != FlightPurpose.Celebrate)
        {
            _waypoint = null;
        }

        var body = ctx.Body;
        var height = body.Height;
        var targetY = body.Position.Y;
        var targetPoint = body.Position;
        if (destination is not null && destination.TryResolve(ctx.World, body, out var center, out _))
        {
            targetY = center.Y;
            targetPoint = center;
        }

        var climb = purpose switch
        {
            FlightPurpose.Dodge or FlightPurpose.Hop => height * random.Range(0.8, 1.3),
            FlightPurpose.Recover => height * 0.3,
            _ => height * random.Range(1.4, 3),
        };

        // Nunca por debajo del destino: rozando el techo si hace falta, pero llegando siempre desde arriba.
        var top = Math.Max(MonitorTop(ctx, body.Position), MonitorTop(ctx, targetPoint)) + body.HalfHeight + 4 * body.Scale;
        var preferred = Math.Max(Math.Min(body.Position.Y, targetY) - climb, top + height * 0.8);
        _cruiseY = Math.Min(preferred, Math.Max(top, targetY - height * 0.5));
    }

    private void StartRecovery(AutopilotContext ctx, bool startled)
    {
        if (startled)
        {
            ctx.Visual.Flash(Expression.Scared, 1.5);
            ctx.Visual.ShowBubble(new Bubble(BubbleIcon.Exclamation, 1.2));
            ctx.Signals.Add(GameEventKind.Startled);
        }

        _sleepAfterLanding = false;
        _waypoint = null;
        var body = ctx.Body;
        StartFlight(ctx, DestinationPicker.PickBelow(ctx.World, body, body.Position, BrakingDistance(body, ctx.Physics)),
            FlightPurpose.Recover, AutopilotState.Flying);
    }

    /// <summary>Distancia que necesita para frenar la caída actual, con margen.</summary>
    private static double BrakingDistance(LanderBody body, PhysicsConfig physics)
    {
        if (body.Fuel <= 0 || body.Velocity.Y <= 0)
        {
            return 0;
        }

        var thrustFactor = 1 - physics.MaxDamageThrustPenalty * Math.Clamp(body.Damage, 0, 1);
        var deceleration = (physics.MainThrust * thrustFactor - physics.Gravity) * body.Scale * 0.6;
        return deceleration > 0 ? body.Velocity.Y * body.Velocity.Y / (2 * deceleration) : double.MaxValue / 4;
    }

    private void StartReturnToBase(AutopilotContext ctx, bool sleep, BubbleIcon icon)
    {
        if (DestinationPicker.PickBase(ctx.World, ctx.Body, random) is not { } home)
        {
            return;
        }

        _sleepAfterLanding = sleep;
        BeginChoosing(ctx, home, FlightPurpose.ReturnToBase, icon);
    }

    private void Retarget(AutopilotContext ctx, Destination? destination, FlightPurpose purpose)
    {
        _destination = destination;
        _purpose = purpose;
        _flightTime = 0;
        if (destination is not null && destination.TryResolve(ctx.World, ctx.Body, out var center, out _))
        {
            _cruiseY = Math.Min(ctx.Body.Position.Y, center.Y) - ctx.Body.Height * 0.5;
        }
    }

    private void Replan(AutopilotContext ctx)
    {
        if (_purpose == FlightPurpose.ReturnToBase)
        {
            Retarget(ctx, DestinationPicker.PickBase(ctx.World, ctx.Body, random), FlightPurpose.ReturnToBase);
        }
        else
        {
            Retarget(ctx, DestinationPicker.PickBelow(ctx.World, ctx.Body, ctx.Body.Position), FlightPurpose.Recover);
        }
    }

    private void EnterResting(AutopilotContext ctx, double duration)
    {
        State = AutopilotState.Resting;
        _stateTime = 0;
        _restDuration = duration;
        _destination = null;
        ctx.Visual.LookTarget = null;
    }

    private void EnterSleeping(AutopilotContext ctx)
    {
        State = AutopilotState.Sleeping;
        _stateTime = 0;
        _destination = null;
        _sleepBubbleTimer = 0.5;
        ctx.Visual.SetBaseExpression(Expression.Asleep);
    }

    private static bool CanExplore(AutopilotContext ctx)
    {
        var body = ctx.Body;
        if (ctx.Sensors.BatteryLow || ctx.HoldStill)
        {
            return false;
        }

        // En la base, espera a repostar y reparar antes de salir.
        return !body.IsOnBase || (body.FuelFraction > 0.6 && body.Damage < 0.3);
    }

    private DestinationQuery BuildQuery(AutopilotContext ctx)
    {
        var body = ctx.Body;
        var needsBase = body.FuelFraction < 0.5 || body.Damage > 0.3;
        return new DestinationQuery
        {
            Energy = Personality.Energy,
            Curiosity = Personality.Curiosity,
            BaseBias = needsBase ? 2.5 : body.IsOnBase ? 0.3 : 0.7,
            ExcludeOwner = body.Support?.OwnerId,
            NewWindows = _newWindows.Keys.ToHashSet(),
            AvoidMonitors = ctx.Sensors.FullscreenMonitors,
        };
    }

    private double NewRestDuration(AutopilotContext ctx)
    {
        if (!_hasFlown)
        {
            return random.Range(6, 12);
        }

        var duration = random.Range(50, 150);
        duration *= 0.6 + (1 - Personality.Energy) * 0.9;
        duration *= 1 - 0.45 * Personality.Curiosity;
        duration /= Math.Clamp(ctx.Activity, 0.25, 4);
        if (ctx.Sensors.PowerSaver)
        {
            duration *= 2;
        }

        return duration;
    }

    private void UpdatePersonality(double dt)
    {
        var energyRate = State switch
        {
            AutopilotState.Resting => 0.004,
            AutopilotState.Sleeping => 0.012,
            AutopilotState.Choosing => 0,
            _ => -0.004,
        };

        Personality.Energy = Math.Clamp(Personality.Energy + energyRate * dt, 0, 1);
        Personality.Curiosity = Math.Clamp(Personality.Curiosity - 0.004 * dt, 0.1, 1);
    }

    private void PruneNovelty(double time)
    {
        if (_newWindows.Count == 0)
        {
            return;
        }

        foreach (var (handle, seen) in _newWindows)
        {
            if (time - seen > NoveltySeconds)
            {
                _newWindows.Remove(handle);
            }
        }
    }

    private static double MonitorTop(AutopilotContext ctx, Vec2 point) =>
        (ctx.World.MonitorAt(point) ?? ctx.World.NearestMonitor(point))?.Bounds.Top ?? point.Y - 1000;

    private static Vec2 ClampToScreen(AutopilotContext ctx, Vec2 point, double margin)
    {
        if ((ctx.World.MonitorAt(point) ?? ctx.World.NearestMonitor(point)) is not { } monitor)
        {
            return point;
        }

        var bounds = monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;
        return new Vec2(
            Math.Clamp(point.X, bounds.Left + margin, Math.Max(bounds.Left + margin, bounds.Right - margin)),
            Math.Clamp(point.Y, bounds.Top + margin, Math.Max(bounds.Top + margin, bounds.Bottom - margin * 2)));
    }
}
