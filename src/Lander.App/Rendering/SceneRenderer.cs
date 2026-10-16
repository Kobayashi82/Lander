using System.Windows;
using System.Windows.Media;
using Lander.App.Rendering.Skins;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Missions;

namespace Lander.App.Rendering;

/// <summary>Lo que hay que pintar en la ventana del lander en un fotograma.</summary>
internal sealed class LanderScene
{
    public Vec2 Position { get; set; }

    public double Angle { get; set; }

    public double Scale { get; set; } = 1;

    public bool Visible { get; set; } = true;

    public double Opacity { get; set; } = 1;

    public LanderPose Pose { get; set; }

    public LanderSkin Skin { get; set; } = SkinCatalog.Classic;

    public Bubble? Bubble { get; set; }

    public double BubbleAge { get; set; }

    public ImageSource? BubbleIcon { get; set; }

    public bool ShowHud { get; set; }

    public double Fuel { get; set; }

    public double Damage { get; set; }

    public Mission? Mission { get; set; }

    public ImageSource? MissionIcon { get; set; }

    public IReadOnlyList<ScorePopup> Popups { get; set; } = [];

    public bool Dark { get; set; }

    public double Time { get; set; }
}

internal static class SceneRenderer
{
    /// <summary>Dibuja la escena; <paramref name="window"/> es el rectángulo de la ventana en el mundo.</summary>
    public static void Draw(DrawingContext dc, LanderScene scene, ParticleSystem particles, Rect window)
    {
        dc.PushTransform(new TranslateTransform(-window.X, -window.Y));

        particles.Draw(dc, window, front: false);

        if (scene.Visible && scene.Opacity > 0.01)
        {
            DrawLander(dc, scene.Skin, scene.Position, scene.Angle, scene.Scale, scene.Pose, scene.Opacity);
        }

        particles.Draw(dc, window, front: true);

        var center = new Point(scene.Position.X, scene.Position.Y);
        if (scene.Visible && scene.ShowHud)
        {
            HudRenderer.DrawGauges(dc, center, scene.Scale, scene.Fuel, scene.Damage, scene.Time);
            if (scene.Mission is { Status: MissionStatus.Active } mission)
            {
                HudRenderer.DrawMission(dc, center, scene.Scale, mission, scene.MissionIcon, scene.Dark, scene.Time);
            }
        }

        if (scene.Visible && scene.Bubble is { } bubble)
        {
            var top = new Point(center.X, center.Y - 24 * scene.Scale);
            BubbleRenderer.Draw(dc, bubble, scene.BubbleAge, top, scene.Scale, scene.Dark, scene.BubbleIcon, scene.Time);
        }

        foreach (var popup in scene.Popups)
        {
            HudRenderer.DrawPopup(dc, popup, scene.Scale);
        }

        dc.Pop();
    }

    /// <summary>Dibuja el lander con su posición, giro y escala.</summary>
    public static void DrawLander(DrawingContext dc, LanderSkin skin, Vec2 position, double angle, double scale, in LanderPose pose, double opacity = 1)
    {
        var matrix = Matrix.Identity;
        matrix.Scale(scale, scale);
        matrix.Rotate(angle * 180 / Math.PI);
        matrix.Translate(position.X, position.Y);
        dc.PushTransform(new MatrixTransform(matrix));
        var faded = opacity < 0.99;
        if (faded)
        {
            dc.PushOpacity(opacity);
        }

        skin.Draw(dc, pose);

        if (faded)
        {
            dc.Pop();
        }

        dc.Pop();
    }
}
