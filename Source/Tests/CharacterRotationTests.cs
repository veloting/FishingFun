using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using FishingFun;

internal static class CharacterRotationTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) { throw new Exception(message); }
        Console.WriteLine("PASS: " + message);
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { Console.WriteLine("PASS: " + message); return; }
        throw new Exception(message);
    }

    private static Bitmap MakeFrame()
    {
        var frame = new Bitmap(640, 480);
        using (var g = Graphics.FromImage(frame))
        {
            g.Clear(Color.FromArgb(40, 40, 40));
            using (var font = new Font("Arial", 13))
                for (int y = 20; y < 460; y += 40)
                    g.DrawString("Character " + y, font, Brushes.Gold, 200, y);
        }
        return frame;
    }

    private static CharacterRotationSettings Settings()
    {
        using (var frame = MakeFrame())
            return new CharacterRotationSettings
            {
                Enabled = true, ClientWidth = 640, ClientHeight = 480,
                LogoutButton = ScreenMarker.Capture(frame, new Point(250, 32)),
                CharacterScreen = ScreenMarker.Capture(frame, new Point(250, 72)),
                WorldScreen = ScreenMarker.Capture(frame, new Point(250, 112)),
                Characters = new List<RotationCharacter>
                {
                    new RotationCharacter { Name = "First", SelectedRow = ScreenMarker.Capture(frame, new Point(250, 152)) },
                    new RotationCharacter { Name = "Second", SelectedRow = ScreenMarker.Capture(frame, new Point(250, 192)) },
                    new RotationCharacter { Name = "Third", SelectedRow = ScreenMarker.Capture(frame, new Point(250, 232)) }
                }
            };
    }

    private static int Main()
    {
        try
        {
            var settings = Settings();
            settings.StartingCharacter = 1;
            var schedule = new RotationSchedule(settings);
            Check(schedule.CurrentIndex == 1 && schedule.NextIndex == 2, "Starts from the configured logged-in character");
            Check(!schedule.IsDue(TimeSpan.FromMinutes(89.999)) && schedule.IsDue(TimeSpan.FromMinutes(90)), "90 minute boundary is exact");
            schedule.CompleteSwitch();
            Check(schedule.CurrentIndex == 2 && schedule.NextIndex == 0, "Last character wraps to the first");
            schedule.CompleteSwitch();
            Check(schedule.CurrentIndex == 0 && !schedule.IsDue(TimeSpan.Zero), "A fresh active-time counter starts a full interval");

            var desktop = new FakeDesktop(settings);
            new CharacterRotationRunner(settings, desktop, _ => { }).SwitchTo(1);
            Check(string.Join(",", desktop.Actions) == "key:Escape,logout,select:1,key:Enter", "Logout, target selection, and login inputs occur in order");
            Check(desktop.Elapsed.TotalSeconds >= 60 && desktop.State == "world", "Waits for logout countdown and slow loading before success");
            Check(desktop.TargetChecks >= 3, "Requires stable target selection before entering world");

            desktop = new FakeDesktop(settings) { WrongSelection = true };
            Throws<InvalidOperationException>(() => new CharacterRotationRunner(settings, desktop, _ => { }).SwitchTo(1), "Wrong selected character stops the workflow");
            Check(!desktop.Actions.Contains("key:Enter"), "Never logs in when target selection cannot be verified");

            desktop = new FakeDesktop(settings) { LogoutSeconds = 200 };
            Throws<InvalidOperationException>(() => new CharacterRotationRunner(settings, desktop, _ => { }).SwitchTo(1), "Logout timeout stops the workflow");
            Check(!desktop.Actions.Any(a => a.StartsWith("select:")), "Does not click character rows while logout is pending");

            desktop = new FakeDesktop(settings) { LoadingSeconds = 300 };
            Throws<InvalidOperationException>(() => new CharacterRotationRunner(settings, desktop, _ => { }).SwitchTo(1), "Loading timeout never reports a completed switch");
            Check(desktop.Actions.Count == 4, "Loading failure does not retry login or issue unrelated keys");

            desktop = new FakeDesktop(settings) { CancelAt = TimeSpan.FromSeconds(2) };
            Throws<OperationCanceledException>(() => new CharacterRotationRunner(settings, desktop, _ => { }).SwitchTo(1), "Stop or focus loss cancels a pending logout");
            Check(desktop.Actions.SequenceEqual(new[] { "key:Escape", "logout" }), "No inputs are sent after cancellation");
            desktop = new FakeDesktop(settings) { CancelAt = TimeSpan.Zero };
            Throws<OperationCanceledException>(() => new CharacterRotationRunner(settings, desktop, _ => { }).SwitchTo(1), "A missing foreground game stops before the first key");
            Check(desktop.Actions.Count == 0, "Initial readiness failure produces no input");

            using (var frame = MakeFrame())
            {
                var marker = ScreenMarker.Capture(frame, new Point(250, 32));
                Check(marker.Matches(frame), "A recorded marker matches the same UI pixels");
                using (var g = Graphics.FromImage(frame))
                {
                    g.FillRectangle(Brushes.DarkRed, marker.X, marker.Y, marker.Width, marker.Height);
                    using (var font = new Font("Arial", 13)) { g.DrawString("Wrong User", font, Brushes.Gold, marker.X, marker.Y); }
                }
                Check(!marker.Matches(frame), "Different text and background are rejected");
                Throws<InvalidOperationException>(() => ScreenMarker.Capture(frame, new Point(60, 350)), "Blank calibration regions are rejected");
            }
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rotation-test.xml");
            settings.Save(path);
            settings.Save(path);
            var loaded = CharacterRotationSettings.Load(path);
            loaded.Validate();
            Check(loaded.Characters[1].Name == "Second" && loaded.StartingCharacter == 1 && loaded.MinutesPerCharacter == 90 &&
                loaded.LogoutButton.Pixels.SequenceEqual(settings.LogoutButton.Pixels), "Settings, order, starting character, and marker pixels survive save and replacement");
            loaded.Characters[1].SelectedRow = loaded.Characters[0].SelectedRow;
            Throws<InvalidOperationException>(loaded.Validate, "Duplicate character positions are rejected");
            loaded = Settings(); loaded.MinutesPerCharacter = 0;
            Throws<InvalidOperationException>(loaded.Validate, "Invalid interval is rejected");
            loaded = Settings(); loaded.WorldScreen = new ScreenMarker();
            Throws<InvalidOperationException>(loaded.Validate, "Incomplete world calibration is rejected");
            Console.WriteLine("All character rotation tests passed. No game input was sent.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private sealed class FakeDesktop : IRotationDesktop
    {
        private readonly CharacterRotationSettings settings;
        private TimeSpan transition;
        private int selected;
        public string State = "world";
        public int LogoutSeconds = 20, LoadingSeconds = 40;
        public bool WrongSelection;
        public int TargetChecks;
        public TimeSpan CancelAt = TimeSpan.MaxValue;
        public List<string> Actions = new List<string>();
        public TimeSpan Elapsed { get; private set; }

        public FakeDesktop(CharacterRotationSettings settings) { this.settings = settings; }
        public void CheckReady()
        {
            if (Elapsed >= CancelAt) { throw new OperationCanceledException(); }
            if (Elapsed >= transition && State == "logout") { State = "selection"; }
            if (Elapsed >= transition && State == "loading") { State = "world"; }
        }
        public bool Matches(ScreenMarker marker)
        {
            CheckReady();
            if (marker == settings.LogoutButton) { return State == "menu"; }
            if (marker == settings.CharacterScreen) { return State == "selection"; }
            if (marker == settings.WorldScreen) { return State == "world"; }
            TargetChecks++;
            return State == "selection" && !WrongSelection && marker == settings.Characters[selected].SelectedRow;
        }
        public void Click(ScreenMarker marker)
        {
            CheckReady();
            if (marker == settings.LogoutButton)
            {
                if (State != "menu") { throw new Exception("Clicked logout outside the menu"); }
                Actions.Add("logout"); State = "logout"; transition = Elapsed + TimeSpan.FromSeconds(LogoutSeconds);
            }
            else
            {
                if (State != "selection") { throw new Exception("Clicked character before selection screen"); }
                selected = settings.Characters.FindIndex(c => c.SelectedRow == marker);
                Actions.Add("select:" + selected);
            }
        }
        public void PressKey(ConsoleKey key)
        {
            CheckReady();
            Actions.Add("key:" + key);
            if (key == ConsoleKey.Escape && State == "world") { State = "menu"; }
            else if (key == ConsoleKey.Enter && State == "selection")
            { State = "loading"; transition = Elapsed + TimeSpan.FromSeconds(LoadingSeconds); }
            else { throw new Exception("Unexpected key/state: " + key + "/" + State); }
        }
        public void Wait(int milliseconds) { Elapsed += TimeSpan.FromMilliseconds(milliseconds); CheckReady(); }
    }
}
