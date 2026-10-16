using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Lander.App.Rendering;

/// <summary>
/// Dibuja una escena WPF fuera de pantalla (a 96 ppp: 1 unidad = 1 píxel físico) y la vuelca en una
/// <see cref="LayeredWindow"/>. Reutiliza el mapa de bits para no generar basura en cada fotograma.
/// </summary>
internal sealed class FramePresenter
{
    private readonly DrawingVisual _visual = new();
    private RenderTargetBitmap? _target;

    public unsafe void Present(LayeredWindow window, int x, int y, int width, int height, Action<DrawingContext> draw, byte opacity = 255)
    {
        if (_target is null || _target.PixelWidth != width || _target.PixelHeight != height)
        {
            _target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        }

        using (var dc = _visual.RenderOpen())
        {
            draw(dc);
        }

        _target.Clear();
        _target.Render(_visual);

        window.EnsureBuffer(width, height);
        _target.CopyPixels(new Int32Rect(0, 0, width, height), (nint)window.Bits, width * height * 4, width * 4);
        window.Present(x, y, opacity);
    }
}
