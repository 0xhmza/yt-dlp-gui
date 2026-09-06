using System;
using System.Runtime.InteropServices;

namespace YtDlpGui
{
    internal enum TaskbarState
    {
        NoProgress = 0,
        Indeterminate = 1,
        Normal = 2,
        Error = 4,
        Paused = 8
    }

    /// <summary>
    /// The Windows shell integration a download tool is expected to have: progress on the
    /// taskbar button, a flash when a long job finishes in the background, and keeping the
    /// machine awake while transferring. Every call is best-effort - on an OS or a session
    /// where something is unavailable the app simply carries on without it.
    /// </summary>
    internal static class Native
    {
        // ---- taskbar progress -------------------------------------------------
        [ComImport]
        [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList3
        {
            // ITaskbarList - declared in vtable order; the members below it depend on it.
            void HrInit();
            void AddTab(IntPtr hwnd);
            void DeleteTab(IntPtr hwnd);
            void ActivateTab(IntPtr hwnd);
            void SetActiveAlt(IntPtr hwnd);
            // ITaskbarList2
            void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);
            // ITaskbarList3 - the two we actually want.
            void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
            void SetProgressState(IntPtr hwnd, int state);
        }

        [ComImport]
        [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
        [ClassInterface(ClassInterfaceType.None)]
        private class TaskbarInstance { }

        private static ITaskbarList3 _taskbar;
        private static bool _taskbarTried;

        private static ITaskbarList3 Taskbar
        {
            get
            {
                if (_taskbarTried) return _taskbar;
                _taskbarTried = true;

                // ITaskbarList3 arrived in Windows 7 (6.1).
                var v = Environment.OSVersion.Version;
                if (Environment.OSVersion.Platform != PlatformID.Win32NT ||
                    v.Major < 6 || (v.Major == 6 && v.Minor < 1))
                    return null;

                try
                {
                    var t = (ITaskbarList3)new TaskbarInstance();
                    t.HrInit();
                    _taskbar = t;
                }
                catch { _taskbar = null; }
                return _taskbar;
            }
        }

        public static void SetTaskbarState(IntPtr hwnd, TaskbarState state)
        {
            var t = Taskbar;
            if (t == null || hwnd == IntPtr.Zero) return;
            try { t.SetProgressState(hwnd, (int)state); }
            catch { }
        }

        /// <summary>Fraction in 0..1. Values outside the range are clamped.</summary>
        public static void SetTaskbarProgress(IntPtr hwnd, double fraction)
        {
            var t = Taskbar;
            if (t == null || hwnd == IntPtr.Zero) return;
            if (double.IsNaN(fraction) || double.IsInfinity(fraction)) return;

            if (fraction < 0) fraction = 0;
            if (fraction > 1) fraction = 1;
            try { t.SetProgressValue(hwnd, (ulong)(fraction * 1000.0), 1000UL); }
            catch { }
        }

        // ---- keep the machine awake ------------------------------------------
        [Flags]
        private enum ExecutionState : uint
        {
            SystemRequired = 0x00000001,
            AwayModeRequired = 0x00000040,
            Continuous = 0x80000000
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SetThreadExecutionState(ExecutionState flags);

        private static bool _sleepBlocked;

        /// <summary>
        /// Holds off sleep and hibernation for the duration of a transfer. The display is
        /// deliberately left alone: there is nothing to watch, and blanking the screen during
        /// a long download is what the user wants.
        /// </summary>
        public static void KeepAwake(bool on)
        {
            if (on == _sleepBlocked) return;
            try
            {
                SetThreadExecutionState(on
                    ? ExecutionState.Continuous | ExecutionState.SystemRequired | ExecutionState.AwayModeRequired
                    : ExecutionState.Continuous);
                _sleepBlocked = on;
            }
            catch { }
        }

        // ---- attention --------------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private const uint FLASHW_ALL = 3;
        private const uint FLASHW_TIMERNOFG = 12;

        /// <summary>Flashes the taskbar button until the window is brought to the front.</summary>
        public static void FlashWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                if (GetForegroundWindow() == hwnd) return;   // already looking at it
                var fi = new FLASHWINFO
                {
                    cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO)),
                    hwnd = hwnd,
                    dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
                    uCount = uint.MaxValue,
                    dwTimeout = 0
                };
                FlashWindowEx(ref fi);
            }
            catch { }
        }

        public static bool IsForeground(IntPtr hwnd)
        {
            try { return hwnd != IntPtr.Zero && GetForegroundWindow() == hwnd; }
            catch { return false; }
        }

        // ---- misc -------------------------------------------------------------
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LockWindowUpdate(IntPtr hWndLock);

        /// <summary>Suspends painting for a control while a large batch of text is written.</summary>
        public static void SuspendDrawing(System.Windows.Forms.Control c)
        {
            try { if (c != null && c.IsHandleCreated) LockWindowUpdate(c.Handle); }
            catch { }
        }

        public static void ResumeDrawing(System.Windows.Forms.Control c)
        {
            try { LockWindowUpdate(IntPtr.Zero); if (c != null) c.Invalidate(); }
            catch { }
        }
    }
}
