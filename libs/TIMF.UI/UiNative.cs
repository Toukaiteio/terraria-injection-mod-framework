using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Xna.Framework;
using Terraria;

namespace TIMF.UI
{
    /// <summary>
    /// OS-level helpers used by the UI: game-window focus, clipboard paste.
    /// </summary>
    internal static class UiNative
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public static bool IsOurProcessFocused()
        {
            try
            {
                var fg = GetForegroundWindow();
                if (fg == IntPtr.Zero)
                    return false;
                uint pid;
                GetWindowThreadProcessId(fg, out pid);
                return pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            }
            catch
            {
                // Fall back to XNA Game.IsActive when available.
                try
                {
                    return Main.instance != null && Main.instance.IsActive;
                }
                catch
                {
                    return true;
                }
            }
        }

        public static string GetClipboardText()
        {
            try
            {
                if (!Clipboard.ContainsText())
                    return null;
                return Clipboard.GetText(TextDataFormat.UnicodeText);
            }
            catch
            {
                return null;
            }
        }
    }
}
