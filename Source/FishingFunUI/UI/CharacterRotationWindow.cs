using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using DrawingPoint = System.Drawing.Point;
using Image = System.Windows.Controls.Image;

namespace FishingFun
{
    public sealed class CharacterRotationWindow : Window
    {
        private readonly CheckBox enabled = new CheckBox { Content = "启用自动轮换（同一账号下的角色）", Margin = new Thickness(0, 8, 0, 8) };
        private readonly TextBox minutes = new TextBox { Width = 70, Margin = new Thickness(8, 0, 8, 0) };
        private readonly TextBox characterName = new TextBox { Width = 170, Margin = new Thickness(8, 0, 8, 0) };
        private readonly ListBox characters = new ListBox { Height = 115, Margin = new Thickness(0, 6, 0, 6) };
        private readonly ComboBox startingCharacter = new ComboBox { MinWidth = 170, Margin = new Thickness(8, 0, 0, 0) };
        private readonly TextBlock feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 8) };
        private readonly StackPanel controls = new StackPanel { Margin = new Thickness(18) };
        private readonly StackPanel manualControls = new StackPanel();
        private StackPanel section;
        private readonly CheckBox automatic = new CheckBox { Content = "自动识别位置（推荐，无需手动定位）", Margin = new Thickness(0, 10, 0, 8) };
        private readonly TextBox order = new TextBox { Width = 220, Text = "1,2", Margin = new Thickness(8, 0, 8, 0) };
        private readonly TextBox currentRow = new TextBox { Width = 60, Text = "1", Margin = new Thickness(8, 0, 8, 0) };
        private readonly string settingsPath;
        private readonly System.Collections.Generic.List<TextBlock> markerStates = new System.Collections.Generic.List<TextBlock>();
        private bool closed;
        public CharacterRotationSettings Result { get; }
        public bool TestRequested { get; private set; }

        public CharacterRotationWindow(CharacterRotationSettings original, string settingsPath)
        {
            this.settingsPath = settingsPath;
            section = controls;
            var serializer = new XmlSerializer(typeof(CharacterRotationSettings));
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, original);
                stream.Position = 0;
                Result = (CharacterRotationSettings)serializer.Deserialize(stream);
            }
            Title = "自动换号设置";
            Width = 710; Height = 740;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Content = new ScrollViewer { Content = controls, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Closed += (s, e) => closed = true;
            AddText("每个角色提前站在钓鱼点，使用相同的钓鱼快捷键。按设定顺序循环，最后一个完成后回到第一个。", true);
            enabled.IsChecked = Result.Enabled;
            minutes.Text = Result.MinutesPerCharacter.ToString();
            controls.Children.Add(enabled);
            AddRow(new TextBlock { Text = "每个角色钓鱼", VerticalAlignment = VerticalAlignment.Center }, minutes,
                new TextBlock { Text = "分钟（默认 90；切回前台后继续计时）", VerticalAlignment = VerticalAlignment.Center });
            automatic.IsChecked = Result.AutomaticDetection || Result.ClientWidth == 0;
            controls.Children.Add(automatic);
            var autoPanel = new StackPanel();
            controls.Children.Add(autoPanel);
            section = autoPanel;
            if (Result.AutomaticDetection && Result.Characters.Count > 0)
            {
                order.Text = string.Join(",", Result.Characters.Select(c => c.ListPosition));
                if (Result.StartingCharacter >= 0 && Result.StartingCharacter < Result.Characters.Count)
                    currentRow.Text = Result.Characters[Result.StartingCharacter].ListPosition.ToString();
            }
            AddText("按角色列表从上往下填写序号，例如 1,2,3。自动读取游戏截图中的中文按钮和角色行，并检查金色选中框。启动前请登录当前角色；角色列表保持在顶部，目标角色需可见。", false);
            AddRow(new TextBlock { Text = "轮换顺序", VerticalAlignment = VerticalAlignment.Center }, order);
            AddRow(new TextBlock { Text = "当前已登录的角色序号", VerticalAlignment = VerticalAlignment.Center }, currentRow);
            AddRow(Button("识别当前画面（仅检查）", CheckAutomaticScreen));
            AddText("可选检查：先打开 ESC 菜单或角色选择界面，点击检查后在 5 秒内切回游戏；只读取截图，不会操作游戏。", false);
            AddText("无需录制按钮或角色坐标。启动及换号登录后，程序会打开并关闭 ESC 菜单确认游戏已就绪。识别失败会停止并显示原因。", false);
            controls.Children.Add(manualControls);
            void RefreshMode()
            {
                autoPanel.Visibility = automatic.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                manualControls.Visibility = automatic.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
            }
            automatic.Checked += (s, e) => RefreshMode();
            automatic.Unchecked += (s, e) => RefreshMode();
            RefreshMode();
            section = manualControls;
            AddText("首次定位：先在游戏中打开相应界面，再点定位按钮。设置窗口会最小化 5 秒，请切回游戏；截图后，在静止截图里点击指定文字中心。定位时保持游戏窗口尺寸、UI 缩放和角色列表滚动位置一致。", false);
            AddMarkerButton("1. 定位“返回角色选择”", () => Result.LogoutButton,
                marker => Result.LogoutButton = marker, "在游戏中按 ESC，点击截图里的“返回角色选择”文字中心。");
            AddMarkerButton("2. 定位角色界面标志", () => Result.CharacterScreen,
                marker => Result.CharacterScreen = marker, "先手动返回角色选择界面，点击截图里的“选择服务器”文字中心（只记录，不会点击游戏）。");
            AddMarkerButton("3. 定位游戏内固定标志", () => Result.WorldScreen,
                marker => Result.WorldScreen = marker, "先进入游戏、关闭菜单。选择每个角色都相同的固定 UI 图标区域，例如右下角背包图标。不要选水面、角色名、小地图、冷却或变化数字，也不要让提示框遮挡。");
            AddText("4. 添加角色：先在角色选择界面选中该角色，再截图并点击右侧列表中该角色名字的中心。每个角色分别录一次；金色选中背景也会用于核对。不要滚动列表，所有角色需同时可见。", false);
            AddRow(new TextBlock { Text = "角色备注", VerticalAlignment = VerticalAlignment.Center }, characterName,
                Button("截图并添加角色", async () =>
                {
                    string name = characterName.Text.Trim();
                    if (name.Length == 0) { feedback.Text = "请先填角色备注，例如：1 哈宝。"; return; }
                    var marker = await CaptureMarker("点击右侧列表中【已选中】角色的名字中心，包含名字文字和金色选中背景。");
                    if (marker == null) { return; }
                    Result.Characters.Add(new RotationCharacter { Name = name, SelectedRow = marker });
                    RefreshCharacters(Result.StartingCharacter);
                    characterName.Clear();
                }));
            manualControls.Children.Add(characters);
            AddRow(Button("上移", () => MoveCharacter(-1)), Button("下移", () => MoveCharacter(1)),
                Button("删除所选", () =>
                {
                    int index = characters.SelectedIndex;
                    if (index < 0) { return; }
                    Result.Characters.RemoveAt(index);
                    RefreshCharacters(0);
                }));
            AddRow(new TextBlock { Text = "启动时已登录的角色", VerticalAlignment = VerticalAlignment.Center }, startingCharacter);
            AddText("保存后手动登录上面选择的启动角色，再按主窗口的播放按钮。只在确认目标角色已选中、进入游戏后恢复抛竿。加载超时或换号中切走游戏会停止；重新启动前请核对当前角色。", false);
            section = controls;
            controls.Children.Add(feedback);
            manualControls.Children.Add(Button("清空定位重新设置", () =>
            {
                Result.LogoutButton = new ScreenMarker();
                Result.CharacterScreen = new ScreenMarker();
                Result.WorldScreen = new ScreenMarker();
                Result.Characters.Clear();
                Result.ClientWidth = Result.ClientHeight = 0;
                foreach (var state in markerStates) { state.Text = "未定位"; }
                RefreshCharacters(0);
                feedback.Text = "已清空本次设置中的定位，可按新的窗口尺寸重新录制；保存后生效。";
            }));
            AddText("快速验证：点击“保存并测试（30 秒）”，切回已登录的当前角色。自动模式打开并保留 ESC 菜单，30 秒后换到下一个角色，登录成功后自动开始钓鱼。后续按正常分钟间隔轮换；未启用轮换则在当前角色持续钓鱼。测试失败会停止。请保持游戏在前台，可按主窗口停止。", false);
            AddRow(Button("保存", () => Save()), Button("保存并测试（30 秒）", () => Save(true)), Button("取消", () => Close()));
            RefreshCharacters(Result.StartingCharacter);
        }

        private void AddText(string text, bool bold)
        {
            section.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Margin = new Thickness(0, 8, 0, 8) });
        }

        private static Button Button(string text, Action action)
        {
            var button = new Button { Content = text, Margin = new Thickness(0, 4, 8, 4), Padding = new Thickness(10, 5, 10, 5), MinHeight = 30 };
            button.Click += (s, e) => action();
            return button;
        }

        private void AddRow(params UIElement[] elements)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            foreach (var element in elements) { row.Children.Add(element); }
            section.Children.Add(row);
        }

        private async void CheckAutomaticScreen()
        {
            controls.IsEnabled = false;
            var previousState = WindowState;
            WindowState = WindowState.Minimized;
            try
            {
                await Task.Delay(5000);
                if (closed) { return; }
                using (var frame = WowScreen.GetBitmap(out var bounds, fullClient: true))
                {
                    var screen = await Task.Run(() => AutomaticScreen.Analyze(frame, new ScreenTextReader().Read(frame, () =>
                    {
                        if (closed) { throw new OperationCanceledException(); }
                    })));
                    feedback.Text = screen.IsMenu ? "已识别游戏菜单：“返回角色选择”位置 " + AutomaticScreen.Center(screen.Logout) :
                        screen.IsCharacterScreen ? (screen.Rows.Count == 0 ? "找到角色界面，但列表识别不完整；请将列表滚到顶部并保持角色可见。" :
                            "已识别角色选择界面：" + screen.Rows.Count + " 行；" + (screen.SelectedRow > 0 ? "选中第 " + screen.SelectedRow + " 行。" : "未确认选中框，请重试。")) :
                        "未识别到菜单或角色列表。此检查请先按 ESC 打开游戏菜单，或停在角色选择界面。";
                }
            }
            catch (Exception e) { feedback.Text = "识别失败：" + e.Message; }
            finally { if (!closed) { WindowState = previousState; controls.IsEnabled = true; Activate(); } }
        }

        private void AddMarkerButton(string label, Func<ScreenMarker> get, Action<ScreenMarker> set, string instruction)
        {
            var state = new TextBlock { Text = get().Width == 0 ? "未定位" : "已定位", VerticalAlignment = VerticalAlignment.Center };
            markerStates.Add(state);
            AddRow(Button(label, async () =>
            {
                var marker = await CaptureMarker(instruction);
                if (marker == null) { return; }
                set(marker);
                state.Text = "已定位";
            }), state);
        }

        private async Task<ScreenMarker?> CaptureMarker(string instruction)
        {
            controls.IsEnabled = false;
            feedback.Text = "5 秒后截图，请切回游戏。";
            var previousState = WindowState;
            WindowState = WindowState.Minimized;
            try
            {
                await Task.Delay(5000);
                if (closed) { return null; }
                using (var frame = WowScreen.GetBitmap(out var bounds, fullClient: true))
                {
                    if (Result.ClientWidth != 0 && (Result.ClientWidth != bounds.Width || Result.ClientHeight != bounds.Height))
                        throw new InvalidOperationException("当前游戏窗口尺寸和之前定位时不同，请先恢复原尺寸。");
                    WindowState = previousState;
                    Activate();
                    var picker = new MarkerPicker(frame, instruction) { Owner = this };
                    if (picker.ShowDialog() != true) { return null; }
                    Result.ClientWidth = bounds.Width;
                    Result.ClientHeight = bounds.Height;
                    feedback.Text = "定位成功。";
                    return picker.Marker;
                }
            }
            catch (Exception e) { feedback.Text = "定位失败：" + e.Message; return null; }
            finally
            {
                if (!closed) { WindowState = previousState; controls.IsEnabled = true; Activate(); }
            }
        }

        private void RefreshCharacters(int selected)
        {
            characters.ItemsSource = null;
            startingCharacter.ItemsSource = null;
            characters.ItemsSource = Result.Characters;
            startingCharacter.ItemsSource = Result.Characters;
            startingCharacter.SelectedIndex = Math.Min(selected, Result.Characters.Count - 1);
        }

        private void MoveCharacter(int direction)
        {
            int index = characters.SelectedIndex, target = index + direction;
            if (index < 0 || target < 0 || target >= Result.Characters.Count) { return; }
            var current = startingCharacter.SelectedItem;
            var item = Result.Characters[index];
            Result.Characters.RemoveAt(index);
            Result.Characters.Insert(target, item);
            RefreshCharacters(Result.Characters.IndexOf((RotationCharacter)current));
            characters.SelectedIndex = target;
        }

        private void Save(bool test = false)
        {
            try
            {
                if (!int.TryParse(minutes.Text, out int value)) { throw new InvalidOperationException("请输入整数分钟数。"); }
                Result.MinutesPerCharacter = value;
                Result.Enabled = enabled.IsChecked == true;
                Result.AutomaticDetection = automatic.IsChecked == true;
                if (Result.AutomaticDetection)
                {
                    var parts = Regex.Split(order.Text.Trim(), @"[\s,，→]+");
                    var positions = parts.Select(p => int.TryParse(p, out int number) ? number : 0).ToList();
                    if (!int.TryParse(currentRow.Text, out int current) || !positions.Contains(current))
                        throw new InvalidOperationException("当前角色序号必须包含在轮换顺序中。");
                    Result.Characters = positions.Select(p => new RotationCharacter { ListPosition = p, Name = "角色 " + p }).ToList();
                    Result.StartingCharacter = positions.IndexOf(current);
                }
                else { Result.StartingCharacter = startingCharacter.SelectedIndex; }
                if (Result.Enabled || test) { Result.Validate(); }
                Result.Save(settingsPath);
                TestRequested = test;
                DialogResult = true;
            }
            catch (Exception e) { feedback.Text = e.Message; }
        }

        private sealed class MarkerPicker : Window
        {
            public ScreenMarker Marker { get; private set; } = new ScreenMarker();

            public MarkerPicker(Bitmap frame, string instruction)
            {
                Title = "点击截图定位";
                Width = Math.Min(1400, SystemParameters.WorkArea.Width - 40);
                Height = Math.Min(900, SystemParameters.WorkArea.Height - 40);
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                var layout = new DockPanel { Margin = new Thickness(10) };
                var tip = new TextBlock { Text = instruction + " 选点后关闭此窗口返回设置。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
                DockPanel.SetDock(tip, Dock.Top);
                layout.Children.Add(tip);
                var screenshot = new Image { Source = frame.ToBitmapImage(), Stretch = Stretch.Uniform, Cursor = Cursors.Cross };
                screenshot.MouseLeftButtonDown += (s, e) =>
                {
                    var point = e.GetPosition(screenshot);
                    double scale = Math.Min(screenshot.ActualWidth / frame.Width, screenshot.ActualHeight / frame.Height);
                    int x = (int)((point.X - (screenshot.ActualWidth - frame.Width * scale) / 2) / scale);
                    int y = (int)((point.Y - (screenshot.ActualHeight - frame.Height * scale) / 2) / scale);
                    try { Marker = ScreenMarker.Capture(frame, new DrawingPoint(x, y)); DialogResult = true; }
                    catch (Exception error) { tip.Text = error.Message + " " + instruction; }
                };
                layout.Children.Add(screenshot);
                Content = layout;
            }
        }
    }
}
