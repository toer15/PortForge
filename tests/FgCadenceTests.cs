using System;
using System.IO;

/// <summary>
/// Engine 1.34-1.35's pacing, as shipped in the patch (and the engine source when it is on this PC): the speed lock always
/// keeps a game at its own pace (a limit above it never speeds it up, FSR 3 on or off), the screen shows the limit
/// with FSR 3 off (the game's real frames shown again above its own pace) and twice the limit with it on (one
/// generated picture per real frame, the rest repeats). The behaviour itself is checked by the engine's
/// runtime_present suite.
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

    static void CheckPacing(string text, string where)
    {
        Check(text.Contains("if (lock && game) return game;"), where + ": speed lock keeps the game's pace with FSR 3 on or off");
        Check(!text.Contains("FSR Off locks pace"), where + ": no FSR-off pace at the limit (it sped games up)");
        Check(text.Contains("want = fps_limit > 0 ? fps_limit : side;"), where + ": the screen shows the limit with FSR 3 off");
        Check(text.Contains("if (frame_generation) want *= 2;"), where + ": twice the limit with FSR 3");
        Check(!text.Contains("PRESENT_FG_MAX_PICTURES") && !text.Contains("present_fg_slots(int"),
            where + ": no 3x-10x generated pictures per real frame");
    }

    private static int Main()
    {
        fails = 0;
        string root = FindRoot();
        string ver = Path.Combine(root, "engine", "VERSION");
        Check(File.Exists(ver) && File.ReadAllText(ver).Trim() == "1.35", "engine/VERSION is 1.35");
        string patch = Path.Combine(root, "engine", "portforge-engine.patch");
        Check(File.Exists(patch), "the engine patch is there");
        if (File.Exists(patch))
        {
            string p = File.ReadAllText(patch);
            CheckPacing(p, "patch");
            Check(p.Contains("#define PF_ENGINE_VERSION \"1.35\""), "patch: Esc shows Engine 1.35");
            Check(p.Contains("vk_fg_interpolate_mids(vk_cmd(), dw, dh, &cur, mids, 1)"), "patch: one generated picture per real frame");
            Check(p.Contains("vk_fg_hold("), "patch: FSR 3 off repeats a held copy of the real frame");
            Check(p.Contains("FG_PROP"), "patch: frame generation propagates motion vectors");
        }
        /* the engine source, when it was set up on this PC (engine/src is not part of the repository) */
        string src = Path.Combine(root, "engine", "src", "src", "nx", "runtime", "present.c");
        string mainh = Path.Combine(root, "engine", "src", "src", "nx", "runtime", "main.h");
        if (File.Exists(src) && File.Exists(mainh))
        {
            string t = File.ReadAllText(src) + File.ReadAllText(mainh);
            if (!t.Contains("present_display_target"))
                Console.WriteLine("WARN: engine\\src is older than the patch: apply engine\\portforge-engine.patch to ScarletNX " +
                    "(engine\\BASE) before building the engine");
            else CheckPacing(t, "engine source");
        }

        if (fails != 0) { Console.Error.WriteLine(fails + " FgCadenceTests failed"); return 1; }
        Console.WriteLine("PASS FgCadenceTests");
        return 0;
    }
}
