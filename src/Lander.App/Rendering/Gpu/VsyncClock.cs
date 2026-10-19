using Lander.App.Interop;
using Lander.App.Services;
using Vortice.DXGI;

namespace Lander.App.Rendering.Gpu;

/// <summary>
/// Marca el ritmo de los fotogramas con el del compositor de Windows (el refresco del monitor: 60, 144 Hz…).
/// Un hilo propio espera a cada fotograma y despierta al motor con un evento, así el motor sigue
/// atendiendo mensajes mientras espera y dibuja justo después de cada refresco, sin tirones.
/// </summary>
/// <remarks>
/// Usa el reloj del compositor de Windows 11 y, si no existe, la sincronía vertical de la salida de vídeo
/// o, en último caso, <c>DwmFlush</c>. Solo funciona mientras está activado: en reposo no gasta nada.
/// </remarks>
internal sealed unsafe class VsyncClock : IDisposable
{
    private readonly AutoResetEvent _tick = new(false);
    private readonly ManualResetEvent _enabled = new(false);
    private readonly Thread _thread;
    private readonly IDXGIOutput? _output;
    private volatile bool _running = true;
    private bool _compositorClock = true;
    private bool _isEnabled;

    public VsyncClock(GpuDevice gpu)
    {
        try
        {
            using var adapter = gpu.Dxgi.GetAdapter();
            if (adapter.EnumOutputs(0, out var output).Success)
            {
                _output = output;
            }
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
            _output = null;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "Lander vsync", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>Evento que se activa en cada fotograma del compositor (mientras el reloj está activado).</summary>
    public nint Handle => _tick.SafeWaitHandle.DangerousGetHandle();

    public bool Enabled
    {
        get => _isEnabled;
        set
        {
            if (value == _isEnabled)
            {
                return;
            }

            _isEnabled = value;
            if (value)
            {
                _enabled.Set();
            }
            else
            {
                _enabled.Reset();
            }
        }
    }

    public void Dispose()
    {
        _running = false;
        _enabled.Set();
        _thread.Join(500);
        _output?.Dispose();
        _tick.Dispose();
        _enabled.Dispose();
    }

    private void Run()
    {
        while (_running)
        {
            _enabled.WaitOne();
            if (!_running)
            {
                break;
            }

            try
            {
                WaitForFrame();
            }
            catch (Exception ex)
            {
                Log.Warn("El reloj de fotogramas ha fallado; se usará DwmFlush.", ex);
                _compositorClock = false;
                Thread.Sleep(16);
            }

            _tick.Set();
        }
    }

    private void WaitForFrame()
    {
        if (_compositorClock)
        {
            try
            {
                // 0 objetos: solo el reloj. El tiempo límite evita quedarse colgado si el compositor se para.
                _ = Native.DCompositionWaitForCompositorClock(0, null, 100);
                return;
            }
            catch (EntryPointNotFoundException)
            {
                _compositorClock = false;
            }
        }

        if (_output is not null)
        {
            _output.WaitForVBlank();
        }
        else if (Native.DwmFlush() < 0)
        {
            Thread.Sleep(16);
        }
    }
}
