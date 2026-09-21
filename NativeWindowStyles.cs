using System.Runtime.InteropServices;

namespace CodexInputEnhancer;

internal static class NativeWindowStyles
{
    internal const int GwlExStyle = -20;
    internal const int WsExNoActivate = 0x08000000;
    internal const int WsExToolWindow = 0x00000080;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll")]
    internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    internal static bool IsSameProcess(IntPtr firstWindow, IntPtr secondWindow)
    {
        if (firstWindow == IntPtr.Zero || secondWindow == IntPtr.Zero) return false;

        _ = GetWindowThreadProcessId(firstWindow, out var firstProcessId);
        _ = GetWindowThreadProcessId(secondWindow, out var secondProcessId);
        return firstProcessId != 0 && firstProcessId == secondProcessId;
    }

}
