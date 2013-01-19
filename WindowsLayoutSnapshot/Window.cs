using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowsLayoutSnapshot {

    // Open source
    // Imported by: Adam Smith
    // Imported on: 8/9/2012
    // Imported from: http://www.codeproject.com/Articles/2286/Window-Hiding-with-C
    // License: CPOL (liberal)
    // Modifications: cleanup

    public class Window {

        private IntPtr m_hWnd;
        private string m_Title;
        private bool m_Visible = true;
        private string m_Process;
        private bool m_WasMax = false;

        public Window(string Title, IntPtr hWnd, string Process) {
            m_Title = Title;
            m_hWnd = hWnd;
            m_Process = Process;
        }

        public IntPtr hWnd {
            get { return m_hWnd; }
        }

        public string Title {
            get { return m_Title; }
        }

        public string Process {
            get { return m_Process; }
        }

        public bool Visible {
            get { return m_Visible; }
            set {
                //show the window
                if (value) {
                    if (m_WasMax) {
                        if (ShowWindowAsync(m_hWnd, SW_SHOWMAXIMIZED))
                            m_Visible = true;
                    } else {
                        if (ShowWindowAsync(m_hWnd, SW_SHOWNORMAL))
                            m_Visible = true;
                    }
                } else {
                    m_WasMax = IsZoomed(m_hWnd);
                    if (ShowWindowAsync(m_hWnd, SW_HIDE))
                        m_Visible = false;
                }
            }
        }

        public void Activate() {
            if (m_hWnd == GetForegroundWindow()) {
                return;
            }

            IntPtr ThreadID1 = GetWindowThreadProcessId(GetForegroundWindow(),
                                                        IntPtr.Zero);
            IntPtr ThreadID2 = GetWindowThreadProcessId(m_hWnd, IntPtr.Zero);

            if (ThreadID1 != ThreadID2) {
                AttachThreadInput(ThreadID1, ThreadID2, 1);
                SetForegroundWindow(m_hWnd);
                AttachThreadInput(ThreadID1, ThreadID2, 0);
            } else {
                SetForegroundWindow(m_hWnd);
            }

            if (IsIconic(m_hWnd)) {
                ShowWindowAsync(m_hWnd, SW_RESTORE);
            } else {
                ShowWindowAsync(m_hWnd, SW_SHOWNORMAL);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);
        [DllImport("user32.dll")]
        private static extern IntPtr AttachThreadInput(IntPtr idAttach, IntPtr idAttachTo, int fAttach);

        private const int SW_HIDE = 0;
        private const int SW_SHOWNORMAL = 1;
        private const int SW_SHOWMINIMIZED = 2;
        private const int SW_SHOWMAXIMIZED = 3;
        private const int SW_SHOWNOACTIVATE = 4;
        private const int SW_RESTORE = 9;
        private const int SW_SHOWDEFAULT = 10;
    }
}
