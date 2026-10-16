using Lander.Core.Game;
using Lander.Core.Geometry;

namespace Lander.App.Rendering;

/// <summary>Todo lo que un aspecto necesita para dibujar el lander en un instante.</summary>
internal readonly record struct LanderPose
{
    public double LegsDeploy { get; init; } = 1;

    public Expression Expression { get; init; }

    /// <summary>Mirada, cada componente entre -1 y 1.</summary>
    public Vec2 Look { get; init; }

    /// <summary>Párpado: 0 abierto, 1 cerrado (parpadeo).</summary>
    public double Blink { get; init; }

    public bool Night { get; init; }

    public double Time { get; init; }

    public double Damage { get; init; }

    public double MainFlame { get; init; }

    public double LeftJet { get; init; }

    public double RightJet { get; init; }

    public double BoostFlash { get; init; }

    public bool CarryingPackage { get; init; }

    public LanderPose()
    {
    }
}
