using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;

namespace Lander.Core.Autopilot;

/// <summary>
/// El vuelo del piloto automático: elige una forma de llegar (crucero, rasante, perezoso, en arco o con
/// una frenada en el último instante), la sigue con el controlador de vuelo y vigila el combustible y el
/// techo durante todo el trayecto.
/// </summary>
public sealed partial class Autopilot
{
    /// <summary>Velocidad de contacto de un descenso con frenada final (a escala 1, muy por debajo del límite).</summary>
    public const double SlamTouchdownSpeed = 32;

    private const double SpoolSeconds = 0.4;
    private const int ArcSamples = 32;

    private enum FlightPhase
    {
        Transit,
        Flip,
        Poise,
        Drop,
        Burn,
        Approach,
    }

    private readonly Vec2[] _arcPoints = new Vec2[ArcSamples + 1];
    private readonly double[] _arcLengths = new double[ArcSamples + 1];

    private FlightStyle _style;
    private FlightPhase _phase;
    private double _flightTime;
    private double _airTime;
    private bool _airborneSeen;
    private double _cruiseY;
    private bool _spool;
    private bool _leaf;
    private double _leafTime;
    private double _poiseDuration;
    private bool _hopApex;
    private bool _finalDescent;
    private double _nextFuelCheck;

    private bool _arcStarted;
    private Vec2 _arcStart;
    private double _arcApex;
    private int _arcIndex;

    private bool _flipPlanned;
    private bool _flipDone;
    private int _flipStage;
    private double _flipTurned;
    private double _flipDirection;
    private double _flipPop;
    private double _flipTime;

    private PilotMistake _mistake;
    private double _mistakeSeverity;

    /// <summary>Forma de volar del vuelo actual.</summary>
    public FlightStyle Style => _style;

    /// <summary>El despiste que va a tener en este vuelo, si lo hay.</summary>
    public PilotMistake Mistake => _mistake;

    /// <summary>Dando una voltereta en el aire.</summary>
    public bool IsFlipping => _phase == FlightPhase.Flip;

    /// <summary>Cayendo con el motor apagado antes de la frenada final.</summary>
    public bool IsFreeFalling => _phase == FlightPhase.Drop;

    /// <summary>Frenando a fondo en el último instante.</summary>
    public bool IsBurning => _phase == FlightPhase.Burn;

    private void StartFlight(AutopilotContext ctx, Destination? destination, FlightPurpose purpose, AutopilotState state)
    {
        var body = ctx.Body;
        _destination = destination;
        _purpose = purpose;
        State = state;
        _stateTime = 0;
        _flightTime = 0;
        _airTime = 0;
        _celebrated = false;
        _airborneSeen = body.IsFlying;
        _hasFlown = true;
        _napping = false;
        _phase = FlightPhase.Transit;
        _arcStarted = false;
        _flipDone = false;
        _flipStage = 0;
        _leaf = false;
        _leafTime = 0;
        _hopApex = false;
        _nextFuelCheck = 0;
        if (purpose != FlightPurpose.Celebrate)
        {
            _waypoint = null;
        }

        // Antes de despegar, el motor se enciende un instante sin levantar la nave (salvo con prisa).
        _spool = body.IsLanded && purpose is not (FlightPurpose.Hop or FlightPurpose.Dodge or FlightPurpose.Recover);

        var targetPoint = body.Position;
        if (destination is not null && destination.TryResolve(ctx.World, body, out var center, out _))
        {
            targetPoint = center;
        }

        _style = ChooseStyle(ctx, purpose, targetPoint);
        _cruiseY = PlanAltitude(ctx, _style, purpose, targetPoint);
        RollMistake(ctx, purpose);
        _poiseDuration = random.Range(0.35, 0.9);
        _flipPlanned = _style is FlightStyle.Cruise or FlightStyle.Arc or FlightStyle.Brisk
            && purpose is FlightPurpose.Explore or FlightPurpose.Investigate or FlightPurpose.Follow or FlightPurpose.Celebrate
            && Personality.Energy > 0.55 && random.Chance(0.08 + 0.17 * Personality.Energy);
    }

    /// <summary>
    /// Decide si en este vuelo va a despistarse y cuánto. Cansado se equivoca más. La mayoría de despistes
    /// acaban en un aterrizaje duro; solo los más graves, en accidente.
    /// </summary>
    private void RollMistake(AutopilotContext ctx, FlightPurpose purpose)
    {
        _mistake = PilotMistake.None;
        _mistakeSeverity = 0;
        var clumsiness = Math.Clamp(ctx.PilotErrors, 0, 1);
        if (clumsiness <= 0 || purpose is FlightPurpose.Recover or FlightPurpose.Dodge or FlightPurpose.Hop)
        {
            return;
        }

        if (!random.Chance(clumsiness * (0.14 + 0.12 * (1 - Personality.Energy))))
        {
            return;
        }

        _mistakeSeverity = random.NextDouble();
        if (_style == FlightStyle.HoverSlam)
        {
            _mistake = random.Chance(0.75) ? PilotMistake.LateIgnition : PilotMistake.FastTouchdown;
            return;
        }

        var roll = random.NextDouble();
        _mistake = roll < 0.35 ? PilotMistake.Overshoot : roll < 0.7 ? PilotMistake.FastTouchdown : PilotMistake.Wobble;
    }

    /// <summary>Velocidad de contacto que busca en la aproximación final (más si se ha despistado).</summary>
    /// <summary>Margen con el que enciende el motor en la frenada final (más tarde si se ha despistado).</summary>
    private double IgnitionThreshold =>
        0.78 + (_mistake == PilotMistake.LateIgnition ? 0.12 + 0.5 * _mistakeSeverity * _mistakeSeverity : 0);

    private double ContactSpeed(double normal, double scale) =>
        (normal + (_mistake == PilotMistake.FastTouchdown ? 60 * _mistakeSeverity : 0)) * scale;

    /// <summary>Elige cómo volar según la distancia, el desnivel, la energía y el combustible.</summary>
    private FlightStyle ChooseStyle(AutopilotContext ctx, FlightPurpose purpose, Vec2 target)
    {
        if (purpose is FlightPurpose.Hop or FlightPurpose.Dodge or FlightPurpose.Recover or FlightPurpose.Stroll)
        {
            return FlightStyle.Direct;
        }

        if (purpose == FlightPurpose.EdgeDrop)
        {
            return FlightStyle.HoverSlam;
        }

        var body = ctx.Body;
        var height = body.Height;
        var dx = Math.Abs(target.X - body.Position.X);
        var drop = target.Y - body.Position.Y;
        var energy = Personality.Energy;
        var lowFuel = body.FuelFraction < 0.45;

        // La frenada en el último instante solo con la nave sana y combustible para corregir si algo va mal.
        var slamFeasible = body.FuelFraction > 0.3 && body.Damage < 0.35
            && target.Y - PlanAltitude(ctx, FlightStyle.HoverSlam, purpose, target) >= height * 6;

        var weights = new double[6];
        weights[(int)FlightStyle.Cruise] = 1;
        weights[(int)FlightStyle.Brisk] = dx > height * 5 && !lowFuel ? 0.25 + 0.5 * energy : 0;
        weights[(int)FlightStyle.Lazy] = energy < 0.5 && !lowFuel && dx < height * 15 ? (0.5 - energy) * 2.4 : 0;
        weights[(int)FlightStyle.Arc] = dx > height * 4 ? 0.7 : 0;
        weights[(int)FlightStyle.HoverSlam] = slamFeasible ? (drop > height * 8 ? 1 : 0.55) : 0;
        if (purpose == FlightPurpose.ReturnToBase && slamFeasible)
        {
            // Caer con el motor apagado es la forma más económica de bajar.
            weights[(int)FlightStyle.HoverSlam] *= 2;
        }

        var index = random.PickWeighted(weights);
        return index < 0 ? FlightStyle.Cruise : (FlightStyle)index;
    }

    /// <summary>Altura (Y del centro) a la que viajar, siempre bajo el techo y nunca por debajo del destino.</summary>
    private double PlanAltitude(AutopilotContext ctx, FlightStyle style, FlightPurpose purpose, Vec2 target)
    {
        var body = ctx.Body;
        var height = body.Height;
        var from = body.Position;
        var top = Math.Max(
            FlightEnvelope.HighestCenter(ctx.World, body, from, 0.3),
            FlightEnvelope.HighestCenter(ctx.World, body, target, 0.3));
        if (double.IsNegativeInfinity(top))
        {
            top = Math.Max(MonitorTop(ctx, from), MonitorTop(ctx, target)) + body.HalfHeight + height * 0.5;
        }

        var higher = Math.Min(from.Y, target.Y);
        switch (style)
        {
            case FlightStyle.HoverSlam when purpose == FlightPurpose.EdgeDrop:
                return Math.Max(top, from.Y - height * 0.7);

            case FlightStyle.HoverSlam:
                // Lo más alto que permita el techo, pero sin subir más de la cuenta.
                return Math.Max(top, higher - height * random.Range(1, 2.6));

            case FlightStyle.Arc:
                _arcApex = height * random.Range(2, 5);
                break;
        }

        var climb = style switch
        {
            FlightStyle.Direct => purpose switch
            {
                FlightPurpose.Recover => height * 0.3,
                FlightPurpose.Stroll => height * random.Range(0.5, 0.9),
                _ => height * random.Range(0.8, 1.3),
            },
            FlightStyle.Brisk => height * random.Range(0.6, 1.1),
            FlightStyle.Lazy => height * random.Range(0.8, 1.5),
            _ => height * random.Range(1.4, 3),
        };

        var floor = style is FlightStyle.Cruise or FlightStyle.Arc ? top + height * 0.8 : top;
        var preferred = Math.Max(higher - climb, floor);
        var cruise = Math.Min(preferred, Math.Max(top, target.Y - height * 0.5));

        // Con el destino pegado al techo, lo justo por encima de él para llegar siempre desde arriba.
        var ceiling = FlightEnvelope.HighestCenter(ctx.World, body, target);
        return Math.Max(Math.Min(cruise, target.Y - height * 0.25), double.IsNegativeInfinity(ceiling) ? cruise : ceiling);
    }

    private ThrustInput Takeoff() =>
        _spool && _flightTime < SpoolSeconds
            ? new ThrustInput(Throttle: 0.3, Stabilize: true, LandOn: ThrustInput.GroundOnly)
            : new ThrustInput(Throttle: 1, Stabilize: true, LandOn: ThrustInput.GroundOnly);

    private ThrustInput UpdateCruise(AutopilotContext ctx)
    {
        var body = ctx.Body;
        _flightTime += ctx.Dt;

        if (body.IsFlying)
        {
            _airborneSeen = true;
            _airTime += ctx.Dt;
        }
        else if (body.IsLanded)
        {
            if (_airborneSeen)
            {
                OnArrived(ctx);
                return ThrustInput.None;
            }

            return Takeoff();
        }

        if (_flightTime > MaxFlightSeconds && _purpose != FlightPurpose.Recover)
        {
            _waypoint = null;
            Retarget(ctx, DestinationPicker.PickBelow(ctx.World, body, body.Position, BrakingDistance(body, ctx.Physics)), FlightPurpose.Recover);
        }

        if (!TryResolveTarget(ctx, out var target))
        {
            // Nada a la vista: descender despacio y estable.
            return Steer(ctx, new Vec2(0, 40 * body.Scale));
        }

        if (CheckFuel(ctx, target) && !TryResolveTarget(ctx, out target))
        {
            return Steer(ctx, new Vec2(0, 40 * body.Scale));
        }

        if (_waypoint is { } waypoint)
        {
            return FlyToWaypoint(ctx, waypoint);
        }

        if (_phase == FlightPhase.Flip || TryStartFlip(ctx))
        {
            return UpdateFlip(ctx);
        }

        ctx.Visual.LookTarget = target;
        if (IsShortHop(ctx, target))
        {
            return UpdateHop(ctx, target);
        }

        return _style == FlightStyle.Arc ? UpdateArc(ctx, target) : UpdateTransit(ctx, target);
    }

    /// <summary>Saltitos (paseos, sustos, esquivas): cerca y casi a la misma altura.</summary>
    private bool IsShortHop(AutopilotContext ctx, Vec2 target) =>
        _style == FlightStyle.Direct && _purpose is FlightPurpose.Stroll or FlightPurpose.Hop or FlightPurpose.Dodge
        && Math.Abs(target.X - ctx.Body.Position.X) < ctx.Body.Height * 8
        && Math.Abs(target.Y - ctx.Body.Position.Y) < ctx.Body.Height * 2.5;

    /// <summary>
    /// Un salto corto y ágil: sube hasta la altura del salto, se desplaza y baja en cuanto está encima,
    /// sin la parada en estacionario de una aproximación normal.
    /// </summary>
    private ThrustInput UpdateHop(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var scale = body.Scale;
        var height = body.Height;
        var dx = target.X - body.Position.X;
        var above = target.Y - body.Position.Y;
        if (!_hopApex && (body.Position.Y <= _cruiseY + height * 0.15 || _airTime > 1.1))
        {
            _hopApex = true;
        }

        var vx = Math.Sign(dx) * Math.Min(170 * scale, MathUtil.ApproachSpeed(dx, 70 * scale, body.Velocity.X, 0.45));
        if (!_hopApex || Math.Abs(dx) > height * 0.6 || Math.Abs(body.Velocity.X) > 45 * scale)
        {
            var dy = _cruiseY - body.Position.Y;
            var vy = Math.Sign(dy) * Math.Min(150 * scale, MathUtil.ApproachSpeed(dy, 120 * scale, body.Velocity.Y, 0.1));
            return Steer(ctx, new Vec2(_hopApex ? vx : vx * 0.3, vy), retractLegs: false, maxTilt: 0.6);
        }

        // Ya encima: a por el destino, terminando de centrarse mientras baja.
        State = AutopilotState.Landing;
        if (above > height * 0.6)
        {
            var descent = Math.Clamp(MathUtil.ApproachSpeed(above - height * 0.2, 70 * scale, body.Velocity.Y, 0.1), 32 * scale, 60 * scale);
            return Steer(ctx, new Vec2(Math.Clamp(vx, -35 * scale, 35 * scale), descent), retractLegs: false, maxTilt: 0.2);
        }

        // Los últimos centímetros, recto: el contacto tiene que ser con la nave nivelada.
        var settle = Math.Abs(body.Angle) > 0.09 || Math.Abs(dx) > height * 0.25 ? 0 : ContactSpeed(36, scale);
        return Steer(ctx, new Vec2(Math.Clamp(dx * 1.5, -20 * scale, 20 * scale), settle), retractLegs: false, maxTilt: 0.06);
    }

    private bool TryResolveTarget(AutopilotContext ctx, out Vec2 target)
    {
        if (_destination is not null && _destination.TryResolve(ctx.World, ctx.Body, out target, out _))
        {
            return true;
        }

        Replan(ctx);
        target = default;
        return _destination is not null && _destination.TryResolve(ctx.World, ctx.Body, out target, out _);
    }

    private readonly record struct Profile(double Speed, double Tilt, double Braking);

    private static Profile ProfileOf(FlightStyle style) => style switch
    {
        FlightStyle.Brisk => new Profile(330, 0.72, 105),
        FlightStyle.Lazy => new Profile(170, 0.4, 45),
        FlightStyle.Arc => new Profile(260, 0.65, 75),
        FlightStyle.HoverSlam => new Profile(240, 0.6, 75),
        _ => new Profile(230, FlightGuidance.DefaultMaxTilt, 70),
    };

    /// <summary>Desplazamiento a la altura de crucero hasta quedar sobre el destino.</summary>
    private ThrustInput UpdateTransit(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var scale = body.Scale;
        var height = body.Height;
        var profile = ProfileOf(_style);
        var dx = target.X - body.Position.X;

        if (_style == FlightStyle.HoverSlam)
        {
            if (Math.Abs(dx) < height * 0.35 && Math.Abs(body.Velocity.X) < 22 * scale
                && Math.Abs(body.Position.Y - _cruiseY) < height * 0.5 && Math.Abs(body.Velocity.Y) < 35 * scale)
            {
                State = AutopilotState.Landing;
                _stateTime = 0;
                _phase = FlightPhase.Poise;
                ctx.Visual.Flash(Expression.Focused, 3);
                return UpdateLanding(ctx);
            }
        }
        else if (Math.Abs(dx) < height * 0.5 && Math.Abs(body.Velocity.X) < 45 * scale && body.Position.Y <= target.Y + height * 0.2)
        {
            BeginApproach(ctx, target);
            return UpdateLanding(ctx);
        }

        var energyFactor = 0.55 + 0.45 * Personality.Energy;
        var cruiseSpeed = (_purpose == FlightPurpose.Dodge ? 260 : profile.Speed) * scale * energyFactor;
        var vx = Math.Sign(dx) * Math.Min(cruiseSpeed, MathUtil.ApproachSpeed(dx, profile.Braking * scale * (_mistake == PilotMistake.Overshoot ? 1.5 + 1.5 * _mistakeSeverity : 1), body.Velocity.X));
        if (_airTime < 0.45 && _purpose is not (FlightPurpose.Recover or FlightPurpose.Dodge))
        {
            vx = 0;
        }

        var dy = _cruiseY - body.Position.Y;
        var vy = Math.Sign(dy) * Math.Min(170 * scale, MathUtil.ApproachSpeed(dy, 90 * scale, body.Velocity.Y, 0.1));

        var retractLegs = _airTime > 0.6 && body.Position.Y < target.Y - height * 4 && Math.Abs(dx) > height * 3;
        return Steer(ctx, new Vec2(vx, vy), retractLegs, profile.Tilt);
    }

    /// <summary>Un arco amplio (curva de Bézier) desde el despegue hasta un punto sobre el destino.</summary>
    private ThrustInput UpdateArc(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var scale = body.Scale;
        var height = body.Height;
        if (!_arcStarted)
        {
            if (_airTime < 0.35)
            {
                return Steer(ctx, new Vec2(0, -110 * scale));
            }

            _arcStarted = true;
            _arcStart = body.Position;
            _arcIndex = 0;
            BuildArc(ctx, target + new Vec2(0, -height * 1.4));
            if (!ArcClearsGround(ctx, target))
            {
                // El arco rozaría el suelo de otro monitor por el camino: mejor un crucero.
                _style = FlightStyle.Cruise;
                _cruiseY = PlanAltitude(ctx, FlightStyle.Cruise, _purpose, target);
                return UpdateTransit(ctx, target);
            }
        }

        BuildArc(ctx, target + new Vec2(0, -height * 1.4));

        var best = _arcIndex;
        var bestDistance = double.MaxValue;
        for (var i = _arcIndex; i <= Math.Min(ArcSamples, _arcIndex + 6); i++)
        {
            var distance = (_arcPoints[i] - body.Position).LengthSquared;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        _arcIndex = best;
        var total = _arcLengths[ArcSamples];
        var travelled = _arcLengths[best];
        var remaining = Math.Max(0, total - travelled) + Math.Sqrt(bestDistance) * 0.5;
        if (remaining < height * 0.8 || best == ArcSamples)
        {
            BeginApproach(ctx, target);
            return UpdateLanding(ctx);
        }

        var lookahead = Math.Max(height * 1.1, body.Velocity.Length * 0.35);
        var carrot = PointOnArc(travelled + lookahead);
        var profile = ProfileOf(FlightStyle.Arc);
        var speed = Math.Min(
            profile.Speed * scale * (0.55 + 0.45 * Personality.Energy),
            Math.Min(MathUtil.BrakingSpeed(remaining, profile.Braking * scale), 70 * scale + MathUtil.BrakingSpeed(travelled, 140 * scale)));
        var direction = (carrot - body.Position).Normalized();
        var retract = remaining > height * 4 && travelled > height * 2;
        return Steer(ctx, direction * speed, retract, profile.Tilt);
    }

    private void BuildArc(AutopilotContext ctx, Vec2 end)
    {
        var body = ctx.Body;
        var start = _arcStart;
        var middle = Vec2.Lerp(start, end, 0.5);

        // Punto más alto de la curva: el elegido, pero siempre bajo el techo.
        var peak = Math.Min(start.Y, end.Y) - _arcApex;
        var limit = FlightEnvelope.HighestCenter(ctx.World, body, middle, 0.3);
        if (!double.IsNegativeInfinity(limit))
        {
            peak = Math.Max(peak, limit);
        }

        peak = Math.Min(peak, Math.Min(start.Y, end.Y));
        var control = new Vec2(middle.X, (peak - 0.25 * (start.Y + end.Y)) * 2);
        _arcLengths[0] = 0;
        for (var i = 0; i <= ArcSamples; i++)
        {
            var u = (double)i / ArcSamples;
            var a = (1 - u) * (1 - u);
            var b = 2 * u * (1 - u);
            var c = u * u;
            _arcPoints[i] = new Vec2(a * start.X + b * control.X + c * end.X, a * start.Y + b * control.Y + c * end.Y);
            if (i > 0)
            {
                _arcLengths[i] = _arcLengths[i - 1] + Vec2.Distance(_arcPoints[i], _arcPoints[i - 1]);
            }
        }
    }

    /// <summary>Ningún tramo del arco (salvo el final) pasa demasiado cerca de un suelo.</summary>
    private bool ArcClearsGround(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var feetAtTarget = target.Y + body.HalfHeight;
        for (var i = 0; i <= ArcSamples * 0.85; i++)
        {
            var point = _arcPoints[i];
            foreach (var platform in ctx.World.Platforms)
            {
                if (platform.Kind == Terrain.PlatformKind.Ground && platform.Y < feetAtTarget - 1
                    && platform.Right >= point.X - body.HalfWidth && platform.Left <= point.X + body.HalfWidth
                    && point.Y + body.HalfHeight + body.Height * 0.5 > platform.Y && point.Y < platform.Y)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private Vec2 PointOnArc(double length)
    {
        if (length >= _arcLengths[ArcSamples])
        {
            return _arcPoints[ArcSamples];
        }

        for (var i = 1; i <= ArcSamples; i++)
        {
            if (_arcLengths[i] >= length)
            {
                var span = _arcLengths[i] - _arcLengths[i - 1];
                var t = span > 0 ? (length - _arcLengths[i - 1]) / span : 0;
                return Vec2.Lerp(_arcPoints[i - 1], _arcPoints[i], t);
            }
        }

        return _arcPoints[ArcSamples];
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
                    _cruiseY = Math.Max(Math.Min(body.Position.Y, target.Y) - body.Height * 0.5, FlightEnvelope.HighestCenter(ctx.World, body, body.Position, 0.3));
                }
            }
        }

        var vx = Math.Sign(delta.X) * Math.Min(230 * scale, MathUtil.BrakingSpeed(delta.X, 70 * scale));
        var vy = Math.Sign(delta.Y) * Math.Min(170 * scale, MathUtil.BrakingSpeed(delta.Y, 90 * scale));
        return Steer(ctx, new Vec2(vx, vy));
    }

    // Voltereta ----------------------------------------------------------------------------------------

    /// <summary>
    /// Una voltereta cuando hay sitio de sobra: un empujón hacia arriba, motor apagado, una vuelta completa
    /// con los propulsores de giro y recuperación. El empujón se calcula para no llegar al techo.
    /// </summary>
    private bool TryStartFlip(AutopilotContext ctx)
    {
        var body = ctx.Body;
        if (!_flipPlanned || _flipDone || _phase != FlightPhase.Transit || _airTime < 1.2 || _waypoint is not null)
        {
            return false;
        }

        var scale = body.Scale;
        var height = body.Height;
        if (body.Velocity.Length > 150 * scale || Math.Abs(body.Angle) > 0.15 || body.FuelFraction < 0.35)
        {
            return false;
        }

        var highest = FlightEnvelope.HighestCenter(ctx.World, body, body.Position, 0.2);
        var room = double.IsNegativeInfinity(highest) ? height * 20 : body.Position.Y - highest;
        var gravity = ctx.Physics.Gravity * scale;
        var pop = Math.Min(250 * scale, Math.Sqrt(2 * gravity * Math.Max(0, room - height * 0.4)) * 0.92);
        var groundGap = GroundBelow(ctx, body.Position) - body.FeetY;
        if (pop < 200 * scale || groundGap < height * 4)
        {
            return false;
        }

        _phase = FlightPhase.Flip;
        _flipStage = 0;
        _flipPop = pop;
        _flipTurned = 0;
        _flipTime = 0;
        _flipDirection = body.Velocity.X > 5 * scale ? 1 : body.Velocity.X < -5 * scale ? -1 : random.Sign();
        ctx.Visual.Flash(Expression.Happy, 3.5);
        return true;
    }

    private ThrustInput UpdateFlip(AutopilotContext ctx)
    {
        var body = ctx.Body;
        var config = ctx.Physics;
        var dt = ctx.Dt;
        _flipTime += dt;
        if (_flipTime > 5 || body.Fuel <= 0)
        {
            EndFlip(ctx);
            return Steer(ctx, Vec2.Zero);
        }

        if (_flipStage == 0)
        {
            // Empujón hacia arriba, sin pasarse: lo que suba ahora es lo que subirá sin motor.
            var highest = FlightEnvelope.HighestCenter(ctx.World, body, body.Position, 0.2);
            if (body.Velocity.Y > -_flipPop && FlightEnvelope.CoastApex(body, config) > highest)
            {
                return new ThrustInput(Throttle: 1, Rotate: FlightGuidance.RotateToward(body, config, 0), LandOn: ThrustInput.GroundOnly);
            }

            _flipStage = 1;
        }

        _flipTurned += body.AngularVelocity * dt * _flipDirection;
        if (_flipStage == 1)
        {
            var omega = Math.Abs(body.AngularVelocity);
            var stopping = omega * omega / (2 * config.RotationAcceleration * 0.85);
            if (_flipTurned < Math.Tau - stopping - 0.05)
            {
                return new ThrustInput(Rotate: _flipDirection, LandOn: ThrustInput.GroundOnly);
            }

            _flipStage = 2;
        }

        if (_flipTurned > Math.PI && Math.Abs(body.Angle) < 0.3 && Math.Abs(body.AngularVelocity) < 1.2)
        {
            EndFlip(ctx);
            ctx.Signals.Add(GameEventKind.Flipped);
            return UpdateCruiseAfterFlip(ctx);
        }

        return new ThrustInput(Rotate: FlightGuidance.RotateToward(body, config, 0), LandOn: ThrustInput.GroundOnly);
    }

    private void EndFlip(AutopilotContext ctx)
    {
        _phase = FlightPhase.Transit;
        _flipDone = true;
        var body = ctx.Body;

        // Tras la voltereta se sigue a la altura que haya quedado (sin volver a subir).
        _cruiseY = Math.Max(_cruiseY, Math.Min(body.Position.Y, _cruiseY + body.Height * 2));
    }

    private ThrustInput UpdateCruiseAfterFlip(AutopilotContext ctx) =>
        TryResolveTarget(ctx, out var target) ? UpdateTransit(ctx, target) : Steer(ctx, Vec2.Zero);

    // Aterrizaje ---------------------------------------------------------------------------------------

    private void BeginApproach(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        State = AutopilotState.Landing;
        _stateTime = 0;
        _phase = FlightPhase.Approach;
        var drop = target.Y - body.Position.Y;
        _leaf = _style == FlightStyle.Lazy && drop > body.Height * 4 && drop < body.Height * 12
            && body.Fuel > FuelPlanner.HoverBurn(body, ctx.Physics) * (drop / (55 * body.Scale) + 6) + FuelPlanner.Reserve(body);
        _leafTime = 0;
        _finalDescent = false;
    }

    private ThrustInput UpdateLanding(AutopilotContext ctx)
    {
        var body = ctx.Body;
        if (body.IsLanded)
        {
            if (_airborneSeen)
            {
                OnArrived(ctx);
                return ThrustInput.None;
            }

            _flightTime += ctx.Dt;
            return Takeoff();
        }

        _airborneSeen = true;
        _airTime += ctx.Dt;
        _flightTime += ctx.Dt;
        if (_destination is null || !_destination.TryResolve(ctx.World, body, out var target, out _))
        {
            Replan(ctx);
            State = _purpose == FlightPurpose.ReturnToBase ? AutopilotState.ReturningToBase : AutopilotState.Flying;
            return Steer(ctx, Vec2.Zero);
        }

        // Una aproximación que no termina (algo raro en el terreno): mejor posarse en lo que haya debajo.
        if (_stateTime > 25 && _purpose != FlightPurpose.Recover)
        {
            Retarget(ctx, DestinationPicker.PickBelow(ctx.World, body, body.Position, BrakingDistance(body, ctx.Physics)), FlightPurpose.Recover);
            State = AutopilotState.Flying;
            return Steer(ctx, Vec2.Zero);
        }

        ctx.Visual.LookTarget = target + new Vec2(0, body.Height);
        if (_phase == FlightPhase.Transit && IsShortHop(ctx, target))
        {
            return UpdateHop(ctx, target);
        }

        return _phase switch
        {
            FlightPhase.Poise => UpdatePoise(ctx, target),
            FlightPhase.Drop => UpdateDrop(ctx, target),
            FlightPhase.Burn => UpdateBurn(ctx, target),
            _ => UpdateApproach(ctx, target),
        };
    }

    /// <summary>Aproximación prudente: centrarse, estabilizar en estacionario y bajar despacio.</summary>
    private ThrustInput UpdateApproach(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var scale = body.Scale;
        var height = body.Height;
        var dx = target.X - body.Position.X;
        var heightAbove = target.Y - body.Position.Y;

        // Con el despiste de altura cree que el destino está más abajo de lo que está y frena tarde.
        var perceived = heightAbove + (_mistake == PilotMistake.FastTouchdown ? height * (0.6 + 4 * _mistakeSeverity) : 0);

        // La plataforma ha subido por encima: volver a ganar altura.
        if (heightAbove < -height * 0.3)
        {
            _cruiseY = Math.Max(target.Y - height * 1.5, FlightEnvelope.HighestCenter(ctx.World, body, target, 0.3));
            _phase = FlightPhase.Transit;
            _style = _style is FlightStyle.HoverSlam or FlightStyle.Arc ? FlightStyle.Cruise : _style;
            State = _purpose == FlightPurpose.ReturnToBase ? AutopilotState.ReturningToBase : AutopilotState.Flying;
            return Steer(ctx, new Vec2(Math.Clamp(dx, -40 * scale, 40 * scale), -80 * scale));
        }

        var vxNow = Math.Abs(body.Velocity.X);
        double vx;
        double vy;
        double maxTilt;
        if (_leaf && heightAbove > height * 1.5)
        {
            // Como una hoja: balanceándose de lado a lado mientras baja.
            _leafTime += ctx.Dt;
            var amplitude = height * 1.1 * Math.Clamp((heightAbove - height * 1.5) / (height * 3), 0, 1);
            var offset = amplitude * Math.Sin(_leafTime * 2.1);
            vx = Math.Clamp((dx + offset) * 2.2, -75 * scale, 75 * scale);
            vy = 55 * scale;
            maxTilt = 0.4;
        }
        else if (Math.Abs(dx) > height * 0.9)
        {
            vx = Math.Clamp(dx * 1.2, -60 * scale, 60 * scale);
            vy = 0;
            maxTilt = 0.35;
        }
        else if (perceived > height * 1.5)
        {
            vx = Math.Clamp(dx, -40 * scale, 40 * scale);
            var hurry = _mistake == PilotMistake.FastTouchdown ? 1 + 2 * _mistakeSeverity : 1;
            vy = Math.Clamp(MathUtil.BrakingSpeed(perceived - height * 1.2, 55 * scale * hurry), 28 * scale, 160 * scale * hurry);
            maxTilt = 0.3;
        }
        else if ((Math.Abs(dx) > height * 0.15 || vxNow > 22 * scale || Math.Abs(body.Angle) > 0.12) && !(_finalDescent && _mistake == PilotMistake.Wobble))
        {
            vx = Math.Clamp(dx * 1.5, -30 * scale, 30 * scale);
            vy = heightAbove < height * 0.4 ? -25 * scale : 0;
            maxTilt = 0.22;
        }
        else
        {
            // Contacto a unos 38 px/s: suave y muy por debajo del límite de un aterrizaje perfecto (70).
            vx = Math.Clamp(dx, -12 * scale, 12 * scale);
            vy = ContactSpeed(38, scale);
            _finalDescent = true;
            maxTilt = 0.08;
            if (_mistake == PilotMistake.Wobble)
            {
                // Se tambalea: corrige de más de un lado a otro justo antes de tocar.
                vx += Math.Sin(_stateTime * 7) * 95 * _mistakeSeverity * scale;
                maxTilt += 0.55 * _mistakeSeverity;
            }
        }

        return Steer(ctx, new Vec2(vx, vy), retractLegs: false, maxTilt);
    }

    /// <summary>Quieto sobre el destino, mirando abajo, antes de apagar el motor.</summary>
    private ThrustInput UpdatePoise(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var scale = body.Scale;
        var height = body.Height;
        var dx = target.X - body.Position.X;
        if (target.Y - body.Position.Y < height * 5)
        {
            // El destino se ha acercado (la ventana ha subido): mejor una aproximación normal.
            _phase = FlightPhase.Approach;
            return UpdateApproach(ctx, target);
        }

        if (_stateTime >= _poiseDuration && Math.Abs(dx) < height * 0.3 && Math.Abs(body.Velocity.X) < 15 * scale
            && Math.Abs(body.Velocity.Y) < 25 * scale && Math.Abs(body.Angle) < 0.05)
        {
            _phase = FlightPhase.Drop;
            _stateTime = 0;
            ctx.Signals.Add(GameEventKind.EngineCut);
            return UpdateDrop(ctx, target);
        }

        var vx = Math.Clamp(dx * 1.6, -30 * scale, 30 * scale);
        var vy = Math.Clamp((_cruiseY - body.Position.Y) * 2, -50 * scale, 50 * scale);
        return Steer(ctx, new Vec2(vx, vy), retractLegs: false, maxTilt: 0.25);
    }

    /// <summary>Caída libre con el motor apagado hasta el último instante en que aún puede frenar.</summary>
    private ThrustInput UpdateDrop(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var height = body.Height;
        var remaining = target.Y - body.Position.Y;
        var required = RequiredDeceleration(body, remaining);
        var available = AvailableDeceleration(body, ctx.Physics);
        if (required >= available * IgnitionThreshold || remaining < height * 0.8 || Math.Abs(target.X - body.Position.X) > height * 1.2 || body.Fuel <= 0)
        {
            _phase = FlightPhase.Burn;
            _stateTime = 0;
            ctx.Signals.Add(GameEventKind.Ignition);
            ctx.Visual.Flash(Expression.Focused, 2);
            return UpdateBurn(ctx, target);
        }

        return new ThrustInput(Stabilize: true, LandOn: _destination!.OwnerId);
    }

    /// <summary>Frenada final: el empuje justo para llegar al destino a velocidad de contacto suave.</summary>
    private ThrustInput UpdateBurn(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var config = ctx.Physics;
        var scale = body.Scale;
        var height = body.Height;
        var gravity = config.Gravity * scale;
        var remaining = target.Y - body.Position.Y;
        var vy = body.Velocity.Y;
        var touchdown = SlamTouchdownSpeed * scale;

        if (vy < touchdown * 0.6 && remaining > height * 1.3)
        {
            // Ha frenado demasiado arriba: termina con una aproximación normal.
            _phase = FlightPhase.Approach;
            return UpdateApproach(ctx, target);
        }

        double accelerationUp;
        if (vy > touchdown)
        {
            accelerationUp = gravity + RequiredDeceleration(body, remaining) - config.LinearDrag * vy;
        }
        else
        {
            accelerationUp = gravity + (vy - touchdown) * 2.4 - config.LinearDrag * vy;
        }

        var dx = target.X - body.Position.X;
        var desiredVx = Math.Clamp(dx * 1.5, -25 * scale, 25 * scale);
        var lateral = (desiredVx - body.Velocity.X) * 2.4;
        var desiredAngle = Math.Clamp(Math.Atan2(lateral, Math.Max(accelerationUp, gravity * 0.2)), -0.12, 0.12);
        var maxThrust = config.MainThrust * scale * FuelPlanner.ThrustFactor(body, config) * Math.Max(0.3, Math.Cos(body.Angle));
        return new ThrustInput(
            Throttle: Math.Clamp(accelerationUp / maxThrust, 0, 1),
            Rotate: FlightGuidance.RotateToward(body, config, desiredAngle),
            LandOn: _destination!.OwnerId);
    }

    /// <summary>Deceleración necesaria para llegar a <paramref name="remaining"/> píxeles a velocidad de contacto.</summary>
    private static double RequiredDeceleration(LanderBody body, double remaining)
    {
        var vy = body.Velocity.Y;
        var touchdown = SlamTouchdownSpeed * body.Scale;
        if (vy <= touchdown)
        {
            return 0;
        }

        var distance = Math.Max(body.Height * 0.05, remaining - body.Height * 0.1);
        return (vy * vy - touchdown * touchdown) / (2 * distance);
    }

    /// <summary>Lo que puede frenar con el motor a tope (empuje menos gravedad).</summary>
    private static double AvailableDeceleration(LanderBody body, PhysicsConfig config) =>
        (config.MainThrust * FuelPlanner.ThrustFactor(body, config) * Math.Cos(body.Angle) - config.Gravity) * body.Scale;

    // Mandos, combustible y replanificación ----------------------------------------------------------------

    /// <summary>
    /// Pide al controlador la velocidad deseada, recortada para poder frenar siempre antes del techo y de
    /// las paredes. Solo se posa en su destino y únicamente en la fase de aterrizaje: en crucero pasa por
    /// delante de todas las ventanas, aunque el destino se le cruce.
    /// </summary>
    private ThrustInput Steer(AutopilotContext ctx, Vec2 velocity, bool retractLegs = false, double maxTilt = FlightGuidance.DefaultMaxTilt)
    {
        // Solo se acepta el destino con la nave casi recta: inclinada, mejor pasar de largo y volver.
        var landOn = State == AutopilotState.Landing && _destination is not null && (Math.Abs(ctx.Body.Angle) < 0.12 || _mistake != PilotMistake.None)
            ? _destination.OwnerId
            : ThrustInput.GroundOnly;
        // Si se ha despistado con la altura, no hace caso al aviso de suelo (es justo su error).
        var guardGround = !(_mistake == PilotMistake.FastTouchdown && State == AutopilotState.Landing);
        var limited = FlightEnvelope.Limit(ctx.World, ctx.Body, ctx.Physics, velocity, guardGround ? landOn : FlightEnvelope.NoGroundGuard);
        return FlightGuidance.TrackVelocity(ctx.Body, ctx.Physics, limited, retractLegs, maxTilt) with { LandOn = landOn };
    }

    /// <summary>
    /// Vigila el combustible en vuelo. Si ya no llega al destino, se desvía a la base o, si tampoco llega,
    /// aterriza en lo primero que tenga debajo. Devuelve true si ha cambiado el destino.
    /// </summary>
    private bool CheckFuel(AutopilotContext ctx, Vec2 target)
    {
        if (ctx.Time < _nextFuelCheck || _phase != FlightPhase.Transit)
        {
            return false;
        }

        _nextFuelCheck = ctx.Time + 0.25;
        var body = ctx.Body;
        var config = ctx.Physics;
        var hover = FuelPlanner.HoverBurn(body, config);
        var critical = FuelPlanner.ToStop(body, config) + hover * 4;
        if (body.Fuel < critical && _purpose != FlightPurpose.Recover)
        {
            EmergencyLanding(ctx);
            return true;
        }

        if (_purpose is FlightPurpose.ReturnToBase or FlightPurpose.Recover or FlightPurpose.EdgeDrop or FlightPurpose.Hop or FlightPurpose.Stroll or FlightPurpose.Dodge)
        {
            return false;
        }

        var needed = FuelPlanner.Estimate(body, config, body.Position, target) * 0.8;
        if (body.Fuel >= needed + FuelPlanner.Reserve(body) * 0.5)
        {
            return false;
        }

        var home = FuelPlanner.ToBase(ctx.World, body, config, body.Position);
        if (body.Fuel >= home + FuelPlanner.Reserve(body) * 0.25 && DestinationPicker.PickBase(ctx.World, body, random) is { } baseDestination)
        {
            ctx.Visual.ShowBubble(new Bubble(BubbleIcon.Fuel, 2));
            Retarget(ctx, baseDestination, FlightPurpose.ReturnToBase);
            State = AutopilotState.ReturningToBase;
        }
        else
        {
            EmergencyLanding(ctx);
        }

        return true;
    }

    /// <summary>Aterriza ya en lo que tenga debajo, cayendo y frenando al final para gastar lo mínimo.</summary>
    private void EmergencyLanding(AutopilotContext ctx)
    {
        var body = ctx.Body;
        ctx.Visual.ShowBubble(new Bubble(BubbleIcon.Fuel, 2));
        ctx.Visual.Flash(Expression.Scared, 1.5);
        Retarget(ctx, DestinationPicker.PickBelow(ctx.World, body, body.Position, BrakingDistance(body, ctx.Physics)), FlightPurpose.Recover);
        if (_destination is not null && _destination.TryResolve(ctx.World, body, out var center, out _)
            && center.Y - body.Position.Y > body.Height * 5)
        {
            _style = FlightStyle.HoverSlam;
            _cruiseY = body.Position.Y;
            _poiseDuration = 0.1;
        }
    }

    private void Retarget(AutopilotContext ctx, Destination? destination, FlightPurpose purpose)
    {
        var body = ctx.Body;
        _destination = destination;
        _purpose = purpose;
        _flightTime = 0;
        _phase = FlightPhase.Transit;
        _arcStarted = false;
        _flipPlanned = false;
        _leaf = false;
        _style = purpose == FlightPurpose.Recover ? FlightStyle.Direct : FlightStyle.Cruise;
        if (destination is not null && destination.TryResolve(ctx.World, body, out var center, out _))
        {
            var top = FlightEnvelope.HighestCenter(ctx.World, body, body.Position, 0.3);
            _cruiseY = Math.Max(Math.Min(body.Position.Y, center.Y) - body.Height * 0.5, top);
        }
    }

    /// <summary>Y del suelo (base) bajo un punto, o muy abajo si no hay.</summary>
    private static double GroundBelow(AutopilotContext ctx, Vec2 point)
    {
        var monitor = ctx.World.MonitorAt(point) ?? ctx.World.NearestMonitor(point);
        var ground = (monitor is null ? null : ctx.World.GroundOf(monitor.Index)) ?? ctx.World.NearestGround(point);
        return ground?.Y ?? point.Y + 10_000;
    }
}
