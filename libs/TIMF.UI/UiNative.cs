using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Xna.Framework;
using Terraria;

namespace TIMF.UI
{
    /// <summary>
    /// OS-level helpers used by the UI: game-window focus, clipboard paste,
    /// and a WM_CHAR hook for reliable text input (bypasses Main.GetInputText guards).
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

        // ─── WM_CHAR text input hook ───────────────────────────────────────────
        // Main.GetInputText is gated by FocusHelper.AllowUIInputs and requires the
        // IImeService key listener buffer (keyInt/keyString) to be populated. In an
        // injection context these guards may silently block all input. Instead we
        // subclass the game window and capture WM_CHAR directly from the OS message
        // pump. This receives both ASCII keystrokes and IME-composed characters
        // (Chinese pinyin, etc.) regardless of Terraria's internal state.

        private const int WM_CHAR = 0x0102;

        private static readonly List<char> CharBuffer = new List<char>(16);
        private static CharHookWindow _hook;
        private static bool _hookFailed;

        /// <summary>
        /// Install the WM_CHAR subclass on the game window. Safe to call multiple times.
        /// </summary>
        public static void EnsureCharHook()
        {
            if (_hook != null || _hookFailed)
                return;

            try
            {
                IntPtr hwnd = IntPtr.Zero;
                try
                {
                    if (Main.instance != null && Main.instance.Window != null)
                        hwnd = Main.instance.Window.Handle;
                }
                catch
                {
                    // ignore
                }

                if (hwnd == IntPtr.Zero)
                    return; // window not ready yet; retry next frame

                _hook = new CharHookWindow(hwnd);
            }
            catch
            {
                _hookFailed = true;
            }
        }

        /// <summary>
        /// Drain buffered characters received since the last call.
        /// Returns chars in arrival order. Caller should apply them to the focused field.
        /// </summary>
        public static char[] DrainChars()
        {
            if (CharBuffer.Count == 0)
                return null;
            var arr = CharBuffer.ToArray();
            CharBuffer.Clear();
            return arr;
        }

        /// <summary>Discard any buffered characters (e.g. when no input is focused).</summary>
        public static void ClearChars()
        {
            CharBuffer.Clear();
        }

        public static bool IsCharHookActive => _hook != null;

        /// <summary>
        /// NativeWindow subclass that observes WM_CHAR without consuming it.
        /// All messages are forwarded to the original WndProc so the game and
        /// ReLogic's IME handler continue to function normally.
        /// </summary>
        private sealed class CharHookWindow : NativeWindow
        {
            public CharHookWindow(IntPtr hwnd)
            {
                AssignHandle(hwnd);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_CHAR)
                {
                    var c = (char)(int)m.WParam;
                    // Buffer all chars; ProcessTextInput decides what to do with them.
                    CharBuffer.Add(c);
                }
                // Always forward to the original handler.
                base.WndProc(ref m);
            }
        }
    }
}
