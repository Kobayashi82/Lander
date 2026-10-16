using Lander.Core;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Terrain;

namespace Lander.App.Engine;

/// <summary>
/// Piloto de demostración (<c>--demo-manual</c>): «pulsa» las teclas del modo manual como lo haría una
/// persona, saltando de plataforma en plataforma. Sirve para probar y grabar el modo manual sin tocar el
/// teclado de verdad.
/// </summary>
internal sealed class DemoPilot
{
    private readonly IRandom _random = new GameRandom();
    private long _targetOwner;
    private double _targetLocalX;
    private double _restUntil;

    public ManualControls Next(LanderGame game, World world)
    {
        var body = game.Body;
        if (body.IsLanded)
        {
            if (_targetOwner == 0 || body.Support?.OwnerId == _targetOwner)
            {
                if (_restUntil == 0)
                {
                    _restUntil = game.Time + 2;
                }

                if (game.Time < _restUntil)
                {
                    return default;
                }

                PickTarget(game, world);
                _restUntil = 0;
            }

            return new ManualControls(Up: true, Down: false, Left: false, Right: false, Boost: false);
        }

        if (!world.TryGetOwnerOrigin(_targetOwner, out var origin)
            || world.FindSegment(_targetOwner, origin.X + _targetLocalX) is not { } platform)
        {
            // Destino perdido: mantenerse en el aire mientras se elige otro.
            PickTarget(game, world);
            return new ManualControls(Up: body.Velocity.Y > -20 * body.Scale, Down: false, Left: false, Right: false, Boost: false);
        }

        var scale = body.Scale;
        var target = new Vec2(origin.X + _targetLocalX, platform.Y - body.HalfHeight);
        var dx = target.X - body.Position.X;
        var above = target.Y - body.Position.Y;
        var aligned = Math.Abs(dx) < body.Height * 0.4;
        var top = world.MonitorAt(target)?.Bounds.Top ?? double.MinValue;
        var cruiseHeight = Math.Min(body.Height * 1.5, target.Y - top - body.Height * 0.8);

        // Primero ganar altura por encima del destino, luego desplazarse y por último bajar suave.
        double desiredVy;
        if (above < cruiseHeight && !aligned)
        {
            desiredVy = -120 * scale;
        }
        else if (!aligned)
        {
            desiredVy = 0;
        }
        else
        {
            desiredVy = above > body.Height * 2 ? 90 * scale : 30 * scale;
        }

        var desiredVx = Math.Clamp(dx * 1.4, -160 * scale, 160 * scale);
        var vx = body.Velocity.X;
        return new ManualControls(
            Up: body.Velocity.Y > desiredVy,
            Down: false,
            Left: vx > desiredVx + 12 * scale,
            Right: vx < desiredVx - 12 * scale,
            Boost: false);
    }

    private void PickTarget(LanderGame game, World world)
    {
        var body = game.Body;
        var monitor = world.MonitorAt(body.Position)?.Index ?? -1;
        var margin = body.Height;
        var options = world.Platforms
            .Where(p => p.MonitorIndex == monitor && p.Width > margin * 3)
            .ToList();
        var elsewhere = options.Where(p => p.OwnerId != body.Support?.OwnerId).ToList();
        if (elsewhere.Count > 0)
        {
            options = elsewhere;
        }

        if (options.Count == 0)
        {
            return;
        }

        // Un punto lejos de donde está, para que el salto merezca la pena.
        var platform = _random.Pick(options);
        var x = _random.Range(platform.Left + margin, platform.Right - margin);
        if (Math.Abs(x - body.Position.X) < body.Height * 4)
        {
            x = body.Position.X < platform.CenterX ? platform.Right - margin : platform.Left + margin;
        }

        _targetOwner = platform.OwnerId;
        _targetLocalX = x - platform.OwnerOrigin.X;
    }
}
