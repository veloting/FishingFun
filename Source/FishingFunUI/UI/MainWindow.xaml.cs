#nullable enable
namespace FishingFun
{
    using log4net.Appender;
    using log4net.Core;
    using log4net.Repository.Hierarchy;
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Timers;
    using System.Windows;
    using System.Windows.Controls;

    public partial class MainWindow : Window, IAppender
    {
        private System.Drawing.Point lastPoint = System.Drawing.Point.Empty;
        public ObservableCollection<LogEntry> LogEntries { get; set; }

        private IBobberFinder bobberFinder;
        private IPixelClassifier pixelClassifier;
        private IBiteWatcher biteWatcher;
        private ReticleDrawer reticleDrawer = new ReticleDrawer();

        private FishingBot? bot;
        private int strikeValue = 7; // this is the depth the bobber must go for the bite to be detected
        private bool setImageBackgroundColour = true;
        private Timer WindowSizeChangedTimer;
        private System.Threading.Thread? botThread;
        private CharacterRotationSettings rotationSettings = new CharacterRotationSettings();
        private readonly string rotationSettingsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "character-rotation.xml");
        private readonly string detectionSettingsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bobber-detection.xml");

        public MainWindow()
        {
            InitializeComponent();
            try
            {
                rotationSettings = CharacterRotationSettings.Load(rotationSettingsPath);
                ShowRotationConfiguration();
            }
            catch (Exception e) { RotationStatus.Text = "换号设置读取失败，请重新设置：" + e.Message; }

            ((Logger)FishingBot.logger.Logger).AddAppender(this);

            this.DataContext = LogEntries = new ObservableCollection<LogEntry>();
            this.pixelClassifier = new PixelClassifier();
            pixelClassifier.SetConfiguration(WowProcess.IsWowClassic());
            try { pixelClassifier.Mode = BobberDetectionSettings.LoadMode(detectionSettingsPath); }
            catch (Exception error) { FishingBot.logger.Warn("识别模式设置读取失败，使用新版图像识别。", error); }
             
            this.bobberFinder = new SearchBobberFinder(pixelClassifier);

            var imageProvider = bobberFinder as IImageProvider;
            if (imageProvider != null)
            {
                imageProvider.BitmapEvent += ImageProvider_BitmapEvent;
            }

            this.biteWatcher = new PositionBiteWatcher(strikeValue);

            this.WindowSizeChangedTimer = new Timer { AutoReset = false, Interval = 100 };
            this.WindowSizeChangedTimer.Elapsed += SizeChangedTimer_Elapsed;
            this.CardGrid.SizeChanged += MainWindow_SizeChanged;
            this.Closing += (s, e) =>
            {
                bot?.Stop();
                WindowSizeChangedTimer.Stop();
            };

            this.KeyChooser.CastKeyChanged += (s, e) =>
            {
                this.Settings.Focus();
                this.bot?.SetCastKey(this.KeyChooser.CastKey);
            };
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Reset the timer so it only fires 100ms after the user stop dragging the window.
            WindowSizeChangedTimer.Stop();
            WindowSizeChangedTimer.Start();
        }

        private void SizeChangedTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            this.Dispatch(() =>
            {
                this.flyingFishAnimation.AnimationWidth = (int)this.ActualWidth;
                this.flyingFishAnimation.AnimationHeight = (int)this.ActualHeight;
                this.LogGrid.Height = this.LogFlipper.ActualHeight;
                this.GraphGrid.Height = this.GraphFlipper.ActualHeight;
                this.GraphGrid.Visibility = Visibility.Visible;
                this.GraphFlipper.IsFlipped = true;
                this.LogFlipper.IsFlipped = true;
                this.GraphFlipper.IsFlipped = false;
                this.LogFlipper.IsFlipped = false;
            });
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            bot?.Stop();
            if (rotationSettings.Enabled) { RotationStatus.Text = "已请求停止；重启前请在换号设置中核对当前角色。"; }
        }

        private void ShowRotationConfiguration()
        {
            RotationStatus.Text = rotationSettings.Enabled ?
                "自动换号：每个角色 " + rotationSettings.MinutesPerCharacter + " 分钟；启动前请核对当前角色。" : "自动换号：未启用";
        }

        private void RotationSettings_Click(object sender, RoutedEventArgs e)
        {
            if (bot != null) { return; }
            var window = new CharacterRotationWindow(rotationSettings, rotationSettingsPath) { Owner = this };
            if (window.ShowDialog() == true)
            {
                rotationSettings = window.Result;
                ShowRotationConfiguration();
                if (window.TestRequested) { StartBot(testRotationBeforeFishing: true); }
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
            => new ColourConfiguration(this.pixelClassifier, detectionSettingsPath) { Owner = this }.ShowDialog();

        private void CastKey_Click(object sender, RoutedEventArgs e) => this.KeyChooser.Focus();

        private void FishingEventHandler(object sender, FishingEvent e)
        {
            Dispatch(() =>
            {
                switch (e.Action)
                {
                    case FishingAction.BobberMove:
                        if (!this.GraphFlipper.IsFlipped)
                        {
                            this.Chart.Add(e.Amplitude);
                        }
                        break;

                    case FishingAction.Loot:
                        this.flyingFishAnimation.Start();
                        this.LootingGrid.Visibility = Visibility.Visible;
                        break;

                    case FishingAction.Cast:
                        this.Chart.ClearChart();
                        this.LootingGrid.Visibility = Visibility.Collapsed;
                        this.flyingFishAnimation.Stop();
                        setImageBackgroundColour = true;
                        break;
                };
            });
        }

        public void DoAppend(LoggingEvent loggingEvent)
        {
            Dispatch(() =>
                LogEntries.Insert(0, new LogEntry()
                {
                    DateTime = DateTime.Now,
                    Message = loggingEvent.RenderedMessage
                })
            );
        }

        private void SetImageVisibility(Image imageForVisible, Image imageForCollapsed, bool state)
        {
            imageForVisible.Visibility = state ? Visibility.Visible : Visibility.Collapsed;
            imageForCollapsed.Visibility = !state ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetButtonStates(bool isBotRunning)
        {
            Dispatch(() =>
            {
                this.Play.IsEnabled = isBotRunning;
                this.Stop.IsEnabled = !this.Play.IsEnabled;
                this.RotationSettings.IsEnabled = this.Play.IsEnabled;
                SetImageVisibility(this.PlayImage, this.PlayImage_Disabled, this.Play.IsEnabled);
                SetImageVisibility(this.StopImage, this.StopImage_Disabled, this.Stop.IsEnabled);
            });
        }

        private void Play_Click(object sender, RoutedEventArgs e)
            => StartBot();

        private void StartBot(bool testRotationBeforeFishing = false)
        {
            if (bot == null)
            {
                try { if (rotationSettings.Enabled || testRotationBeforeFishing) { rotationSettings.Validate(); } }
                catch (Exception error) { RotationStatus.Text = error.Message; return; }
                SetButtonStates(false);
                bot = new FishingBot(bobberFinder, this.biteWatcher, KeyChooser.CastKey, new List<ConsoleKey>(), rotationSettings, testRotationBeforeFishing);
                bot.RotationStatusChanged += message => Dispatch(() => RotationStatus.Text = message);
                bot.RotationCharacterChanged += index => Dispatch(() => rotationSettings.StartingCharacter = index);
                bot.FishingEventHandler += FishingEventHandler;
                botThread = new System.Threading.Thread(new System.Threading.ThreadStart(this.BotThread)) { IsBackground = true };
                botThread.Start();

                // Hide cards after 10 minutes
                var timer = new Timer { Interval = 1000 * 60 * 10, AutoReset = false };
                timer.Elapsed += (s, ev) => this.Dispatch(() => this.LogFlipper.IsFlipped = this.GraphFlipper.IsFlipped = true);
                timer.Start();
            }
        }

        public void BotThread()
        {
            try { bot?.Start(); }
            finally
            {
                bot = null;
                SetButtonStates(true);
            }
        }

        private void ImageProvider_BitmapEvent(object sender, BobberBitmapEvent e)
        {
            // The finder owns the bitmap; finish copying it before returning to the capture thread.
            Application.Current?.Dispatcher.Invoke(() =>
            {
                SetBackgroundImageColour(e);
                reticleDrawer.Draw(e.Bitmap, e.Point);
                var bitmapImage = e.Bitmap.ToBitmapImage();
                this.Screenshot.Source = bitmapImage;
            });
        }

        private void SetBackgroundImageColour(BobberBitmapEvent e)
        {
            if (this.setImageBackgroundColour)
            {
                this.setImageBackgroundColour = false;
                this.ImageBackground.Background = e.Bitmap.GetBackgroundColourBrush();
            }
        }

        private void Dispatch(Action action)
        {
            if (Application.Current == null || Application.Current.Dispatcher.HasShutdownStarted) { return; }
            Application.Current?.Dispatcher.BeginInvoke((Action)(() => action()));
            Application.Current?.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(delegate { }));
        }
    }
}
