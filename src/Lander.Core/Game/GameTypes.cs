using Lander.Core.Geometry;

namespace Lander.Core.Game;

public enum ControlMode
{
    Auto,
    Manual,
}

public enum PilotMode
{
    /// <summary>Izquierda y derecha giran la nave; el motor empuja hacia donde apunta.</summary>
    Classic,

    /// <summary>Izquierda y derecha desplazan sin girar; la nave se mantiene recta.</summary>
    Assisted,
}

public enum ModeChangeReason
{
    User,
    Timeout,
    Fullscreen,
    Paused,
}

public enum MissionFrequency
{
    Off,
    Low,
    Normal,
    High,
}

/// <summary>Teclas del juego pulsadas en este instante.</summary>
public readonly record struct ManualControls(bool Up, bool Down, bool Left, bool Right, bool Boost)
{
    public bool Any => Up || Down || Left || Right || Boost;
}

/// <summary>Lo que el lander percibe del sistema en cada paso.</summary>
public sealed class Sensors
{
    public Vec2? Cursor { get; set; }

    /// <summary>Velocidad suavizada del cursor (px/s).</summary>
    public Vec2 CursorVelocity { get; set; }

    /// <summary>Segundos que lleva el cursor sin moverse.</summary>
    public double CursorStillSeconds { get; set; }

    /// <summary>Segundos desde la última entrada del usuario (teclado o ratón).</summary>
    public double UserIdleSeconds { get; set; }

    public bool BatteryLow { get; set; }

    public bool PowerSaver { get; set; }

    /// <summary>No molestar: el lander se queda quieto y en silencio.</summary>
    public bool QuietMode { get; set; }

    public bool DarkMode { get; set; }

    /// <summary>Monitores con una aplicación a pantalla completa.</summary>
    public IReadOnlySet<int> FullscreenMonitors { get; set; } = new HashSet<int>();

    /// <summary>Hora local (para las horas en punto). Null en los tests y simulaciones.</summary>
    public DateTime? LocalTime { get; set; }
}

/// <summary>Sucesos puntuales del sistema a los que el lander reacciona.</summary>
public abstract record SystemEvent;

public sealed record WindowOpened(long Handle) : SystemEvent;

/// <summary>El usuario ha pasado a otra ventana (primer plano).</summary>
public sealed record WindowActivated(long Handle) : SystemEvent;

public sealed record NotificationArrived : SystemEvent;

public sealed record RecycleBinEmptied(Vec2 Location) : SystemEvent;

/// <summary>Ráfaga de viento (por ejemplo, scroll rápido cerca del lander).</summary>
public sealed record WindGust(Vec2 Acceleration, double Duration) : SystemEvent;

public sealed record GameOptions
{
    public PilotMode PilotMode { get; init; } = PilotMode.Assisted;

    public double LanderSizeDip { get; init; } = 48;

    /// <summary>0 = sigue al cursor, 1 = huye de él.</summary>
    public double Shyness { get; init; } = 0.5;

    /// <summary>Multiplicador de la frecuencia de vuelos automáticos (0,5 tranquilo – 2 activo).</summary>
    public double Activity { get; init; } = 1;

    public MissionFrequency MissionFrequency { get; init; } = MissionFrequency.Normal;

    public double ManualTimeoutSeconds { get; init; } = 30;

    /// <summary>Visita el reloj de la barra de tareas a las horas en punto.</summary>
    public bool VisitClock { get; init; } = true;

    /// <summary>
    /// Torpeza del piloto automático, de 0 (nunca se equivoca) a 1: a veces calcula mal la frenada, baja
    /// demasiado deprisa o llega torcido, y puede acabar en un aterrizaje duro o en un accidente.
    /// </summary>
    public double PilotErrors { get; init; }
}
