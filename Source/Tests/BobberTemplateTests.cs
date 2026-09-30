using System;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using FishingFun;

internal static class BobberTemplateTests
{
    private static string images;
    private static int checks;
    private static void Check(bool value, string message)
    { if (!value) { throw new Exception(message); } checks++; Console.WriteLine("PASS: " + message); }
    private static Bitmap Crop(string file, Rectangle bounds)
    { using (var source = new Bitmap(Path.Combine(images, file))) { return source.Clone(bounds, source.PixelFormat); } }
    private static Point Find(SearchBobberFinder finder, Bitmap frame)
    { using (var copy = new Bitmap(frame)) { return finder.Find(copy, new Rectangle(Point.Empty, frame.Size)); } }
    private static Point Lock(SearchBobberFinder finder, Bitmap frame)
    { Find(finder, frame); Find(finder, frame); return Find(finder, frame); }
    private static Bitmap Scene(Bitmap bobber, int y = 75, bool clutter = false, double scale = 1)
    {
        var scene = new Bitmap(420, 300);
        using (var g = Graphics.FromImage(scene))
        {
            g.Clear(Color.Gray);
            if (clutter) { g.FillRectangle(Brushes.Red, 5, 10, 45, 45); g.FillRectangle(Brushes.Blue, 5, 85, 45, 45); }
            g.DrawImage(bobber, new Rectangle(150, y, (int)(bobber.Width * scale), (int)(bobber.Height * scale)));
        }
        return scene;
    }
    private static int Main()
    {
        try
        {
            images = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../post/img"));
            var matcher = BobberTemplateMatcher.LoadDefault();
            var files = new[] { "Screenshot1.png", "lava.png", "FishingFun_ZoomedIn.jpg", "fishingfun_zoomedout.jpg" };
            var areas = new[] { new Rectangle(0, 103, 789, 444), new Rectangle(0, 0, 975, 190), new Rectangle(245, 140, 470, 255), new Rectangle(387, 140, 313, 260) };
            var expected = new[] { new Rectangle(370, 147, 69, 58), new Rectangle(470, 94, 71, 51), new Rectangle(216, 62, 35, 36), new Rectangle(74, 104, 15, 15) };
            for (int i = 0; i < files.Length; i++)
                using (var frame = Crop(files[i], areas[i]))
                {
                    var timer = Stopwatch.StartNew();
                    var finder = new SearchBobberFinder(new PixelClassifier());
                    var point = Lock(finder, frame);
                    Console.WriteLine("  Scene " + files[i] + " point=" + point + " feather=" + finder.DetectedColour);
                    Check(point != Point.Empty && expected[i].Contains(point), "Default Auto finds the bobber in independent scene " + files[i]);
                    Check(Find(finder, frame) == point, "Independent scene anchor stays stable after locking " + files[i]);
                    Console.WriteLine("  Three-frame acquisition ms=" + timer.ElapsedMilliseconds);
                }

            // These examples are outside the eleven template crops. They check different lighting and poses.
            var heldOut = new[] { new Rectangle(191,114,89,87), new Rectangle(278,115,86,90), new Rectangle(454,109,109,95),
                new Rectangle(562,106,94,87), new Rectangle(0,156,97,91), new Rectangle(97,176,89,88),
                new Rectangle(299,207,100,83), new Rectangle(395,207,82,91), new Rectangle(477,201,87,92),
                new Rectangle(0,248,98,83), new Rectangle(98,265,109,71) };
            for (int i = 0; i < heldOut.Length; i++)
                using (var frame = Crop("fishingfun_bobbers.png", heldOut[i]))
                {
                    var match = matcher.Find(frame, new Rectangle(Point.Empty, frame.Size));
                    Check(match != null && match.Score >= BobberTemplateMatcher.MinimumScore &&
                        match.Point.X > 15 && match.Point.X < frame.Width - 10 && match.Point.Y > 5 && match.Point.Y < frame.Height * .65,
                        "Finds held-out lighting/pose sample " + i);
                }

            using (var bobber = Crop("fishingfun_bobbers.png", heldOut[2]))
            using (var frame = Scene(bobber))
            using (var cluttered = Scene(bobber, clutter: true))
            using (var empty = new Bitmap(frame.Width, frame.Height))
            {
                using (var g = Graphics.FromImage(empty)) { g.Clear(Color.Gray); }
                var finder = new SearchBobberFinder(new PixelClassifier());
                finder.PrepareForCast(empty, new Rectangle(Point.Empty, empty.Size));
                Check(Find(finder, frame) == Point.Empty && Find(finder, frame) == Point.Empty,
                    "Auto requires consecutive frames before accepting an appearance match");
                var point = Find(finder, frame);
                Check(point != Point.Empty, "New bobber is accepted after the pre-cast background");
                using (var dip = Scene(bobber, y: 83))
                {
                    var moved = Find(finder, dip);
                    Check(moved.X == point.X && moved.Y == point.Y + 8,
                        "Local feather tracking preserves an actual eight-pixel dip without changing bite detection");
                }
                Check(Find(finder, empty) == Point.Empty, "Disappeared bobber does not become a background match");
                using (var distant = new Bitmap(frame.Width, frame.Height))
                {
                    using (var g = Graphics.FromImage(distant)) { g.Clear(Color.Gray); g.DrawImageUnscaled(bobber, 290, 190); }
                    Check(Find(finder, distant) == Point.Empty, "Locked feather does not jump to another distant bobber");
                }

                // Replay gradual drift followed by a sudden dip through the real bite watcher.
                finder.Reset();
                point = Lock(finder, frame);
                var watcher = new PositionBiteWatcher(7);
                watcher.Reset(point);
                bool driftTriggered = false;
                for (int i = 0; i < 60; i++)
                    using (var drift = Scene(bobber, y: 75 + i / 10))
                    {
                        var tracked = Find(finder, drift);
                        if (tracked == Point.Empty) { throw new Exception("Lost float during gradual drift"); }
                        driftTriggered |= watcher.IsBite(tracked);
                    }
                Check(!driftTriggered, "Gradual drift does not trigger a bite");
                using (var sudden = Scene(bobber, y: 88))
                {
                    var tracked = Find(finder, sudden);
                    Check(tracked != Point.Empty && watcher.IsBite(tracked), "Sudden dip after gradual drift triggers the unchanged bite watcher");
                }

                // Preserve feather pixels while obscuring the cork and changing the surrounding image.
                // Whole-float correlation must not suppress coordinates needed to observe the bite.
                finder.Reset();
                point = Lock(finder, frame);
                var appearance = matcher.Find(frame, new Rectangle(Point.Empty, frame.Size));
                using (var changed = new Bitmap(frame.Width, frame.Height))
                {
                    using (var g = Graphics.FromImage(changed))
                    {
                        g.Clear(Color.Gray);
                        var feather = new Rectangle(appearance.Bounds.X, appearance.Bounds.Y,
                            appearance.Bounds.Width, (int)Math.Ceiling(appearance.Bounds.Height * .6));
                        g.DrawImage(frame, feather, feather, GraphicsUnit.Pixel);
                    }
                    var oldMatch = matcher.Track(changed, appearance);
                    Check(oldMatch == null || oldMatch.Score < BobberTemplateMatcher.MinimumScore,
                        "Regression fixture reproduces rejection by the old whole-float tracking threshold");
                    var tracked = Find(finder, changed);
                    Check(tracked == point, "Changed cork/background does not move or lose the tracked feather");
                    using (var changedDip = new Bitmap(frame.Width, frame.Height))
                    {
                        using (var g = Graphics.FromImage(changedDip))
                        { g.Clear(Color.Gray); g.DrawImageUnscaled(changed, 0, 8); }
                        watcher.Reset(point);
                        tracked = Find(finder, changedDip);
                        Check(tracked == new Point(point.X, point.Y + 8) && watcher.IsBite(tracked),
                            "Sudden feather dip remains visible to bite detection despite low whole-float similarity");
                    }
                }
                finder.Reset();
                Check(Lock(finder, cluttered).X > 140, "Large red and blue distractors do not suppress the real fish float");
                var legacy = new SearchBobberFinder(new PixelClassifier(), useTemplates: false);
                Check(Lock(legacy, cluttered) == Point.Empty, "Comparison reproduces the old global colour-limit failure on the same frame");

                finder = new SearchBobberFinder(new PixelClassifier());
                finder.PrepareForCast(frame, new Rectangle(Point.Empty, frame.Size));
                Check(Lock(finder, frame) == Point.Empty, "A bobber already present before casting is not accepted as a new cast");
                bool movedRejected = false;
                try { finder.Find(frame, new Rectangle(10, 0, frame.Width, frame.Height)); }
                catch (OperationCanceledException) { movedRejected = true; }
                Check(movedRejected, "Moved capture bounds invalidate the pre-cast appearance snapshot");

                foreach (double scale in new[] { .10, .12, .15, .20, .25, .35, .65, 1.35 })
                    using (var scaled = Scene(bobber, scale: scale))
                    {
                        var scaledFinder = new SearchBobberFinder(new PixelClassifier());
                        var result = Lock(scaledFinder, scaled);
                        Check(result.X > 145 && result.X < 150 + bobber.Width * scale && result.Y >= 75 && result.Y < 75 + bobber.Height * scale,
                            "Multi-size matching finds a rescaled held-out float: " + scale);
                        if (scale <= .35)
                        {
                            Check(Find(scaledFinder, scaled) == result, "Small float stays at the same coordinate after locking: " + scale);
                            using (var dip = Scene(bobber, y: 83, scale: scale))
                            {
                                var moved = Find(scaledFinder, dip);
                                Console.WriteLine("  Small dip scale=" + scale + " before=" + result + " after=" + moved + " colour=" + scaledFinder.DetectedColour);
                                Check(moved == new Point(result.X, result.Y + 8), "Small float remains trackable during an eight-pixel dip: " + scale);
                            }
                        }
                    }

                using (var background = new Bitmap(420, 300))
                using (var smallBobber = Scene(bobber, scale: .12))
                {
                    using (var g = Graphics.FromImage(background))
                    {
                        g.Clear(Color.Gray);
                        for (int i = 0; i < 150; i++)
                            g.FillRectangle(i % 2 == 0 ? Brushes.Red : Brushes.Blue, (i % 25) * 16, (i / 25) * 10, 5, 5);
                    }
                    using (var g = Graphics.FromImage(smallBobber))
                        g.DrawImage(background, new Rectangle(0, 0, 420, 60), new Rectangle(0, 0, 420, 60), GraphicsUnit.Pixel);
                    var smallFinder = new SearchBobberFinder(new PixelClassifier());
                    smallFinder.PrepareForCast(background, new Rectangle(Point.Empty, background.Size));
                    var acquired = Lock(smallFinder, smallBobber);
                    Check(new Rectangle(150, 75, 14, 12).Contains(acquired),
                        "Stationary colour clutter cannot evict the new tiny feather from the candidate budget");
                    using (var vanished = new Bitmap(background))
                    {
                        // An isolated pixel close to the last feather must not become a new lock.
                        vanished.SetPixel(acquired.X, acquired.Y + 2, smallFinder.DetectedColour == PixelClassifier.ClassifierMode.Red ? Color.Red : Color.Blue);
                        Check(Find(smallFinder, vanished) == Point.Empty, "A lone noise pixel cannot replace a missing small feather");
                    }
                }
            }
            using (var water = Crop("Screenshot1.png", new Rectangle(0, 310, 780, 220)))
                Check(Lock(new SearchBobberFinder(new PixelClassifier()), water) == Point.Empty, "Water-only scene is rejected");
            using (var blocks = new Bitmap(320, 240))
            {
                using (var g = Graphics.FromImage(blocks))
                { g.Clear(Color.Gray); g.FillRectangle(Brushes.Red, 50, 50, 15, 12); g.FillRectangle(Brushes.Blue, 130, 100, 20, 15); }
                Check(Lock(new SearchBobberFinder(new PixelClassifier()), blocks) == Point.Empty, "Plain red/blue blocks cannot pass as a bobber image");
            }
            Console.WriteLine("All " + checks + " bobber image checks passed. No game input was sent.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
