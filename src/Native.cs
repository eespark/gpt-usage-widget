using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexUsageTaskbar
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect
        {
            public int Left, Top, Right, Bottom;
            public Rectangle Rectangle { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } }
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [StructLayout(LayoutKind.Sequential)] internal struct ComboBoxInfo {public int Size;public Rect Item,Button;public uint State;public IntPtr Combo,Edit,List;}
        [DllImport("user32.dll")] internal static extern bool GetComboBoxInfo(IntPtr combo,ref ComboBoxInfo info);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        public static void EnableDpi()
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { SetProcessDPIAware(); }
        }
        public static float Dpi(IntPtr window)
        {
            try { uint dpi = GetDpiForWindow(window); return dpi == 0 ? 1 : dpi / 96f; } catch (EntryPointNotFoundException) { return 1; }
        }
        public static Rectangle GetPlacement(Rectangle bar, Rectangle notification, float scale, int offset, bool above, int logicalWidth = Widget.LogicalWidth)
        {
            int width = (int)Math.Round(logicalWidth * scale);
            int height = (int)Math.Round(40 * scale);
            int margin = Math.Max(2, (int)Math.Round(4 * scale));
            int right = notification.Width > 0 ? notification.Left : bar.Right - (int)(180 * scale);
            int x = Math.Max(bar.Left + margin, Math.Min(bar.Right - width - margin, right - width - margin - (int)(offset * scale)));
            int y = above ? bar.Top - height - margin : bar.Top + (bar.Height - height) / 2;
            if (bar.Height > bar.Width)
            {
                // Vertical taskbars have insufficient width for the horizontal card.
                var screen = Screen.FromRectangle(bar).Bounds;
                x = bar.Left <= screen.Left ? bar.Right + margin : bar.Left - width - margin;
                y = Math.Max(bar.Top + margin, bar.Bottom - height - (int)(180 * scale));
            }
            return new Rectangle(x, y, width, height);
        }
        public static bool IsFullscreen(IntPtr ownWindow, IntPtr taskbar)
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == ownWindow || foreground == taskbar) return false;
            // Desktop and taskbar should keep the card visible.
            if (foreground == FindWindow("Progman", null) || foreground == FindWindow("WorkerW", null)) return false;
            Rect rect;
            if (!GetWindowRect(foreground, out rect)) return false;
            Rectangle screen = Screen.FromHandle(taskbar).Bounds;
            return rect.Left <= screen.Left && rect.Top <= screen.Top && rect.Right >= screen.Right && rect.Bottom >= screen.Bottom;
        }
    }
}
