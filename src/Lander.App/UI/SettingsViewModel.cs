using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Lander.App.Engine;
using Lander.App.Rendering;
using Lander.App.Services;
using Lander.Core.Game;
using Lander.Core.Progression;
using Lander.Core.Settings;

namespace Lander.App.UI;

/// <summary>Base mínima para notificar cambios a los enlaces de datos.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}

/// <summary>Un aspecto o una estela en la galería.</summary>
public sealed class UnlockOption(Unlock unlock, ImageSource? preview, Brush? swatch) : ObservableObject
{
    private bool _isUnlocked;

    public string Id => unlock.Id;

    public string Name => unlock.Name;

    public string Description => unlock.Description;

    public int Level => unlock.Level;

    public ImageSource? Preview => preview;

    public Brush? Swatch => swatch;

    public bool IsUnlocked
    {
        get => _isUnlocked;
        set
        {
            if (_isUnlocked != value)
            {
                _isUnlocked = value;
                Raise();
                Raise(nameof(LockText));
            }
        }
    }

    public string LockText => IsUnlocked ? string.Empty : $"Nivel {Level}";
}

/// <summary>Una fila de la lista de desbloqueos.</summary>
public sealed record UnlockRow(string Name, string Description, string LevelText, string KindText, bool IsUnlocked);

/// <summary>Una cifra de las estadísticas del piloto.</summary>
public sealed record StatTile(string Value, string Label);

/// <summary>Un logro en la lista: conseguido o con su progreso.</summary>
public sealed record AchievementRow(string Name, string Description, string ProgressText, double Progress, bool IsEarned);

public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _service;
    private readonly App _app;
    private EngineStatus _status;

    internal SettingsViewModel(SettingsService service, App app)
    {
        _service = service;
        _app = app;
        _status = app.Status;

        foreach (var unlock in Unlocks.OfKind(UnlockKind.Skin))
        {
            Skins.Add(new UnlockOption(unlock, IconFactory.RenderLander(72, unlock.Id, look: new Core.Geometry.Vec2(0.4, 0)), null));
        }

        foreach (var unlock in Unlocks.OfKind(UnlockKind.Trail))
        {
            Brush swatch = unlock.Id == "rainbow" ? RainbowBrush() : Gfx.Brush(Rendering.Trails.Swatch(unlock.Id));
            Trails.Add(new UnlockOption(unlock, null, swatch));
        }

        AppIcon = IconFactory.RenderLander(64, look: new Core.Geometry.Vec2(0.4, 0));
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
        OpenRepositoryCommand = new RelayCommand(() => Open("https://github.com/Kobayashi82/Lander"));
        ResetProgressCommand = new RelayCommand(ResetProgress);
        OfferMissionCommand = new RelayCommand(() => _app.Engine?.OfferMission());
        StartRaceCommand = new RelayCommand(() => _app.Engine?.StartRace());
        ToggleControlCommand = new RelayCommand(() => _app.Engine?.ToggleManual());

        _service.Changed += _ => RaiseAll();
        _service.ProgressChanged += _ => RefreshProgress();
        RefreshProgress();
    }

    private LanderSettings S => _service.Current;

    public ImageSource AppIcon { get; }

    public ObservableCollection<UnlockOption> Skins { get; } = [];

    public ObservableCollection<UnlockOption> Trails { get; } = [];

    public ObservableCollection<StatTile> Stats { get; } = [];

    public ObservableCollection<UnlockRow> UnlockRows { get; } = [];

    public ObservableCollection<AchievementRow> AchievementRows { get; } = [];

    public string AchievementSummary { get; private set; } = string.Empty;

    public ICommand OpenDataFolderCommand { get; }

    public ICommand OpenRepositoryCommand { get; }

    public ICommand ResetProgressCommand { get; }

    public ICommand OfferMissionCommand { get; }

    public ICommand StartRaceCommand { get; }

    public ICommand ToggleControlCommand { get; }

    // General -------------------------------------------------------------------------------------

    public bool StartWithWindows
    {
        get => S.StartWithWindows;
        set => _service.Update(s => s.StartWithWindows = value);
    }

    public bool Paused
    {
        get => S.Paused;
        set => _service.Update(s => s.Paused = value);
    }

    public HotkeySetting Hotkey
    {
        get => S.ToggleHotkey;
        set => _service.Update(s => s.ToggleHotkey = value);
    }

    public string HotkeyStatus => _status.HotkeyAvailable
        ? $"Pulsa {HotkeyText.Format(S.ToggleHotkey)} en cualquier momento para pilotar."
        : "Otra aplicación ya usa esta combinación: elige otra.";

    public bool HotkeyUnavailable => !_status.HotkeyAvailable;

    public double ManualTimeout
    {
        get => S.ManualTimeoutSeconds;
        set => _service.Update(s => s.ManualTimeoutSeconds = (int)Math.Round(value));
    }

    public string ManualTimeoutText => $"{S.ManualTimeoutSeconds} s";

    public string ControlButtonText => _status.Mode == ControlMode.Manual ? "Devolver el control" : "Tomar el control";

    // Lander --------------------------------------------------------------------------------------

    public double LanderSize
    {
        get => S.LanderSize;
        set => _service.Update(s => s.LanderSize = Math.Round(value));
    }

    public string LanderSizeText => $"{S.LanderSize:0} px";

    public UnlockOption? SelectedSkin
    {
        get => Skins.FirstOrDefault(o => o.Id == S.Skin);
        set
        {
            if (value is { IsUnlocked: true })
            {
                _service.Update(s => s.Skin = value.Id);
            }
            else
            {
                Raise();
            }
        }
    }

    public UnlockOption? SelectedTrail
    {
        get => Trails.FirstOrDefault(o => o.Id == S.Trail);
        set
        {
            if (value is { IsUnlocked: true })
            {
                _service.Update(s => s.Trail = value.Id);
            }
            else
            {
                Raise();
            }
        }
    }

    // Comportamiento ------------------------------------------------------------------------------

    public double Activity
    {
        get => S.Activity;
        set => _service.Update(s => s.Activity = Math.Round(value, 2));
    }

    public double Shyness
    {
        get => S.Shyness;
        set => _service.Update(s => s.Shyness = Math.Round(value, 2));
    }

    public bool ReactToNotifications
    {
        get => S.ReactToNotifications;
        set => _service.Update(s => s.ReactToNotifications = value);
    }

    public bool AllowDragging
    {
        get => S.AllowDragging;
        set => _service.Update(s => s.AllowDragging = value);
    }

    public int MissionFrequency
    {
        get => (int)S.MissionFrequency;
        set => _service.Update(s => s.MissionFrequency = (MissionFrequency)value);
    }

    /// <summary>Niveles de despiste del piloto automático que ofrece la lista (ninguno, pocos, normales, muchos).</summary>
    private static readonly double[] PilotErrorLevels = [0, 0.25, 0.5, 1];

    public int PilotErrors
    {
        get
        {
            var value = S.PilotErrors;
            return Array.IndexOf(PilotErrorLevels, PilotErrorLevels.MinBy(level => Math.Abs(level - value)));
        }
        set => _service.Update(s => s.PilotErrors = PilotErrorLevels[Math.Clamp(value, 0, PilotErrorLevels.Length - 1)]);
    }

    private static readonly double[] AcrobaticsLevels = [0, 0.25, 0.5, 1];

    public int Acrobatics
    {
        get
        {
            var value = S.Acrobatics;
            return Array.IndexOf(AcrobaticsLevels, AcrobaticsLevels.MinBy(level => Math.Abs(level - value)));
        }
        set => _service.Update(s => s.Acrobatics = AcrobaticsLevels[Math.Clamp(value, 0, AcrobaticsLevels.Length - 1)]);
    }

    public bool VisitClock
    {
        get => S.VisitClock;
        set => _service.Update(s => s.VisitClock = value);
    }

    public bool LandOnContent
    {
        get => S.LandOnContent;
        set => _service.Update(s => s.LandOnContent = value);
    }

    /// <summary>Aplicaciones excluidas, separadas por comas («notepad, slack»).</summary>
    public string ContentExcludedApps
    {
        get => string.Join(", ", S.ContentExcludedApps);
        set => _service.Update(s => s.ContentExcludedApps = [.. (value ?? string.Empty).Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]);
    }

    // Sonido --------------------------------------------------------------------------------------

    public bool SoundEnabled
    {
        get => S.SoundEnabled;
        set => _service.Update(s => s.SoundEnabled = value);
    }

    public double Volume
    {
        get => S.Volume * 100;
        set => _service.Update(s => s.Volume = Math.Round(value) / 100);
    }

    public string VolumeText => $"{S.Volume * 100:0} %";

    // Piloto ----------------------------------------------------------------------------------------

    public int Level => _service.Progress.Level;

    public string LevelSummary => $"Piloto de nivel {Level}";

    public string PointsText => $"{_service.Progress.TotalPoints.ToString("N0", CultureInfo.CurrentCulture)} puntos";

    public double LevelProgress => Levels.ProgressToNext(_service.Progress.TotalPoints) * 100;

    public string NextLevelText => Level >= Levels.MaxLevel
        ? "Nivel máximo alcanzado"
        : $"{(Levels.PointsForLevel(Level + 1) - _service.Progress.TotalPoints).ToString("N0", CultureInfo.CurrentCulture)} puntos para el nivel {Level + 1}";

    // Acerca de -------------------------------------------------------------------------------------

    public string VersionText { get; } = $"Versión {typeof(App).Assembly.GetName().Version?.ToString(3)}";

    public string DataFolder { get; } = AppPaths.RoamingData;

    internal void RefreshStatus(EngineStatus status)
    {
        _status = status;
        Raise(nameof(HotkeyStatus));
        Raise(nameof(HotkeyUnavailable));
        Raise(nameof(ControlButtonText));
    }

    private void RefreshProgress()
    {
        var progress = _service.Progress;
        foreach (var option in Skins.Concat(Trails))
        {
            option.IsUnlocked = option.Level <= progress.Level;
        }

        Stats.Clear();
        Stats.Add(new StatTile(FormatDuration(progress.FlightSeconds), "Tiempo de vuelo"));
        Stats.Add(new StatTile(FormatDuration(progress.ManualFlightSeconds), "Pilotando tú"));
        Stats.Add(new StatTile(progress.PerfectLandings.ToString("N0", CultureInfo.CurrentCulture), "Aterrizajes perfectos"));
        Stats.Add(new StatTile(progress.HardLandings.ToString("N0", CultureInfo.CurrentCulture), "Aterrizajes duros"));
        Stats.Add(new StatTile(progress.Crashes.ToString("N0", CultureInfo.CurrentCulture), "Accidentes"));
        Stats.Add(new StatTile(progress.BestStreak.ToString("N0", CultureInfo.CurrentCulture), "Mejor racha"));
        Stats.Add(new StatTile(progress.MissionsCompleted.ToString("N0", CultureInfo.CurrentCulture), "Misiones completadas"));
        Stats.Add(new StatTile(progress.AutoLandings.ToString("N0", CultureInfo.CurrentCulture), "Aterrizajes en automático"));
        Stats.Add(new StatTile(progress.CrystalsCollected.ToString("N0", CultureInfo.CurrentCulture), "Cristales recogidos"));
        Stats.Add(new StatTile(progress.RacesCompleted.ToString("N0", CultureInfo.CurrentCulture), "Carreras terminadas"));
        Stats.Add(new StatTile(progress.GoldMedals.ToString("N0", CultureInfo.CurrentCulture), "Medallas de oro"));

        AchievementRows.Clear();
        var earned = 0;
        foreach (var achievement in Achievements.All)
        {
            var isEarned = achievement.IsEarned(progress);
            earned += isEarned ? 1 : 0;
            var current = Math.Min(achievement.Value(progress), achievement.Target);
            var progressText = isEarned
                ? "Conseguido"
                : achievement.Id == "flight-hour"
                    ? $"{FormatDuration(current)} de 1 h"
                    : $"{Math.Floor(current).ToString("N0", CultureInfo.CurrentCulture)} de {achievement.Target.ToString("N0", CultureInfo.CurrentCulture)}";
            AchievementRows.Add(new AchievementRow(achievement.Name, achievement.Description, progressText, achievement.ProgressOf(progress) * 100, isEarned));
        }

        Stats.Add(new StatTile($"{earned} de {Achievements.All.Count}", "Logros"));
        AchievementSummary = $"{earned} de {Achievements.All.Count} conseguidos";
        Raise(nameof(AchievementSummary));

        UnlockRows.Clear();
        foreach (var unlock in Unlocks.All.Where(u => u.Level > 1))
        {
            var kind = unlock.Kind switch
            {
                UnlockKind.Skin => "Aspecto",
                UnlockKind.Trail => "Estela",
                _ => "Mejora",
            };
            UnlockRows.Add(new UnlockRow(unlock.Name, unlock.Description, $"Nivel {unlock.Level}", kind, unlock.Level <= progress.Level));
        }

        Raise(nameof(Level));
        Raise(nameof(LevelSummary));
        Raise(nameof(PointsText));
        Raise(nameof(LevelProgress));
        Raise(nameof(NextLevelText));
        Raise(nameof(SelectedSkin));
        Raise(nameof(SelectedTrail));
    }

    private void ResetProgress()
    {
        var answer = MessageBox.Show(
            "Se borrarán los puntos, el nivel, las estadísticas y lo desbloqueado. ¿Continuar?",
            "Reiniciar progreso",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _service.Update(s =>
        {
            s.Skin = Unlocks.DefaultSkin;
            s.Trail = Unlocks.DefaultTrail;
        });
        _app.Engine?.ResetProgress();
        _service.SetProgress(new PilotProgress());
    }

    private void OpenDataFolder()
    {
        Directory.CreateDirectory(AppPaths.RoamingData);
        Open(AppPaths.RoamingData);
    }

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"No se pudo abrir {target}.", ex);
        }
    }

    private static string FormatDuration(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours} h {time.Minutes} min" : $"{time.Minutes} min {time.Seconds} s";
    }

    private static LinearGradientBrush RainbowBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x5C, 0x8A), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xD2, 0x3F), 0.35));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x3D, 0xDC, 0x97), 0.65));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x4C, 0xC9, 0xF0), 1));
        brush.Freeze();
        return brush;
    }
}
