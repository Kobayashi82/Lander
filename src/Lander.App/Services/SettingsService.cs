using System.IO;
using System.Windows.Threading;
using Lander.Core.Progression;
using Lander.Core.Settings;

namespace Lander.App.Services;

/// <summary>
/// Ajustes y progreso del usuario (hilo de la interfaz). Cada cambio se avisa al momento y se guarda en
/// disco poco después, agrupando los cambios seguidos (por ejemplo, al arrastrar un deslizador).
/// </summary>
internal sealed class SettingsService
{
    private readonly JsonStore<LanderSettings> _settingsStore = new(AppPaths.SettingsFile, LanderJsonContext.Default.LanderSettings);
    private readonly JsonStore<PilotProgress> _progressStore = new(AppPaths.ProgressFile, LanderJsonContext.Default.PilotProgress);
    private readonly DispatcherTimer _saveTimer;
    private bool _settingsDirty;
    private bool _progressDirty;

    private SettingsService()
    {
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => Flush();
    }

    public LanderSettings Current { get; private set; } = new();

    public PilotProgress Progress { get; private set; } = new();

    /// <summary>Los ajustes han cambiado (copia nueva e independiente).</summary>
    public event Action<LanderSettings>? Changed;

    public event Action<PilotProgress>? ProgressChanged;

    public static SettingsService Load()
    {
        var service = new SettingsService();
        service.Current = (service._settingsStore.Load() ?? new LanderSettings()).Normalize();
        service.Progress = service._progressStore.Load() ?? new PilotProgress();
        return service;
    }

    public void Update(Action<LanderSettings> change)
    {
        var next = Current.Clone();
        change(next);
        next.Normalize();
        Current = next;
        _settingsDirty = true;
        ScheduleSave();
        Changed?.Invoke(next.Clone());
    }

    public void SetProgress(PilotProgress snapshot)
    {
        Progress = snapshot;
        _progressDirty = true;
        ScheduleSave();
        ProgressChanged?.Invoke(snapshot);
    }

    public void Flush()
    {
        _saveTimer.Stop();
        try
        {
            if (_settingsDirty)
            {
                _settingsStore.Save(Current);
                _settingsDirty = false;
            }

            if (_progressDirty)
            {
                _progressStore.Save(Progress);
                _progressDirty = false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("No se pudieron guardar los ajustes.", ex);
        }
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
