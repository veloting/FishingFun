using System.Drawing;
using System.Windows.Forms;
using System;
using System.Runtime.InteropServices;

namespace FishingFun
{
    public static class WowScreen
    {
        public static Color GetColorAt(Point pos, Bitmap bmp)
        {
            return bmp.GetPixel(pos.X, pos.Y);
        }

        // Capture and mouse coordinates must use the same physical pixels on mixed-DPI monitors.
        public static IDisposable UsePhysicalPixels() => new DpiScope();

        public static Rectangle GetCaptureBounds(bool requireForeground = true)
        {
            using (UsePhysicalPixels())
            using (var process = WowProcess.Get(logMissing: false))
            {
                if (process == null) { return Rectangle.Empty; }
                var window = process.MainWindowHandle;
                if (window == IntPtr.Zero || IsIconic(window) || !IsWindowVisible(window) ||
                    (requireForeground && GetForegroundWindow() != window) || !GetClientRect(window, out var client))
                {
                    return Rectangle.Empty;
                }

                var origin = new Point();
                if (!ClientToScreen(window, ref origin)) { return Rectangle.Empty; }
                int width = client.Right - client.Left;
                int height = client.Bottom - client.Top;
                if (width < 4 || height < 4) { return Rectangle.Empty; }

                // Preserve the central search area, relative to the game instead of the primary screen.
                var bounds = new Rectangle(origin.X + width / 4, origin.Y + height / 4,
                    width / 2, Math.Max(1, height / 2 - Math.Min(100, height / 4)));
                return SystemInformation.VirtualScreen.Contains(bounds) ? bounds : Rectangle.Empty;
            }
        }

        public static Bitmap GetBitmap(out Rectangle bounds, bool requireForeground = true)
        {
            using (UsePhysicalPixels())
            {
                bounds = GetCaptureBounds(requireForeground);
                if (bounds.IsEmpty) { throw new OperationCanceledException("Waiting for a visible, foreground WoW window."); }
                var bitmap = new Bitmap(bounds.Width, bounds.Height);
                try
                {
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                    }
                    return bitmap;
                }
                catch
                {
                    bitmap.Dispose();
                    throw;
                }
            }
        }

        public static Point GetScreenPositionFromBitmapPostion(Point pos, Rectangle bounds)
            => new Point(pos.X + bounds.X, pos.Y + bounds.Y);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr window, ref Point point);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        private sealed class DpiScope : IDisposable
        {
            private readonly IntPtr previous;

            public DpiScope()
            {
                try { previous = SetThreadDpiAwarenessContext(new IntPtr(-3)); }
                catch (EntryPointNotFoundException) { } // Older Windows uses the application's DPI mode.
            }

            public void Dispose()
            {
                if (previous != IntPtr.Zero) { SetThreadDpiAwarenessContext(previous); }
            }
        }
    }
}
