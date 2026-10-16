namespace Lander.App.Interop;

/// <summary>Constantes de Win32 que usa Lander.</summary>
internal static class NativeConstants
{
    // Estilos de ventana
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_CAPTION = 0x00C00000;
    public const uint WS_MAXIMIZE = 0x01000000;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_APPWINDOW = 0x00040000;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_NOACTIVATE = 0x08000000;

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int GCLP_HICON = -14;
    public const int GCLP_HICONSM = -34;

    public const uint CS_DBLCLKS = 0x0008;

    public const uint GW_OWNER = 4;
    public const uint GA_ROOT = 2;

    // ShowWindow / SetWindowPos
    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;
    public static readonly nint HWND_TOPMOST = -1;
    public static readonly nint HWND_MESSAGE = -3;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOOWNERZORDER = 0x0200;

    // Mensajes
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_SETCURSOR = 0x0020;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_GETICON = 0x007F;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_INPUT = 0x00FF;
    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_CAPTURECHANGED = 0x0215;
    public const uint WM_POWERBROADCAST = 0x0218;
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint WM_APP = 0x8000;
    public const uint WM_USER = 0x0400;
    public const uint WM_NULL = 0x0000;

    public const int MA_NOACTIVATE = 3;
    public const int HTCLIENT = 1;

    public const nint IDC_ARROW = 32512;
    public const nint IDC_HAND = 32649;

    public const uint PM_REMOVE = 0x0001;
    public const uint QS_ALLINPUT = 0x04FF;
    public const uint MWMO_INPUTAVAILABLE = 0x0004;
    public const uint WAIT_OBJECT_0 = 0;
    public const uint WAIT_TIMEOUT = 0x102;
    public const uint INFINITE = 0xFFFFFFFF;

    // Capas
    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;
    public const uint ULW_ALPHA = 0x00000002;
    public const uint DIB_RGB_COLORS = 0;
    public const uint BI_RGB = 0;

    // Monitores
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint MONITORINFOF_PRIMARY = 1;
    public const int MDT_EFFECTIVE_DPI = 0;
    public const int SM_CXSMICON = 49;
    public const int SM_CYSMICON = 50;

    // Atajos
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public const int VK_SPACE = 0x20;
    public const int VK_ESCAPE = 0x1B;
    public const int VK_LEFT = 0x25;
    public const int VK_UP = 0x26;
    public const int VK_RIGHT = 0x27;
    public const int VK_DOWN = 0x28;

    // Raw input
    public const uint RIDEV_INPUTSINK = 0x00000100;
    public const uint RIDEV_REMOVE = 0x00000001;
    public const uint RID_INPUT = 0x10000003;
    public const uint RIM_TYPEMOUSE = 0;
    public const ushort RI_MOUSE_WHEEL = 0x0400;
    public const ushort RI_MOUSE_HWHEEL = 0x0800;

    // Eventos de accesibilidad
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_OBJECT_CREATE = 0x8000;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint EVENT_OBJECT_HIDE = 0x8003;
    public const uint EVENT_OBJECT_REORDER = 0x8004;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint EVENT_OBJECT_CLOAKED = 0x8017;
    public const uint EVENT_OBJECT_UNCLOAKED = 0x8018;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const int OBJID_WINDOW = 0;
    public const int CHILDID_SELF = 0;

    // DWM
    public const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const uint DWMWA_CLOAKED = 14;

    // Temporizador de alta resolución
    public const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
    public const uint TIMER_ALL_ACCESS = 0x1F0003;

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // Bandeja del sistema
    public const uint NIM_ADD = 0x00000000;
    public const uint NIM_MODIFY = 0x00000001;
    public const uint NIM_DELETE = 0x00000002;
    public const uint NIM_SETVERSION = 0x00000004;
    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON = 0x00000002;
    public const uint NIF_TIP = 0x00000004;
    public const uint NIF_INFO = 0x00000010;
    public const uint NIF_SHOWTIP = 0x00000080;
    public const uint NIIF_NONE = 0x00000000;
    public const uint NIIF_INFO = 0x00000001;
    public const uint NIIF_WARNING = 0x00000002;
    public const uint NIIF_NOSOUND = 0x00000010;
    public const uint NOTIFYICON_VERSION_4 = 4;
    public const uint NIN_SELECT = WM_USER + 0;
    public const uint NIN_KEYSELECT = NIN_SELECT | 0x1;

    // Barra de tareas
    public const uint ABM_GETSTATE = 0x00000004;
    public const uint ABM_GETTASKBARPOS = 0x00000005;
    public const uint ABS_AUTOHIDE = 0x0000001;

    // Notificaciones del usuario (SHQueryUserNotificationState)
    public const int QUNS_NOT_PRESENT = 1;
    public const int QUNS_BUSY = 2;
    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    public const int QUNS_PRESENTATION_MODE = 4;
    public const int QUNS_ACCEPTS_NOTIFICATIONS = 5;
    public const int QUNS_QUIET_TIME = 6;
    public const int QUNS_APP = 7;

    // Avisos de cambios del shell
    public const int CSIDL_BITBUCKET = 0x000A;
    public const int SHCNRF_InterruptLevel = 0x0001;
    public const int SHCNRF_ShellLevel = 0x0002;
    public const int SHCNRF_NewDelivery = 0x8000;
    public const int SHCNE_DELETE = 0x00000004;
    public const int SHCNE_RMDIR = 0x00000010;
    public const int SHCNE_UPDATEDIR = 0x00001000;
    public const int SHCNE_UPDATEITEM = 0x00002000;
    public const int SHCNE_UPDATEIMAGE = 0x00008000;
    public const int SHCNE_ALLEVENTS = 0x7FFFFFFF;

    public const uint SHGFI_ICON = 0x000000100;
    public const uint SHGFI_LARGEICON = 0x000000000;

    public const uint ICON_BIG = 1;
    public const uint ICON_SMALL2 = 2;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    // Audio
    public const uint WAVE_MAPPER = 0xFFFFFFFF;
    public const ushort WAVE_FORMAT_PCM = 1;
    public const uint CALLBACK_EVENT = 0x00050000;
    public const uint WHDR_DONE = 0x00000001;

    // Energía
    public const nint PBT_APMRESUMEAUTOMATIC = 0x0012;
    public const nint PBT_APMPOWERSTATUSCHANGE = 0x000A;
}
