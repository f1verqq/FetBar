// Global keys: Win+1..9 (open bar apps), the Win key (peek at an auto-hidden bar)
// and Alt+Space (launcher). A low-level keyboard hook on its own thread, so a busy
// UI thread can never make typing lag.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace StayBar
{
    static class Hooks
    {
        const int WM_HOOK_WINNUM = Native.WM_APP + 1, WM_HOOK_PEEK = Native.WM_APP + 2, WM_HOOK_LAUNCHER = Native.WM_APP + 3,
                  WM_HOOK_EDIT = Native.WM_APP + 4, WM_HOOK_PROFILE = Native.WM_APP + 5;
        const byte DummyKey = 0xE8;   // unassigned; tapping it stops Start / menus from opening on key-up

        static Thread thread;
        static uint threadId;
        static IntPtr hook, target;
        static readonly Native.LowLevelProc proc = Proc;   // keep the delegate alive
        static volatile bool winKeys, winPeek, launcher, allowInjected, barKeys;
        static bool lwin, rwin;
        static bool Pressed(int vk) { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; }
        static readonly HashSet<uint> swallowed = new HashSet<uint>();

        public static void Start(IntPtr bar)
        {
            target = bar;
            Configure();
            if (thread != null) return;
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "StayBar.Keys";
            thread.Start();
        }

        public static void Configure()
        {
            winKeys = Config.WinKeys;
            winPeek = Config.WinPeek;
            launcher = Config.LauncherKey;
            barKeys = Config.BarKeys;
            allowInjected = Settings.Get("debug.hookinjected", "false") == "true";   // for automated tests only
        }

        public static void Stop()
        {
            if (threadId != 0) Native.PostThreadMessage(threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        static void Run()
        {
            threadId = Native.GetCurrentThreadId();
            hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, proc, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) { Program.Log("Keyboard hook failed: " + Marshal.GetLastWin32Error()); return; }
            Native.MSG msg;
            while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
            }
            Native.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }

        static void Dummy()
        {
            Native.keybd_event(DummyKey, 0, 0, UIntPtr.Zero);
            Native.keybd_event(DummyKey, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        static IntPtr Proc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    var k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                    bool injected = (k.flags & Native.LLKHF_INJECTED) != 0;
                    if ((!injected || (allowInjected && k.vkCode != DummyKey)) && Handle(wParam.ToInt32(), k.vkCode)) return new IntPtr(1);
                }
                catch { }
            }
            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        // Returns true to swallow the key.
        static bool Handle(int msg, uint vk)
        {
            bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
            bool up = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;
            if (vk == 0x5B || vk == 0x5C)
            {
                // only used to peek at an auto-hidden bar; a missed key-up here is harmless
                bool wasDown = lwin || rwin;
                if (vk == 0x5B) lwin = down; else rwin = down;
                if (down && !wasDown && winPeek) Native.PostMessage(target, WM_HOOK_PEEK, IntPtr.Zero, IntPtr.Zero);
                return false;
            }
            if (vk >= 0xA0 && vk <= 0xA5) return false;   // shift / ctrl / alt themselves
            if (up) return swallowed.Remove(vk);
            if (!down) return false;

            // Ask Windows for the real modifier state, so a key-up we never saw (UAC, Ctrl+Alt+Del)
            // can never make us swallow ordinary typing.
            bool win = Pressed(0x5B) || Pressed(0x5C), alt = Pressed(0x12), ctrl = Pressed(0x11), shift = Pressed(0x10);
            if (winKeys && win && !alt && !ctrl && vk >= 0x31 && vk <= 0x39)
            {
                if (!swallowed.Add(vk)) return true;   // auto-repeat: keep swallowing, act once
                Native.PostMessage(target, WM_HOOK_WINNUM, new IntPtr((int)(vk - 0x31)), new IntPtr(shift ? 1 : 0));
                Dummy();
                return true;
            }
            // Win+Alt+E: edit mode.  Win+Alt+P: next profile.  Win+Alt+1..4: profile 1..4.
            if (barKeys && win && alt && !ctrl && (vk == 0x45 || vk == 0x50 || (vk >= 0x31 && vk <= 0x34)))
            {
                if (!swallowed.Add(vk)) return true;
                if (vk == 0x45) Native.PostMessage(target, WM_HOOK_EDIT, IntPtr.Zero, IntPtr.Zero);
                else Native.PostMessage(target, WM_HOOK_PROFILE, new IntPtr(vk == 0x50 ? -1 : (int)(vk - 0x31)), IntPtr.Zero);
                Dummy();
                return true;
            }
            if (launcher && alt && !win && !ctrl && !shift && vk == 0x20)
            {
                if (!swallowed.Add(vk)) return true;
                Native.PostMessage(target, WM_HOOK_LAUNCHER, IntPtr.Zero, IntPtr.Zero);
                Dummy();
                return true;
            }
            swallowed.Remove(vk);   // an ordinary key press: never swallow its key-up
            return false;
        }
    }
}
