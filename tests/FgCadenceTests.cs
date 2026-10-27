using System;
using System.IO;

/// <summary>
/// Engine pacing, as shipped in the patch (and the engine source when it is on this PC). Engine 1.48: the FPS limit
/// is the game's own frame rate - a number is a cap on the game itself (present_period waits 1/limit, so a 30 FPS
/// game at 120 runs four times its console speed and a 60 FPS game twice), and Maximum (fps_limit=0) waits for
/// nothing at all, so the game runs as fast as the PC renders it. Game speed (SNX_FPS_GAME) is the console's own
/// pace in either case, and the speed lock (speed_lock=1, the app writes it for Game speed) keeps a game at that
/// pace whatever the limit: the screen then shows the game's frames, doubled by FSR 3 frame generation, with a
/// typed limit above twice the game's pace and Unlimited paying up to PRESENT_FG_MAX_MIDS generated pictures a frame
/// - four times the game's own frames, so 240 on a 60 FPS game is 240 - and without FSR 3 the game's own frames,
/// since a picture the game never drew cannot be shown. With the lock off there is nothing for the pipeline to fill:
/// the limit is the game's own rate and FSR 3 doubles at most. The behaviour itself is checked by the engine's
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
        Check(text.Contains("want = frame_generation ? side * 2 : side;"), where + ": real limiter: the game's frames, doubled by FSR 3 frame generation");
        Check(text.Contains("if (frame_generation && locked && fps_limit >= 0 && (fps_limit == 0 || fps_limit > want))") &&
              text.Contains("want = side * (PRESENT_FG_MAX_MIDS + 1);"),
            where + ": a typed limit above twice the game's pace, and Unlimited, are paid with generated pictures - while the lock keeps the game at its own pace - up to the ceiling");
        Check(text.Contains("if (cap && want > cap) want = cap > side ? cap : side;") && !text.Contains("if (frame_generation) want *= 2;"),
            where + ": the typed limit caps distinct pictures and never repeats a frame to reach it");
        Check(!text.Contains("PRESENT_FG_MAX_PICTURES") && !text.Contains("present_fg_slots(int"),
            where + ": the ceiling is four pictures a real frame (three generated), not the old 3x to 10x generation");
        /* Engine 1.48: the limit drives the game itself through the engine, and the menu's top entry is a real one */
        Check(text.Contains("if (lock && game) return game;") && text.Contains("if (!fps) return 0;"),
            where + ": with the lock off a number is the game's frame rate and Maximum (0) waits for nothing at all");
        Check(text.Contains("CHECK(present_period(0, 2, 0) == 0") &&
              text.Contains("CHECK(present_period(240, 1, 0) == 1000000000u / 240") &&
              text.Contains("CHECK(present_display_target(120, 0) == 120") &&
              text.Contains("CHECK(present_display_target(240, 0) == 240"),
            where + ": the engine's own tests hold the unlocked limit to the game's 240 and Maximum to no wait");
    }

    private static int Main()
    {
        fails = 0;
        string root = FindRoot();
        string ver = Path.Combine(root, "engine", "VERSION");
        string version = File.Exists(ver) ? File.ReadAllText(ver).Trim() : "";
        Check(version.Length != 0, "engine/VERSION is present");
        string patch = Path.Combine(root, "engine", "portforge-engine.patch");
        Check(File.Exists(patch), "the engine patch is there");
        if (File.Exists(patch))
        {
            string p = File.ReadAllText(patch);
            CheckPacing(p, "patch");
            Check(version.Length != 0 && p.Contains("#define PF_ENGINE_VERSION \"" + version + "\""),
                "patch: Esc shows Engine " + (version.Length == 0 ? "(unknown)" : version));
            Check(p.Contains("vk_fg_interpolate_mids(vk_cmd(), dw, dh, &cur, mids, k >= 2 ? k - 1 : 1)"),
                "patch: one generated picture for 2x, three when a typed limit asks for more");
            Check(p.Contains("g_fg_owed += (double)(target - side) / (double)side;") &&
                  p.Contains("while (g_fg_owed >= 1.0 && k < PRESENT_FG_MAX_MIDS + 1)"),
                "patch: the frame's share of the typed limit is carried as a fraction, never a repeat");
            Check(p.Contains("#define PRESENT_FG_MAX_MIDS 3") && p.Contains("_Static_assert(FG_MAX_MIDS == PRESENT_FG_MAX_MIDS,"),
                "patch: the pacing ceiling is the renderer's own FG_MAX_MIDS, checked when the engine builds");
            Check(p.Contains("CHECK(present_display_target(240, 1) == 240") && p.Contains("CHECK(present_display_target(144, 1) == 144") &&
                  p.Contains("CHECK(present_display_target(0, 1) == 240") && p.Contains("CHECK(present_display_target(0, 1) == 144"),
                "patch: the engine's own tests hold 240 and 144 to the rate typed, and Unlimited to the most the pipeline can make");
            Check(p.Contains("FG_PROP"), "patch: frame generation propagates motion vectors");
            Check(p.Contains("snprintf(word, sizeof word, \"Maximum\")") && p.Contains("\"Maximum\"") &&
                  !p.Contains("if (fps == 0) snprintf(word, sizeof word, \"Unlimited\")"),
                "patch: the limit menu's top entry is Maximum, matching the app's settings");
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
