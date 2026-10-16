using Lander.App.Interop;
using Lander.App.Services;
using static Lander.App.Interop.NativeConstants;

namespace Lander.App.Platform;

/// <summary>
/// Detecta cuándo se vacía la papelera: el shell avisa de cambios en ella y se compara el número de
/// elementos antes y después (sin sondear el disco periódicamente).
/// </summary>
internal sealed unsafe class RecycleBinWatcher : IDisposable
{
    public const uint Message = WM_APP + 20;

    private readonly uint _registration;
    private readonly nint _pidl;
    private long _lastCount;

    public RecycleBinWatcher(nint hwnd)
    {
        if (Native.SHGetSpecialFolderLocation(0, CSIDL_BITBUCKET, out _pidl) != 0)
        {
            Log.Warn("No se encontró la papelera.");
            return;
        }

        var entry = new SHChangeNotifyEntry { pidl = _pidl, fRecursive = 0 };
        _registration = Native.SHChangeNotifyRegister(
            hwnd,
            SHCNRF_ShellLevel | SHCNRF_InterruptLevel,
            SHCNE_UPDATEDIR | SHCNE_UPDATEITEM | SHCNE_UPDATEIMAGE | SHCNE_DELETE | SHCNE_RMDIR,
            Message,
            1,
            &entry);
        _lastCount = CountItems() ?? 0;
    }

    /// <summary>Tras un aviso del shell: ¿se acaba de vaciar?</summary>
    public bool CheckEmptied()
    {
        if (CountItems() is not { } count)
        {
            return false;
        }

        var emptied = _lastCount > 0 && count == 0;
        _lastCount = count;
        return emptied;
    }

    public void Dispose()
    {
        if (_registration != 0)
        {
            Native.SHChangeNotifyDeregister(_registration);
        }

        if (_pidl != 0)
        {
            Native.ILFree(_pidl);
        }
    }

    private static long? CountItems()
    {
        var info = new SHQUERYRBINFO { cbSize = (uint)sizeof(SHQUERYRBINFO) };
        return Native.SHQueryRecycleBin(null, ref info) == 0 ? info.i64NumItems : null;
    }
}
