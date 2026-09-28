using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace FishingFun
{
    public sealed class RotationDesktop : IRotationDesktop, IAutomaticRotationDesktop
    {
        private readonly CharacterRotationSettings settings;
        private readonly Func<bool> canContinue;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly IntPtr window;
        private Rectangle lastReadBounds;
        public TimeSpan Elapsed => clock.Elapsed;

        public RotationDesktop(CharacterRotationSettings settings, Func<bool> canContinue)
        {
            this.settings = settings;
            this.canContinue = canContinue;
            using (var process = WowProcess.Get()) { window = process?.MainWindowHandle ?? IntPtr.Zero; }
        }

        public void CheckReady()
        {
            if (!canContinue()) { throw new OperationCanceledException("已停止换号。"); }
            using (var process = WowProcess.Get(logMissing: false))
                if (window == IntPtr.Zero || process == null || process.MainWindowHandle != window)
                    throw new OperationCanceledException("游戏窗口已改变，换号已停止。");
            var bounds = WowScreen.GetClientBounds();
            if (bounds.IsEmpty) { throw new OperationCanceledException("换号时游戏失去前台或被最小化，已停止。请手动确认当前角色后重新启动。"); }
            if (!settings.AutomaticDetection && (bounds.Width != settings.ClientWidth || bounds.Height != settings.ClientHeight))
                throw new InvalidOperationException("游戏窗口尺寸与定位时不同，请恢复窗口尺寸或重新定位。");
        }

        public bool Matches(ScreenMarker marker)
        {
            CheckReady();
            using (var frame = WowScreen.GetBitmap(out var bounds, fullClient: true))
            {
                CheckReady();
                if (bounds != WowScreen.GetClientBounds())
                    throw new OperationCanceledException("截图过程中游戏窗口移动，换号已停止。");
                return marker.Matches(frame);
            }
        }

        public void Click(ScreenMarker marker)
        {
            CheckReady();
            var bounds = WowScreen.GetClientBounds();
            var point = marker.Center;
            WowProcess.LeftClickMouse(new Point(bounds.X + point.X, bounds.Y + point.Y),
                () => canContinue() && WowScreen.GetClientBounds() == bounds);
        }

        public void PressKey(ConsoleKey key) { CheckReady(); WowProcess.PressKey(key); }

        public AutomaticScreen ReadScreen()
        {
            CheckReady();
            using (var frame = WowScreen.GetBitmap(out var bounds, fullClient: true))
            {
                var text = new ScreenTextReader().Read(frame, () =>
                {
                    CheckReady();
                    if (WowScreen.GetClientBounds() != bounds)
                        throw new OperationCanceledException("识别过程中游戏窗口改变，请保持窗口稳定后重新启动。");
                });
                lastReadBounds = bounds;
                return AutomaticScreen.Analyze(frame, text);
            }
        }

        public void Click(Point point)
        {
            CheckReady();
            var bounds = lastReadBounds;
            if (bounds.IsEmpty || WowScreen.GetClientBounds() != bounds)
                throw new OperationCanceledException("游戏窗口已改变，自动定位点击已取消。");
            WowProcess.LeftClickMouse(new Point(bounds.X + point.X, bounds.Y + point.Y),
                () => canContinue() && WowScreen.GetClientBounds() == bounds);
        }

        public void Wait(int milliseconds)
        {
            var end = clock.ElapsedMilliseconds + milliseconds;
            do { CheckReady(); Thread.Sleep(50); } while (clock.ElapsedMilliseconds < end);
        }
    }
}
