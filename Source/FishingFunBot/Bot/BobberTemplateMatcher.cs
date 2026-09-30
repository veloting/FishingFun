using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace FishingFun
{
    // Appearance examples come from the repository's bobber collage, not from the water being searched.
    public sealed class BobberTemplateMatcher
    {
        public const double MinimumScore = .73;
        internal sealed class Template
        {
            public int Width, Height;
            public PointF RedAnchor, BlueAnchor;
            public Point[] Samples = Array.Empty<Point>();
            public double[] Values = Array.Empty<double>();
            public double Sum, Variance;
            public double[] Chroma = Array.Empty<double>();
            public double ChromaSum, ChromaVariance;
        }

        public sealed class Match
        {
            public Rectangle Bounds;
            public Point Point;
            public double Score;
            internal Template? Model;
        }

        private sealed class Pixels
        {
            public readonly int Width, Height;
            public readonly int[] Colours;
            public readonly double[] Grey;
            public readonly double[] Red, Blue;
            public Pixels(Bitmap image)
            {
                Width = image.Width; Height = image.Height;
                Colours = new int[Width * Height]; Grey = new double[Colours.Length];
                Red = new double[Colours.Length]; Blue = new double[Colours.Length];
                using (var copy = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(copy)) { g.DrawImageUnscaled(image, 0, 0); }
                    var data = copy.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        for (int y = 0; y < Height; y++)
                            Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), Colours, y * Width, Width);
                    }
                    finally { copy.UnlockBits(data); }
                }
                for (int i = 0; i < Colours.Length; i++)
                {
                    int rgb = Colours[i];
                    int r = (rgb >> 16) & 255, g = (rgb >> 8) & 255, b = rgb & 255;
                    Grey[i] = r * .299 + g * .587 + b * .114;
                    double brightness = Math.Max(30, Math.Max(r, Math.Max(g, b)));
                    Red[i] = (r - g) / brightness;
                    Blue[i] = (b - g) / brightness;
                }
            }

            public int Colour(int i)
            {
                return FeatherColour(Color.FromArgb(Colours[i]));
            }
        }

        internal static int FeatherColour(Color pixel)
        {
            int r = pixel.R, g = pixel.G, b = pixel.B;
            if (r > 25 && r > g * 1.15 && r > b * 1.12) { return 1; }
            if (b > 25 && b > g * 1.15 && b > r * 1.12) { return 2; }
            return 0;
        }

        private readonly List<Template> templates = new List<Template>();
        private Pixels? beforeCast;

        public static BobberTemplateMatcher LoadDefault()
        {
            using (var stream = typeof(BobberTemplateMatcher).Assembly.GetManifestResourceStream("FishingFun.BobberTemplates.png"))
            {
                if (stream == null) { throw new InvalidOperationException("缺少内置鱼漂模板，请重新编译完整程序。"); }
                using (var collage = new Bitmap(stream)) { return new BobberTemplateMatcher(collage); }
            }
        }

        public void PrepareForCast(Bitmap frame) { beforeCast = new Pixels(frame); }

        public BobberTemplateMatcher(Bitmap collage)
        {
            var regions = new[] {
                new Rectangle(7, 6, 68, 66), new Rectangle(98, 5, 86, 80),
                new Rectangle(207, 17, 96, 85), new Rectangle(330, 9, 76, 78),
                new Rectangle(427, 11, 79, 77), new Rectangle(532, 7, 71, 79),
                new Rectangle(197, 205, 89, 84), new Rectangle(569, 204, 85, 85),
                new Rectangle(4, 80, 67, 67), new Rectangle(99, 103, 77, 73), new Rectangle(371, 127, 71, 72)
            };
            foreach (var region in regions)
            {
                using (var original = collage.Clone(region, PixelFormat.Format32bppArgb))
                // At a distance a one-pixel scale difference is significant. Cover every small size.
                for (double width = 8; width <= 120; width = width < 40 ? width + 1 : width * 1.10)
                {
                    using (var scaled = new Bitmap((int)Math.Round(width), (int)Math.Round(region.Height * width / region.Width)))
                    {
                        using (var g = Graphics.FromImage(scaled))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(original, new Rectangle(Point.Empty, scaled.Size));
                        }
                        templates.Add(CreateTemplate(new Pixels(scaled)));
                    }
                }
            }
        }

        private static Template CreateTemplate(Pixels pixels)
        {
            var positions = new List<Point>();
            var values = new List<double>();
            // Keep feather, stem and cork structure. Exclude the empty corners of the source crop.
            int columns = Math.Min(14, pixels.Width), rows = Math.Min(14, pixels.Height);
            for (int gy = 0; gy < rows; gy++)
                for (int gx = 0; gx < columns; gx++)
                {
                    double x = (gx + .5) / columns, y = (gy + .5) / rows;
                    if ((y < .15 && x > .7) || (y > .62 && x < .45)) { continue; }
                    var p = new Point(Math.Min(pixels.Width - 1, (int)(x * pixels.Width)), Math.Min(pixels.Height - 1, (int)(y * pixels.Height)));
                    positions.Add(p); values.Add(pixels.Grey[p.Y * pixels.Width + p.X]);
                }
            var template = new Template { Width = pixels.Width, Height = pixels.Height, Samples = positions.ToArray(), Values = values.ToArray() };
            template.Sum = values.Sum();
            template.Variance = values.Sum(v => v * v) - template.Sum * template.Sum / values.Count;
            template.Chroma = positions.SelectMany(p => new[] { pixels.Red[p.Y * pixels.Width + p.X], pixels.Blue[p.Y * pixels.Width + p.X] }).ToArray();
            template.ChromaSum = template.Chroma.Sum();
            template.ChromaVariance = template.Chroma.Sum(v => v * v) - template.ChromaSum * template.ChromaSum / template.Chroma.Length;
            template.RedAnchor = Anchor(pixels, 1);
            template.BlueAnchor = Anchor(pixels, 2);
            return template;
        }

        private static PointF Anchor(Pixels pixels, int colour)
        {
            int n = 0, xsum = 0, ysum = 0;
            for (int y = 0; y < pixels.Height * .6; y++)
                for (int x = 0; x < pixels.Width; x++)
                    if (pixels.Colour(y * pixels.Width + x) == colour) { n++; xsum += x; ysum += y; }
            return n == 0 ? new PointF(pixels.Width * .45f, pixels.Height * .35f) : new PointF((float)xsum / n, (float)ysum / n);
        }

        public Match? Find(Bitmap frame, Rectangle area)
        {
            var pixels = new Pixels(frame);
            area.Intersect(new Rectangle(Point.Empty, frame.Size));
            Match? best = null;
            var proposals = new List<Tuple<Match, Template>>();
            foreach (var seed in Seeds(pixels, area))
            {
                foreach (var template in templates)
                {
                    var anchor = seed.Item2 == 1 ? template.RedAnchor : template.BlueAnchor;
                    int cx = (int)Math.Round(seed.Item1.X - anchor.X), cy = (int)Math.Round(seed.Item1.Y - anchor.Y);
                    int offset = Math.Max(1, template.Width / 12);
                    for (int dy = -offset; dy <= offset; dy += offset)
                        for (int dx = -offset; dx <= offset; dx += offset)
                        {
                            int x = cx + dx, y = cy + dy;
                            if (x < area.Left || y < area.Top || x + template.Width > area.Right || y + template.Height > area.Bottom) { continue; }
                            double score = Correlation(pixels, template, x, y);
                            if (!IsNew(template, x, y, score)) { continue; }
                            if (proposals.Count == 16 && score <= proposals[15].Item1.Score) { continue; }
                            var match = Result(template, x, y, score);
                            proposals.Add(Tuple.Create(match, template));
                            proposals.Sort((a, b) => b.Item1.Score.CompareTo(a.Item1.Score));
                            if (proposals.Count > 16) { proposals.RemoveAt(16); }
                        }
                }
            }
            foreach (var proposal in proposals)
            {
                var template = proposal.Item2;
                int radius = Math.Max(1, template.Width / 12);
                for (int y = proposal.Item1.Bounds.Y - radius; y <= proposal.Item1.Bounds.Y + radius; y++)
                    for (int x = proposal.Item1.Bounds.X - radius; x <= proposal.Item1.Bounds.X + radius; x++)
                    {
                        if (x < area.Left || y < area.Top || x + template.Width > area.Right || y + template.Height > area.Bottom) { continue; }
                        double score = Correlation(pixels, template, x, y);
                        if (!IsNew(template, x, y, score)) { continue; }
                        if (best == null || score > best.Score)
                            best = Result(template, x, y, score);
                    }
            }
            return best;
        }

        private bool IsNew(Template template, int x, int y, double score)
        {
            if (beforeCast == null) { return true; }
            if (x + template.Width > beforeCast.Width || y + template.Height > beforeCast.Height) { return false; }
            double oldScore = Correlation(beforeCast, template, x, y);
            return oldScore < .65 && score - oldScore >= .12;
        }

        private static Match Result(Template template, int x, int y, double score)
            => new Match { Score = score, Bounds = new Rectangle(x, y, template.Width, template.Height), Model = template,
                Point = new Point(x + (int)Math.Round(template.RedAnchor.X), y + (int)Math.Round(template.RedAnchor.Y)) };

        public Match? Track(Bitmap frame, Match previous)
        {
            var template = previous.Model ?? throw new ArgumentException("The previous match has no template.", nameof(previous));
            var pixels = new Pixels(frame);
            Match? best = null;
            // Keep the same appearance, size and feather anchor so matching cannot manufacture a bite-sized jump.
            for (int y = Math.Max(0, previous.Bounds.Y - 24); y <= Math.Min(frame.Height - template.Height, previous.Bounds.Y + 24); y++)
                for (int x = Math.Max(0, previous.Bounds.X - 24); x <= Math.Min(frame.Width - template.Width, previous.Bounds.X + 24); x++)
                {
                    double score = Correlation(pixels, template, x, y);
                    if (best == null || score > best.Score) { best = Result(template, x, y, score); }
                }
            return best;
        }

        private static double Correlation(Pixels pixels, Template template, int x, int y)
        {
            double sum = 0, square = 0, product = 0, chromaSum = 0, chromaSquare = 0, chromaProduct = 0;
            for (int i = 0; i < template.Samples.Length; i++)
            {
                var p = template.Samples[i];
                int index = (y + p.Y) * pixels.Width + x + p.X;
                double value = pixels.Grey[index], red = pixels.Red[index], blue = pixels.Blue[index];
                sum += value; square += value * value; product += value * template.Values[i];
                chromaSum += red + blue; chromaSquare += red * red + blue * blue;
                chromaProduct += red * template.Chroma[i * 2] + blue * template.Chroma[i * 2 + 1];
            }
            double variance = square - sum * sum / template.Samples.Length;
            if (variance < template.Samples.Length * 4 || template.Variance <= 0) { return -1; }
            double grey = (product - sum * template.Sum / template.Samples.Length) / Math.Sqrt(variance * template.Variance);
            double chromaVariance = chromaSquare - chromaSum * chromaSum / template.Chroma.Length;
            if (chromaVariance < .01 || template.ChromaVariance < .01) { return -1; }
            double chroma = (chromaProduct - chromaSum * template.ChromaSum / template.Chroma.Length) /
                Math.Sqrt(chromaVariance * template.ChromaVariance);
            // Colour layout helps against plain scenery; a luminance-led comparison handles strongly coloured water.
            return Math.Max(grey * .9 + chroma * .1, grey * .4 + chroma * .6);
        }

        private IEnumerable<Tuple<Point, int>> Seeds(Pixels pixels, Rectangle area)
        {
            var seen = new bool[pixels.Colours.Length];
            var queue = new Queue<int>();
            var seeds = new List<Tuple<Point, int, int>>();
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    int start = y * pixels.Width + x, colour = pixels.Colour(start);
                    if (seen[start] || colour == 0) { continue; }
                    queue.Enqueue(start); seen[start] = true;
                    int count = 0, newPixels = 0, xsum = 0, ysum = 0, left = x, right = x, top = y, bottom = y;
                    while (queue.Count > 0)
                    {
                        int i = queue.Dequeue(), px = i % pixels.Width, py = i / pixels.Width;
                        count++; xsum += px; ysum += py;
                        if (beforeCast == null || !ColourWasPresent(px, py, colour)) { newPixels++; }
                        left = Math.Min(left, px); right = Math.Max(right, px); top = Math.Min(top, py); bottom = Math.Max(bottom, py);
                        for (int ny = Math.Max(area.Top, py - 1); ny <= Math.Min(area.Bottom - 1, py + 1); ny++)
                            for (int nx = Math.Max(area.Left, px - 1); nx <= Math.Min(area.Right - 1, px + 1); nx++)
                            {
                                int next = ny * pixels.Width + nx;
                                if (!seen[next] && pixels.Colour(next) == colour) { seen[next] = true; queue.Enqueue(next); }
                            }
                    }
                    // Remove stationary scenery before capping candidates. Large existing colour
                    // patches must not push a newly appeared, two-pixel feather out of the list.
                    if (count >= 2 && newPixels >= 2 && right - left < 120 && bottom - top < 90)
                        seeds.Add(Tuple.Create(new Point(xsum / count, ysum / count), colour, count));
                }
            return seeds.OrderByDescending(s => s.Item3).Take(100).Select(s => Tuple.Create(s.Item1, s.Item2));
        }

        private bool ColourWasPresent(int x, int y, int colour)
        {
            var baseline = beforeCast!;
            for (int py = Math.Max(0, y - 1); py <= Math.Min(baseline.Height - 1, y + 1); py++)
                for (int px = Math.Max(0, x - 1); px <= Math.Min(baseline.Width - 1, x + 1); px++)
                    if (baseline.Colour(py * baseline.Width + px) == colour) { return true; }
            return false;
        }
    }
}
