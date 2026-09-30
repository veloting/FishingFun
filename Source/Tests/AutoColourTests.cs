using System;
using System.Drawing;
using System.IO;
using FishingFun;

internal static class AutoColourTests
{
    private static readonly Color Red = Color.FromArgb(200, 40, 40);
    private static readonly Color Blue = Color.FromArgb(40, 40, 200);
    private static readonly Rectangle Bounds = new Rectangle(-500, 200, 160, 120);
    private static int checks;

    private static void Check(bool condition, string message)
    {
        if (!condition) { throw new Exception(message); }
        checks++;
        Console.WriteLine("PASS: " + message);
    }

    private static Bitmap Frame(Color background, Action<Graphics> draw)
    {
        var frame = new Bitmap(Bounds.Width, Bounds.Height);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(background);
            draw(graphics);
        }
        return frame;
    }

    private static void Patch(Graphics graphics, Color colour, int x = 60, int y = 50, int size = 6)
    {
        using (var brush = new SolidBrush(colour)) { graphics.FillRectangle(brush, x, y, size, size); }
    }

    private static Point Find(SearchBobberFinder finder, Bitmap frame)
    {
        // Detection annotates the preview; each call receives an unmodified source image.
        using (var copy = new Bitmap(frame)) { return finder.Find(copy, Bounds); }
    }

    private static void Confirm(SearchBobberFinder finder, Bitmap frame, PixelClassifier.ClassifierMode colour)
    {
        Check(Find(finder, frame) == Point.Empty && finder.DetectedColour == null, "First candidate frame is not locked");
        Check(Find(finder, frame) == Point.Empty && finder.DetectedColour == null, "Second candidate frame is not locked");
        Check(Find(finder, frame) != Point.Empty && finder.DetectedColour == colour, "Third stable frame locks " + colour);
    }

    private static int Main()
    {
        try
        {
            var classifier = new PixelClassifier();
            Check(classifier.Mode == PixelClassifier.ClassifierMode.Auto, "Automatic colour is the default");
            var selected = new PixelClassifier { Mode = PixelClassifier.ClassifierMode.AutoColour };
            var selectableFinder = new SearchBobberFinder(selected);
            using (var red = Frame(Color.Gray, g => Patch(g, Red, size: 30)))
            using (var blue = Frame(Color.Gray, g => Patch(g, Blue)))
            using (var empty = Frame(Color.Gray, g => { }))
            {
                selectableFinder.PrepareForCast(empty, Bounds);
                Confirm(selectableFinder, red, PixelClassifier.ClassifierMode.Red);
                selected.Mode = PixelClassifier.ClassifierMode.Auto;
                bool restarted = false;
                try { Find(selectableFinder, red); } catch (OperationCanceledException) { restarted = true; }
                Check(restarted, "Switching old to new clears tracking and requests a fresh cast/bite baseline");
                selectableFinder.PrepareForCast(empty, Bounds);
                Check(Find(selectableFinder, red) == Point.Empty && Find(selectableFinder, red) == Point.Empty && Find(selectableFinder, red) == Point.Empty,
                    "New mode uses appearance matching and rejects the plain patch accepted by old mode");
                selected.Mode = PixelClassifier.ClassifierMode.AutoColour;
                restarted = false;
                try { Find(selectableFinder, blue); } catch (OperationCanceledException) { restarted = true; }
                Check(restarted, "Switching back to old mode requests a fresh cast");
                selectableFinder.PrepareForCast(empty, Bounds);
                Confirm(selectableFinder, blue, PixelClassifier.ClassifierMode.Blue);
            }
            string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "detection-test-" + Guid.NewGuid() + ".xml");
            try
            {
                Check(BobberDetectionSettings.LoadMode(settingsPath) == PixelClassifier.ClassifierMode.Auto, "Missing saved mode preserves the new-mode default");
                foreach (PixelClassifier.ClassifierMode mode in Enum.GetValues(typeof(PixelClassifier.ClassifierMode)))
                {
                    BobberDetectionSettings.SaveMode(settingsPath, mode);
                    Check(BobberDetectionSettings.LoadMode(settingsPath) == mode, "Selected mode survives save/reload: " + mode);
                }
                File.WriteAllText(settingsPath, "<BobberDetection><Mode>999</Mode></BobberDetection>");
                bool invalid = false;
                try { BobberDetectionSettings.LoadMode(settingsPath); } catch (InvalidDataException) { invalid = true; }
                Check(invalid, "Invalid stored mode cannot silently select an unintended detector");
            }
            finally { if (File.Exists(settingsPath)) { File.Delete(settingsPath); } }
            var finder = new SearchBobberFinder(classifier, useTemplates: false);
            using (var red = Frame(Color.Gray, g => Patch(g, Red)))
            using (var blue = Frame(Color.Gray, g => Patch(g, Blue)))
            using (var empty = Frame(Color.Gray, g => { }))
            {
                Confirm(finder, red, PixelClassifier.ClassifierMode.Red);
                Check(Find(finder, blue) == Point.Empty && finder.DetectedColour == PixelClassifier.ClassifierMode.Red,
                    "Locked red does not switch to a blue object");
                Check(Find(finder, empty) == Point.Empty && finder.DetectedColour == PixelClassifier.ClassifierMode.Red,
                    "Missing feather retains its colour until the next cast");
                Check(Find(finder, red) == new Point(Bounds.X + 60, Bounds.Y + 50), "Reappearing feather retains screen coordinates");

                using (var otherRed = Frame(Color.Gray, g => Patch(g, Red, 130, 100)))
                {
                    Check(Find(finder, otherRed) == Point.Empty, "Locked detection does not jump to a distant same-colour object");
                }
                using (var dipping = Frame(Color.Gray, g => Patch(g, Red, 60, 58)))
                {
                    Check(Find(finder, dipping) == new Point(Bounds.X + 60, Bounds.Y + 58), "Locked feather still tracks a bite-sized downward movement");
                }
                finder.Reset();
                Confirm(finder, blue, PixelClassifier.ClassifierMode.Blue);

                finder.Reset();
                Find(finder, red);
                Find(finder, empty);
                Confirm(finder, red, PixelClassifier.ClassifierMode.Red);

                finder.Reset();
                for (int i = 0; i < 6; i++)
                {
                    Check(Find(finder, i % 2 == 0 ? red : blue) == Point.Empty, "Alternating colours do not prematurely lock");
                }

                classifier.Mode = PixelClassifier.ClassifierMode.Blue;
                bool restartRequested = false;
                try { Find(finder, blue); }
                catch (OperationCanceledException) { restartRequested = true; }
                Check(restartRequested, "Changing mode requests a new bite baseline");
                finder.Reset();
                Check(Find(finder, red) == Point.Empty, "Manual blue ignores red");
                Check(Find(finder, blue) != Point.Empty, "Manual blue remains immediately available");
                classifier.Mode = PixelClassifier.ClassifierMode.Red;
                finder.Reset();
                Check(Find(finder, blue) == Point.Empty && Find(finder, red) != Point.Empty, "Manual red remains available");
            }

            classifier.Mode = PixelClassifier.ClassifierMode.Auto;
            using (var lava = Frame(Red, g => Patch(g, Blue)))
            using (var water = Frame(Blue, g => Patch(g, Red)))
            {
                finder.Reset();
                Confirm(finder, lava, PixelClassifier.ClassifierMode.Blue);
                finder.Reset();
                Confirm(finder, water, PixelClassifier.ClassifierMode.Red);
            }

            using (var noise = Frame(Color.Gray, g =>
            {
                Patch(g, Red, 20, 20, 1);
                Patch(g, Blue, 100, 80, 1);
            }))
            {
                finder.Reset();
                for (int i = 0; i < 4; i++) { Check(Find(finder, noise) == Point.Empty, "Isolated noise pixels are rejected"); }
            }
            using (var competing = Frame(Color.Gray, g =>
            {
                Patch(g, Red);
                Patch(g, Blue, 110, 80, 4);
            }))
            {
                finder.Reset();
                Confirm(finder, competing, PixelClassifier.ClassifierMode.Red);
                using (var strongerBlue = Frame(Color.Gray, g =>
                {
                    Patch(g, Red);
                    Patch(g, Blue, 80, 70, 12);
                }))
                {
                    Check(Find(finder, strongerBlue) == new Point(Bounds.X + 60, Bounds.Y + 50),
                        "A stronger competing colour cannot displace a locked feather");
                }
            }
            using (var beforeCast = Frame(Color.Gray, g => Patch(g, Blue, 90, 70, 10)))
            using (var afterCast = Frame(Color.Gray, g =>
            {
                Patch(g, Blue, 90, 70, 10);
                Patch(g, Red);
            }))
            {
                finder.PrepareForCast(beforeCast, Bounds);
                for (int i = 0; i < 4; i++)
                {
                    Check(Find(finder, beforeCast) == Point.Empty, "Unchanged pre-cast scenery cannot lock as a bobber");
                }
                Confirm(finder, afterCast, PixelClassifier.ClassifierMode.Red);
                finder.Reset(); // Warm-up reset must keep the pre-cast exclusion map.
                Check(Find(finder, beforeCast) == Point.Empty, "Tracking reset retains pre-cast scenery exclusion");
                Confirm(finder, afterCast, PixelClassifier.ClassifierMode.Red);
                bool movedRejected = false;
                using (var copy = new Bitmap(afterCast))
                {
                    try { finder.Find(copy, new Rectangle(Bounds.X + 10, Bounds.Y, Bounds.Width, Bounds.Height)); }
                    catch (OperationCanceledException) { movedRejected = true; }
                }
                Check(movedRejected, "A moved window cannot reuse an old pre-cast snapshot");
            }
            var watcher = new PositionBiteWatcher(7);
            int lootEvents = 0;
            watcher.FishingEventHandler = ev => { if (ev.Action == FishingAction.Loot) { lootEvents++; } };
            watcher.Reset(new Point(60, 50));
            Check(watcher.IsBite(new Point(60, 58)) && lootEvents == 1,
                "The first downward jump triggers a bite instead of becoming the baseline");
            watcher.Reset(new Point(60, 50));
            for (int i = 0; i < 40; i++)
            {
                if (watcher.IsBite(new Point(60, 50 + i % 3))) { throw new Exception("Normal bobbing triggered loot."); }
            }
            Check(watcher.IsBite(new Point(60, 59)), "A bite remains detectable after repeated normal bobbing");
            watcher.Reset(new Point(60, 100));
            Check(!watcher.IsBite(new Point(60, 95)), "Upward movement is not a bite");
            watcher.Reset(new Point(60, 200));
            Check(!watcher.IsBite(new Point(60, 200)), "A new cast discards the old bite baseline");

            var nativeInput = typeof(WowProcess).GetNestedType("NativeInput", System.Reflection.BindingFlags.NonPublic);
            Check(System.Runtime.InteropServices.Marshal.SizeOf(nativeInput) == (IntPtr.Size == 8 ? 40 : 28),
                "SendInput structure matches the Windows ABI size");
            Check(System.Runtime.InteropServices.Marshal.OffsetOf(nativeInput, "Data").ToInt32() == (IntPtr.Size == 8 ? 8 : 4),
                "SendInput union has the required native alignment");
            CheckLeftClickSequence();
            Console.WriteLine("All " + checks + " fishing regression checks passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void CheckLeftClickSequence()
    {
        var method = typeof(WowProcess).GetMethod("PerformLeftClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        void Click(Action move, Func<bool> ready, Action<int> wait, Action<bool> send)
        {
            try { method.Invoke(null, new object[] { move, ready, wait, send }); }
            catch (System.Reflection.TargetInvocationException error) { throw error.InnerException; }
        }
        var events = new System.Collections.Generic.List<string>();
        Click(() => events.Add("move"), () => true, ms => events.Add("wait:" + ms), down => events.Add(down ? "down" : "up"));
        Check(string.Join(",", events) == "move,wait:250,down,wait:120,up", "Rotation mouse settles before a full left-button press and release");
        events.Clear();
        bool cancelled = false;
        try { Click(() => events.Add("move"), () => false, ms => { }, down => events.Add("button")); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && events.Count == 0, "Cancelled click does not move or press the mouse");
        bool ready = true;
        cancelled = false;
        try { Click(() => events.Add("move"), () => ready, ms => ready = false, down => events.Add("button")); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && string.Join(",", events) == "move", "Focus or cursor changes during hover prevent the click");
        events.Clear();
        cancelled = false;
        try
        {
            Click(() => { }, () => true, ms => { if (ms == 120) { throw new OperationCanceledException(); } },
                down => events.Add(down ? "down" : "up"));
        }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && string.Join(",", events) == "down,up", "Interrupted button hold still releases the left mouse button");
    }
}
