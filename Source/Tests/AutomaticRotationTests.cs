using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using FishingFun;

internal static class AutomaticRotationTests
{
    private static void Check(bool value, string message)
    { if (!value) { throw new Exception(message); } Console.WriteLine("PASS: " + message); }
    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); } catch (T) { Console.WriteLine("PASS: " + message); return; }
        throw new Exception(message);
    }
    private static int Main()
    {
        try
        {
            string source = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../.."));
            var reader = new ScreenTextReader();
            using (var menu = new Bitmap(Path.Combine(source, "img_v3_0215v_05a8de11-993c-48cd-a257-c10bcf8c29eg.jpg")))
            {
                var text = reader.Read(menu, () => { });
                var screen = AutomaticScreen.Analyze(menu, text);
                Check(screen.IsMenu, "Windows OCR recognizes the supplied ESC menu photo");
                var point = AutomaticScreen.Center(screen.Logout);
                Check(point.X > 250 && point.X < 400 && point.Y > 550 && point.Y < 585, "Logout point is inside the correct button, not Exit Game");
                using (var moved = new Bitmap(1200, 900))
                {
                    using (var g = Graphics.FromImage(moved))
                    {
                        g.Clear(Color.DarkGreen);
                        g.DrawImage(menu, new Rectangle(300, 20, (int)(menu.Width * 0.8), (int)(menu.Height * 0.8)));
                    }
                    var relocated = AutomaticScreen.Analyze(moved, reader.Read(moved, () => { }));
                    var target = AutomaticScreen.Center(relocated.Logout);
                    Check(relocated.IsMenu && Math.Abs(target.X - (300 + point.X * 0.8)) < 8 &&
                        Math.Abs(target.Y - (20 + point.Y * 0.8)) < 8, "Finds scaled and relocated menu without stored coordinates");
                }
                text.Add(new ScreenText { Text = "返回角色选择", Bounds = new Rectangle(10, 10, 50, 20) });
                Check(!AutomaticScreen.Analyze(menu, text).IsMenu, "Ambiguous duplicate logout labels are rejected");
            }
            using (var selection = new Bitmap(Path.Combine(source, "img_v3_0215v_c34f4def-75f6-4c1d-b438-44cff269b8bg.jpg")))
            {
                var text = reader.Read(selection, () => { });
                var screen = AutomaticScreen.Analyze(selection, text);
                Console.WriteLine("Detected rows=" + screen.Rows.Count + ", selected=" + screen.SelectedRow);
                Check(screen.IsCharacterScreen && screen.Rows.Count == 9, "Finds all nine rows in supplied character photo, including OCR-obscured selected row");
                Check(screen.SelectedRow == 2, "Detects gold selection on second character");
                Check(screen.Rows[1].X > 1950 && screen.Rows[1].Y > 250 && screen.Rows[1].Y < 330, "OCR text angle is mapped back to the actual image coordinates");
                text.RemoveAll(t => t.Text.StartsWith("等级") && t.Bounds.Top < 220);
                Check(AutomaticScreen.Analyze(selection, text).Rows.Count == 0, "Missing first row is rejected instead of renumbering characters");
            }
            using (var blank = new Bitmap(800, 600))
                Check(!AutomaticScreen.Analyze(blank, new List<ScreenText>()).IsCharacterScreen, "Unknown/loading screen is not a character screen");
            var settings = new CharacterRotationSettings { Enabled = true, AutomaticDetection = true,
                Characters = new List<RotationCharacter> { new RotationCharacter { ListPosition = 1 }, new RotationCharacter { ListPosition = 2 } } };
            settings.Validate();
            Check(new RotationSchedule(settings).NextIndex == 1, "Automatic mode requires no manual markers");
            settings.Characters[1].ListPosition = 1;
            Throws<InvalidOperationException>(settings.Validate, "Duplicate automatic row numbers are rejected");
            var desktop = new FakeDesktop();
            new AutomaticRotationRunner(desktop, _ => { }).SwitchTo(2);
            Check(desktop.Actions.SequenceEqual(new[] { "Escape", "logout", "row2", "Enter", "Escape", "resume" }), "Automatic flow verifies menu, target selection, and loaded world in order");
            Check(desktop.Elapsed.TotalSeconds >= 30, "Waits for delayed login before resuming fishing");
            desktop = new FakeDesktop { WrongSelection = true };
            Throws<InvalidOperationException>(() => new AutomaticRotationRunner(desktop, _ => { }).SwitchTo(2), "Wrong selection aborts automatic flow");
            Check(!desktop.Actions.Contains("Enter"), "No login is sent when the gold selection disagrees");
            desktop = new FakeDesktop { CancelAt = TimeSpan.FromSeconds(2) };
            Throws<OperationCanceledException>(() => new AutomaticRotationRunner(desktop, _ => { }).SwitchTo(2), "Stop/focus loss cancels automatic rotation");
            desktop = new FakeDesktop { State = "selection" };
            Throws<InvalidOperationException>(() => new AutomaticRotationRunner(desktop, _ => { }).ConfirmWorld(), "Startup on character selection stops before fishing");
            Check(desktop.Actions.Count == 0, "Startup failure sends no keys");
            Console.WriteLine("All automatic rotation checks passed. No game input was sent.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private sealed class FakeDesktop : IAutomaticRotationDesktop
    {
        public TimeSpan Elapsed { get; private set; }
        public TimeSpan CancelAt = TimeSpan.MaxValue;
        public string State = "world";
        public bool WrongSelection;
        public List<string> Actions = new List<string>();
        private TimeSpan transition;
        private int selected = 1;
        public AutomaticScreen ReadScreen()
        {
            Wait(100);
            if (State == "menu") { return new AutomaticScreen { HasMenuTitle = true, Logout = new Rectangle(100, 100, 50, 20), ReturnToGame = new Rectangle(100, 200, 50, 20) }; }
            if (State == "selection") { return new AutomaticScreen { IsCharacterScreen = true, Rows = new List<Point> { new Point(500, 100), new Point(500, 200) }, SelectedRow = WrongSelection ? 1 : selected }; }
            return new AutomaticScreen();
        }
        public void Click(Point point)
        {
            Wait(0);
            if (point == new Point(125, 110) && State == "menu")
            { Actions.Add("logout"); State = "logout"; transition = Elapsed + TimeSpan.FromSeconds(3); }
            else if (point == new Point(125, 210) && State == "menu") { Actions.Add("resume"); State = "world"; }
            else if (point == new Point(500, 200) && State == "selection") { Actions.Add("row2"); selected = 2; }
            else { throw new Exception("Unexpected click"); }
        }
        public void PressKey(ConsoleKey key)
        {
            Wait(0);
            Actions.Add(key.ToString());
            if (key == ConsoleKey.Escape && State == "world") { State = "menu"; }
            else if (key == ConsoleKey.Enter && State == "selection") { State = "loading"; transition = Elapsed + TimeSpan.FromSeconds(30); }
            else if (key == ConsoleKey.Escape && State == "loading") { Actions.RemoveAt(Actions.Count - 1); }
            else { throw new Exception("Unexpected key"); }
        }
        public void Wait(int milliseconds)
        {
            Elapsed += TimeSpan.FromMilliseconds(milliseconds);
            if (Elapsed >= CancelAt) { throw new OperationCanceledException(); }
            if (Elapsed >= transition && State == "logout") { State = "selection"; }
            if (Elapsed >= transition && State == "loading") { State = "world"; }
        }
    }
}
