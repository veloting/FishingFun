using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FishingFun
{
    public partial class KeyBindChooser : UserControl
    {
        public ConsoleKey CastKey { get; set; } = ConsoleKey.D4;

        private static readonly string Filename = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "keybind.txt");

        public EventHandler CastKeyChanged;

        public KeyBindChooser()
        {
            CastKeyChanged += (s, e) => { };

            InitializeComponent();
            ReadConfiguration();
        }

        private void ReadConfiguration()
        {
            try
            {
                if (File.Exists(Filename))
                {
                    var contents = File.ReadAllText(Filename);
                    var configuredKey = (ConsoleKey)int.Parse(contents);
                    if (Enum.IsDefined(typeof(ConsoleKey), configuredKey)) { CastKey = configuredKey; }
                }
                KeyBind.Text = GetCastKeyText(this.CastKey);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine(e.Message);
                CastKey = ConsoleKey.D4;
                KeyBind.Text = GetCastKeyText(this.CastKey);
            }
        }

        private void WriteConfiguration()
        {
            File.WriteAllText(Filename, ((int)CastKey).ToString());
        }

        private void CastKey_Focus(object sender, RoutedEventArgs e)
        {
            KeyBind.SelectAll();
        }

        private void KeyBind_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var key = e.Key.ToString();
            ProcessKeybindText(key);
        }

        private void ProcessKeybindText(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                ConsoleKey ck;
                if (Enum.TryParse<ConsoleKey>(key, out ck) && Enum.IsDefined(typeof(ConsoleKey), ck))
                {
                    this.CastKey = ck;
                    KeyBind.Text = GetCastKeyText(ck);
                    WriteConfiguration();
                    CastKeyChanged?.Invoke(this, null);
                    return;
                }
            }
            KeyBind.Text = GetCastKeyText(this.CastKey);
        }

        private string GetCastKeyText(ConsoleKey ck)
        {
            string keyText = ck.ToString();
            if (keyText.Length == 1) { return keyText; }
            if (keyText.StartsWith("D") && keyText.Length == 2)
            {
                return keyText.Substring(1, 1);
            }
            return keyText;
        }
    }
}
