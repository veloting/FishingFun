using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using FishingFun;

// Built as Wow.exe so the real process discovery and Win32 capture path can be exercised.
// Run with WoW closed; this fixture never sends input to the actual game.
internal static class WindowCaptureSmokeTests
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    private static void Check(bool condition, string message)
    {
        if (!condition) { throw new Exception(message); }
        Console.WriteLine("PASS: " + message);
    }

    [STAThread]
    private static int Main()
    {
        try
        {
            using (WowScreen.UsePhysicalPixels())
            using (var window = new Form())
            {
                window.Text = "FishingFun capture test";
                window.StartPosition = FormStartPosition.Manual;
                window.Location = new Point(80, 90);
                window.ClientSize = new Size(600, 500);
                window.BackColor = Color.FromArgb(180, 30, 170);
                window.Show();
                window.Activate();
                SetForegroundWindow(window.Handle);
                Application.DoEvents();

                var origin = window.PointToScreen(Point.Empty);
                bool hasForeground = !WowScreen.GetCaptureBounds().IsEmpty;
                var initial = WowScreen.GetCaptureBounds(false);
                Check(initial == new Rectangle(origin.X + 150, origin.Y + 125, 300, 150),
                    "Capture is relative to the client area, excluding window borders");
                if (hasForeground)
                {
                    using (var frame = WowScreen.GetBitmap(out var capturedBounds))
                    {
                        Check(frame.Size == initial.Size && capturedBounds == initial,
                            "Bitmap dimensions and coordinate snapshot agree");
                        Check(frame.GetPixel(10, 10).ToArgb() == window.BackColor.ToArgb(),
                            "Capture contains the fixture's pixels");
                    }
                }

                var finder = new BobberColourPointFinder(window.BackColor);
                if (hasForeground) { Check(finder.Find() == initial.Location, "Detected bitmap point maps to the window's screen position"); }
                window.Location = new Point(160, 150);
                Application.DoEvents();
                var moved = WowScreen.GetCaptureBounds(false);
                Check(moved.Location == new Point(initial.X + 80, initial.Y + 60),
                    "Capture follows a moved window");
                if (hasForeground) { Check(finder.Find() == moved.Location, "Detection follows the moved window"); }

                window.ClientSize = new Size(400, 300);
                Application.DoEvents();
                Check(WowScreen.GetCaptureBounds(false).Size == new Size(200, 75),
                    "Small windows retain a positive capture area");
                var negativeBounds = new Rectangle(-1500, -800, 300, 200);
                Check(WowScreen.GetScreenPositionFromBitmapPostion(new Point(20, 30), negativeBounds)
                    == new Point(-1480, -770), "Negative secondary-monitor coordinates are preserved");

                var oldCursor = Cursor.Position;
                WowProcess.RightClickMouse(FishingBot.logger, WowScreen.GetCaptureBounds().Location, () => false);
                Check(Cursor.Position == oldCursor, "Cancelled looting does not move the cursor");

                var fakeFinder = new CountingFinder();
                var bot = new FishingBot(fakeFinder, new PositionBiteWatcher(7), ConsoleKey.F24, new List<ConsoleKey>());
                var thread = new Thread(bot.Start) { IsBackground = true };
                if (hasForeground)
                {
                    thread.Start();
                    var timer = Stopwatch.StartNew();
                    while (fakeFinder.Calls == 0 && timer.ElapsedMilliseconds < 2000)
                    {
                        Application.DoEvents();
                        Thread.Sleep(10);
                    }
                    bot.Stop();
                    Check(thread.Join(1000) && fakeFinder.Calls > 0, "Stop interrupts the initial detection loop");
                }
                else
                {
                    Console.WriteLine("SKIP: Foreground pixel/detection checks; Windows did not grant foreground focus to the fixture.");
                    Check(WowScreen.GetCaptureBounds().IsEmpty, "Background window pauses automatic capture");
                }

                window.WindowState = FormWindowState.Minimized;
                Application.DoEvents();
                Check(WowScreen.GetCaptureBounds().IsEmpty, "Minimized window pauses capture");
                Check(WowScreen.GetCaptureBounds(false).IsEmpty, "Manual capture also rejects minimized windows");

                fakeFinder = new CountingFinder();
                bot = new FishingBot(fakeFinder, new PositionBiteWatcher(7), ConsoleKey.F24, new List<ConsoleKey>());
                thread = new Thread(bot.Start) { IsBackground = true };
                thread.Start();
                Thread.Sleep(200);
                bot.Stop();
                Check(thread.Join(1000) && fakeFinder.Calls == 0, "Paused bot does not detect and stops promptly");

                bot = new FishingBot(fakeFinder, new PositionBiteWatcher(7), ConsoleKey.F24, new List<ConsoleKey>());
                bot.Stop();
                thread = new Thread(bot.Start) { IsBackground = true };
                thread.Start();
                Check(thread.Join(1000), "Stop before the worker starts is not lost");
            }
            Console.WriteLine("All executed window capture smoke tests passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private sealed class CountingFinder : IBobberFinder
    {
        public int Calls;
        public Point Find() { Interlocked.Increment(ref Calls); return Point.Empty; }
        public void Reset() { }
    }
}
