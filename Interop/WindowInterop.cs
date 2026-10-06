using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Lutris.Interop;

/// <summary>
/// The little Win32 the app still needs: the window's DPI before it is shown, a minimum size
/// (WinUI has no property for it) and the mouse wheel setting.
/// </summary>
internal static class WindowInterop
{
    private const int GWLP_WNDPROC = -4;
    private const uint WM_GETMINMAXINFO = 0x0024;
    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint SPI_GETWHEELSCROLLLINES = 0x0068;

    // Delegates must stay referenced for as long as the window exists.
    private static readonly List<WndProc> KeepAlive = new();

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public static double GetDpiScale(Window window) =>
        GetDpiForWindow(WindowNative.GetWindowHandle(window)) / 96.0;

    /// <summary>Effective DPI scale of the monitor that hosts the given display area.</summary>
    public static double GetDpiScale(DisplayArea displayArea)
    {
        var monitor = Win32Interop.GetMonitorFromDisplayId(displayArea.DisplayId);
        if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0)
        {
            return dpiX / 96.0;
        }
        return 1.0;
    }

    public static void SetMinimumSize(Window window, int minWidth, int minHeight)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var previous = IntPtr.Zero;

        WndProc proc = (h, msg, wParam, lParam) =>
        {
            if (msg == WM_GETMINMAXINFO)
            {
                var scale = GetDpiForWindow(h) / 96.0;
                var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                info.ptMinTrackSize.X = (int)(minWidth * scale);
                info.ptMinTrackSize.Y = (int)(minHeight * scale);
                Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
            }
            return CallWindowProc(previous, h, msg, wParam, lParam);
        };
        KeepAlive.Add(proc);

        var pointer = Marshal.GetFunctionPointerForDelegate(proc);
        previous = IntPtr.Size == 8
            ? SetWindowLongPtr64(hwnd, GWLP_WNDPROC, pointer)
            : SetWindowLong32(hwnd, GWLP_WNDPROC, pointer);
    }

    /// <summary>
    /// The "lines to scroll" mouse setting: lines per wheel notch, or <see cref="uint.MaxValue"/> when
    /// Windows is set to scroll one screen at a time.
    /// </summary>
    public static uint GetWheelScrollLines() =>
        SystemParametersInfo(SPI_GETWHEELSCROLLLINES, 0, out var lines, 0) ? lines : 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out uint pvParam, uint fWinIni);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
