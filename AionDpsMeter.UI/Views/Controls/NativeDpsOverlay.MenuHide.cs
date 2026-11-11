using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace AionDpsMeter.UI.Views.Controls
{
    /// <summary>
    /// Hide In Menus: AION 2 hides its cursor and locks it to the screen centre while you play (camera mode)
    /// and shows a free cursor in menus (skills, stigma, settings...). So while the game is in front, its cursor
    /// has been free for a moment and nobody has dealt damage for a few seconds, the meter fades out; it comes
    /// back when the cursor is captured again or on the next hit. Holding Alt (to click the locked meter) or
    /// hovering the meter keeps it shown. Free-cursor play out of combat also counts as "menu".
    /// </summary>
    public sealed partial class NativeDpsOverlay
    {
        private static readonly TimeSpan MenuCursorFor = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan MenuCombatGrace = TimeSpan.FromSeconds(5);
        private DispatcherTimer? menuTick;
        private DateTime cursorFreeSince = DateTime.MaxValue;
        private bool menuHidden;

        private void EnsureMenuTick()
        {
            if (menuTick is not null) return;
            menuTick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            menuTick.Tick += (_, _) => UpdateMenuHide();
            menuTick.Start();
        }

        private void UpdateMenuHide()
        {
            if (disposed) { menuTick?.Stop(); return; }
            var now = DateTime.Now;
            bool cursorFree = settingsService.HideInMenus && GameInFront() && CursorShowing();
            if (!cursorFree) cursorFreeSince = DateTime.MaxValue;
            else if (cursorFreeSince == DateTime.MaxValue) cursorFreeSince = now;

            combatSessions ??= App.AppHost.Services.GetService(typeof(AionDpsMeter.Services.Services.Session.CombatSessionManager))
                as AionDpsMeter.Services.Services.Session.CombatSessionManager;
            bool inCombat = combatSessions is not null && now - combatSessions.LastDamageAt < MenuCombatGrace;
            bool altHeld = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

            bool hide = cursorFree && now - cursorFreeSince >= MenuCursorFor && !inCombat && !altHeld && !IsMouseOver;
            if (hide == menuHidden) return;
            menuHidden = hide;
            ApplyFade();
        }

        private static bool CursorShowing()
        {
            var info = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
            return GetCursorInfo(ref info) && (info.flags & CURSOR_SHOWING) != 0;
        }

        /// <summary>The foreground window is the AION 2 client (title "AION2").</summary>
        private static bool GameInFront()
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            var title = new System.Text.StringBuilder(32);
            GetWindowText(hwnd, title, title.Capacity);
            return title.ToString().StartsWith("AION2", StringComparison.Ordinal);
        }

        private const int CURSOR_SHOWING = 0x1;
        private const int VK_MENU = 0x12; // Alt

        [StructLayout(LayoutKind.Sequential)]
        private struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public int x, y;
        }

        [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO pci);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
    }
}
