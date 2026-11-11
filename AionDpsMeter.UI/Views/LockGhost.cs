using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AionDpsMeter.UI.Views
{
    /// <summary>
    /// Locked meter = "ghost": every meter window (meter, side meter, drawers, ⏱ tab) turns see-through
    /// and lets every click through to the game, so game menus that pop up underneath stay readable and
    /// clickable. Holding Alt (shows the cursor in AION 2) brings them back to full opacity and clickable.
    /// </summary>
    internal static class LockGhost
    {
        /// <param name="clickThrough">False for windows that are always click-through anyway (network panel): opacity only.</param>
        public static void Apply(Window? window, bool ghost, double opacity, bool clickThrough = true)
        {
            if (window is null) return;
            double target = ghost ? Math.Clamp(opacity, 0.1, 1.0) : 1.0;
            if (Math.Abs(window.Opacity - target) > 0.001) window.Opacity = target;
            if (!clickThrough) return;

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int style = GetWindowLong(hwnd, GWL_EXSTYLE);
            int wanted = ghost ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
            if (wanted != style) SetWindowLong(hwnd, GWL_EXSTYLE, wanted);
        }

        public static bool AltHeld() => (GetAsyncKeyState(0x12) & 0x8000) != 0; // VK_MENU

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}
