using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lander.App.Rendering;
using Lander.App.Rendering.Skins;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Expression = Lander.Core.Game.Expression;

namespace Lander.App.UI;

/// <summary>
/// Herramienta de desarrollo (<c>Lander.exe --preview archivo.png</c>): dibuja todos los aspectos con
/// varias poses y todos los bocadillos en una sola imagen, para revisar el arte sin volar.
/// </summary>
internal static class PreviewSheet
{
    private const int Cell = 150;
    private const double Scale = 2;

    private static readonly (string Label, LanderPose Pose, double Angle)[] Poses =
    [
        ("Reposo", new LanderPose { Look = new Vec2(0.4, 0) }, 0),
        ("Motor", new LanderPose { MainFlame = 1, Time = 0.3, Expression = Expression.Focused }, -0.25),
        ("Contento", new LanderPose { Expression = Expression.Happy }, 0),
        ("Asustado", new LanderPose { Expression = Expression.Scared, Look = new Vec2(-0.6, -0.3) }, 0.12),
        ("Dormido", new LanderPose { Expression = Expression.Asleep }, 0),
        ("Noche", new LanderPose { Night = true, Time = 0.1, Look = new Vec2(-0.5, 0.4) }, 0),
        ("Patas arriba", new LanderPose { LegsDeploy = 0, LeftJet = 1, MainFlame = 0.4, Damage = 0.8, CarryingPackage = true }, 0.2),
    ];

    public static void Save(string path)
    {
        var skins = SkinCatalog.Ids.ToList();
        var icons = Enum.GetValues<BubbleIcon>();
        var width = Cell * Poses.Length;
        var bubbleRows = (icons.Length + Poses.Length - 1) / Poses.Length;
        var height = Cell * (skins.Count + bubbleRows);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            for (var row = 0; row < skins.Count + bubbleRows; row++)
            {
                var dark = row % 2 == 1;
                dc.DrawRectangle(Gfx.Brush(dark ? 0x202020U : 0xF3F3F3U), null, new Rect(0, row * Cell, width, Cell));
            }

            for (var row = 0; row < skins.Count; row++)
            {
                var skin = SkinCatalog.Get(skins[row]);
                for (var column = 0; column < Poses.Length; column++)
                {
                    var (label, pose, angle) = Poses[column];
                    var center = new Vec2(column * Cell + Cell / 2.0, row * Cell + Cell / 2.0 + 4);
                    SceneRenderer.DrawLander(dc, skin, center, angle, Scale, pose);
                    Label(dc, $"{skin.Id} · {label}", column * Cell + 6, row * Cell + 4, row % 2 == 1);
                }
            }

            var bubbleRow = skins.Count;
            for (var i = 0; i < icons.Length; i++)
            {
                var column = i % Poses.Length;
                var row = bubbleRow + i / Poses.Length;
                var dark = row % 2 == 1;
                var anchor = new Point(column * Cell + Cell / 2.0, row * Cell + Cell * 0.78);
                BubbleRenderer.Draw(dc, new Bubble(icons[i], 10, -0.6), 1, anchor, Scale, dark, null, 0.4);
                Label(dc, icons[i].ToString(), column * Cell + 6, row * Cell + 4, dark);
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>
    /// <c>Lander.exe --preview-settings carpeta</c>: abre los ajustes fuera de la pantalla, sin activarlos,
    /// y guarda cada página como imagen.
    /// </summary>
    public static void SaveSettingsPages(string folder, Services.SettingsService settings, App app)
    {
        Directory.CreateDirectory(folder);
        var window = new SettingsWindow(settings, app)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        window.Show();

        var content = (FrameworkElement)window.Content;
        var background = window.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush ?? Brushes.White;
        for (var page = 0; page < window.Navigation.Items.Count; page++)
        {
            window.Navigation.SelectedIndex = page;
            window.UpdateLayout();
            var width = (int)Math.Ceiling(content.ActualWidth);
            var height = (int)Math.Ceiling(content.ActualHeight);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(background, null, new Rect(0, 0, width, height));
                dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, width, height));
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(folder, $"ajustes-{page + 1}.png"));
            encoder.Save(stream);
        }

        window.Close();
    }

    private static void Label(DrawingContext dc, string text, double x, double y, bool dark)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, Gfx.Brush(dark ? 0xBBBBBBU : 0x555555U), 1);
        dc.DrawText(formatted, new Point(x, y));
    }
}
