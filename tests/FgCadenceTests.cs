using System;
using System.IO;

/// <summary>
/// Engine pacing, as shipped in the patch (and the engine source when it is on this PC). Engine 1.49: no FPS limit
/// changes a game's speed. present_period returns the game's own swap interval at every limit and in every mode -
/// Game speed, a number, Maximum - and ignores the speed lock, so a leftover speed_lock=0 cannot speed a game up;
/// a game that times itself (interval 0) is paced only by a numeric limit, Maximum leaving it to its own timers.
/// What a limit governs is the pictures (Engine 1.51): the number typed is the pictures a second on screen, whatever
/// the limit, and FSR 3 frame generation doubles it - 120 typed is 240 with it on and 120 with it off. The pictures
/// above the game's own frames are generated where AMD's interpolation can make them (up to PRESENT_FG_MAX_MIDS a
/// frame) and repeats of the game's frame for the rest, so 240 from a 30 FPS game is 8 a frame with the game still at
/// its console pace. Game speed is the console's pace, doubled by FSR 3; Maximum is the panel. VSync caps it at the
/// refresh. Pacing waits on the microsecond clock (plat_wait_until). The behaviour itself is checked by the engine's
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
        Check(text.Contains("u64 game = interval ? 1000000000u / (u64)VI_REFRESH_HZ * interval : 0;") && text.Contains("if (game) return game;"),
            where + ": the game's own swap interval is its pace, whatever the limit and the lock say (Engine 1.49)");
        Check(text.Contains("(void)lock;") && !text.Contains("if (lock && game) return game;"),
            where + ": the speed lock no longer decides pacing, so a leftover speed_lock=0 cannot speed a game up");
        Check(text.Contains("if (fps == SNX_FPS_GAME || !fps) return 0;"),
            where + ": a self-timed game at Maximum (0) is left to its own timers");
        Check(!text.Contains("FSR Off locks pace"), where + ": no FSR-off pace at the limit (it sped games up)");
        Check(text.Contains("want = frame_generation ? base * 2 : base;"), where + ": the limiter's number is the pictures a second on screen, doubled by FSR 3 frame generation (Engine 1.51)");
        Check(text.Contains("base = fps_limit;") && text.Contains("base = hz > 0 ? hz : side;")
              && text.Contains("base = side;"),
            where + ": a typed number is the rate asked for, Game speed the console's pace, Maximum the panel");
        Check(text.Contains("if (want > 1000) want = 1000;") &&
              text.Contains("if (hz > 0 && __atomic_load_n(&g_settings.vsync, __ATOMIC_RELAXED) && want > hz) want = hz;"),
            where + ": the limiter's ceiling holds, and VSync caps the pictures at the display's refresh");
        Check(text.Contains("repeats of the game's frame") && !text.Contains("No frame is shown twice"),
            where + ": pictures above what generation can make are repeats of the game's frame, never a faster game");
        Check(!text.Contains("PRESENT_FG_MAX_PICTURES") && !text.Contains("present_fg_slots(int"),
            where + ": the ceiling is four pictures a real frame (three generated), not the old 3x to 10x generation");
        Check(text.Contains("made = present_game_fps_now();") && text.Contains("if (made > 0) return fps_limit > 0 && fps_limit < made ? fps_limit : made;"),
            where + ": a self-timed game's measured rate stands in for the pace its interval cannot give");
        Check(text.Contains("#define PLAT_WAIT_SPIN_NS 800000ull") && text.Contains("void plat_wait_until(u64 due)") &&
              text.Contains("if (due - now > PLAT_WAIT_SPIN_NS) plat_sleep_ns((i64)(due - now - PLAT_WAIT_SPIN_NS));"),
            where + ": the last 0.8 ms of a pacing wait is decided on plat_ns's microsecond clock, the timer the rest");
        Check(text.Contains("if (next > now) plat_wait_until(next);"), where + ": present_pace waits on the schedule's own clock");
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
                  p.Contains("while (g_fg_owed >= 1.0 && k < FG_QMAX + 1)"),
                "patch: the frame's share of the limit is carried as a fraction, up to the picture queue's ceiling");
            Check(p.Contains("#define PRESENT_FG_MAX_MIDS 3") && p.Contains("_Static_assert(FG_MAX_MIDS == PRESENT_FG_MAX_MIDS,"),
                "patch: the pacing ceiling is the renderer's own FG_MAX_MIDS, checked when the engine builds");
            Check(p.Contains("CHECK(present_display_target(240, 1) == 480") && p.Contains("CHECK(present_display_target(144, 1) == 288") &&
                  p.Contains("CHECK(present_display_target(0, 1) == 120") && p.Contains("CHECK(present_display_target(0, 1) == 144"),
                "patch: the engine's own tests hold a typed number to twice itself with FSR 3, and Maximum to the panel");
            /* the engine's own suite, not the built source: these rows are in tests/runtime_present.c */
            Check(p.Contains("CHECK(present_period(120, 1, 0) == refresh") &&
                  p.Contains("CHECK(present_period(0, 2, 0) == 2 * refresh") &&
                  p.Contains("CHECK(present_display_target(120, 0) == 120") &&
                  p.Contains("CHECK(present_display_target(120, 1) == 240") &&
                  p.Contains("int g_opt_speed_lock = 0; /* the settings row and Esc only: pacing ignores it (Engine 1.49) */"),
                "patch: the engine's own tests hold every limit to the game's own pace, and the pictures to the limiter's number");
            Check(p.Contains("FG_PROP"), "patch: frame generation propagates motion vectors");
            Check(p.Contains("snprintf(word, sizeof word, \"Maximum\")") && p.Contains("\"Maximum\"") &&
                  !p.Contains("if (fps == 0) snprintf(word, sizeof word, \"Unlimited\")"),
                "patch: the limit menu's top entry is Maximum, matching the app's settings");
        }
        /* the engine source, when it was set up on this PC or by CI (engine/src is not part of the repository). The
           pacing wait lives in the platform layer, so plat.h and the Windows implementation are read with it. */
        string src = Path.Combine(root, "engine", "src", "src", "nx", "runtime", "present.c");
        string mainh = Path.Combine(root, "engine", "src", "src", "nx", "runtime", "main.h");
        if (File.Exists(src) && File.Exists(mainh))
        {
            string t = File.ReadAllText(src) + File.ReadAllText(mainh);
            foreach (string platform in new[] { Path.Combine("platform", "plat.h"), Path.Combine("platform", "plat_win32.c") })
            {
                string file = Path.Combine(root, "engine", "src", "src", "nx", "runtime", platform);
                if (File.Exists(file)) t += File.ReadAllText(file);
            }
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
