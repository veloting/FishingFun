using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;

namespace FishingFun
{
    public sealed class AutomaticScreen
    {
        public Rectangle Logout { get; set; }
        public Rectangle ReturnToGame { get; set; }
        public bool HasMenuTitle { get; set; }
        public string MenuProblem { get; private set; } = "";
        public bool IsMenu => !Logout.IsEmpty && !ReturnToGame.IsEmpty && HasMenuTitle;
        public bool IsCharacterScreen { get; set; }
        public List<Point> Rows { get; set; } = new List<Point>();
        public int SelectedRow { get; set; }

        public static AutomaticScreen Analyze(Bitmap frame, List<ScreenText> text)
        {
            Rectangle Find(string value) => text.Where(t => t.Text == value).Select(t => t.Bounds).SingleOrDefaultSafe();
            var screen = new AutomaticScreen { Logout = Find("返回角色选择"), ReturnToGame = Find("返回游戏"),
                HasMenuTitle = text.Any(t => t.Text == "游戏菜单") };
            if (!screen.HasMenuTitle) { screen.MenuProblem += "未识别到游戏菜单标题；"; }
            if (screen.Logout.IsEmpty) { screen.MenuProblem += "未唯一识别到返回角色选择；"; }
            if (screen.ReturnToGame.IsEmpty) { screen.MenuProblem += "未唯一识别到返回游戏；"; }
            // Require the three menu labels to share a column and be in the expected order.
            if (screen.IsMenu && (Math.Abs(Center(screen.Logout).X - Center(screen.ReturnToGame).X) > screen.Logout.Width / 2 ||
                screen.ReturnToGame.Top <= screen.Logout.Bottom || !IsRedButton(frame, screen.Logout) ||
                !IsRedButton(frame, screen.ReturnToGame)))
            {
                screen.MenuProblem = "菜单按钮的排列或红色背景校验未通过；";
                screen.Logout = screen.ReturnToGame = Rectangle.Empty;
            }
            var server = text.Where(t => t.Text.StartsWith("选择服务") && t.Bounds.Left > frame.Width * 0.6).ToList();
            var create = text.Where(t => t.Text == "创建新角色" && t.Bounds.Left > frame.Width * 0.6).ToList();
            if (server.Count != 1 || create.Count != 1 || !IsRedButton(frame, server[0].Bounds) ||
                !IsRedButton(frame, create[0].Bounds)) { return screen; }
            screen.IsCharacterScreen = true;
            var levels = text.Where(t => Regex.IsMatch(t.Text, "^等[级級][0-9]+") &&
                t.Bounds.Left > frame.Width * 0.6 && t.Bounds.Top > server[0].Bounds.Bottom &&
                t.Bounds.Bottom < create[0].Bounds.Top).OrderBy(t => t.Bounds.Top).ToList();
            if (levels.Count < 2) { return screen; }
            var gaps = levels.Zip(levels.Skip(1), (a, b) => (double)Center(b.Bounds).Y - Center(a.Bounds).Y).OrderBy(v => v).ToArray();
            double pitch = gaps[(gaps.Length - 1) / 2];
            if (pitch < levels.Average(l => l.Bounds.Height) * 1.8) { return screen; }
            double first = Center(levels[0].Bounds).Y, last = Center(levels.Last().Bounds).Y;
            // Missing leading/trailing rows are ambiguous; never silently renumber the list.
            if (first - server[0].Bounds.Bottom < pitch * 0.5 || first - server[0].Bounds.Bottom > pitch * 1.8 ||
                create[0].Bounds.Top - last < pitch * 0.5 || create[0].Bounds.Top - last > pitch * 1.8 ||
                gaps.Any(g => Math.Abs(g / pitch - Math.Round(g / pitch)) > 0.18)) { return screen; }
            int count = (int)Math.Round((last - first) / pitch) + 1;
            if (count < 2 || count > 20) { return screen; }
            for (int i = 0; i < count; i++)
            {
                var nearest = levels.OrderBy(l => Math.Abs(Center(l.Bounds).Y - (first + i * pitch))).First();
                int x = Center(nearest.Bounds).X, y = (int)Math.Round(first + i * pitch);
                screen.Rows.Add(new Point(x, y));
                // A broad gold fill to the right of the text distinguishes selection from gold names.
                int left = (int)(levels.Max(l => l.Bounds.Right) + pitch * 0.15);
                int right = Math.Min(frame.Width - 5, left + (int)(pitch * 1.2));
                int gold = 0, samples = 0;
                for (int sy = Math.Max(0, y - (int)(pitch * 0.18)); sy < Math.Min(frame.Height, y + pitch * 0.18); sy += 3)
                    for (int sx = left; sx < right; sx += 3)
                    {
                        var c = frame.GetPixel(sx, sy);
                        if (c.R > 100 && c.G > 85 && c.G > c.R * 0.65 && c.B < c.G * 0.72) { gold++; }
                        samples++;
                    }
                if (samples > 0 && gold > samples * 0.6)
                {
                    if (screen.SelectedRow != 0) { screen.SelectedRow = -1; }
                    else { screen.SelectedRow = i + 1; }
                }
            }
            return screen;
        }

        public static Point Center(Rectangle area) => new Point(area.X + area.Width / 2, area.Y + area.Height / 2);

        private static bool IsRedButton(Bitmap frame, Rectangle text)
        {
            text.Inflate(5, 3);
            text.Intersect(new Rectangle(Point.Empty, frame.Size));
            int red = 0, count = 0;
            for (int y = text.Top; y < text.Bottom; y += 2)
                for (int x = text.Left; x < text.Right; x += 2)
                {
                    var c = frame.GetPixel(x, y);
                    if (c.R > 45 && c.R > c.G * 1.4 && c.R > c.B * 1.4) { red++; }
                    count++;
                }
            return count > 0 && red > count * 0.15;
        }
    }

    internal static class UniqueRectangle
    {
        public static Rectangle SingleOrDefaultSafe(this IEnumerable<Rectangle> source)
        {
            var found = source.Take(2).ToArray();
            return found.Length == 1 ? found[0] : Rectangle.Empty;
        }
    }

    public interface IAutomaticRotationDesktop
    {
        TimeSpan Elapsed { get; }
        AutomaticScreen ReadScreen();
        void Click(Point point);
        void PressKey(ConsoleKey key);
        void Wait(int milliseconds);
    }

    public sealed class AutomaticRotationRunner
    {
        private readonly IAutomaticRotationDesktop desktop;
        private readonly Action<string> status;
        public AutomaticRotationRunner(IAutomaticRotationDesktop desktop, Action<string> status)
        { this.desktop = desktop; this.status = status; }

        public void ConfirmWorld(bool afterLogin = false, bool keepMenuOpen = false)
        {
            status(afterLogin ? "等待进入游戏，自动识别游戏菜单…" :
                keepMenuOpen ? "正在打开游戏菜单，定位“返回角色选择”…" : "正在自动检查游戏界面…");
            var deadline = desktop.Elapsed + TimeSpan.FromSeconds(afterLogin ? 180 : 25);
            var nextEscape = desktop.Elapsed + TimeSpan.FromSeconds(afterLogin ? 10 : 0);
            string lastMenuProblem = "";
            while (desktop.Elapsed < deadline)
            {
                var screen = desktop.ReadScreen();
                lastMenuProblem = screen.MenuProblem;
                if (screen.IsCharacterScreen && !afterLogin)
                    throw new InvalidOperationException("请先登录设置中指定的当前角色，再启动钓鱼。");
                if (screen.IsMenu)
                {
                    if (keepMenuOpen)
                    {
                        status("已定位“返回角色选择”，保持菜单打开，倒计时结束后点击。");
                        return;
                    }
                    status("已确认游戏界面，按 ESC 关闭检查菜单。");
                    desktop.PressKey(ConsoleKey.Escape);
                    WaitFor(s => !s.HasMenuTitle && !s.IsCharacterScreen, 10, "游戏菜单未关闭");
                    desktop.Wait(1000);
                    return;
                }
                if (!screen.IsCharacterScreen && !screen.HasMenuTitle && desktop.Elapsed >= nextEscape)
                {
                    desktop.PressKey(ConsoleKey.Escape);
                    nextEscape = desktop.Elapsed + TimeSpan.FromSeconds(5);
                }
                desktop.Wait(500);
            }
            throw new InvalidOperationException("菜单识别未通过，" + (afterLogin ? "尚未确认目标角色进入游戏。" : "尚未点击“返回角色选择”。") +
                lastMenuProblem + "请查看 rotation-diagnostics 中的识别截图和文字。");
        }

        public void SwitchTo(int row)
        {
            status("正在自动返回角色选择…");
            var screen = desktop.ReadScreen();
            if (!screen.IsMenu) { desktop.PressKey(ConsoleKey.Escape); }
            screen = WaitFor(s => s.IsMenu, 12, "未识别到“返回角色选择”");
            var logoutPoint = AutomaticScreen.Center(screen.Logout);
            status("正在点击“返回角色选择”：窗口内坐标 " + logoutPoint + "。");
            desktop.Click(logoutPoint);
            status("已发送“返回角色选择”点击，等待退出倒计时及角色列表…");
            screen = WaitFor(s => s.IsCharacterScreen && s.Rows.Count >= row, 120, "未能完整识别角色列表，请保持列表在顶部且目标角色可见");
            status("正在选择列表第 " + row + " 个角色…");
            desktop.Click(screen.Rows[row - 1]);
            WaitFor(s => s.IsCharacterScreen && s.Rows.Count >= row && s.SelectedRow == row, 15, "未确认目标角色的金色选中状态");
            desktop.PressKey(ConsoleKey.Enter);
            ConfirmWorld(true);
            status("已进入列表第 " + row + " 个角色，继续钓鱼。");
        }

        private AutomaticScreen WaitFor(Func<AutomaticScreen, bool> condition, int seconds, string error)
        {
            var deadline = desktop.Elapsed + TimeSpan.FromSeconds(seconds);
            int stable = 0;
            while (desktop.Elapsed < deadline)
            {
                var screen = desktop.ReadScreen();
                stable = condition(screen) ? stable + 1 : 0;
                if (stable >= 2) { return screen; }
                desktop.Wait(300);
            }
            throw new InvalidOperationException(error + "；已停止自动操作。");
        }
    }
}
