using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace FishingFun
{
    public sealed class ScreenMarker
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public byte[] Pixels { get; set; } = Array.Empty<byte>();

        public static ScreenMarker Capture(Bitmap frame, Point center)
        {
            const int width = 96, height = 24;
            var area = new Rectangle(center.X - width / 2, center.Y - height / 2, width, height);
            if (!new Rectangle(Point.Empty, frame.Size).Contains(area))
                throw new InvalidOperationException("请选择离窗口边缘至少 48 像素的位置。");
            var marker = new ScreenMarker { X = area.X, Y = area.Y, Width = width, Height = height };
            marker.Pixels = ReadPixels(frame, area);
            if (CountEdges(marker.Pixels, width, height) < 60)
                throw new InvalidOperationException("该区域缺少可识别细节，请点击文字或固定图标的中心，不要选空白背景。");
            return marker;
        }

        public bool IsValid(Size size) => Width > 0 && Height > 0 && Width <= size.Width && Height <= size.Height &&
            X >= 0 && Y >= 0 && X <= size.Width - Width && Y <= size.Height - Height &&
            Pixels != null && Pixels.Length == (long)Width * Height * 3;

        public Point Center => new Point(X + Width / 2, Y + Height / 2);

        public bool Matches(Bitmap frame)
        {
            if (!IsValid(frame.Size)) { return false; }
            var actual = ReadPixels(frame, new Rectangle(X, Y, Width, Height));
            // Require both the overall appearance and most individual pixels to agree.
            long error = 0;
            int differing = 0;
            for (int i = 0; i < actual.Length; i += 3)
            {
                int delta = Math.Abs(actual[i] - Pixels[i]) + Math.Abs(actual[i + 1] - Pixels[i + 1]) +
                    Math.Abs(actual[i + 2] - Pixels[i + 2]);
                error += delta;
                if (delta > 90) { differing++; }
            }
            if (error / (double)actual.Length >= 15 || differing >= Width * Height * 0.12) { return false; }
            int edges = 0, matchingEdges = 0;
            for (int y = 0; y < Height - 1; y++)
                for (int x = 0; x < Width - 1; x++)
                {
                    int i = (y * Width + x) * 3;
                    bool expectedEdge = IsEdge(Pixels, i, Width);
                    bool actualEdge = IsEdge(actual, i, Width);
                    if (expectedEdge || actualEdge) { edges++; }
                    if (expectedEdge && actualEdge) { matchingEdges++; }
                }
            // Text edges distinguish nearby names even when most of the panel background is identical.
            return edges >= 60 && matchingEdges >= edges * 0.88;
        }

        private static bool IsEdge(byte[] pixels, int i, int width)
        {
            int right = i + 3, below = i + width * 3;
            int horizontal = 0, vertical = 0;
            for (int c = 0; c < 3; c++)
            {
                horizontal += Math.Abs(pixels[i + c] - pixels[right + c]);
                vertical += Math.Abs(pixels[i + c] - pixels[below + c]);
            }
            return Math.Max(horizontal, vertical) > 120;
        }

        private static int CountEdges(byte[] pixels, int width, int height)
        {
            int count = 0;
            for (int y = 0; y < height - 1; y++)
                for (int x = 0; x < width - 1; x++)
                    if (IsEdge(pixels, (y * width + x) * 3, width)) { count++; }
            return count;
        }

        private static byte[] ReadPixels(Bitmap frame, Rectangle area)
        {
            var bytes = new byte[area.Width * area.Height * 3];
            int i = 0;
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    var c = frame.GetPixel(x, y);
                    bytes[i++] = c.R; bytes[i++] = c.G; bytes[i++] = c.B;
                }
            return bytes;
        }
    }

    public sealed class RotationCharacter
    {
        public int ListPosition { get; set; }
        public string Name { get; set; } = "角色";
        public ScreenMarker SelectedRow { get; set; } = new ScreenMarker();
        public override string ToString() => Name;
    }

    public sealed class CharacterRotationSettings
    {
        public bool Enabled { get; set; }
        public bool AutomaticDetection { get; set; }
        public int MinutesPerCharacter { get; set; } = 90;
        public int StartingCharacter { get; set; }
        public int ClientWidth { get; set; }
        public int ClientHeight { get; set; }
        public ScreenMarker LogoutButton { get; set; } = new ScreenMarker();
        public ScreenMarker CharacterScreen { get; set; } = new ScreenMarker();
        public ScreenMarker WorldScreen { get; set; } = new ScreenMarker();
        public List<RotationCharacter> Characters { get; set; } = new List<RotationCharacter>();

        public void Validate()
        {
            if (MinutesPerCharacter < 1 || MinutesPerCharacter > 1440)
                throw new InvalidOperationException("每个角色的时长需要在 1～1440 分钟之间。");
            if (Characters == null || Characters.Count < 2 || StartingCharacter < 0 || StartingCharacter >= Characters.Count)
                throw new InvalidOperationException("请至少添加两个角色，并选择启动时已登录的角色。");
            if (AutomaticDetection)
            {
                if (Characters.Any(c => c == null || c.ListPosition < 1 || c.ListPosition > 20) ||
                    Characters.Select(c => c.ListPosition).Distinct().Count() != Characters.Count)
                    throw new InvalidOperationException("请输入不重复的角色序号（1～20），例如 1,2,3。");
                return;
            }
            var size = new Size(ClientWidth, ClientHeight);
            if (ClientWidth < 100 || ClientHeight < 100 || LogoutButton == null || CharacterScreen == null || WorldScreen == null ||
                !LogoutButton.IsValid(size) || !CharacterScreen.IsValid(size) || !WorldScreen.IsValid(size) ||
                Characters.Any(c => c == null || string.IsNullOrWhiteSpace(c.Name) || c.SelectedRow == null || !c.SelectedRow.IsValid(size)))
                throw new InvalidOperationException("请完成菜单、角色界面、游戏界面和每个角色的定位。");
            if (Characters.Select(c => c.SelectedRow.Center).Distinct().Count() != Characters.Count)
                throw new InvalidOperationException("两个角色不能使用同一个列表位置，请分别选中角色后定位。");
        }

        public static CharacterRotationSettings Load(string path)
        {
            if (!File.Exists(path)) { return new CharacterRotationSettings(); }
            using (var stream = File.OpenRead(path))
                return (CharacterRotationSettings)new XmlSerializer(typeof(CharacterRotationSettings)).Deserialize(stream);
        }

        public void Save(string path)
        {
            string temporary = path + ".tmp";
            using (var stream = File.Create(temporary))
                new XmlSerializer(typeof(CharacterRotationSettings)).Serialize(stream, this);
            if (File.Exists(path)) { File.Replace(temporary, path, null); }
            else { File.Move(temporary, path); }
        }
    }

    public sealed class RotationSchedule
    {
        public int CurrentIndex { get; private set; }
        public int NextIndex => (CurrentIndex + 1) % count;
        private readonly int count;
        private readonly TimeSpan duration;

        public RotationSchedule(CharacterRotationSettings settings)
        {
            settings.Validate();
            CurrentIndex = settings.StartingCharacter;
            count = settings.Characters.Count;
            duration = TimeSpan.FromMinutes(settings.MinutesPerCharacter);
        }

        public bool IsDue(TimeSpan activeFishingTime) => activeFishingTime >= duration;
        public void CompleteSwitch() { CurrentIndex = NextIndex; }
    }

    public interface IRotationDesktop
    {
        TimeSpan Elapsed { get; }
        void CheckReady();
        bool Matches(ScreenMarker marker);
        void Click(ScreenMarker marker);
        void PressKey(ConsoleKey key);
        void Wait(int milliseconds);
    }

    public sealed class RotationTestRunner
    {
        private readonly CharacterRotationSettings settings;
        private readonly IRotationDesktop desktop;
        private readonly Action<string> status;

        public RotationTestRunner(CharacterRotationSettings settings, IRotationDesktop desktop, Action<string> status)
        {
            settings.Validate();
            this.settings = settings;
            this.desktop = desktop;
            this.status = status;
        }

        // One real switch, independent of fishing/bite detection and the saved normal interval.
        public int Run()
        {
            desktop.CheckReady();
            AutomaticRotationRunner? automatic = null;
            if (settings.AutomaticDetection)
            {
                automatic = new AutomaticRotationRunner((IAutomaticRotationDesktop)desktop, status);
                automatic.ConfirmWorld(keepMenuOpen: true);
            }
            else if (!desktop.Matches(settings.WorldScreen) || desktop.Matches(settings.LogoutButton) ||
                desktop.Matches(settings.CharacterScreen))
                throw new InvalidOperationException("请先登录设置中的当前角色并关闭游戏菜单，再进行测试。");

            int target = (settings.StartingCharacter + 1) % settings.Characters.Count;
            var deadline = desktop.Elapsed + TimeSpan.FromSeconds(30);
            while (desktop.Elapsed < deadline)
            {
                desktop.CheckReady();
                var remaining = deadline - desktop.Elapsed;
                status("换号测试：" + (int)Math.Ceiling(remaining.TotalSeconds) + " 秒后切换到 " + settings.Characters[target].Name + "；请保持游戏在前台。");
                desktop.Wait(Math.Max(1, (int)Math.Min(1000, remaining.TotalMilliseconds)));
            }
            desktop.CheckReady();
            status("30 秒倒计时结束，开始测试换号…");
            if (automatic != null) { automatic.SwitchTo(settings.Characters[target].ListPosition); }
            else { new CharacterRotationRunner(settings, desktop, status).SwitchTo(target); }
            return target;
        }
    }

    public sealed class CharacterRotationRunner
    {
        private readonly CharacterRotationSettings settings;
        private readonly IRotationDesktop desktop;
        private readonly Action<string> status;

        public CharacterRotationRunner(CharacterRotationSettings settings, IRotationDesktop desktop, Action<string> status)
        {
            settings.Validate();
            this.settings = settings;
            this.desktop = desktop;
            this.status = status;
        }

        public void SwitchTo(int index)
        {
            var target = settings.Characters[index];
            status("正在换号：" + target.Name + "，打开游戏菜单…");
            desktop.CheckReady();
            if (!desktop.Matches(settings.LogoutButton))
            {
                WaitFor(() => desktop.Matches(settings.WorldScreen), 10, "未识别到游戏界面");
                desktop.PressKey(ConsoleKey.Escape);
            }
            WaitFor(() => desktop.Matches(settings.LogoutButton), 10, "未找到“返回角色选择”按钮");
            desktop.Click(settings.LogoutButton);
            status("正在等待退出倒计时及角色选择界面…");
            WaitFor(() => desktop.Matches(settings.CharacterScreen), 120, "等待角色选择界面超时");
            desktop.Click(target.SelectedRow);
            WaitFor(() => desktop.Matches(settings.CharacterScreen) && desktop.Matches(target.SelectedRow),
                10, "未确认选中目标角色，请检查列表顺序和角色定位");
            status("已选中 " + target.Name + "，正在进入游戏…");
            desktop.PressKey(ConsoleKey.Enter);
            WaitFor(() => !desktop.Matches(settings.CharacterScreen) && desktop.Matches(settings.WorldScreen),
                180, "等待进入游戏超时");
            // Let the world settle, then confirm again before allowing any fishing input.
            desktop.Wait(2000);
            WaitFor(() => !desktop.Matches(settings.CharacterScreen) && desktop.Matches(settings.WorldScreen),
                10, "游戏界面尚未就绪");
            status("已进入 " + target.Name + "，继续钓鱼。");
        }

        private void WaitFor(Func<bool> condition, int seconds, string error)
        {
            var deadline = desktop.Elapsed + TimeSpan.FromSeconds(seconds);
            int stable = 0;
            while (desktop.Elapsed < deadline)
            {
                desktop.CheckReady();
                stable = condition() ? stable + 1 : 0;
                if (stable >= 3) { return; }
                desktop.Wait(250);
            }
            throw new InvalidOperationException(error + "；已停止自动操作。");
        }
    }
}
