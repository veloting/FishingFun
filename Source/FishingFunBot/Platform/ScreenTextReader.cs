using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace FishingFun
{
    public sealed class ScreenText
    {
        public string Text { get; set; } = "";
        public Rectangle Bounds { get; set; }
    }

    // Windows OCR runs locally in a short-lived helper, so no SDK or cloud service is required.
    public sealed class ScreenTextReader
    {
        public List<ScreenText> Read(Bitmap image, Action checkReady)
        {
            checkReady();
            string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Platform", "RecognizeScreen.ps1");
            if (!File.Exists(script)) { throw new InvalidOperationException("缺少文字识别组件，请使用完整的新版程序目录。"); }
            string temporary = Path.Combine(Path.GetTempPath(), "FishingFun-ocr-" + Guid.NewGuid().ToString("N") + ".png");
            double scale = Math.Min(1, 3000.0 / Math.Max(image.Width, image.Height));
            try
            {
                using (var resized = new Bitmap(image, new Size((int)(image.Width * scale), (int)(image.Height * scale))))
                    resized.Save(temporary, ImageFormat.Png);
                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                        Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\" -ImagePath \"" + temporary + "\"",
                        UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                        RedirectStandardOutput = true, RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                    };
                    process.Start();
                    var output = process.StandardOutput.ReadToEndAsync();
                    var errors = process.StandardError.ReadToEndAsync();
                    try
                    {
                        var timer = Stopwatch.StartNew();
                        while (!process.WaitForExit(50))
                        {
                            checkReady();
                            if (timer.Elapsed.TotalSeconds > 20) { throw new InvalidOperationException("文字识别超时，已停止自动操作。"); }
                        }
                        checkReady();
                        if (process.ExitCode != 0)
                            throw new InvalidOperationException("中文文字识别失败。请检查 Windows 简体中文 OCR 语言组件，或切换手动定位。详情：" + errors.GetAwaiter().GetResult());
                        var result = new JavaScriptSerializer().Deserialize<OcrResult>(output.GetAwaiter().GetResult());
                        return result.Lines.Where(l => l.Words.Length > 0).Select(l => new ScreenText
                        {
                            Text = string.Concat(l.Text.Where(c => !char.IsWhiteSpace(c))),
                            Bounds = TransformBounds(l.Words, result, scale)
                        }).ToList();
                    }
                    finally
                    {
                        if (!process.HasExited) { process.Kill(); process.WaitForExit(2000); }
                    }
                }
            }
            finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
        }

        private static Rectangle TransformBounds(OcrWord[] words, OcrResult result, double scale)
        {
            double angle = (result.Angle ?? 0) * Math.PI / 180;
            var points = words.SelectMany(w => new[] { new PointF((float)w.X, (float)w.Y),
                new PointF((float)(w.X + w.Width), (float)w.Y), new PointF((float)w.X, (float)(w.Y + w.Height)),
                new PointF((float)(w.X + w.Width), (float)(w.Y + w.Height)) }).Select(p =>
            {
                double x = p.X - result.Width / 2.0, y = p.Y - result.Height / 2.0;
                return new PointF((float)((x * Math.Cos(angle) - y * Math.Sin(angle) + result.Width / 2.0) / scale),
                    (float)((x * Math.Sin(angle) + y * Math.Cos(angle) + result.Height / 2.0) / scale));
            }).ToArray();
            return Rectangle.FromLTRB((int)Math.Floor(points.Min(p => p.X)), (int)Math.Floor(points.Min(p => p.Y)),
                (int)Math.Ceiling(points.Max(p => p.X)), (int)Math.Ceiling(points.Max(p => p.Y)));
        }

        public sealed class OcrResult
        {
            public OcrLine[] Lines { get; set; } = Array.Empty<OcrLine>();
            public double? Angle { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }
        public sealed class OcrLine
        {
            public string Text { get; set; } = "";
            public OcrWord[] Words { get; set; } = Array.Empty<OcrWord>();
        }
        public sealed class OcrWord
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
        }
    }
}
