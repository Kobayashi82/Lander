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
        var hudRow = skins.Count + bubbleRows;
        var height = Cell * (hudRow + 1);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            for (var row = 0; row <= hudRow; row++)
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

            DrawHudSamples(dc, hudRow);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>
    /// <c>Lander.exe --preview-hero archivo.png</c>: los cinco aspectos en fila sobre un «escritorio»,
    /// para la portada del README.
    /// </summary>
    public static void SaveHero(string path)
    {
        const int width = 1200;
        const int height = 360;
        const double scale = 3;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Gfx.Vertical((0, Gfx.Rgb(0x1B2A4A)), (0.7, Gfx.Rgb(0x2C3E66)), (1, Gfx.Rgb(0x3B4F7D))), null, new Rect(0, 0, width, height));
            var stars = new Random(7);
            for (var i = 0; i < 70; i++)
            {
                var star = new Point(stars.NextDouble() * width, stars.NextDouble() * height * 0.75);
                dc.DrawEllipse(Gfx.Brush(Colors.White.WithAlpha(0.25 + stars.NextDouble() * 0.6)), null, star, 0.6 + stars.NextDouble(), 0.6 + stars.NextDouble());
            }

            // Barra de tareas y la barra de título de una ventana como plataformas.
            dc.DrawRectangle(Gfx.Brush(0x202020, 235), null, new Rect(0, height - 44, width, 44));
            var window = new Rect(470, 210, 300, 120);
            dc.DrawRoundedRectangle(Gfx.Brush(0xF3F3F3), Gfx.Pen(0x000000, 1, 40), window, 8, 8);
            dc.DrawRectangle(Gfx.Brush(0xE6E6E6), null, new Rect(window.X + 1, window.Y + 1, window.Width - 2, 30));

            var ground = height - 44;
            (string Skin, Vec2 At, LanderPose Pose, double Angle)[] landers =
            [
                ("classic", new Vec2(150, ground - 24 * scale - 70), new LanderPose { MainFlame = 0.9, Time = 0.35, Look = new Vec2(0.6, 0.2), LegsDeploy = 0.6 }, 0.18),
                ("apollo", new Vec2(355, ground - 24 * scale), new LanderPose { Look = new Vec2(-0.3, 0) }, 0),
                ("capsule", new Vec2(620, window.Y - 24 * scale), new LanderPose { Expression = Expression.Happy }, 0),
                ("saucer", new Vec2(860, ground - 24 * scale - 50), new LanderPose { MainFlame = 0.7, Time = 0.2, Look = new Vec2(-0.4, 0.5) }, -0.08),
                ("pixel", new Vec2(1060, ground - 24 * scale), new LanderPose { Night = true, Time = 0.1, Look = new Vec2(-0.8, 0) }, 0),
            ];

            foreach (var (skin, at, pose, angle) in landers)
            {
                SceneRenderer.DrawLander(dc, SkinCatalog.Get(skin), at, angle, scale, pose);
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Modo manual: indicadores, misión, puntos y marcadores.</summary>
    private static void DrawHudSamples(DrawingContext dc, int row)
    {
        var dark = row % 2 == 1;
        var top = row * Cell;
        const double scale = 1.5;

        // Lander con combustible, casco dañado, misión de cristales y puntos.
        var center = new Vec2(Cell * 0.8, top + Cell * 0.55);
        SceneRenderer.DrawLander(dc, SkinCatalog.Classic, center, 0, scale, new LanderPose { Expression = Expression.Happy, Damage = 0.45 });
        var point = new Point(center.X, center.Y);
        HudRenderer.DrawGauges(dc, point, scale, 0.65, 0.45, 0);
        var mission = new Lander.Core.Missions.Mission { Kind = Lander.Core.Missions.MissionKind.CollectCrystals, Reward = 250, Duration = 150 };
        HudRenderer.DrawMission(dc, point, scale, mission, null, dark, 0);
        HudRenderer.DrawPopup(dc, new ScorePopup("+150", center - new Vec2(0, 60), PopupStyle.Perfect) { Age = 0.4 }, scale);
        HudRenderer.DrawPopup(dc, new ScorePopup("×2", center - new Vec2(-48, 44), PopupStyle.Bonus) { Age = 0.4 }, scale);
        Label(dc, "Manual · HUD", 6, top + 4, dark);

        // Poco combustible.
        var low = new Vec2(Cell * 2.3, top + Cell * 0.55);
        SceneRenderer.DrawLander(dc, SkinCatalog.Classic, low, -0.15, scale, new LanderPose { MainFlame = 0.8, Expression = Expression.Focused, Time = 0.2 });
        HudRenderer.DrawGauges(dc, new Point(low.X, low.Y), scale, 0.12, 0, 0.1);
        Label(dc, "Poco combustible", Cell * 1.6 + 6, top + 4, dark);

        // Marcadores de misión.
        dc.PushTransform(new TranslateTransform(Cell * 3 + 20, top + 20));
        MarkerLayer.DrawCrystal(dc, 110, scale, 0.7);
        dc.Pop();
        Label(dc, "Cristal", Cell * 3 + 6, top + 4, dark);

        dc.PushTransform(new TranslateTransform(Cell * 4 + 30, top - 30));
        MarkerLayer.DrawBeacon(dc, 90, 180, new MarkerSpec(MarkerKind.Beacon, default, scale, BubbleIcon.Package), 0.6, dark);
        dc.Pop();
        Label(dc, "Baliza", Cell * 4 + 6, top + 4, dark);
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
