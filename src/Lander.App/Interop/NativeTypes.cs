using System.Runtime.InteropServices;

namespace Lander.App.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly Core.Geometry.PixelRect ToPixelRect() => new(Left, Top, Right, Bottom);
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;

    public POINT(int x, int y)
    {
        X = x;
        Y = y;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct SIZE
{
    public int cx;
    public int cy;

    public SIZE(int width, int height)
    {
        cx = width;
        cy = height;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public nint hwnd;
    public uint message;
    public nint wParam;
    public nint lParam;
    public uint time;
    public POINT pt;
    public uint lPrivate;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MONITORINFOEXW
{
    public uint cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
    public fixed char szDevice[32];
}

[StructLayout(LayoutKind.Sequential)]
internal struct LASTINPUTINFO
{
    public uint cbSize;
    public uint dwTime;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WNDCLASSEXW
{
    public uint cbSize;
    public uint style;
    public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public nint hInstance;
    public nint hIcon;
    public nint hCursor;
    public nint hbrBackground;
    public char* lpszMenuName;
    public char* lpszClassName;
    public nint hIconSm;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BLENDFUNCTION
{
    public byte BlendOp;
    public byte BlendFlags;
    public byte SourceConstantAlpha;
    public byte AlphaFormat;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public uint biSize;
    public int biWidth;
    public int biHeight;
    public ushort biPlanes;
    public ushort biBitCount;
    public uint biCompression;
    public uint biSizeImage;
    public int biXPelsPerMeter;
    public int biYPelsPerMeter;
    public uint biClrUsed;
    public uint biClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ICONINFO
{
    public int fIcon;
    public int xHotspot;
    public int yHotspot;
    public nint hbmMask;
    public nint hbmColor;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SYSTEM_POWER_STATUS
{
    public byte ACLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    public uint BatteryLifeTime;
    public uint BatteryFullLifeTime;
}

[StructLayout(LayoutKind.Sequential)]
internal struct APPBARDATA
{
    public uint cbSize;
    public nint hWnd;
    public uint uCallbackMessage;
    public uint uEdge;
    public RECT rc;
    public nint lParam;
}

/// <summary>NOTIFYICONDATAW (solo x64/arm64: alineación natural).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NOTIFYICONDATAW
{
    public uint cbSize;
    public nint hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public nint hIcon;
    public fixed char szTip[128];
    public uint dwState;
    public uint dwStateMask;
    public fixed char szInfo[256];
    public uint uVersion;
    public fixed char szInfoTitle[64];
    public uint dwInfoFlags;
    public Guid guidItem;
    public nint hBalloonIcon;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SHQUERYRBINFO
{
    public uint cbSize;
    public long i64Size;
    public long i64NumItems;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SHChangeNotifyEntry
{
    public nint pidl;
    public int fRecursive;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SHFILEINFOW
{
    public nint hIcon;
    public int iIcon;
    public uint dwAttributes;
    public fixed char szDisplayName[260];
    public fixed char szTypeName[80];
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WAVEFORMATEX
{
    public ushort wFormatTag;
    public ushort nChannels;
    public uint nSamplesPerSec;
    public uint nAvgBytesPerSec;
    public ushort nBlockAlign;
    public ushort wBitsPerSample;
    public ushort cbSize;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WAVEHDR
{
    public byte* lpData;
    public uint dwBufferLength;
    public uint dwBytesRecorded;
    public nint dwUser;
    public uint dwFlags;
    public uint dwLoops;
    public WAVEHDR* lpNext;
    public nint reserved;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RAWINPUTDEVICE
{
    public ushort usUsagePage;
    public ushort usUsage;
    public uint dwFlags;
    public nint hwndTarget;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RAWINPUTHEADER
{
    public uint dwType;
    public uint dwSize;
    public nint hDevice;
    public nint wParam;
}

/// <summary>RAWMOUSE: la unión de botones está alineada a 4 bytes.</summary>
[StructLayout(LayoutKind.Explicit)]
internal struct RAWMOUSE
{
    [FieldOffset(0)]
    public ushort usFlags;

    [FieldOffset(4)]
    public ushort usButtonFlags;

    [FieldOffset(6)]
    public ushort usButtonData;

    [FieldOffset(8)]
    public uint ulRawButtons;

    [FieldOffset(12)]
    public int lLastX;

    [FieldOffset(16)]
    public int lLastY;

    [FieldOffset(20)]
    public uint ulExtraInformation;
}
