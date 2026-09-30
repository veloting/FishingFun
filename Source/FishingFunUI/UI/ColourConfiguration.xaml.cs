using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace FishingFun
{
    public partial class ColourConfiguration : System.Windows.Window
    {
        private readonly IPixelClassifier pixelClassifier;
        private readonly string settingsPath;
        private sealed class ModeOption
        {
            public string Name { get; set; } = "";
            public string Swatch { get; set; } = "";
            public PixelClassifier.ClassifierMode Mode { get; set; }
        }
        private readonly Lazy<BobberTemplateMatcher> templateMatcher = new Lazy<BobberTemplateMatcher>(BobberTemplateMatcher.LoadDefault);

        private Bitmap ScreenCapture = new Bitmap(1, 1);

        public int FindColourValue { get; set; }

        public string PrimaryColor = "Red";
        public string SecondaryColor = "blue";

        public int ColourMultiplier
        {
            get
            {
                return (int)(pixelClassifier.ColourMultiplier * 100);
            }
            set
            {
                pixelClassifier.ColourMultiplier = ((double)value) / 100;
            }
        }

        public int ColourClosenessMultiplier
        {
            get
            {
                return (int)(pixelClassifier.ColourClosenessMultiplier * 100);
            }
            set
            {
                pixelClassifier.ColourClosenessMultiplier = ((double)value) / 100;
            }
        }

        public ColourConfiguration(IPixelClassifier pixelClassifier, string? settingsPath = null)
        {
            this.pixelClassifier = pixelClassifier;
            this.settingsPath = settingsPath ?? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bobber-detection.xml");
            FindColourValue = 100;

            InitializeComponent();

            this.DataContext = this;

            cmbColors.ItemsSource = new[]
            {
                new ModeOption { Name = "新版：图像识别", Swatch = "Gray", Mode = PixelClassifier.ClassifierMode.Auto },
                new ModeOption { Name = "旧版：自动颜色", Swatch = "Purple", Mode = PixelClassifier.ClassifierMode.AutoColour },
                new ModeOption { Name = "旧版：红色羽毛", Swatch = "Red", Mode = PixelClassifier.ClassifierMode.Red },
                new ModeOption { Name = "旧版：蓝色羽毛", Swatch = "Blue", Mode = PixelClassifier.ClassifierMode.Blue }
            };
            cmbColors.SelectedValue = this.pixelClassifier.Mode;
            LootDelay.Value = WowProcess.LootDelay;
        }

        private void RenderColour(bool renderMatchedArea)
        {
            var bitmap = new System.Drawing.Bitmap(256, 256);

            var points = new List<Point>();

            for (var i = 0; i < 256; i++)
            {
                for (var g = 0; g < 256; g++)
                {
                    var r = (byte)this.FindColourValue;
                    var b = (byte)i;

                    if (this.pixelClassifier.Mode == PixelClassifier.ClassifierMode.Blue)
                    {
                        r = (byte)i;
                        b = (byte)this.FindColourValue;

                    }

                    if (pixelClassifier.IsMatch(r, (byte)g, b))
                    {
                        points.Add(new Point(i, g));
                    }
                    bitmap.SetPixel(i, g, Color.FromArgb(r, g, b));

                }
            }

            if (ScreenCapture == null)
            {
                ScreenCapture = WowScreen.GetBitmap(out _, requireForeground: false);
                renderMatchedArea = true;
            }

            this.ColourDisplay.Source = bitmap.ToBitmapImage();
            this.WowScreenshot.Source = ScreenCapture.ToBitmapImage();

            if (renderMatchedArea)
            {
                Dispatch(() =>
                {
                    MarkEdgeOfRedArea(bitmap, points);
                    this.ColourDisplay.Source = bitmap.ToBitmapImage();
                });

                Dispatch(() =>
                {
                    Bitmap bmp = new Bitmap(ScreenCapture);
                    MarkHighlightOnBitmap(bmp);
                    this.WowScreenshot.Source = bmp.ToBitmapImage();
                });
            }
        }

        private void MarkHighlightOnBitmap(Bitmap bmp)
        {
            if (pixelClassifier.Mode == PixelClassifier.ClassifierMode.Auto)
            {
                var match = templateMatcher.Value.Find(bmp, new Rectangle(Point.Empty, bmp.Size));
                if (match != null && match.Score >= BobberTemplateMatcher.MinimumScore)
                    using (var g = Graphics.FromImage(bmp))
                    using (var pen = new Pen(Color.LimeGreen, 2)) { g.DrawRectangle(pen, match.Bounds); }
                return;
            }
            for (int x = 0; x < bmp.Width; x++)
            {
                for (int y = 0; y < bmp.Height; y++)
                {
                    var pixel = bmp.GetPixel(x, y);
                    if (this.pixelClassifier.IsMatch(pixel.R, pixel.G, pixel.B))
                    {
                        bool blue = this.pixelClassifier.Mode == PixelClassifier.ClassifierMode.Blue ||
                            (this.pixelClassifier.Mode == PixelClassifier.ClassifierMode.AutoColour &&
                            !this.pixelClassifier.IsMatch(pixel.R, pixel.G, pixel.B, PixelClassifier.ClassifierMode.Red));
                        bmp.SetPixel(x, y, blue ? Color.Blue : Color.Red);
                    }
                }
            }
        }

        private static void MarkEdgeOfRedArea(Bitmap bitmap, List<Point> points)
        {
            foreach (var point in points)
            {
                var pointsClose = points.Count(p => (p.X == point.X && (p.Y == point.Y - 1 || p.Y == point.Y + 1)) || (p.Y == point.Y && (p.X == point.X - 1 || p.X == point.X + 1)));
                if (pointsClose < 4)
                {
                    bitmap.SetPixel(point.X, point.Y, Color.White);
                }
            }
        }
        private void LootDelay_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            WowProcess.LootDelay = (int)this.LootDelay.Value;
        }

        private void FindColour_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            this.LabelRed.Content = this.FindColourValue;
            RenderColour(false);
        }

        private void ColourMultiplier_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateColourText();
        }

        private void ColourClosenessMultiplier_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateColourText();
        }

        public void UpdateColourText()
        {
            if (pixelClassifier.Mode == PixelClassifier.ClassifierMode.Auto)
            {
                this.LabelColourMultiplier.Text = "Auto：先用图像定位鱼漂，再追踪同一根羽毛；预览绿框为候选位置。";
                this.LabelColourClosenessMultiplier.Text = "颜色滑块仅用于旧版颜色识别。新版连续确认后局部追踪，保留下沉动作。";
                this.ColourLabel.Content = "Red:";
                return;
            }
            if (pixelClassifier.Mode == PixelClassifier.ClassifierMode.AutoColour)
            {
                this.LabelColourMultiplier.Text = "旧版：按红 / 蓝颜色寻找羽毛，连续确认后跟踪同一种颜色，不进行图像匹配。";
                this.LabelColourClosenessMultiplier.Text = "下方颜色参数对旧版生效；切换模式会重新抛竿建立基准，自动换号仍按原配置运行。";
                this.ColourLabel.Content = "Red / Blue:";
                return;
            }
            this.LabelColourMultiplier.Text = $"{PrimaryColor} multiplied by {this.pixelClassifier.ColourMultiplier} must be greater than green and {SecondaryColor}.";
            this.LabelColourClosenessMultiplier.Text = $"How close green and {SecondaryColor} need to be to each other: {this.pixelClassifier.ColourClosenessMultiplier}";
            this.ColourLabel.Content = PrimaryColor + ":";
        }

        private void Slider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            RenderColour(true);
        }

        private void Capture_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                var capture = WowScreen.GetBitmap(out _, requireForeground: false);
                ScreenCapture.Dispose();
                ScreenCapture = capture;
                RenderColour(true);
            }
            catch (OperationCanceledException)
            {
                System.Windows.MessageBox.Show(this, "请先打开 WoW，恢复游戏窗口，并确保识别区域在屏幕内且没有被遮挡。", "无法截取游戏画面");
            }
        }

        private void Dispatch(Action action)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke((Action)(() => action()));
            System.Windows.Application.Current?.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(delegate { }));
        }

        private void cmbColors_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cmbColors.SelectedItem is ModeOption option)
            {
                var mode = option.Mode;
                this.pixelClassifier.Mode = mode;
                PrimaryColor = mode == PixelClassifier.ClassifierMode.Blue ? "Blue" : "Red";
                SecondaryColor = mode == PixelClassifier.ClassifierMode.Blue ? "red" : "blue";
                UpdateColourText();
                RenderColour(true);
            }
        }

        private void Save_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                BobberDetectionSettings.SaveMode(settingsPath, pixelClassifier.Mode);
                Close();
            }
            catch (Exception error)
            {
                System.Windows.MessageBox.Show(this, "识别模式保存失败：" + error.Message, "保存失败");
            }
        }
    }
}
