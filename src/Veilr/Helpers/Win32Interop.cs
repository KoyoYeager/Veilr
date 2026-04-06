using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Veilr.Helpers;

public static class Win32Interop
{
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private static readonly nint HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;

    public static void SetClickThrough(Window window, bool enable)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

        if (enable)
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        else
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
    }

    public static void SetTopmost(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
    }

    public static (int Width, int Height) GetScreenSize(int monitorIndex = 0)
    {
        // TODO: multi-monitor support via EnumDisplayMonitors
        int w = GetSystemMetrics(0); // SM_CXSCREEN
        int h = GetSystemMetrics(1); // SM_CYSCREEN
        return (w, h);
    }
}
