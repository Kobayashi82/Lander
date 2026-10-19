using Lander.App.Services;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using FeatureLevel = Vortice.Direct3D.FeatureLevel;

namespace Lander.App.Rendering.Gpu;

/// <summary>
/// Dispositivos gráficos del lander: Direct3D 11 (en la GPU o, si no hay, con WARP), Direct2D encima para
/// dibujar y DirectComposition para enseñarlo. Se crean y usan siempre desde el hilo del motor.
/// </summary>
internal sealed class GpuDevice : IDisposable
{
    private static readonly FeatureLevel[] FeatureLevels =
    [
        FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0, FeatureLevel.Level_9_3,
    ];

    private GpuDevice(ID3D11Device d3d, bool warp)
    {
        Direct3D = d3d;
        IsWarp = warp;
        Dxgi = d3d.QueryInterface<IDXGIDevice>();
        Factory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
        Direct2D = Factory.CreateDevice(Dxgi);
        Composition = CreateDesktopDevice(Direct2D);
        using var adapter = Dxgi.GetAdapter();
        AdapterName = adapter.Description.Description;
    }

    public ID3D11Device Direct3D { get; }

    public IDXGIDevice Dxgi { get; }

    public ID2D1Factory1 Factory { get; }

    public ID2D1Device Direct2D { get; }

    public IDCompositionDesktopDevice Composition { get; }

    /// <summary>Sin GPU utilizable: Direct3D se emula en la CPU (más lento, pero funciona igual).</summary>
    public bool IsWarp { get; }

    public string AdapterName { get; }

    /// <summary>Crea los dispositivos o devuelve null si este equipo no puede (se usará el dibujo por CPU).</summary>
    public static GpuDevice? TryCreate()
    {
        foreach (var driver in new[] { DriverType.Hardware, DriverType.Warp })
        {
            try
            {
                var result = D3D11.D3D11CreateDevice(IntPtr.Zero, driver, DeviceCreationFlags.BgraSupport, FeatureLevels, out var device);
                if (result.Failure || device is null)
                {
                    continue;
                }

                var gpu = new GpuDevice(device, driver == DriverType.Warp);
                Log.Info($"Render por GPU: {gpu.AdapterName}{(gpu.IsWarp ? " (WARP)" : string.Empty)}.");
                return gpu;
            }
            catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or DllNotFoundException or EntryPointNotFoundException or InvalidCastException)
            {
                Log.Warn($"No se pudo crear el dispositivo {driver}.", ex);
            }
        }

        return null;
    }

    /// <summary>
    /// Dispositivo de composición de escritorio que dibuja con nuestro Direct2D (sus superficies se abren
    /// directamente como contextos de Direct2D).
    /// </summary>
    private static unsafe IDCompositionDesktopDevice CreateDesktopDevice(ID2D1Device renderingDevice)
    {
        var iid = typeof(IDCompositionDesktopDevice).GUID;
        var result = Interop.Native.DCompositionCreateDevice2(renderingDevice.NativePointer, &iid, out var device);
        if (result < 0 || device == 0)
        {
            throw new InvalidOperationException($"DCompositionCreateDevice2 falló (0x{result:X8}).");
        }

        return new IDCompositionDesktopDevice(device);
    }

    public void Dispose()
    {
        Composition.Dispose();
        Direct2D.Dispose();
        Factory.Dispose();
        Dxgi.Dispose();
        Direct3D.Dispose();
    }
}
