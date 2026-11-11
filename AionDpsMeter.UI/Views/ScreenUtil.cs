using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AionDpsMeter.UI.Views
{
    /// <summary>Work area (taskbar excluded) of the monitor a window is on, in WPF units.</summary>
    internal static class ScreenUtil
    {
        public static Rect WorkAreaOf(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var dpi = VisualTreeHelper.GetDpi(window);
            return hwnd != IntPtr.Zero && AreaOf(hwnd, dpi.DpiScaleX, dpi.DpiScaleY) is { } r ? r : SystemParameters.WorkArea;
        }

        /// <summary>
        /// Work area of the monitor the AION 2 window is on — where alerts beside the character and the game's corner
        /// toasts belong, even when the meter lives on another screen. Falls back to <paramref name="fallback"/>'s monitor.
        /// </summary>
        public static Rect GameWorkArea(Window? fallback)
        {
            var game = FindGameWindow();
            if (game != IntPtr.Zero)
            {
                double scale = GetDpiForWindow(game) / 96.0;
                if (scale > 0 && AreaOf(game, scale, scale) is { } r) return r;
            }
            return fallback is not null ? WorkAreaOf(fallback) : SystemParameters.WorkArea;
        }

        private static IntPtr FindGameWindow()
        {
            foreach (var p in Process.GetProcessesByName("AION2"))
                using (p)
                {
                    try { if (p.MainWindowTitle.StartsWith("AION2", StringComparison.Ordinal)) return p.MainWindowHandle; }
                    catch (InvalidOperationException) { }
                }
            return IntPtr.Zero;
        }

        private static Rect? AreaOf(IntPtr hwnd, double scaleX, double scaleY)
        {
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return null;
            var r = info.rcWork;
            return new Rect(r.Left / scaleX, r.Top / scaleY, (r.Right - r.Left) / scaleX, (r.Bottom - r.Top) / scaleY);
        }

        /// <summary>Moves a window so it is fully inside the given area.</summary>
        public static void KeepInside(Window window, Rect area)
        {
            double w = window.ActualWidth, h = window.ActualHeight;
            window.Left = Math.Max(area.Left, Math.Min(window.Left, area.Right - w));
            window.Top = Math.Max(area.Top, Math.Min(window.Top, area.Bottom - h));
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);
    }
}
