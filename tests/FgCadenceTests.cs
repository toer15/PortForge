using System;
using System.IO;

/// <summary>
/// Engine 1.32's pacing, as shipped in the source and the patch: the speed lock always keeps a game at its own
/// pace (a limit above it never speeds it up, FSR 3 on or off), and frame generation targets twice the limit with
/// 2 to 4 pictures per real frame. The behaviour itself is checked by the engine's runtime_present suite.
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
            Check(t.Contains("if (lock && game) return game;"), "speed lock keeps the game's pace with FSR 3 on or off");
            Check(!t.Contains("FSR Off locks pace"), "no FSR-off pace at the limit (it sped games up)");
            Check(t.Contains("PRESENT_FG_MAX_SLOTS = 4"), "up to 4 pictures per real frame");
            Check(t.Contains("k = 2 * fps_limit / side"), "frame generation targets twice the limit");
        }
        string ver = Path.Combine(root, "engine", "VERSION");
        Check(File.Exists(ver) && File.ReadAllText(ver).Trim() == "1.32", "engine/VERSION is 1.32");
        string patch = Path.Combine(root, "engine", "portforge-engine.patch");
        Check(File.Exists(patch) && File.ReadAllText(patch).Contains("k = 2 * fps_limit / side"),
            "the shipped patch has the same pacing");

        if (fails != 0) { Console.Error.WriteLine(fails + " FgCadenceTests failed"); return 1; }
        Console.WriteLine("PASS FgCadenceTests");
        return 0;
    }
}
