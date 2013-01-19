using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace WindowsLayoutSnapshot {

    internal class Snapshot {

        private Dictionary<IntPtr, WINDOWPLACEMENT> m_placements = new Dictionary<IntPtr, WINDOWPLACEMENT>();
        private List<IntPtr> m_windowsBackToTop = new List<IntPtr>();

        private Snapshot(bool userInitiated) {
            EnumWindows(EvalWindow, 0);

            TimeTaken = DateTime.UtcNow;
            UserInitiated = userInitiated;

            var pixels = new List<long>();
            foreach (var screen in Screen.AllScreens) {
                pixels.Add(screen.Bounds.Width * screen.Bounds.Height);
            }
            MonitorPixelCounts = pixels.ToArray();
            NumMonitors = pixels.Count;
        }

        internal static Snapshot TakeSnapshot(bool userInitiated) {
            return new Snapshot(userInitiated);
        }

        private bool EvalWindow(int hwndInt, int lParam) {
            var hwnd = new IntPtr(hwndInt);

            if (!IsAltTabWindow(hwnd)) {
                return true;
            }

            // EnumWindows returns windows in Z order from back to front
            m_windowsBackToTop.Add(hwnd);

            var placement = new WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
            if (!GetWindowPlacement(hwnd, ref placement)) {
                throw new Exception("Error getting window placement");
            }
            m_placements.Add(hwnd, placement);

            return true;
        }

        internal DateTime TimeTaken { get; private set; }
        internal bool UserInitiated { get; private set; }
        internal long[] MonitorPixelCounts { get; private set; }
        internal int NumMonitors { get; private set; }

        internal TimeSpan Age {
            get { return DateTime.UtcNow.Subtract(TimeTaken); }
        }

        internal void RestoreAndPreserveMenu(object sender, EventArgs e) { // ignore extra params
            // We save and restore the current foreground window because it's our tray menu
            // I couldn't find a way to get this handle straight from the tray menu's properties;
            //   the ContextMenuStrip.Handle isn't the right one, so I'm using win32
            // More info RE the restore is below, where we do it
            var currentForegroundWindow = GetForegroundWindow();

            try {
                Restore(sender, e);
            } finally {
                // A combination of SetForegroundWindow + SetWindowPos (via set_Visible) seems to be needed
                // This was determined by trying a bunch of stuff
                // This prevents the tray menu from closing, and makes sure it's still on top
                SetForegroundWindow(currentForegroundWindow);
                TrayIconForm.me.Visible = true;
            }
        }

        internal void Restore(object sender, EventArgs e) { // ignore extra params
            // first, restore the window rectangles and normal/maximized/minimized states
            foreach (var placement in m_placements) {
                // this might error out if the window no longer exists
                var placementValue = placement.Value;

                // make sure points and rects will be inside monitor
                IntPtr extendedStyles = GetWindowLongPtr(placement.Key, (-20)); // GWL_EXSTYLE
                placementValue.ptMaxPosition = GetUpperLeftCornerOfNearestMonitor(extendedStyles, placementValue.ptMaxPosition);
                placementValue.ptMinPosition = GetUpperLeftCornerOfNearestMonitor(extendedStyles, placementValue.ptMinPosition);
                placementValue.rcNormalPosition = GetRectInsideNearestMonitor(extendedStyles, placementValue.rcNormalPosition);

                SetWindowPlacement(placement.Key, ref placementValue);
            }

            // now update the z-orders
            m_windowsBackToTop = m_windowsBackToTop.FindAll(IsWindowVisible);
            IntPtr positionStructure = BeginDeferWindowPos(m_windowsBackToTop.Count);
            for (int i = 0; i < m_windowsBackToTop.Count; i++) {
                positionStructure = DeferWindowPos(positionStructure, m_windowsBackToTop[i], i == 0 ? IntPtr.Zero : m_windowsBackToTop[i - 1],
                    0, 0, 0, 0, DeferWindowPosCommands.SWP_NOMOVE | DeferWindowPosCommands.SWP_NOSIZE | DeferWindowPosCommands.SWP_NOACTIVATE);
            }
            EndDeferWindowPos(positionStructure);
        }

        private static Point GetUpperLeftCornerOfNearestMonitor(IntPtr windowExtendedStyles, Point point) {
            if ((windowExtendedStyles.ToInt64() & 0x00000080) > 0) { // WS_EX_TOOLWINDOW
                return Screen.GetBounds(point).Location; // use screen coordinates
            } else {
                return Screen.GetWorkingArea(point).Location; // use workspace coordinates
            }
        }

        private static RECT GetRectInsideNearestMonitor(IntPtr windowExtendedStyles, RECT rect) {
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            Rectangle rectAsRectangle = new Rectangle(rect.Left, rect.Top, width, height);
            Rectangle monitorRect;
            if ((windowExtendedStyles.ToInt64() & 0x00000080) > 0) { // WS_EX_TOOLWINDOW
                monitorRect = Screen.GetBounds(rectAsRectangle); // use screen coordinates
            } else {
                monitorRect = Screen.GetWorkingArea(rectAsRectangle); // use workspace coordinates
            }

            var y = new RECT();
            y.Left = Math.Max(monitorRect.Left, Math.Min(monitorRect.Right - width, rect.Left));
            y.Top = Math.Max(monitorRect.Top, Math.Min(monitorRect.Bottom - height, rect.Top));
            y.Right = y.Left + Math.Min(monitorRect.Width, width);
            y.Bottom = y.Top + Math.Min(monitorRect.Height, height);
            return y;
        }

        private static bool IsAltTabWindow(IntPtr hwnd) {
            if (!IsWindowVisible(hwnd)) {
                return false;
            }

            IntPtr extendedStyles = GetWindowLongPtr(hwnd, (-20)); // GWL_EXSTYLE
            if ((extendedStyles.ToInt64() & 0x00040000) > 0) { // WS_EX_APPWINDOW
                return true;
            }
            if ((extendedStyles.ToInt64() & 0x00000080) > 0) { // WS_EX_TOOLWINDOW
                return false;
            }

            IntPtr hwndTry = GetAncestor(hwnd, GetAncestor_Flags.GetRootOwner);
            IntPtr hwndWalk = IntPtr.Zero;
            while (hwndTry != hwndWalk) {
                hwndWalk = hwndTry;
                hwndTry = GetLastActivePopup(hwndWalk);
                if (IsWindowVisible(hwndTry)) {
                    break;
                }
            }
            if (hwndWalk != hwnd) {
                return false;
            }

            return true;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr BeginDeferWindowPos(int nNumWindows);

        [DllImport("user32.dll")]
        private static extern IntPtr DeferWindowPos(IntPtr hWinPosInfo, IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy,
            [MarshalAs(UnmanagedType.U4)]DeferWindowPosCommands uFlags);

        private enum DeferWindowPosCommands : uint {
            SWP_DRAWFRAME = 0x0020,
            SWP_FRAMECHANGED = 0x0020,
            SWP_HIDEWINDOW = 0x0080,
            SWP_NOACTIVATE = 0x0010,
            SWP_NOCOPYBITS = 0x0100,
            SWP_NOMOVE = 0x0002,
            SWP_NOOWNERZORDER = 0x0200,
            SWP_NOREDRAW = 0x0008,
            SWP_NOREPOSITION = 0x0200,
            SWP_NOSENDCHANGING = 0x0400,
            SWP_NOSIZE = 0x0001,
            SWP_NOZORDER = 0x0004,
            SWP_SHOWWINDOW = 0x0040
        };

        [DllImport("user32.dll")]
        private static extern bool EndDeferWindowPos(IntPtr hWinPosInfo);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) {
            if (IntPtr.Size == 8) {
                return GetWindowLongPtr64(hWnd, nIndex);
            }
            return GetWindowLongPtr32(hWnd, nIndex);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetLastActivePopup(IntPtr hWnd);

        private enum GetAncestor_Flags {
            GetParent = 1,
            GetRoot = 2,
            GetRootOwner = 3
        }

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern IntPtr GetAncestor(IntPtr hwnd, GetAncestor_Flags gaFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPlacement(IntPtr hWnd, [In] ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT {
            public int length;
            public int flags;
            public int showCmd;
            public Point ptMinPosition;
            public Point ptMaxPosition;
            public RECT rcNormalPosition;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int EnumWindows(EnumWindowsProc ewp, int lParam);
        private delegate bool EnumWindowsProc(int hWnd, int lParam);
    }
}
