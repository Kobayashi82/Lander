using System.Numerics;
using System.Runtime.InteropServices;
using Lander.App.Interop;
using Lander.App.Services;
using Lander.Core.Geometry;
using Lander.Core.Terrain;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice;
using Vortice.Mathematics;
using static Lander.App.Interop.NativeConstants;

namespace Lander.App.Rendering.Gpu;

/// <summary>
/// La capa donde se ve el lander cuando se dibuja por GPU. Hay una ventana transparente por monitor, sin
/// mapa de bits de redirección (no ocupa memoria ni le cuesta nada a DWM mientras está vacía) y que deja
/// pasar siempre el ratón. Todas muestran la misma superficie de DirectComposition, desplazada a donde
/// está el lander: la imagen y su posición cambian en la misma transacción (<c>Commit</c>), así que el
/// movimiento nunca tiembla, y pasar de un monitor a otro es continuo.
/// </summary>
/// <remarks>Debe crearse y usarse siempre desde el hilo del motor.</remarks>
internal sealed unsafe class CompositionOverlay : IDisposable
{
    public const string ClassName = "LanderSurface";

    private static bool _classRegistered;

    private readonly GpuDevice _gpu;
    private readonly HashSet<nint> _ownWindows;
    private readonly List<MonitorLayer> _layers = [];
    private IDCompositionSurface? _surface;
    private int _surfaceWidth;
    private int _surfaceHeight;
    private bool _visible;

    public CompositionOverlay(GpuDevice gpu, HashSet<nint> ownWindows)
    {
        _gpu = gpu;
        _ownWindows = ownWindows;
        EnsureClass();
    }

    public bool IsVisible => _visible;

    /// <summary>Tiempos acumulados (diagnóstico): dibujo, cierre de la superficie y envío al compositor.</summary>
    public long DrawTicks { get; set; }

    public long EndDrawTicks { get; set; }

    public long BeginTicks { get; set; }

    public long CommitTicks { get; set; }

    /// <summary>Crea (o rehace) una ventana por monitor cuando cambia la disposición de pantallas.</summary>
    public void SetMonitors(IReadOnlyList<MonitorInfo> monitors)
    {
        if (_layers.Count == monitors.Count && _layers.Zip(monitors).All(p => p.First.Bounds == p.Second.Bounds))
        {
            return;
        }

        DestroyLayers();
        foreach (var monitor in monitors)
        {
            _layers.Add(CreateLayer(monitor.Bounds, monitor.Index));
        }

        if (_surface is not null)
        {
            foreach (var layer in _layers)
            {
                layer.Visual.SetContent(_surface);
            }
        }

        _gpu.Composition.Commit();
        _visible = false;
    }

    /// <summary>
    /// Dibuja un fotograma de <paramref name="width"/>×<paramref name="height"/> con su esquina superior
    /// izquierda en (<paramref name="x"/>, <paramref name="y"/>) del escritorio.
    /// </summary>
    /// <param name="draw">Recibe el contexto de Direct2D y la transformación base (el origen del fotograma).</param>
    /// <param name="hiddenMonitors">Monitores donde no debe verse nada (por ejemplo, con algo a pantalla completa).</param>
    public void Present(int x, int y, int width, int height, Action<ID2D1DeviceContext, Matrix3x2> draw, IReadOnlySet<int> hiddenMonitors)
    {
        EnsureSurface(width, height);
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        using (var context = _surface!.BeginDraw<ID2D1DeviceContext>(null, out var offset))
        {
            BeginTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            var clip = new RawRectF(offset.X, offset.Y, offset.X + _surfaceWidth, offset.Y + _surfaceHeight);
            context.PushAxisAlignedClip(clip, AntialiasMode.Aliased);
            context.Clear(new Color4(0, 0, 0, 0));
            draw(context, Matrix3x2.CreateTranslation(offset.X, offset.Y));
            context.PopAxisAlignedClip();
        }

        var t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        _surface.EndDraw();
        var t2 = System.Diagnostics.Stopwatch.GetTimestamp();
        DrawTicks += t1 - t0;
        EndDrawTicks += t2 - t1;

        var frame = new PixelRect(x, y, x + width, y + height);
        for (var i = 0; i < _layers.Count; i++)
        {
            var layer = _layers[i];
            var show = frame.Intersects(layer.Bounds) && !hiddenMonitors.Contains(layer.Index);
            if (show)
            {
                layer.Visual.SetOffsetX(x - layer.Origin.X);
                layer.Visual.SetOffsetY(y - layer.Origin.Y);
            }

            layer.Show(show);
        }

        var t3 = System.Diagnostics.Stopwatch.GetTimestamp();
        _gpu.Composition.Commit();
        CommitTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t3;
        _visible = true;
    }

    public void Hide()
    {
        if (!_visible)
        {
            return;
        }

        foreach (var layer in _layers)
        {
            layer.Show(false);
        }

        _visible = false;
    }

    /// <summary>Recupera el primer puesto entre las ventanas «siempre encima».</summary>
    public void BringToTop()
    {
        foreach (var layer in _layers)
        {
            if (layer.Shown)
            {
                Native.SetWindowPos(layer.Hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            }
        }
    }

    public void Dispose()
    {
        DestroyLayers();
        _surface?.Dispose();
        _surface = null;
        try
        {
            _gpu.Composition.Commit();
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
            // El dispositivo ya puede estar perdido: no importa al cerrar.
        }
    }

    private void EnsureSurface(int width, int height)
    {
        if (_surface is not null && width == _surfaceWidth && height == _surfaceHeight)
        {
            return;
        }

        _surface?.Dispose();
        _surface = _gpu.Composition.CreateSurface((uint)width, (uint)height, Format.B8G8R8A8_UNorm, Vortice.DXGI.AlphaMode.Premultiplied);
        _surfaceWidth = width;
        _surfaceHeight = height;
        foreach (var layer in _layers)
        {
            layer.Visual.SetContent(_surface);
        }
    }

    private MonitorLayer CreateLayer(PixelRect bounds, int index)
    {
        // Un píxel menos por arriba: así ninguna heurística de Windows la toma por una ventana a pantalla
        // completa (y el lander nunca necesita esa fila).
        var origin = new PixelRect(bounds.Left, bounds.Top + 1, bounds.Right, bounds.Bottom);
        const uint exStyle = WS_EX_NOREDIRECTIONBITMAP | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
        var hwnd = Native.CreateWindowEx(exStyle, ClassName, "Lander", WS_POPUP,
            origin.Left, origin.Top, origin.Width, origin.Height, 0, 0, Native.GetModuleHandleW(null), 0);
        if (hwnd == 0)
        {
            throw new InvalidOperationException($"CreateWindowEx falló ({Marshal.GetLastPInvokeError()}).");
        }

        // Una ventana por capas no se muestra hasta tener atributos; con opacidad total no altera nada.
        Native.SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
        _ownWindows.Add(hwnd);
        _gpu.Composition.CreateTargetForHwnd(hwnd, true, out var target).CheckError();
        var visual = _gpu.Composition.CreateVisual();
        visual.SetBitmapInterpolationMode(Vortice.DirectComposition.BitmapInterpolationMode.NearestNeighbor);
        target.SetRoot(visual);
        return new MonitorLayer(hwnd, index, bounds, new Int2(origin.Left, origin.Top), target, visual);
    }

    private void DestroyLayers()
    {
        foreach (var layer in _layers)
        {
            _ownWindows.Remove(layer.Hwnd);
            layer.Dispose();
        }

        _layers.Clear();
    }

    private static void EnsureClass()
    {
        if (_classRegistered)
        {
            return;
        }

        fixed (char* name = ClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProcedure,
                hInstance = Native.GetModuleHandleW(null),
                hCursor = Native.LoadCursor(0, IDC_ARROW),
                lpszClassName = name,
            };

            if (Native.RegisterClassEx(windowClass) == 0)
            {
                throw new InvalidOperationException("RegisterClassEx falló.");
            }
        }

        _classRegistered = true;
    }

    [UnmanagedCallersOnly]
    private static nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WM_MOUSEACTIVATE:
                return MA_NOACTIVATE;

            case WM_DPICHANGED:
                // La ventana mide siempre su monitor en píxeles físicos: no hay nada que reescalar.
                return 0;
        }

        return Native.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private sealed class MonitorLayer(nint hwnd, int index, PixelRect bounds, Int2 origin, IDCompositionTarget target, IDCompositionVisual2 visual) : IDisposable
    {
        public nint Hwnd { get; } = hwnd;

        public int Index { get; } = index;

        public PixelRect Bounds { get; } = bounds;

        public Int2 Origin { get; } = origin;

        public IDCompositionVisual2 Visual { get; } = visual;

        public bool Shown { get; private set; }

        public void Show(bool show)
        {
            if (show == Shown)
            {
                return;
            }

            Shown = show;
            Native.ShowWindow(Hwnd, show ? SW_SHOWNOACTIVATE : SW_HIDE);
            if (show)
            {
                Native.SetWindowPos(Hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            }
        }

        public void Dispose()
        {
            Visual.Dispose();
            target.Dispose();
            Native.DestroyWindow(Hwnd);
        }
    }
}
