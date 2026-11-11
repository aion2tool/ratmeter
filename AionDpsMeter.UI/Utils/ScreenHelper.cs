using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace AionDpsMeter.UI.Utils
{
    internal static class ScreenHelper
    {
        private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, int dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

       
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect(ref RECT rect, int dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        /// <summary>
        /// Window not on any monitor (its screen is gone, or the saved position was off on a mixed-DPI setup):
        /// moves it to the top-right of the nearest monitor. Works in pixels only, so it can't be fooled by scaling.
        /// </summary>
        public static void EnsureOnScreen(Window window)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return;
            if (MonitorFromRect(ref r, 0 /* MONITOR_DEFAULTTONULL */) != IntPtr.Zero) return;
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(MonitorFromRect(ref r, MONITOR_DEFAULTTONEAREST), ref mi)) return;
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            int x = Math.Max(mi.rcWork.Left, mi.rcWork.Right - w - 40);
            int y = Math.Max(mi.rcWork.Top, Math.Min(r.Top, mi.rcWork.Bottom - h));
            SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010); // NOSIZE | NOZORDER | NOACTIVATE
        }

        public static Rect GetWorkingAreaForPoint(double x, double y)
        {
            var pt = new POINT { X = (int)x, Y = (int)y };
            var hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            return GetWorkArea(hMonitor, 96.0, 96.0);
        }

        public static Rect GetWorkingAreaForWindow(Window window)
        {
            GetDpi(window, out double dpiX, out double dpiY);

            var helper = new System.Windows.Interop.WindowInteropHelper(window);
            IntPtr hMonitor;
            if (helper.Handle != IntPtr.Zero)
            {
                hMonitor = MonitorFromWindow(helper.Handle, MONITOR_DEFAULTTONEAREST);
            }
            else
            {
                var pt = new POINT
                {
                    X = (int)(window.Left * dpiX / 96.0),
                    Y = (int)(window.Top  * dpiY / 96.0)
                };
                hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            }

            return GetWorkArea(hMonitor, dpiX, dpiY);
        }

        private static Rect GetWorkArea(IntPtr hMonitor, double dpiX, double dpiY)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                var wa = mi.rcWork;
                return new Rect(
                    wa.Left   * 96.0 / dpiX,
                    wa.Top    * 96.0 / dpiY,
                    (wa.Right  - wa.Left) * 96.0 / dpiX,
                    (wa.Bottom - wa.Top)  * 96.0 / dpiY);
            }
            return new Rect(0, 0, SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
        }

        private static void GetDpi(Visual visual, out double dpiX, out double dpiY)
        {
            try
            {
                var src = PresentationSource.FromVisual(visual);
                if (src?.CompositionTarget != null)
                {
                    dpiX = 96.0 * src.CompositionTarget.TransformToDevice.M11;
                    dpiY = 96.0 * src.CompositionTarget.TransformToDevice.M22;
                    return;
                }
            }
            catch { }
            dpiX = 96.0;
            dpiY = 96.0;
        }
    }
}
