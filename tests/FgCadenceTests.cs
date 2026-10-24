using System;
using System.IO;

/// <summary>
/// FG On => classic 2x game (speed lock holds guest). FG Off => Display/pace equals Limit.
/// </summary>
internal static class FgCadenceTests
{
    static int fails;
    static void Check(bool ok, string msg)
    {
        if (ok) { Console.WriteLine("PASS: " + msg); return; }
        fails++;
        Console.Error.WriteLine("FAIL: " + msg);
    }

    static string FindRoot()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            if (File.Exists(Path.Combine(dir, "engine", "VERSION"))) return dir;
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }
        return Directory.GetCurrentDirectory();
    }

    private static int Main()
    {
        fails = 0;
        string root = FindRoot();
        string present = Path.Combine(root, "engine", "src", "src", "nx", "runtime", "present.c");
        Check(File.Exists(present), "live present.c exists");
        if (File.Exists(present))
        {
            string t = File.ReadAllText(present);
            Check(t.Contains("PRESENT_FG_MAX_SLOTS = 2"), "classic 2x clamp");
            Check(t.Contains("present_fg_on"), "FG-aware present_period helper");
            Check(t.Contains("lock && game && fg"), "FG On keeps guest VI period");
            Check(t.Contains("FSR Off locks pace") || t.Contains("Display = Limit"), "FG Off Limit pace log");
            Check(!t.Contains("PRESENT_FG_MAX_SLOTS = 4"), "no 4-slot default");
        }
        string ver = Path.Combine(root, "engine", "VERSION");
        Check(File.Exists(ver) && File.ReadAllText(ver).Trim() == "1.31", "engine/VERSION is 1.31");
        string patch = Path.Combine(root, "engine", "portforge-engine.patch");
        Check(File.Exists(patch) && File.ReadAllText(patch).Contains("PRESENT_FG_MAX_SLOTS = 2"),
            "patch clamps FG to 2x");

        if (fails != 0) { Console.Error.WriteLine(fails + " FgCadenceTests failed"); return 1; }
        Console.WriteLine("PASS FgCadenceTests");
        return 0;
    }
}
