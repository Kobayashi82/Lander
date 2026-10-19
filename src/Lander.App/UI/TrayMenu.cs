using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Lander.App.Engine;
using Lander.App.Interop;
using Lander.App.Rendering;
using Lander.App.Services;
using Lander.Core.Game;
using Lander.Core.Progression;
using Lander.Core.Racing;

namespace Lander.App.UI;

/// <summary>Menú de la bandeja y del clic derecho sobre el lander (tema Fluent de WPF).</summary>
internal sealed class TrayMenu(SettingsService settings, Func<EngineStatus> status, nint ownerWindow)
{
    private ContextMenu? _menu;

    public event Action? ToggleManualRequested;

    public event Action? MissionRequested;

    /// <summary>Empezar una carrera; el argumento indica si se repite el último circuito.</summary>
    public event Action<bool>? RaceRequested;

    public event Action? AbandonRaceRequested;

    public event Action? SettingsRequested;

    public event Action? ExitRequested;

    public void Show()
    {
        if (_menu is { IsOpen: true })
        {
            _menu.IsOpen = false;
            return;
        }

        _menu = Build();

        // Sin esto, el menú no se cierra al hacer clic fuera (la app no tiene ventana en primer plano).
        Native.SetForegroundWindow(ownerWindow);
        _menu.Placement = PlacementMode.MousePoint;
        _menu.IsOpen = true;
    }

    private ContextMenu Build()
    {
        var current = settings.Current;
        var engine = status();
        var menu = new ContextMenu { MinWidth = 240 };

        menu.Items.Add(new MenuItem
        {
            Header = $"Lander · Piloto nivel {engine.Level}",
            FontWeight = FontWeights.SemiBold,
            IsHitTestVisible = false,
            Focusable = false,
        });
        menu.Items.Add(new Separator());

        menu.Items.Add(Item(current.Paused ? "Reanudar" : "Pausar", () => settings.Update(s => s.Paused = !s.Paused)));

        var manual = engine.Mode == ControlMode.Manual;
        var toggle = Item(manual ? "Devolver el control" : "Tomar el control", () => ToggleManualRequested?.Invoke());
        toggle.InputGestureText = HotkeyText.Format(current.ToggleHotkey);
        menu.Items.Add(toggle);
        menu.Items.Add(Item("Proponer una misión", () => MissionRequested?.Invoke()));
        if (engine.Racing)
        {
            menu.Items.Add(Item("Abandonar la carrera", () => AbandonRaceRequested?.Invoke()));
        }
        else
        {
            menu.Items.Add(Item("Empezar una carrera", () => RaceRequested?.Invoke(false)));
            if (engine.CanRepeatRace)
            {
                var record = engine.RaceRecord is { } seconds ? $" (récord {Race.FormatTime(seconds)} s)" : string.Empty;
                menu.Items.Add(Item("Repetir el circuito" + record, () => RaceRequested?.Invoke(true)));
            }
        }

        menu.Items.Add(new Separator());

        var mistakes = new MenuItem { Header = "Despistes del piloto" };
        foreach (var (label, level) in new[] { ("Ninguno", 0.0), ("Pocos", 0.25), ("Normales", 0.5), ("Muchos", 1.0) })
        {
            mistakes.Items.Add(Check(label, Math.Abs(current.PilotErrors - level) < 0.01, () => settings.Update(s => s.PilotErrors = level)));
        }

        menu.Items.Add(mistakes);

        var skins = new MenuItem { Header = "Aspecto" };
        foreach (var unlock in Unlocks.OfKind(UnlockKind.Skin))
        {
            skins.Items.Add(UnlockItem(unlock, engine.Level, current.Skin == unlock.Id, () => settings.Update(s => s.Skin = unlock.Id)));
        }

        menu.Items.Add(skins);

        var trails = new MenuItem { Header = "Estela" };
        foreach (var unlock in Unlocks.OfKind(UnlockKind.Trail))
        {
            var item = UnlockItem(unlock, engine.Level, current.Trail == unlock.Id, () => settings.Update(s => s.Trail = unlock.Id));
            item.Icon = new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(Trails.Swatch(unlock.Id)) };
            trails.Items.Add(item);
        }

        menu.Items.Add(trails);
        menu.Items.Add(Check("Sonido", current.SoundEnabled, () => settings.Update(s => s.SoundEnabled = !s.SoundEnabled)));
        menu.Items.Add(new Separator());

        menu.Items.Add(Item("Ajustes…", () => SettingsRequested?.Invoke()));
        menu.Items.Add(Item("Salir", () => ExitRequested?.Invoke()));
        return menu;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem Check(string header, bool isChecked, Action action)
    {
        var item = Item(header, action);
        item.IsCheckable = true;
        item.IsChecked = isChecked;
        return item;
    }

    private static MenuItem UnlockItem(Unlock unlock, int level, bool selected, Action action)
    {
        var unlocked = unlock.Level <= level;
        var item = Check(unlocked ? unlock.Name : $"{unlock.Name} (nivel {unlock.Level})", selected, action);
        item.IsEnabled = unlocked;
        item.ToolTip = unlock.Description;
        return item;
    }
}
