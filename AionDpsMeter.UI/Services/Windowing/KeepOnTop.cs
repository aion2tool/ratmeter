using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AionDpsMeter.UI.Services.Windowing;

/// <summary>
/// AION 2 pushes its own window above "always on top" windows (seen 2026-10-15): the meter kept running but
/// sat behind the game until restarted. Every 2 s this puts each visible topmost window of the meter back on top,
/// without activating, moving or resizing it.
/// </summary>
internal static class KeepOnTop
{
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_NOOWNERZORDER = 0x0200;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    public static void Start(Application app)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => RaiseAll(app);
        timer.Start();
    }

    private static void RaiseAll(Application app)
    {
        foreach (Window w in app.Windows)
        {
            if (!w.Topmost || !w.IsVisible || w.WindowState == WindowState.Minimized) continue;
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }
    }
}
