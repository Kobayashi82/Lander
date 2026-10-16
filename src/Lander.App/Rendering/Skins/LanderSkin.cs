using System.Windows;
using System.Windows.Media;
using Lander.Core.Progression;

namespace Lander.App.Rendering.Skins;

/// <summary>
/// Aspecto del lander. Se dibuja en unidades base: 48 de alto, origen en el centro de masas, y hacia
/// abajo, las patas apoyan en y = 24.
/// </summary>
internal abstract class LanderSkin
{
    public abstract string Id { get; }

    /// <summary>Boca del motor principal (de ahí sale el humo).</summary>
    public virtual Point Nozzle => new(0, 13);

    public virtual Point LeftJetPoint => new(-15.5, -12.8);

    public virtual Point RightJetPoint => new(15.5, -12.8);

    /// <summary>Colores de los trozos al estallar.</summary>
    public virtual Color[] DebrisColors { get; } = [Gfx.Rgb(0xE9AE45), Gfx.Rgb(0xE8ECF1), Gfx.Rgb(0x6B7280)];

    public void Draw(DrawingContext dc, in LanderPose pose)
    {
        DrawExhaust(dc, pose);
        DrawLegs(dc, pose);
        DrawBody(dc, pose);
        LanderParts.Jet(dc, LeftJetPoint, -1, pose.LeftJet);
        LanderParts.Jet(dc, RightJetPoint, 1, pose.RightJet);
        if (pose.Night)
        {
            DrawLights(dc, pose);
        }
    }

    protected virtual void DrawExhaust(DrawingContext dc, in LanderPose pose) => LanderParts.Flame(dc, Nozzle, pose);

    protected virtual void DrawLegs(DrawingContext dc, in LanderPose pose) =>
        LanderParts.Legs(dc, pose.LegsDeploy, Gfx.Rgb(0xA3ACB9), Gfx.Rgb(0x7B8494), Gfx.Rgb(0x5B6472));

    protected abstract void DrawBody(DrawingContext dc, in LanderPose pose);

    protected virtual void DrawLights(DrawingContext dc, in LanderPose pose) =>
        LanderParts.NavigationLights(dc, new Point(-16.5, 1.5), new Point(16.5, 1.5), new Point(8.8, -28.6), pose.Time);
}

public static class SkinCatalog
{
    internal static LanderSkin Get(string? id) => id switch
    {
        "apollo" => Apollo,
        "capsule" => Capsule,
        "saucer" => Saucer,
        "pixel" => Pixel,
        _ => Classic,
    };

    internal static LanderSkin Classic { get; } = new ClassicSkin();

    internal static LanderSkin Apollo { get; } = new ApolloSkin();

    internal static LanderSkin Capsule { get; } = new CapsuleSkin();

    internal static LanderSkin Saucer { get; } = new SaucerSkin();

    internal static LanderSkin Pixel { get; } = new PixelSkin();

    public static IEnumerable<string> Ids => Unlocks.OfKind(UnlockKind.Skin).Select(u => u.Id);
}
