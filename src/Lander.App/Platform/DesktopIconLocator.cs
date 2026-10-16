using System.Runtime.InteropServices;
using Lander.App.Interop;
using Lander.App.Services;
using Lander.Core.Geometry;

namespace Lander.App.Platform;

/// <summary>
/// Busca en qué punto de la pantalla está el icono de la papelera, preguntando a la vista del escritorio
/// del shell (IShellWindows → IShellBrowser → IFolderView), la técnica documentada para esto.
/// </summary>
internal static class DesktopIconLocator
{
    private const string RecycleBinParsingName = "::{645FF040-5081-101B-9F08-00AA002F954E}";
    private const int SWC_DESKTOP = 8;
    private const int SWFO_NEEDDISPATCH = 1;
    private const int SVGIO_ALLVIEW = 2;
    private const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;

    private static readonly Guid ShellWindowsClsid = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid TopLevelBrowserService = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid ShellBrowserIid = new("000214E2-0000-0000-C000-000000000046");

    /// <summary>Centro del icono de la papelera en coordenadas de pantalla, si se encuentra.</summary>
    public static Vec2? FindRecycleBin()
    {
        object? shellWindows = null;
        object? dispatch = null;
        object? browser = null;
        object? view = null;
        try
        {
            var type = Type.GetTypeFromCLSID(ShellWindowsClsid);
            if (type is null)
            {
                return null;
            }

            shellWindows = Activator.CreateInstance(type);
            if (shellWindows is not IShellWindows windows)
            {
                return null;
            }

            object location = 0;
            object root = null!;
            if (windows.FindWindowSW(ref location, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH, out dispatch) != 0
                || dispatch is not IServiceProvider provider)
            {
                return null;
            }

            var service = TopLevelBrowserService;
            var iid = ShellBrowserIid;
            if (provider.QueryService(ref service, ref iid, out browser) != 0 || browser is not IShellBrowser shellBrowser)
            {
                return null;
            }

            if (shellBrowser.QueryActiveShellView(out view) != 0 || view is not IFolderView folderView)
            {
                return null;
            }

            if (folderView.ItemCount(SVGIO_ALLVIEW, out var count) != 0)
            {
                return null;
            }

            folderView.GetSpacing(out var spacing);
            for (var i = 0; i < count; i++)
            {
                if (folderView.Item(i, out var pidl) != 0 || pidl == 0)
                {
                    continue;
                }

                try
                {
                    if (!IsRecycleBin(pidl) || folderView.GetItemPosition(pidl, out var position) != 0)
                    {
                        continue;
                    }

                    var point = new POINT(position.X + spacing.X / 2, position.Y + spacing.Y / 3);
                    var listView = FindDesktopListView();
                    if (listView != 0)
                    {
                        Native.ClientToScreen(listView, ref point);
                    }

                    return new Vec2(point.X, point.Y);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            Log.Warn("No se pudo localizar la papelera en el escritorio.", ex);
        }
        finally
        {
            Release(view);
            Release(browser);
            Release(dispatch);
            Release(shellWindows);
        }

        return null;
    }

    private static bool IsRecycleBin(nint pidl)
    {
        if (SHGetNameFromIDList(pidl, SIGDN_DESKTOPABSOLUTEPARSING, out var name) != 0 || name == 0)
        {
            return false;
        }

        try
        {
            return string.Equals(Marshal.PtrToStringUni(name), RecycleBinParsingName, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Marshal.FreeCoTaskMem(name);
        }
    }

    /// <summary>La lista de iconos del escritorio cuelga de Progman o de un WorkerW según la versión.</summary>
    private static nint FindDesktopListView()
    {
        var progman = Native.FindWindow("Progman", null);
        var defView = Native.FindWindowEx(progman, 0, "SHELLDLL_DefView", null);
        nint worker = 0;
        while (defView == 0)
        {
            worker = Native.FindWindowEx(0, worker, "WorkerW", null);
            if (worker == 0)
            {
                return 0;
            }

            defView = Native.FindWindowEx(worker, 0, "SHELLDLL_DefView", null);
        }

        return Native.FindWindowEx(defView, 0, "SysListView32", null);
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetNameFromIDList(nint pidl, uint sigdnName, out nint name);

    [ComImport]
    [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        void GetCount();

        void Item();

        void NewEnum();

        void Register();

        void RegisterPending();

        void Revoke();

        void OnNavigate();

        void OnActivated();

        [PreserveSig]
        int FindWindowSW(
            [MarshalAs(UnmanagedType.Struct)] ref object location,
            [MarshalAs(UnmanagedType.Struct)] ref object root,
            int windowClass,
            out int hwnd,
            int options,
            [MarshalAs(UnmanagedType.IDispatch)] out object dispatch);
    }

    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }

    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow();

        void ContextSensitiveHelp();

        void InsertMenusSB();

        void SetMenuSB();

        void RemoveMenusSB();

        void SetStatusTextSB();

        void EnableModelessSB();

        void TranslateAcceleratorSB();

        void BrowseObject();

        void GetViewStateStream();

        void GetControlWindow();

        void SendControlMsg();

        [PreserveSig]
        int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object view);
    }

    [ComImport]
    [Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        void GetCurrentViewMode();

        void SetCurrentViewMode();

        void GetFolder();

        [PreserveSig]
        int Item(int index, out nint pidl);

        [PreserveSig]
        int ItemCount(int flags, out int count);

        void Items();

        void GetSelectionMarkedItem();

        void GetFocusedItem();

        [PreserveSig]
        int GetItemPosition(nint pidl, out POINT position);

        [PreserveSig]
        int GetSpacing(out POINT spacing);
    }
}
