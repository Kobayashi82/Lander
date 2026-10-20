using System.Runtime.InteropServices;

namespace Lander.App.Interop;

/// <summary>Declaraciones Win32 (P/Invoke generado en compilación con LibraryImport).</summary>
internal static unsafe partial class Native
{
    private const string User32 = "user32.dll";
    private const string Gdi32 = "gdi32.dll";
    private const string Kernel32 = "kernel32.dll";
    private const string Dwmapi = "dwmapi.dll";
    private const string Shell32 = "shell32.dll";
    private const string Shcore = "shcore.dll";
    private const string Winmm = "winmm.dll";

    // ---------------------------------------------------------------- Ventanas

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint lParam);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hwnd);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsZoomed(nint hwnd);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hwnd);

    [LibraryImport(User32, EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport(User32, EntryPoint = "SetWindowLongPtrW")]
    public static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [LibraryImport(User32)]
    public static partial nint GetWindow(nint hwnd, uint command);

    [LibraryImport(User32)]
    public static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport(User32, EntryPoint = "GetClassNameW")]
    public static partial int GetClassName(nint hwnd, char* buffer, int maxCount);

    [LibraryImport(User32, EntryPoint = "GetWindowTextW")]
    public static partial int GetWindowText(nint hwnd, char* buffer, int maxCount);

    [LibraryImport(User32)]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hwnd, out RECT rect);

    [LibraryImport(User32)]
    public static partial nint GetForegroundWindow();

    [LibraryImport(User32)]
    public static partial nint WindowFromPoint(POINT point);

    /// <summary>Con <c>WDA_EXCLUDEFROMCAPTURE</c> la ventana no sale en capturas de pantalla (Windows 10 2004 o posterior).</summary>
    [LibraryImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowDisplayAffinity(nint hwnd, uint affinity);

    [LibraryImport(User32)]
    public static partial nint GetShellWindow();

    [LibraryImport(User32)]
    public static partial nint GetDesktopWindow();

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport(User32, EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindWindow(string? className, string? windowName);

    [LibraryImport(User32, EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(nint hwnd, ref POINT point);

    [LibraryImport(User32, EntryPoint = "SendMessageTimeoutW")]
    public static partial nint SendMessageTimeout(nint hwnd, uint msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    [LibraryImport(User32, EntryPoint = "GetClassLongPtrW")]
    public static partial nint GetClassLongPtr(nint hwnd, int index);

    [LibraryImport(User32, EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessage(string name);

    // ---------------------------------------------------------------- Creación de ventanas y mensajes

    [LibraryImport(User32, EntryPoint = "RegisterClassExW")]
    public static partial ushort RegisterClassEx(in WNDCLASSEXW windowClass);

    [LibraryImport(User32, EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial nint CreateWindowEx(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hwnd);

    [LibraryImport(User32, EntryPoint = "DefWindowProcW")]
    public static partial nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport(User32, EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PeekMessage(out MSG msg, nint hwnd, uint filterMin, uint filterMax, uint remove);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(in MSG msg);

    [LibraryImport(User32, EntryPoint = "DispatchMessageW")]
    public static partial nint DispatchMessage(in MSG msg);

    [LibraryImport(User32, EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport(User32)]
    public static partial uint MsgWaitForMultipleObjectsEx(uint count, nint* handles, uint milliseconds, uint wakeMask, uint flags);

    [LibraryImport(User32)]
    public static partial nint SetCapture(nint hwnd);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport(User32)]
    public static partial nint GetCapture();

    [LibraryImport(User32, EntryPoint = "LoadCursorW")]
    public static partial nint LoadCursor(nint instance, nint cursorName);

    [LibraryImport(User32)]
    public static partial nint SetCursor(nint cursor);

    // ---------------------------------------------------------------- Capas, DC y mapas de bits

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UpdateLayeredWindow(
        nint hwnd, nint hdcDst, POINT* pptDst, SIZE* psize, nint hdcSrc, POINT* pptSrc,
        uint crKey, BLENDFUNCTION* pblend, uint flags);

    [LibraryImport(User32)]
    public static partial nint GetDC(nint hwnd);

    [LibraryImport(User32)]
    public static partial int ReleaseDC(nint hwnd, nint hdc);

    [LibraryImport(Gdi32)]
    public static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport(Gdi32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint hdc);

    [LibraryImport(Gdi32)]
    public static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport(Gdi32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint obj);

    [LibraryImport(Gdi32)]
    public static partial nint CreateDIBSection(nint hdc, BITMAPINFOHEADER* info, uint usage, out void* bits, nint section, uint offset);

    [LibraryImport(Gdi32)]
    public static partial nint CreateBitmap(int width, int height, uint planes, uint bitCount, void* bits);

    [LibraryImport(User32)]
    public static partial nint CreateIconIndirect(in ICONINFO info);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    // ---------------------------------------------------------------- Monitores y DPI

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDisplayMonitors(nint hdc, nint clip, delegate* unmanaged<nint, nint, RECT*, nint, int> callback, nint data);

    [LibraryImport(User32, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(nint monitor, ref MONITORINFOEXW info);

    [LibraryImport(User32)]
    public static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport(User32)]
    public static partial nint MonitorFromPoint(POINT point, uint flags);

    [LibraryImport(Shcore)]
    public static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport(User32)]
    public static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport(User32)]
    public static partial uint GetDpiForSystem();

    // ---------------------------------------------------------------- Entrada

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT point);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetLastInputInfo(ref LASTINPUTINFO info);

    [LibraryImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hwnd, int id);

    [LibraryImport(User32)]
    public static partial short GetAsyncKeyState(int virtualKey);

    [LibraryImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterRawInputDevices(RAWINPUTDEVICE* devices, uint count, uint size);

    [LibraryImport(User32)]
    public static partial uint GetRawInputData(nint rawInput, uint command, void* data, ref uint size, uint headerSize);

    // ---------------------------------------------------------------- Eventos de accesibilidad

    [LibraryImport(User32)]
    public static partial nint SetWinEventHook(
        uint eventMin, uint eventMax, nint module,
        delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> callback,
        uint processId, uint threadId, uint flags);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWinEvent(nint hook);

    // ---------------------------------------------------------------- DWM

    [LibraryImport(Dwmapi)]
    public static partial int DwmGetWindowAttribute(nint hwnd, uint attribute, void* value, uint size);

    [LibraryImport(Dwmapi)]
    public static partial int DwmFlush();

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetLayeredWindowAttributes(nint hwnd, uint colorKey, byte alpha, uint flags);

    /// <summary>
    /// Espera al siguiente fotograma del compositor (Windows 11) o a alguno de los objetos indicados.
    /// Devuelve el índice del objeto, <paramref name="count"/> si ha sido el reloj o WAIT_TIMEOUT.
    /// </summary>
    [LibraryImport("dcomp.dll")]
    public static partial uint DCompositionWaitForCompositorClock(uint count, nint* handles, uint timeoutInMs);

    [LibraryImport("dcomp.dll")]
    public static partial int DCompositionCreateDevice2(nint renderingDevice, Guid* iid, out nint device);

    // ---------------------------------------------------------------- Kernel

    [LibraryImport(Kernel32, EntryPoint = "CreateWaitableTimerExW")]
    public static partial nint CreateWaitableTimerEx(nint attributes, char* name, uint flags, uint access);

    [LibraryImport(Kernel32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWaitableTimer(nint timer, in long dueTime, int period, nint completion, nint argument, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [LibraryImport(Kernel32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport(Kernel32, EntryPoint = "CreateEventW")]
    public static partial nint CreateEvent(nint attributes, [MarshalAs(UnmanagedType.Bool)] bool manualReset, [MarshalAs(UnmanagedType.Bool)] bool initialState, char* name);

    [LibraryImport(Kernel32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetEvent(nint handle);

    [LibraryImport(Kernel32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [LibraryImport(Kernel32)]
    public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport(Kernel32, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageName(nint process, uint flags, char* buffer, ref uint size);

    [LibraryImport(Kernel32)]
    public static partial nint GetModuleHandleW(char* moduleName);

    [LibraryImport(Kernel32)]
    public static partial uint GetCurrentThreadId();

    // ---------------------------------------------------------------- Shell

    [LibraryImport(Shell32, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATAW data);

    [LibraryImport(Shell32)]
    public static partial nuint SHAppBarMessage(uint message, ref APPBARDATA data);

    [LibraryImport(Shell32)]
    public static partial int SHQueryUserNotificationState(out int state);

    [LibraryImport(Shell32, EntryPoint = "SHQueryRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHQueryRecycleBin(string? rootPath, ref SHQUERYRBINFO info);

    [LibraryImport(Shell32)]
    public static partial uint SHChangeNotifyRegister(nint hwnd, int sources, int events, uint message, int entries, SHChangeNotifyEntry* items);

    [LibraryImport(Shell32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SHChangeNotifyDeregister(uint registration);

    [LibraryImport(Shell32)]
    public static partial int SHGetSpecialFolderLocation(nint hwnd, int csidl, out nint pidl);

    [LibraryImport(Shell32)]
    public static partial void ILFree(nint pidl);

    [LibraryImport(Shell32, EntryPoint = "SHGetFileInfoW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint SHGetFileInfo(string path, uint attributes, ref SHFILEINFOW info, uint size, uint flags);

    // ---------------------------------------------------------------- Audio

    [LibraryImport(Winmm)]
    public static partial int waveOutOpen(out nint handle, uint deviceId, WAVEFORMATEX* format, nint callback, nint instance, uint flags);

    [LibraryImport(Winmm)]
    public static partial int waveOutPrepareHeader(nint handle, WAVEHDR* header, uint size);

    [LibraryImport(Winmm)]
    public static partial int waveOutUnprepareHeader(nint handle, WAVEHDR* header, uint size);

    [LibraryImport(Winmm)]
    public static partial int waveOutWrite(nint handle, WAVEHDR* header, uint size);

    [LibraryImport(Winmm)]
    public static partial int waveOutReset(nint handle);

    [LibraryImport(Winmm)]
    public static partial int waveOutClose(nint handle);
}
