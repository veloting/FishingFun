using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace FishingFun
{
    public partial class ColourConfiguration : System.Windows.Window
    {
        private readonly IPixelClassifier pixelClassifier;

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

        public ColourConfiguration(IPixelClassifier pixelClassifier)
        {
            this.pixelClassifier = pixelClassifier;
            FindColourValue = 100;

            InitializeComponent();

            this.DataContext = this;

            cmbColors.ItemsSource = new[]
            {
                new { Name = "Auto", Swatch = "Gray", Mode = PixelClassifier.ClassifierMode.Auto },
                new { Name = "Red", Swatch = "Red", Mode = PixelClassifier.ClassifierMode.Red },
                new { Name = "Blue", Swatch = "Blue", Mode = PixelClassifier.ClassifierMode.Blue }
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
            for (int x = 0; x < bmp.Width; x++)
            {
                for (int y = 0; y < bmp.Height; y++)
                {
                    var pixel = bmp.GetPixel(x, y);
                    if (this.pixelClassifier.IsMatch(pixel.R, pixel.G, pixel.B))
                    {
                        bool blue = this.pixelClassifier.Mode == PixelClassifier.ClassifierMode.Blue ||
                            (this.pixelClassifier.Mode == PixelClassifier.ClassifierMode.Auto &&
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
                this.LabelColourMultiplier.Text = "Auto: choose a stable red or blue feather for each cast.";
                this.LabelColourClosenessMultiplier.Text = "Colour sliders are optional fine-tuning if detection fails.";
                this.ColourLabel.Content = "Red:";
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
            if (cmbColors.SelectedValue is PixelClassifier.ClassifierMode mode)
            {
                this.pixelClassifier.Mode = mode;
                PrimaryColor = mode == PixelClassifier.ClassifierMode.Blue ? "Blue" : "Red";
                SecondaryColor = mode == PixelClassifier.ClassifierMode.Blue ? "red" : "blue";
                UpdateColourText();
                RenderColour(true);
            }
        }
    }
}
