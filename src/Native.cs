using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace CovertCrew
{
    // Thin wrappers over the Win32 calls the crew needs to find windows to stand on.
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO i);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int idx, int v);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        // Re-assert always-on-top without moving or activating the window.
        public static void BringTopmost(IntPtr h)
        {
            SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x80;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        static readonly uint OwnPid = (uint)Process.GetCurrentProcess().Id;

        // Physical pixels per WPF unit (DIP).
        public static double Dpi = 1.0;

        public static string Title(IntPtr h)
        {
            var sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string ClassOf(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static bool IsOwn(IntPtr h)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            return pid == OwnPid;
        }

        public static bool IsCloaked(IntPtr h)
        {
            int v;
            return DwmGetWindowAttribute(h, 14, out v, 4) == 0 && v != 0;
        }

        // Visible frame of a window, in DIPs.
        public static Rect Frame(IntPtr h)
        {
            RECT r;
            if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))) != 0) GetWindowRect(h, out r);
            return new Rect(r.Left / Dpi, r.Top / Dpi, Math.Max(0, r.Right - r.Left) / Dpi, Math.Max(0, r.Bottom - r.Top) / Dpi);
        }

        public static Point Cursor()
        {
            POINT p;
            GetCursorPos(out p);
            return new Point(p.X / Dpi, p.Y / Dpi);
        }

        // Top-level window under a point given in DIPs.
        public static IntPtr TopWindowAt(double x, double y)
        {
            var p = new POINT { X = (int)Math.Round(x * Dpi), Y = (int)Math.Round(y * Dpi) };
            IntPtr h = WindowFromPoint(p);
            if (h == IntPtr.Zero) return h;
            IntPtr root = GetAncestor(h, 2);
            return root == IntPtr.Zero ? h : root;
        }

        public static uint IdleMs()
        {
            var info = new LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf(info);
            if (!GetLastInputInfo(ref info)) return 0;
            return (uint)Environment.TickCount - info.dwTime;
        }

        static readonly string[] ShellClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "Windows.UI.Core.CoreWindow" };

        // Can a character stand on top of this window?
        public static bool IsLandable(IntPtr h)
        {
            if (h == IntPtr.Zero || !IsWindow(h) || !IsWindowVisible(h) || IsIconic(h)) return false;
            if (IsOwn(h) || IsCloaked(h)) return false;
            if (Title(h).Length == 0) return false;
            string cls = ClassOf(h);
            foreach (var s in ShellClasses) if (cls == s) return false;
            return true;
        }

        public static bool IsShell(IntPtr h)
        {
            string cls = ClassOf(h);
            foreach (var s in ShellClasses) if (cls == s) return true;
            return false;
        }
    }
}
