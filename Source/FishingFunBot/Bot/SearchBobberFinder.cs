using log4net;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

#nullable enable
namespace FishingFun
{
    public class SearchBobberFinder : IBobberFinder, IImageProvider, ICastAwareBobberFinder
    {
        private const int MaxColourPoints = 1000;
        private const int ConfirmationFrames = 3;
        private readonly IPixelClassifier pixelClassifier;
        private static readonly ILog logger = LogManager.GetLogger("Fishbot");

        private Point previousLocation;
        private Rectangle previousBounds;
        private PixelClassifier.ClassifierMode previousMode;
        private PixelClassifier.ClassifierMode? pendingColour;
        private Point pendingLocation;
        private int stableFrames;
        private byte[,]? preCastColours;
        private Rectangle preCastBounds;
        private readonly System.Diagnostics.Stopwatch diagnosticTimer = System.Diagnostics.Stopwatch.StartNew();

        public PixelClassifier.ClassifierMode? DetectedColour { get; private set; }
        public event EventHandler<BobberBitmapEvent> BitmapEvent;

        public SearchBobberFinder(IPixelClassifier pixelClassifier)
        {
            this.pixelClassifier = pixelClassifier;
            previousMode = pixelClassifier.Mode;
            BitmapEvent += (s, e) => { };
        }

        public void Reset()
        {
            previousLocation = Point.Empty;
            DetectedColour = null;
            pendingColour = null;
            stableFrames = 0;
            previousMode = pixelClassifier.Mode;
        }

        public void PrepareForCast()
        {
            using var frame = WowScreen.GetBitmap(out var bounds);
            PrepareForCast(frame, bounds);
        }

        public void PrepareForCast(Bitmap frame, Rectangle bounds)
        {
            if (frame.Size != bounds.Size) { throw new ArgumentException("Frame size must match capture bounds.", nameof(bounds)); }
            Reset();
            preCastBounds = bounds;
            preCastColours = null;
            if (pixelClassifier.Mode != PixelClassifier.ClassifierMode.Auto) { return; }
            var colours = new byte[frame.Width, frame.Height];
            for (int x = 0; x < frame.Width; x++)
            {
                for (int y = 0; y < frame.Height; y++)
                {
                    var pixel = frame.GetPixel(x, y);
                    if (pixelClassifier.IsMatch(pixel.R, pixel.G, pixel.B, PixelClassifier.ClassifierMode.Red)) { colours[x, y] |= 1; }
                    if (pixelClassifier.IsMatch(pixel.R, pixel.G, pixel.B, PixelClassifier.ClassifierMode.Blue)) { colours[x, y] |= 2; }
                }
            }
            preCastColours = colours;
        }

        public Point Find()
        {
            using var frame = WowScreen.GetBitmap(out var bounds);
            return Find(frame, bounds);
        }

        // The caller owns the frame. Also supports replaying saved frames without WoW.
        public Point Find(Bitmap frame, Rectangle bounds)
        {
            if (frame.Size != bounds.Size) { throw new ArgumentException("Frame size must match capture bounds.", nameof(bounds)); }
            if (preCastColours != null && bounds != preCastBounds)
            {
                throw new OperationCanceledException("Capture area moved after the pre-cast frame.");
            }
            var mode = pixelClassifier.Mode;
            if (mode != previousMode)
            {
                Reset();
                // A different feather has a different Y position: reset the bite baseline too.
                throw new OperationCanceledException("Feather mode changed; restart detection.");
            }
            if (bounds != previousBounds) { Reset(); }
            previousBounds = bounds;

            bool automatic = mode == PixelClassifier.ClassifierMode.Auto;
            var searchMode = automatic ? DetectedColour ?? mode : mode;
            var wholeFrame = new Rectangle(Point.Empty, frame.Size);
            var searchArea = previousLocation == Point.Empty ? wholeFrame :
                Rectangle.Intersect(wholeFrame, new Rectangle(previousLocation.X - 40, previousLocation.Y - 40, 80, 80));

            var best = FindCandidate(frame, searchArea, searchMode, automatic);
            if (best == null && previousLocation != Point.Empty && !automatic)
            {
                best = FindCandidate(frame, wholeFrame, searchMode, false);
            }

            Point location = Point.Empty;
            if (best != null)
            {
                if (automatic && DetectedColour == null)
                {
                    if (pendingColour == best.Colour && Math.Abs(best.Point.X - pendingLocation.X) <= 10 &&
                        Math.Abs(best.Point.Y - pendingLocation.Y) <= 10)
                    {
                        stableFrames++;
                    }
                    else
                    {
                        stableFrames = 1;
                    }
                    pendingColour = best.Colour;
                    pendingLocation = best.Point;
                    if (stableFrames >= ConfirmationFrames)
                    {
                        DetectedColour = best.Colour;
                        logger.Info($"Auto feather: locked {DetectedColour}.");
                        location = best.Point;
                    }
                }
                else
                {
                    DetectedColour = best.Colour;
                    location = best.Point;
                }
            }
            else
            {
                pendingColour = null;
                stableFrames = 0;
            }

            // Keep the local area and colour when a locked feather disappears.
            // The next cast calls Reset, instead of jumping to another object during bite detection.
            if (location != Point.Empty || !automatic) { previousLocation = location; }

            BitmapEvent?.Invoke(this, new BobberBitmapEvent { Point = location, Bitmap = frame });
            return location == Point.Empty ? Point.Empty : WowScreen.GetScreenPositionFromBitmapPostion(location, bounds);
        }

        private Candidate? FindCandidate(Bitmap frame, Rectangle area, PixelClassifier.ClassifierMode mode, bool automatic)
        {
            var scanTimer = System.Diagnostics.Stopwatch.StartNew();
            var redPoints = new List<Point>();
            var bluePoints = new List<Point>();
            for (int x = area.Left; x < area.Right; x++)
            {
                for (int y = area.Top; y < area.Bottom; y++)
                {
                    var pixel = frame.GetPixel(x, y);
                    if (mode != PixelClassifier.ClassifierMode.Blue && redPoints.Count <= MaxColourPoints &&
                        pixelClassifier.IsMatch(pixel.R, pixel.G, pixel.B, PixelClassifier.ClassifierMode.Red) &&
                        (!automatic || !WasPresentBeforeCast(x, y, 1)))
                    {
                        redPoints.Add(new Point(x, y));
                    }
                    if (mode != PixelClassifier.ClassifierMode.Red && bluePoints.Count <= MaxColourPoints &&
                        pixelClassifier.IsMatch(pixel.R, pixel.G, pixel.B, PixelClassifier.ClassifierMode.Blue) &&
                        (!automatic || !WasPresentBeforeCast(x, y, 2)))
                    {
                        bluePoints.Add(new Point(x, y));
                    }
                }
            }

            var red = Score(redPoints, PixelClassifier.ClassifierMode.Red, automatic);
            var blue = Score(bluePoints, PixelClassifier.ClassifierMode.Blue, automatic);
            var best = red == null ? blue : blue == null ? red : red.Confidence >= blue.Confidence ? red : blue;

            if (diagnosticTimer.ElapsedMilliseconds >= 2000)
            {
                string result = best == null ? "none (zero pixels, over limit, or cluster too small)" :
                    $"{best.Colour} at {best.Point}, score={best.Confidence:F2}";
                logger.Info($"Bobber scan: mode={mode}, area={area}, redPixels={redPoints.Count}, bluePixels={bluePoints.Count}, limit={MaxColourPoints}, preCastFilter={automatic && preCastColours != null}, candidate={result}, stableFramesBeforeUpdate={stableFrames}, scanMs={scanTimer.ElapsedMilliseconds}.");
                diagnosticTimer.Restart();
            }

            // Score original pixels before adding preview highlights.
            if (best != null)
            {
                var points = best.Colour == PixelClassifier.ClassifierMode.Red ? redPoints : bluePoints;
                var colour = best.Colour == PixelClassifier.ClassifierMode.Red ? Color.Red : Color.Blue;
                foreach (var point in points) { frame.SetPixel(point.X, point.Y, colour); }
            }
            return best;
        }

        private bool WasPresentBeforeCast(int x, int y, byte colour)
        {
            if (preCastColours == null) { return false; }
            // Allow a one-pixel edge variation without treating stationary scenery as a new feather.
            for (int px = Math.Max(0, x - 1); px <= Math.Min(preCastColours.GetLength(0) - 1, x + 1); px++)
            {
                for (int py = Math.Max(0, y - 1); py <= Math.Min(preCastColours.GetLength(1) - 1, y + 1); py++)
                {
                    if ((preCastColours[px, py] & colour) != 0) { return true; }
                }
            }
            return false;
        }

        private static Candidate? Score(List<Point> points, PixelClassifier.ClassifierMode colour, bool automatic)
        {
            // Reject broad areas (e.g. lava or blue water) independently for each colour.
            if (points.Count == 0 || points.Count > MaxColourPoints) { return null; }
            Point best = Point.Empty;
            int bestCount = 0;
            foreach (var point in points)
            {
                int count = points.Count(other => Math.Abs(other.X - point.X) < 10 && Math.Abs(other.Y - point.Y) < 10);
                if (count > bestCount) { best = point; bestCount = count; }
            }
            if (automatic && bestCount < 4) { return null; }
            // Prefer a dense cluster over the same number of scattered pixels.
            return new Candidate { Point = best, Colour = colour, Confidence = (double)bestCount * bestCount / points.Count };
        }

        private sealed class Candidate
        {
            public Point Point;
            public PixelClassifier.ClassifierMode Colour;
            public double Confidence;
        }
    }
}
