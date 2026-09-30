using log4net;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

#nullable enable
namespace FishingFun
{
    public class SearchBobberFinder : IBobberFinder, IImageProvider, ICastAwareBobberFinder
    {
        private const int MaxColourPoints = 1000;
        private const int ConfirmationFrames = 3;
        private readonly IPixelClassifier pixelClassifier;
        private readonly BobberTemplateMatcher? templateMatcher;
        private BobberTemplateMatcher.Match? lockedTemplate;
        private BobberTemplateMatcher.Match? pendingTemplate;
        private Rectangle featherArea;
        private bool savedTrackingLoss;
        private bool hasTemplateBaseline;
        private const double MinimumTemplateScore = BobberTemplateMatcher.MinimumScore;
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

        public SearchBobberFinder(IPixelClassifier pixelClassifier, bool useTemplates = true)
        {
            this.pixelClassifier = pixelClassifier;
            if (useTemplates) { templateMatcher = BobberTemplateMatcher.LoadDefault(); }
            previousMode = pixelClassifier.Mode;
            BitmapEvent += (s, e) => { };
        }

        public void Reset()
        {
            previousLocation = Point.Empty;
            DetectedColour = null;
            pendingColour = null;
            stableFrames = 0;
            lockedTemplate = pendingTemplate = null;
            savedTrackingLoss = false;
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
            hasTemplateBaseline = false;
            if (pixelClassifier.Mode != PixelClassifier.ClassifierMode.Auto &&
                pixelClassifier.Mode != PixelClassifier.ClassifierMode.AutoColour) { return; }
            if (templateMatcher != null && pixelClassifier.Mode == PixelClassifier.ClassifierMode.Auto)
            {
                templateMatcher.PrepareForCast(frame);
                SaveDiagnosticFrame(frame, "before-cast.png");
                hasTemplateBaseline = true;
                return;
            }
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
            if ((preCastColours != null || hasTemplateBaseline) && bounds != preCastBounds)
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

            bool automatic = mode == PixelClassifier.ClassifierMode.Auto || mode == PixelClassifier.ClassifierMode.AutoColour;
            if (mode == PixelClassifier.ClassifierMode.Auto && templateMatcher != null) { return FindTemplate(frame, bounds); }
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

        private Point FindTemplate(Bitmap frame, Rectangle bounds)
        {
            Point location = Point.Empty;
            if (lockedTemplate != null)
            {
                // A dipping feather changes the whole float's appearance. Track its actual pixels,
                // using the same colour and coordinate definition as the initial bite baseline.
                var searchArea = featherArea;
                bool small = lockedTemplate.Bounds.Width <= 40;
                // A small float can move completely outside its old feather rectangle in one bite.
                // Allow that motion, then choose the nearest same-colour component, not nearby clutter.
                if (small) { searchArea.Inflate(10, 10); }
                var feather = FindTemplateFeather(frame, searchArea, DetectedColour!.Value,
                    small ? lockedTemplate.Bounds.Size : (Size?)null, small ? previousLocation : (Point?)null);
                if (feather != null && Math.Abs(feather.Point.X - previousLocation.X) <= 24 &&
                    Math.Abs(feather.Point.Y - previousLocation.Y) <= 24)
                {
                    location = feather.Point;
                    featherArea.Offset(location.X - previousLocation.X, location.Y - previousLocation.Y);
                    previousLocation = location;
                }
                if (location == Point.Empty && !savedTrackingLoss)
                {
                    SaveDiagnosticFrame(frame, "tracking-lost.png");
                    savedTrackingLoss = true;
                }
                if (diagnosticTimer.ElapsedMilliseconds >= 2000)
                {
                    logger.Info($"Bobber feather tracking: colour={DetectedColour}, area={featherArea}, point={location}, found={location != Point.Empty}.");
                    diagnosticTimer.Restart();
                }
            }
            else
            {
                var best = templateMatcher!.Find(frame, new Rectangle(Point.Empty, frame.Size));
                if (best != null && best.Score >= MinimumTemplateScore)
                {
                    bool consistent = pendingTemplate != null && Math.Abs(best.Point.X - pendingTemplate.Point.X) <= 10 &&
                        Math.Abs(best.Point.Y - pendingTemplate.Point.Y) <= 10 &&
                        Math.Abs(best.Bounds.Width - pendingTemplate.Bounds.Width) <= pendingTemplate.Bounds.Width * .25;
                    stableFrames = consistent ? stableFrames + 1 : 1;
                    pendingTemplate = best;
                    if (stableFrames >= ConfirmationFrames)
                    {
                        // The lower part contains the cork; only the upper part provides feather pixels.
                        featherArea = new Rectangle(best.Bounds.X, best.Bounds.Y, best.Bounds.Width,
                            Math.Max(1, (int)Math.Ceiling(best.Bounds.Height * .6)));
                        var feather = FindTemplateFeather(frame, featherArea, PixelClassifier.ClassifierMode.Auto,
                            best.Bounds.Width <= 40 ? best.Bounds.Size : (Size?)null);
                        if (feather != null && best.Bounds.Width <= 40)
                        {
                            var trackingArea = featherArea;
                            trackingArea.Inflate(10, 10);
                            // Use the complete component for both the first sample and later tracking.
                            // A tight template crop can otherwise clip a few pixels and shift the anchor.
                            feather = FindTemplateFeather(frame, trackingArea, feather.Colour, best.Bounds.Size, feather.Point);
                        }
                        if (feather != null)
                        {
                            lockedTemplate = best;
                            DetectedColour = feather.Colour;
                            location = previousLocation = feather.Point;
                            SaveDiagnosticFrame(frame, "locked.png");
                            logger.Info($"Bobber template locked: bounds={best.Bounds}, score={best.Score:F3}; tracking {DetectedColour} feather at {location} locally.");
                        }
                    }
                }
                else { pendingTemplate = null; stableFrames = 0; }

                if (diagnosticTimer.ElapsedMilliseconds >= 2000)
                {
                    logger.Info($"Bobber template scan: score={(best == null ? "none" : best.Score.ToString("F3"))}, minimum={MinimumTemplateScore}, locked={lockedTemplate != null}, stableFrames={stableFrames}, bounds={best?.Bounds}.");
                    if (lockedTemplate == null) { SaveDiagnosticFrame(frame, "searching.png"); }
                    diagnosticTimer.Restart();
                }
            }
            BitmapEvent?.Invoke(this, new BobberBitmapEvent { Point = location, Bitmap = frame });
            return location == Point.Empty ? Point.Empty : WowScreen.GetScreenPositionFromBitmapPostion(location, bounds);
        }

        private static Candidate? FindTemplateFeather(Bitmap frame, Rectangle area, PixelClassifier.ClassifierMode mode,
            Size? smallSize, Point? anchor = null)
        {
            area.Intersect(new Rectangle(Point.Empty, frame.Size));
            var red = new List<Point>();
            var blue = new List<Point>();
            for (int x = area.Left; x < area.Right; x++)
                for (int y = area.Top; y < area.Bottom; y++)
                {
                    int colour = BobberTemplateMatcher.FeatherColour(frame.GetPixel(x, y));
                    if (colour == 1 && mode != PixelClassifier.ClassifierMode.Blue && red.Count <= MaxColourPoints)
                        red.Add(new Point(x, y));
                    if (colour == 2 && mode != PixelClassifier.ClassifierMode.Red && blue.Count <= MaxColourPoints)
                        blue.Add(new Point(x, y));
                }
            var redCandidate = smallSize.HasValue ? ScoreSmallFeather(red, PixelClassifier.ClassifierMode.Red, anchor, smallSize.Value) :
                Score(red, PixelClassifier.ClassifierMode.Red, true);
            var blueCandidate = smallSize.HasValue ? ScoreSmallFeather(blue, PixelClassifier.ClassifierMode.Blue, anchor, smallSize.Value) :
                Score(blue, PixelClassifier.ClassifierMode.Blue, true);
            return redCandidate == null ? blueCandidate : blueCandidate == null ? redCandidate :
                redCandidate.Confidence >= blueCandidate.Confidence ? redCandidate : blueCandidate;
        }

        private static Candidate? ScoreSmallFeather(List<Point> points, PixelClassifier.ClassifierMode colour, Point? anchor, Size bobberSize)
        {
            if (points.Count > MaxColourPoints) { return null; }
            var remaining = new HashSet<Point>(points);
            var queue = new Queue<Point>();
            Candidate? best = null;
            int bestDistance = int.MaxValue;
            foreach (var start in points)
            {
                if (!remaining.Remove(start)) { continue; }
                queue.Enqueue(start);
                var component = new List<Point>();
                while (queue.Count > 0)
                {
                    var p = queue.Dequeue();
                    component.Add(p);
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            var next = new Point(p.X + dx, p.Y + dy);
                            if (remaining.Remove(next)) { queue.Enqueue(next); }
                        }
                }
                // Two neighbouring pixels can be the entire distant feather. Single pixels remain noise.
                if (component.Count < 2) { continue; }
                // Coloured water surrounding a tiny float is not a feather, even inside this local area.
                if (component.Max(p => p.X) - component.Min(p => p.X) + 1 > Math.Max(3, bobberSize.Width * .8) ||
                    component.Max(p => p.Y) - component.Min(p => p.Y) + 1 > Math.Max(3, bobberSize.Height * .6)) { continue; }
                var candidate = Score(component.OrderBy(p => p.X).ThenBy(p => p.Y).ToList(), colour, false)!;
                int distance = anchor.HasValue ? (candidate.Point.X - anchor.Value.X) * (candidate.Point.X - anchor.Value.X) +
                    (candidate.Point.Y - anchor.Value.Y) * (candidate.Point.Y - anchor.Value.Y) : 0;
                if (best == null || distance < bestDistance || distance == bestDistance && candidate.Confidence > best.Confidence)
                { best = candidate; bestDistance = distance; }
            }
            return best;
        }

        private static void SaveDiagnosticFrame(Bitmap frame, string filename)
        {
            try
            {
                var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bobber-diagnostics");
                Directory.CreateDirectory(directory);
                frame.Save(Path.Combine(directory, filename), System.Drawing.Imaging.ImageFormat.Png);
            }
            catch (Exception error)
            {
                // Diagnostics must never interrupt fishing (for example if the folder is read-only).
                logger.Warn("Could not save bobber diagnostic frame: " + filename, error);
            }
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
