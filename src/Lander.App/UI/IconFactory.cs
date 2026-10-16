using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lander.App.Interop;
using Lander.App.Rendering;
using Lander.App.Rendering.Skins;
using Lander.Core.Game;
using Expression = Lander.Core.Game.Expression;
using Lander.Core.Geometry;

namespace Lander.App.UI;

/// <summary>Iconos del programa dibujados con el mismo código que el lander.</summary>
internal static class IconFactory
{
    /// <summary>El lander centrado en un cuadrado de <paramref name="size"/> píxeles.</summary>
    public static BitmapSource RenderLander(int size, string skinId = "classic", bool paused = false, Vec2 look = default)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var scale = size / 54.0;
            var pose = new LanderPose { Look = look, Expression = Expression.Neutral };
            SceneRenderer.DrawLander(dc, SkinCatalog.Get(skinId), new Vec2(size / 2.0, size / 2.0 + 1.5 * scale), 0, scale, pose);

            if (paused)
            {
                var badge = size * 0.22;
                var center = new Point(size - badge - 0.5, size - badge - 0.5);
                dc.DrawEllipse(Gfx.Brush(0x2B2F36), Gfx.Pen(0xFFFFFF, Math.Max(1, size / 32.0)), center, badge, badge);
                var bar = badge * 0.28;
                dc.DrawRectangle(Gfx.Brush(0xFFFFFF), null, new Rect(center.X - bar * 1.5, center.Y - badge * 0.5, bar, badge));
                dc.DrawRectangle(Gfx.Brush(0xFFFFFF), null, new Rect(center.X + bar * 0.5, center.Y - badge * 0.5, bar, badge));
            }
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Convierte una imagen en un HICON (hay que liberarlo con DestroyIcon).</summary>
    public static unsafe nint CreateIconHandle(BitmapSource source)
    {
        var straight = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = straight.PixelWidth;
        var height = straight.PixelHeight;
        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
        };

        var color = Native.CreateDIBSection(0, &header, 0, out var bits, 0, 0);
        straight.CopyPixels(new Int32Rect(0, 0, width, height), (nint)bits, width * height * 4, width * 4);
        var maskBits = stackalloc byte[((width + 15) / 16 * 2) * height];
        var mask = Native.CreateBitmap(width, height, 1, 1, maskBits);
        try
        {
            var info = new ICONINFO { fIcon = 1, hbmColor = color, hbmMask = mask };
            return Native.CreateIconIndirect(info);
        }
        finally
        {
            Native.DeleteObject(color);
            Native.DeleteObject(mask);
        }
    }

    /// <summary>Genera un .ico con varias resoluciones (entradas PNG, compatibles desde Windows Vista).</summary>
    public static void SaveIco(string path, params int[] sizes)
    {
        var images = sizes.Select(size =>
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(RenderLander(size, look: new Vec2(0.35, 0))));
            using var memory = new MemoryStream();
            encoder.Save(memory);
            return (Size: size, Data: memory.ToArray());
        }).ToList();

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var (size, data) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(data.Length);
            writer.Write(offset);
            offset += data.Length;
        }

        foreach (var (_, data) in images)
        {
            writer.Write(data);
        }
    }
}
