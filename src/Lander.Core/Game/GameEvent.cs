using Lander.Core.Geometry;
using Lander.Core.Missions;
using Lander.Core.Physics;
using Lander.Core.Progression;
using Lander.Core.Racing;
using Lander.Core.Terrain;

namespace Lander.Core.Game;

public enum GameEventKind
{
    TookOff,
    Touchdown,
    Crashed,
    Respawned,
    SupportLost,
    Launched,
    Toppled,
    Slipped,
    HitCeiling,
    HitWall,
    Boosted,
    LegsDeployed,
    LegsRetracted,
    FuelEmpty,
    ModeChanged,
    Startled,
    Celebrated,
    LevelUp,
    MissionOffered,
    MissionStarted,
    MissionCompleted,
    MissionExpired,
    CrystalCollected,
    ProgressChanged,
    RaceStarted,
    RaceCheckpoint,
    RaceFinished,
    RaceFailed,
    AchievementUnlocked,
}

/// <summary>Algo que ha pasado en la simulación y que la capa de presentación puede querer mostrar o sonar.</summary>
public sealed record GameEvent(GameEventKind Kind, Vec2 Position)
{
    public double Magnitude { get; init; }

    public Platform? Platform { get; init; }

    public LandingResult? Landing { get; init; }

    public bool Manual { get; init; }

    public int Points { get; init; }

    public int Streak { get; init; }

    public int Level { get; init; }

    public IReadOnlyList<Unlock> Unlocks { get; init; } = [];

    public ControlMode Mode { get; init; }

    public ModeChangeReason Reason { get; init; }

    public Mission? Mission { get; init; }

    public Race? Race { get; init; }

    public Achievement? Achievement { get; init; }
}
