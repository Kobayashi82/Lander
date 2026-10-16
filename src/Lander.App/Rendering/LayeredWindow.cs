using System.Runtime.InteropServices;
using Lander.App.Interop;
using Lander.App.Services;
using static Lander.App.Interop.NativeConstants;

namespace Lander.App.Rendering;

/// <summary>
/// Ventana Win32 por capas, sin borde, siempre encima, fuera de Alt+Tab y que nunca roba el foco.
/// Su contenido se actualiza con UpdateLayeredWindow, que cambia posición e imagen a la vez (sin
/// parpadeos al moverse). Los píxeles transparentes dejan pasar el ratón.
/// </summary>
/// <remarks>Debe crearse y usarse siempre desde el mismo hilo (el del motor).</remarks>
internal sealed unsafe class LayeredWindow : IDisposable
{
    public const string ClassName = "LanderOverlay";

    private static readonly Dictionary<nint, LayeredWindow> Instances = [];
    private static bool _classRegistered;

    private nint _memoryDc;
    private nint _bitmap;
    private nint _previousBitmap;
    private bool _visible;
    private bool _clickThrough;

    public LayeredWindow(string title, bool clickThrough)
    {
        EnsureClass();
        _clickThrough = clickThrough;
        var exStyle = WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE | (clickThrough ? WS_EX_TRANSPARENT : 0);
        Handle = Native.CreateWindowEx(exStyle, ClassName, title, WS_POPUP, 0, 0, 1, 1, 0, 0, Native.GetModuleHandleW(null), 0);
        if (Handle == 0)
        {
            throw new InvalidOperationException($"CreateWindowEx falló ({Marshal.GetLastPInvokeError()}).");
        }

        Instances[Handle] = this;
        _memoryDc = Native.CreateCompatibleDC(0);
    }

    public nint Handle { get; }

    /// <summary>Píxeles BGRA premultiplicados del búfer actual (filas de arriba abajo).</summary>
    public void* Bits { get; private set; }

    public int BufferWidth { get; private set; }

    public int BufferHeight { get; private set; }

    public bool IsVisible => _visible;

    /// <summary>Manejador de mensajes: devuelve un resultado o null para el tratamiento por defecto.</summary>
    public Func<uint, nint, nint, nint?>? MessageHandler { get; set; }

    /// <summary>Prepara un búfer de al menos <paramref name="width"/>×<paramref name="height"/>.</summary>
    public void EnsureBuffer(int width, int height)
    {
        if (_bitmap != 0 && width == BufferWidth && height == BufferHeight)
        {
            return;
        }

        ReleaseBitmap();
        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB,
        };

        _bitmap = Native.CreateDIBSection(0, &header, DIB_RGB_COLORS, out var bits, 0, 0);
        if (_bitmap == 0)
        {
            throw new InvalidOperationException("CreateDIBSection falló.");
        }

        Bits = bits;
        BufferWidth = width;
        BufferHeight = height;
        _previousBitmap = Native.SelectObject(_memoryDc, _bitmap);
    }

    /// <summary>Muestra el contenido del búfer con su esquina superior izquierda en (x, y).</summary>
    public void Present(int x, int y, byte opacity = 255)
    {
        if (_bitmap == 0)
        {
            return;
        }

        var destination = new POINT(x, y);
        var size = new SIZE(BufferWidth, BufferHeight);
        var source = new POINT(0, 0);
        var blend = new BLENDFUNCTION
        {
            BlendOp = AC_SRC_OVER,
            SourceConstantAlpha = opacity,
            AlphaFormat = AC_SRC_ALPHA,
        };

        Native.UpdateLayeredWindow(Handle, 0, &destination, &size, _memoryDc, &source, 0, &blend, ULW_ALPHA);
        if (!_visible)
        {
            Native.ShowWindow(Handle, SW_SHOWNOACTIVATE);
            _visible = true;
            BringToTop();
        }
    }

    public void Hide()
    {
        if (_visible)
        {
            Native.ShowWindow(Handle, SW_HIDE);
            _visible = false;
        }
    }

    /// <summary>Con <c>true</c>, todos los clics atraviesan la ventana.</summary>
    public void SetClickThrough(bool clickThrough)
    {
        if (_clickThrough == clickThrough)
        {
            return;
        }

        _clickThrough = clickThrough;
        var exStyle = (long)Native.GetWindowLongPtr(Handle, GWL_EXSTYLE);
        exStyle = clickThrough ? exStyle | WS_EX_TRANSPARENT : exStyle & ~(long)WS_EX_TRANSPARENT;
        Native.SetWindowLongPtr(Handle, GWL_EXSTYLE, (nint)exStyle);
    }

    /// <summary>Recupera el primer puesto entre las ventanas «siempre encima».</summary>
    public void BringToTop() =>
        Native.SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

    public void Dispose()
    {
        ReleaseBitmap();
        if (_memoryDc != 0)
        {
            Native.DeleteDC(_memoryDc);
            _memoryDc = 0;
        }

        if (Handle != 0)
        {
            Instances.Remove(Handle);
            Native.DestroyWindow(Handle);
        }
    }

    private void ReleaseBitmap()
    {
        if (_bitmap == 0)
        {
            return;
        }

        Native.SelectObject(_memoryDc, _previousBitmap);
        Native.DeleteObject(_bitmap);
        _bitmap = 0;
        Bits = null;
        BufferWidth = BufferHeight = 0;
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
                style = CS_DBLCLKS,
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
        try
        {
            if (Instances.TryGetValue(hwnd, out var window) && window.MessageHandler?.Invoke(message, wParam, lParam) is { } result)
            {
                return result;
            }

            if (message == WM_MOUSEACTIVATE)
            {
                return MA_NOACTIVATE;
            }
        }
        catch (Exception ex)
        {
            // Una excepción no puede cruzar a código nativo: se registra y se sigue.
            Log.Error($"Error procesando el mensaje 0x{message:X4}.", ex);
        }

        return Native.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
