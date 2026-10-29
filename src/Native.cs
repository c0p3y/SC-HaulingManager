// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace HaulingManager
{
    internal static class NativeMethods
    {
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr h);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hwnd, string subApp, string idList);

        public static bool IsCurrentProcessElevated()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        // true = elevated, false = not elevated, null = could not tell.
        // A normal process cannot open the token of an elevated one, which is itself the answer.
        public static bool? IsProcessElevated(int pid)
        {
            IntPtr h = IntPtr.Zero, tok = IntPtr.Zero;
            try
            {
                h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
                if (h == IntPtr.Zero) return null;
                if (!OpenProcessToken(h, 0x0008 /* TOKEN_QUERY */, out tok))
                    return Marshal.GetLastWin32Error() == 5 ? (bool?)true : null;
                int elevated, ret;
                if (GetTokenInformation(tok, 20 /* TokenElevation */, out elevated, 4, out ret)) return elevated != 0;
                return null;
            }
            catch { return null; }
            finally
            {
                if (tok != IntPtr.Zero) CloseHandle(tok);
                if (h != IntPtr.Zero) CloseHandle(h);
            }
        }

        public static void UseDarkTitleBar(IntPtr hwnd)
        {
            try
            {
                int on = Theme.IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, 4);
            }
            catch { }
        }

        public static void UseDarkScrollbars(IntPtr hwnd)
        {
            try { SetWindowTheme(hwnd, Theme.IsDark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }
    }

    // Low-level keyboard hook: sees keys even while a full-screen game has focus (unlike RegisterHotKey,
    // which games that use raw input can switch off). It only watches for the configured combo and
    // always passes every key on to the game untouched.
    internal sealed class KeyboardHook : IDisposable
    {
        const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        readonly NativeMethods.LowLevelKeyboardProc proc;   // kept in a field so the GC cannot collect it
        IntPtr hook = IntPtr.Zero;
        public int Key, Mods;
        public event EventHandler Pressed;

        public KeyboardHook() { proc = HookProc; }

        public bool Install()
        {
            if (hook != IntPtr.Zero) return true;
            using (Process p = Process.GetCurrentProcess())
            using (ProcessModule m = p.MainModule)
                hook = NativeMethods.SetWindowsHookEx(WH_KEYBOARD_LL, proc, NativeMethods.GetModuleHandle(m.ModuleName), 0);
            return hook != IntPtr.Zero;
        }

        public void Uninstall()
        {
            if (hook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        }

        static bool Down(int vk) { return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0; }

        IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && Key != 0 && (wParam.ToInt32() == WM_KEYDOWN || wParam.ToInt32() == WM_SYSKEYDOWN))
                {
                    int vk = Marshal.ReadInt32(lParam);
                    if (vk == (Key & 0xFFFF)
                        && ((Mods & 2) != 0) == Down(0x11)
                        && ((Mods & 1) != 0) == Down(0x12)
                        && ((Mods & 4) != 0) == Down(0x10)
                        && ((Mods & 8) != 0) == (Down(0x5B) || Down(0x5C)))
                    {
                        EventHandler h = Pressed;
                        if (h != null) h(this, EventArgs.Empty);
                    }
                }
            }
            catch { }
            return NativeMethods.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        public void Dispose() { Uninstall(); }
    }
}
