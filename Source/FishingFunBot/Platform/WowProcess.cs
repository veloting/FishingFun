using log4net;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

#nullable enable

namespace FishingFun
{
    public static class WowProcess
    {
        public static ILog logger = LogManager.GetLogger("Fishbot");

        private const UInt32 WM_KEYDOWN = 0x0100;
        private const UInt32 WM_KEYUP = 0x0101;
        private static ConsoleKey lastKey;
        private static Random random = new Random();
        public static int LootDelay=2000;


        public static bool IsWowClassic()
        {
            using (var wowProcess = Get())
                return wowProcess != null && wowProcess.ProcessName.ToLowerInvariant().Contains("classic");
        }

        //Get the wow-process, if success returns the process else null
        public static Process? Get(string name = "", bool logMissing = true)
        {
            var names = string.IsNullOrEmpty(name) ? new List<string> { "Wow", "WowClassic", "Wow-64" } : new List<string> { name };

            var matches = new List<Process>();
            Process? selected = null;
            try
            {
                foreach (var processName in names) { matches.AddRange(Process.GetProcessesByName(processName)); }
                var windows = new List<IntPtr>();
                foreach (var process in matches)
                {
                    try { windows.Add(process.MainWindowHandle); }
                    catch (InvalidOperationException) { windows.Add(IntPtr.Zero); } // Process exited during discovery.
                }
                int index = SelectWindowIndex(windows, GetForegroundWindow());
                if (index >= 0) { selected = matches[index]; }
                else if (logMissing) { logger.Error($"Failed to find a WoW window, tried: {string.Join(", ", names)}"); }
                return selected;
            }
            finally
            {
                foreach (var process in matches)
                    if (!ReferenceEquals(process, selected)) { process.Dispose(); }
            }
        }

        // Enumerate all clients before choosing: process order and game edition do not identify the active game.
        internal static int SelectWindowIndex(IReadOnlyList<IntPtr> windows, IntPtr foreground)
        {
            int fallback = -1;
            for (int i = 0; i < windows.Count; i++)
            {
                if (windows[i] == IntPtr.Zero) { continue; }
                if (windows[i] == foreground) { return i; }
                if (fallback < 0) { fallback = i; }
            }
            return fallback;
        }

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, UInt32 Msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowThreadProcessId(IntPtr hWnd, out uint ProcessId);

        private static Process GetActiveProcess()
        {
            IntPtr hwnd = GetForegroundWindow();
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            return Process.GetProcessById((int)pid);
        }

        private static void KeyDown(ConsoleKey key)
        {
            lastKey = key;
            var wowProcess = Get();
            if (wowProcess != null)
            {
                PostMessage(wowProcess.MainWindowHandle, WM_KEYDOWN, (int)key, 0);
            }
        }

        private static void KeyUp()
        {
            KeyUp(lastKey);
        }

        public static void PressKey(ConsoleKey key)
        {
            using (var process = Get())
            {
                var window = process?.MainWindowHandle ?? IntPtr.Zero;
                if (window == IntPtr.Zero || GetForegroundWindow() != window)
                {
                    throw new OperationCanceledException("WoW must be in the foreground before sending a key.");
                }
                // Do not turn a plain fishing key into Shift/Ctrl/Alt + key while the user switches windows.
                if (IsKeyHeld(0x10) || IsKeyHeld(0x11) || IsKeyHeld(0x12) || IsKeyHeld(0x5B) || IsKeyHeld(0x5C) || IsKeyHeld((int)key))
                {
                    throw new OperationCanceledException("Release held keys before automatic casting.");
                }
                uint scan = MapVirtualKey((uint)key, 4);
                if (scan == 0) { throw new ArgumentException("No scan code is available for " + key, nameof(key)); }
                uint flags = 0x0008u | ((scan & 0xFF00) == 0xE000 ? 0x0001u : 0u);
                if (GetForegroundWindow() != window) { throw new OperationCanceledException("WoW lost focus before casting."); }
                SendKeyboardInput((ushort)(scan & 0xFF), flags);
                try { Thread.Sleep(50 + random.Next(0, 75)); }
                finally { SendKeyboardInput((ushort)(scan & 0xFF), flags | 0x0002u); }
                logger.Info($"Keyboard input sent for {key}; waiting for a new bobber (casting is not yet confirmed).");
            }
        }

        private static bool IsKeyHeld(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

        private static void SendKeyboardInput(ushort scanCode, uint flags)
        {
            var input = new NativeInput
            {
                Type = 1,
                Data = new InputData { Keyboard = new KeyboardInput { ScanCode = scanCode, Flags = flags } }
            };
            if (SendInput(1, new[] { input }, Marshal.SizeOf(typeof(NativeInput))) != 1)
            {
                int error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"Keyboard input was not inserted (Win32={error}). Check that WoW and this app run on the same desktop and at the same privilege level.");
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeInput { public uint Type; public InputData Data; }
        [StructLayout(LayoutKind.Explicit)]
        private struct InputData
        {
            [FieldOffset(0)] public KeyboardInput Keyboard;
            [FieldOffset(0)] public MouseInput Mouse;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort VirtualKey, ScanCode;
            public uint Flags, Time;
            public UIntPtr ExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int X, Y;
            public uint MouseData, Flags, Time;
            public UIntPtr ExtraInfo;
        }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);

        public static void KeyUp(ConsoleKey key)
        {
            var wowProcess = Get();
            if (wowProcess != null)
            {
                PostMessage(wowProcess.MainWindowHandle, WM_KEYUP, (int)key, 0);
            }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetCursorPos(int x, int y);

        public static void RightClickMouse(ILog logger, System.Drawing.Point position, Func<bool>? canContinue = null)
        {
            //RightClickMouse_Original(logger, position);
            RightClickMouse_LiamCooper(logger, position, canContinue);
        }

        public static void RightClickMouse_Original(ILog logger, System.Drawing.Point position)
        {
            var activeProcess = GetActiveProcess();
            var wowProcess = WowProcess.Get();
            if (wowProcess != null)
            {
                var oldPosition = System.Windows.Forms.Cursor.Position;

                for (int i = 20; i > 0; i--)
                {
                    SetCursorPos(position.X + i, position.Y + i);
                    Thread.Sleep(1);
                }
                Thread.Sleep(1000);

                PostMessage(wowProcess.MainWindowHandle, Keys.WM_RBUTTONDOWN, Keys.VK_RMB, 0);
                Thread.Sleep(30 + random.Next(0, 47));
                PostMessage(wowProcess.MainWindowHandle, Keys.WM_RBUTTONUP, Keys.VK_RMB, 0);

                RefocusOnOldScreen(logger, activeProcess, wowProcess, oldPosition);
            }
        }

        public static void RightClickMouse()
        {
            var activeProcess = GetActiveProcess();
            var wowProcess = WowProcess.Get();
            if (wowProcess != null)
            {
                var oldPosition = System.Windows.Forms.Cursor.Position;
                PostMessage(wowProcess.MainWindowHandle, Keys.WM_RBUTTONDOWN, Keys.VK_RMB, 0);
                Thread.Sleep(30 + random.Next(0, 47));
                PostMessage(wowProcess.MainWindowHandle, Keys.WM_RBUTTONUP, Keys.VK_RMB, 0);
            }
        }

        public static void LeftClickMouse()
        {
            var activeProcess = GetActiveProcess();
            var wowProcess = WowProcess.Get();
            if (wowProcess != null)
            {
                var oldPosition = System.Windows.Forms.Cursor.Position;
                PostMessage(wowProcess.MainWindowHandle, Keys.WM_LBUTTONDOWN, Keys.VK_RMB, 0);
                Thread.Sleep(30 + random.Next(0, 47));
                PostMessage(wowProcess.MainWindowHandle, Keys.WM_LBUTTONUP, Keys.VK_RMB, 0);
            }
        }

        public static void RightClickMouse_LiamCooper(ILog logger, System.Drawing.Point position, Func<bool>? canContinue = null)
        {
            var clickTimer = Stopwatch.StartNew();
            bool inputIssued = false;
            string stage = "find WoW process";
            try
            {
            using (WowScreen.UsePhysicalPixels())
            using (var wowProcess = WowProcess.Get())
            {
                if (wowProcess == null) { return; }
                var window = wowProcess.MainWindowHandle;
                var bounds = WowScreen.GetCaptureBounds();
                bool CanClick() => (canContinue == null || canContinue()) &&
                    !bounds.IsEmpty && bounds.Contains(position) &&
                    WowScreen.GetCaptureBounds() == bounds && GetForegroundWindow() == window &&
                    GetAncestor(WindowFromPoint(position), 2) == window;

                stage = "validate foreground, target bounds and window under cursor";
                if (!CanClick()) { return; }
                stage = "initial 200ms wait";
                if (!WaitForLootDelay(200, CanClick)) { return; }
                SetCursorPos(position.X, position.Y);
                stage = "configured loot delay";
                if (!WaitForLootDelay(LootDelay, CanClick)) { return; }
                // The user can move the mouse while waiting; position it again immediately before clicking.
                SetCursorPos(position.X, position.Y);
                if (!CanClick()) { return; }
                mouse_event((int)MouseEventFlags.RightDown, 0, 0, 0, 0);
                try { Thread.Sleep(30 + random.Next(0, 47)); }
                finally { mouse_event((int)MouseEventFlags.RightUp, 0, 0, 0, 0); }
                inputIssued = true;
                logger.Info($"Loot mouse input issued: position={position}, elapsedMs={clickTimer.ElapsedMilliseconds}, configuredDelayMs={LootDelay}. Game pickup is not confirmed.");
                WaitForLootDelay(LootDelay / 2, CanClick);
            }
            }
            finally
            {
                if (!inputIssued) { logger.Warn($"Loot click not issued: stage={stage}, position={position}, elapsedMs={clickTimer.ElapsedMilliseconds}; check stop state, foreground and capture bounds."); }
            }
        }

        public static void LeftClickMouse(System.Drawing.Point position, Func<bool> canContinue)
        {
            using (WowScreen.UsePhysicalPixels())
            using (var process = Get())
            {
                var window = process?.MainWindowHandle ?? IntPtr.Zero;
                var bounds = WowScreen.GetClientBounds();
                bool CanClick() => canContinue() && window != IntPtr.Zero && !bounds.IsEmpty &&
                    bounds.Contains(position) && WowScreen.GetClientBounds() == bounds &&
                    GetForegroundWindow() == window && GetAncestor(WindowFromPoint(position), 2) == window &&
                    !IsKeyHeld(0x10) && !IsKeyHeld(0x11) && !IsKeyHeld(0x12) &&
                    !IsKeyHeld(0x5B) && !IsKeyHeld(0x5C) && !IsKeyHeld(0x01) && !IsKeyHeld(0x02);
                if (!CanClick()) { throw new OperationCanceledException("换号点击已取消：窗口或按键状态改变。"); }
                if (!SetCursorPos(position.X, position.Y) || !CanClick())
                    throw new OperationCanceledException("无法定位换号按钮。");
                SendMouseInput(0x0002);
                try { Thread.Sleep(50); }
                finally { SendMouseInput(0x0004); }
            }
        }

        private static void SendMouseInput(uint flags)
        {
            var input = new NativeInput { Type = 0, Data = new InputData { Mouse = new MouseInput { Flags = flags } } };
            if (SendInput(1, new[] { input }, Marshal.SizeOf(typeof(NativeInput))) != 1)
                throw new InvalidOperationException("换号鼠标输入失败，Win32=" + Marshal.GetLastWin32Error());
        }

        private static bool WaitForLootDelay(int milliseconds, Func<bool> canContinue)
        {
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < milliseconds)
            {
                if (!canContinue()) { return false; }
                Thread.Sleep(30);
            }
            return canContinue();
        }

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(System.Drawing.Point point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);

        private static void RefocusOnOldScreen(ILog logger, Process activeProcess, Process wowProcess, System.Drawing.Point oldPosition)
        {
            try
            {
                if (activeProcess.MainWindowTitle != wowProcess.MainWindowTitle)
                {
                    // get focus back on this screen
                    PostMessage(activeProcess.MainWindowHandle, Keys.WM_RBUTTONDOWN, Keys.VK_RMB, 0);
                    Thread.Sleep(30);
                    PostMessage(activeProcess.MainWindowHandle, Keys.WM_RBUTTONUP, Keys.VK_RMB, 0);

                    KeyDown(ConsoleKey.Escape);
                    Thread.Sleep(30);
                    KeyUp(ConsoleKey.Escape);

                    System.Windows.Forms.Cursor.Position = oldPosition;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message);
            }
        }

        [DllImport("user32.dll")]
        private static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

        [Flags]
        public enum MouseEventFlags
        {
            LeftDown = 0x00000002,
            LeftUp = 0x00000004,
            MiddleDown = 0x00000020,
            MiddleUp = 0x00000040,
            Move = 0x00000001,
            Absolute = 0x00008000,
            RightDown = 0x00000008,
            RightUp = 0x00000010
        }
    }
}
