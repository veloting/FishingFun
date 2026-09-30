using System;
using System.Reflection;
using FishingFun;

internal static class WowProcessSelectionTests
{
    private static void Check(int expected, IntPtr foreground, string message, params IntPtr[] windows)
    {
        var select = typeof(WowProcess).GetMethod("SelectWindowIndex", BindingFlags.Static | BindingFlags.NonPublic);
        int actual = (int)select.Invoke(null, new object[] { windows, foreground });
        if (actual != expected) { throw new Exception(message + ": expected " + expected + ", got " + actual); }
        Console.WriteLine("PASS: " + message);
    }

    private static int Main()
    {
        try
        {
            var first = new IntPtr(101);
            var second = new IntPtr(202);
            Check(1, second, "Foreground client wins over the first process", first, second);
            Check(1, second, "A windowless process cannot block the active client", IntPtr.Zero, second);
            Check(2, second, "Foreground client wins across all supported game editions", first, IntPtr.Zero, second);
            Check(0, first, "Switching foreground selects the other client", first, second);
            Check(1, new IntPtr(303), "Background discovery skips windowless processes", IntPtr.Zero, first, second);
            Check(0, IntPtr.Zero, "No foreground still permits discovering a real game window", first, second);
            Check(-1, IntPtr.Zero, "Windowless processes are never selected", IntPtr.Zero, IntPtr.Zero);
            Check(-1, first, "No processes yields no selection");
            Console.WriteLine("All process selection checks passed. No game input was sent.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
