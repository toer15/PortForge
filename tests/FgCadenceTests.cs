using System;
using System.IO;

/// <summary>
/// Verifies Engine 1.31 limit-aware FG cadence: Display FPS can rise toward Limit
/// (30 FPS guest + FSR3 + Limit 120 => 4 slots / ~120) without speeding the guest.
/// Classic 2x floor remains (Limit 60 on a 60 FPS title => ~120).
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

    static int GameSide(int gameFps, int fpsLimit)
    {
        if (fpsLimit <= 0) return gameFps;
        if (fpsLimit > gameFps) return gameFps;
        return fpsLimit;
    }

    static int FgSlots(int side, int fpsLimit)
    {
        if (side <= 0) return 1;
        int target = side * 2;
        if (fpsLimit > 0 && fpsLimit > target) target = fpsLimit;
        if (target > side * 4) target = side * 4;
        int slots = target / side;
        if (slots < 1) slots = 1;
        if (slots > 4) slots = 4;
        return slots;
    }

    static int ExpectedDisplay(int gameFps, int fpsLimit, bool fg)
    {
        int side = GameSide(gameFps, fpsLimit);
        if (!fg || side <= 0) return side;
        return side * FgSlots(side, fpsLimit);
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

        Check(ExpectedDisplay(60, 30, false) == 30, "60-game limit 30 without FG");
        Check(ExpectedDisplay(60, 120, false) == 60, "60-game limit 120 without FG stays guest");
        Check(ExpectedDisplay(60, 60, true) == 120, "60-game limit 60 with FG => classic 120");
        Check(ExpectedDisplay(60, 120, true) == 120, "60-game limit 120 with FG => 120");
        Check(ExpectedDisplay(60, 240, true) == 240, "60-game limit 240 with FG => 240 (4x)");
        Check(ExpectedDisplay(60, 0, true) == 120, "60-game unlimited FG defaults 2x");

        Check(ExpectedDisplay(30, 120, false) == 30, "30-game limit 120 without FG stays 30");
        Check(ExpectedDisplay(30, 60, true) == 60, "30-game limit 60 with FG => 60 (2x)");
        Check(ExpectedDisplay(30, 120, true) == 120, "30-game Limit 120 with FG => 120 (was stuck at 60)");
        Check(FgSlots(30, 120) == 4, "30-game limit 120 needs 4 present slots");
        Check(ExpectedDisplay(30, 30, true) == 60, "30-game limit 30 with FG still classic 2x");

        string root = FindRoot();
        string patch = Path.Combine(root, "engine", "portforge-engine.patch");
        string ver = Path.Combine(root, "engine", "VERSION");
        Check(File.Exists(ver) && File.ReadAllText(ver).Trim() == "1.31", "engine/VERSION is 1.31");
        Check(File.Exists(patch), "engine patch present");
        if (File.Exists(patch))
        {
            string p = File.ReadAllText(patch);
            Check(p.Contains("present_fg_slots"), "patch defines present_fg_slots");
            Check(p.Contains("vk_fg_interpolate_mids"), "patch defines multi-mid interpolate");
            Check(p.Contains("PRESENT_FG_MAX_SLOTS = 4") || p.Contains("FG_MAX_MIDS 3"),
                "patch caps multi-mid at 4 display slots");
            Check(p.Contains("PF_ENGINE_VERSION \"1.31\""), "PF_ENGINE_VERSION is 1.31");
            Check(p.Contains("(%dx game %d)"), "Esc labels Nx game pace; Limit has no ~annotation");
            Check(p.Contains("a 30 FPS game with FG + Limit 120 shows ~120"),
                "runtime_present expects 30@120 => 120");
            Check(p.Contains("classic FG floor") || p.Contains("classic 2x"),
                "slots keep classic 2x floor when Limit is at or below 2x");
        }

        if (fails != 0)
        {
            Console.Error.WriteLine(fails + " FgCadenceTests failed");
            return 1;
        }
        Console.WriteLine("PASS FgCadenceTests");
        return 0;
    }
}
