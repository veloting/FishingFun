using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using FishingFun;

internal static class RotationUiSmokeTests
{
    [STAThread]
    private static int Main()
    {
        try
        {
            var app = new Application();
            var xaml = XDocument.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../FishingFunUI/App.xaml"));
            foreach (var dictionary in xaml.Descendants().Where(n => n.Name.LocalName == "ResourceDictionary" && n.Attribute("Source") != null))
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri((string)dictionary.Attribute("Source")) });
            var settings = new CharacterRotationWindow(new CharacterRotationSettings(), "unused-ui-test.xml");
            var content = (FrameworkElement)settings.Content;
            content.Measure(new Size(670, 680));
            content.Arrange(new Rect(0, 0, 670, 680));
            content.UpdateLayout();
            var image = new RenderTargetBitmap(670, 680, 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var drawing = background.RenderOpen()) { drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 670, 680)); }
            image.Render(background);
            image.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create("rotation-settings-preview.png")) { encoder.Save(stream); }
            settings.Close();
            app.Shutdown();
            Console.WriteLine("PASS: Rotation settings load with application resources and render. No windows shown or game inputs sent.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
