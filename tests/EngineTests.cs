using System;
using System.IO;
using System.Linq;
using System.Threading;
using PortForge;

internal static class EngineTests
{
    private static int checks;
    private static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }

    private static string Package(string root, string name, string runtime)
    {
        string folder = Path.Combine(root, name); Directory.CreateDirectory(folder);
        File.Copy(runtime, Path.Combine(folder, "scarletnx.exe"));
        File.WriteAllText(Path.Combine(folder, "libengine-test.dll"), "engine library");
        File.WriteAllText(Path.Combine(folder, "zlib1.dll"), "new zlib");
        File.WriteAllText(Path.Combine(folder, "ENGINE.txt"), "version=1.0\nbase=0de0a87\nfeatures=Smart async shaders\n");
        Directory.CreateDirectory(Path.Combine(folder, "licenses"));
        File.WriteAllText(Path.Combine(folder, "licenses", "AMD-FidelityFX-FSR1-LICENSE.txt"), "AMD license fixture");
        return folder;
    }

    private static int Main(string[] args)
    {
        try
        {
            string working = args[0], failing = args[1], original = args[2];
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine-test-" + Guid.NewGuid().ToString("N"));
            string games = Path.Combine(root, "My Game ø"); Directory.CreateDirectory(games);
            string exe = Path.Combine(games, "My Game.exe"); File.Copy(original, exe); File.Copy(original, Path.Combine(games, "My Game_code.dll"));
            File.WriteAllText(Path.Combine(games, "zlib1.dll"), "original zlib");
            string save = Path.Combine(games, "My Game_data", "save"); Directory.CreateDirectory(save); File.WriteAllText(Path.Combine(save, "slot.dat"), "progress");
            File.WriteAllText(Path.ChangeExtension(exe, ".ini"), "fps_limit=120\r\n");
            byte[] originalBytes = File.ReadAllBytes(exe);

            var package = EngineManager.Find(Package(root, "Engine", working));
            string explicitRun = EngineManager.TimedRun(working, "-H --seconds=1", exe, CancellationToken.None);
            Check(explicitRun.Contains("game_argument=" + Path.GetFullPath(exe)), "A test engine loads the chosen executable even when the folder and game names differ");
            Check(package != null && package.Version == "1.0" && package.Libraries.SequenceEqual(new[] { "libengine-test.dll", "zlib1.dll" }) && package.Name == "PortForge Engine 1.0", "The shipped engine package is read with its version and libraries");
            Check(EngineManager.Find(Path.Combine(root, "missing")) == null, "A missing engine package is reported as missing");
            Check(!EngineManager.IsInstalled(exe, package) && EngineManager.Describe(exe, package).Contains("needs the " + package.Name), "A converted game starts out on its original engine and is told it needs the app's");

            string message = EngineManager.Install(exe, package, null, CancellationToken.None);
            Check(message.Contains("Installed") && EngineManager.IsInstalled(exe, package), "Installing puts the PortForge Engine under the game and checks that it starts");
            Check(File.ReadAllText(Path.Combine(games,"PortForge-engine-licenses","AMD-FidelityFX-FSR1-LICENSE.txt")) == "AMD license fixture", "The engine carries its upscaler license into the player's game folder");
            Check(File.ReadAllText(Path.Combine(games, "zlib1.dll")) == "new zlib" && File.Exists(Path.Combine(games, "libengine-test.dll")), "The engine's libraries are installed beside it");
            string backup = Path.Combine(games, EngineManager.BackupFolderName);
            Check(File.ReadAllBytes(Path.Combine(backup, "My Game.exe")).SequenceEqual(originalBytes) && File.ReadAllText(Path.Combine(backup, "zlib1.dll")) == "original zlib", "The original engine and the libraries it replaced are kept");
            Check(File.ReadAllText(Path.Combine(save, "slot.dat")) == "progress" && File.ReadAllText(Path.ChangeExtension(exe, ".ini")) == "fps_limit=120\r\n", "The start-up check leaves the save and settings exactly as they were");
            Check(EngineManager.Install(exe, package, null, CancellationToken.None).Contains("already installed"), "Installing twice changes nothing");
            string gameIni = Path.ChangeExtension(exe, ".ini");
            File.WriteAllText(gameIni, "fps_limit=120\r\npaced_display=0\r\n");
            var lifted = EngineManager.ReleaseSafePacing(exe).Contains("on again") ? GameOptimization.ReadValues(File.ReadAllText(gameIni)) : null;
            Check(lifted != null && lifted["paced_display"] == "1" && lifted["fps_limit"] == "120" && EngineManager.ReleaseSafePacing(exe) == "",
                  "A new engine lifts the Doctor's own-frames repair once and keeps the player's FPS limit");
            File.WriteAllText(gameIni, "fps_limit=120\r\n");

            EngineManager.Restore(exe);
            Check(File.ReadAllBytes(exe).SequenceEqual(originalBytes) && File.ReadAllText(Path.Combine(games, "zlib1.dll")) == "original zlib" && !File.Exists(Path.Combine(games, "libengine-test.dll")), "Restoring puts the original engine and libraries back and removes added ones");

            var broken = EngineManager.Find(Package(root, "Broken engine", failing));
            bool refused = false;
            try { EngineManager.Install(exe, broken, null, CancellationToken.None); } catch (IOException e) { refused = e.Message.Contains("original engine was put back"); }
            Check(refused && File.ReadAllBytes(exe).SequenceEqual(originalBytes) && File.ReadAllText(Path.Combine(games, "zlib1.dll")) == "original zlib", "An engine that cannot run the game is rolled back automatically");

            // Auto fix everything: the engine goes in, the game is checked, and a player's own choice is respected.
            EngineManager.Restore(exe); EngineManager.Decline(exe, "The PortForge Engine could not run this game: an earlier failure.");
            string fixedReport = AutoFix.Run(exe, new Progress<string>(m => { }), CancellationToken.None, package);
            Console.WriteLine(fixedReport);
            Check(EngineManager.IsInstalled(exe, package) && fixedReport.Contains("FIXED") && fixedReport.Contains("Installed") && fixedReport.Contains("CHECKED"), "Auto fix everything installs the engine after an earlier failure, then checks that the game plays");
            Check(File.ReadAllText(Path.Combine(save, "slot.dat")) == "progress", "Auto fix everything leaves the player's save alone");
            EngineManager.Restore(exe); EngineManager.Decline(exe, "The player restored the original engine.");
            string respected = AutoFix.Run(exe, new Progress<string>(m => { }), CancellationToken.None, package);
            Check(!EngineManager.IsInstalled(exe, package) && respected.Contains("because you chose that"), "Auto fix everything keeps the original engine when the player chose it, and says so");
            try { File.Delete(Path.Combine(backup, EngineManager.DeclinedFile)); } catch (IOException) { }

            string log = "frames=2900 hash=1\nvulkan: a 977 ms frame: 0 shaders translated (0 ms), 2 pipelines made (980 ms waited for), 0 render targets made, 0 texture uploads (0 KB), 0 ms in finishes\n" +
                         "vulkan: a 120 ms frame: 0 shaders translated (0 ms), 0 pipelines made (0 ms waited for), 4 render targets made, 1 texture uploads (128 KB), 0 ms in finishes\n" +
                         "vulkan: 151 shaders translated (440 ms), 111 pipelines made (1 from the cache, 9566 ms making, 6650 ms waited for), 7098 render targets made, 2349 texture uploads (619 MB); 0 frames over 50 ms in the last 600, the slowest 21 ms\n" +
                         "vulkan: 37 binds dropped their draws for a frame while their pipelines were made (async shaders)\n";
            var run = EngineManager.Parse("Test", log);
            Check(run.Frames == 2900 && run.SlowFrames == 2 && run.WorstMs == 977 && run.WaitedMs == 6650 && run.PipelinesMade == 111 && run.AsyncDrops == 37 && !run.Crashed, "Engine runs are measured from the engine's own log lines");
            Check(EngineManager.Parse("Crash", "FATAL: guest abort\nframes=10").Crashed, "A crash in a measured run is noticed");

            var baseline = new EngineRun { Frames = 3000, SlowFrames = 20, WorstMs = 900, WaitedMs = 6000 };
            Check(EngineLab.Better(baseline, new EngineRun { Frames = 3000, SlowFrames = 5, WorstMs = 300, WaitedMs = 200 }), "A change that removes most stutter is kept");
            Check(!EngineLab.Better(baseline, new EngineRun { Frames = 3000, SlowFrames = 19, WorstMs = 880, WaitedMs = 5900 }), "A change within measurement noise is thrown away");
            Check(!EngineLab.Better(baseline, new EngineRun { Frames = 3000, SlowFrames = 2, WorstMs = 100, Crashed = true }), "A change that crashes is thrown away even when it looks faster");
            Check(!EngineLab.Better(baseline, new EngineRun { Frames = 2000, SlowFrames = 2, WorstMs = 100 }), "A change that lowers the frame rate is thrown away");
            Check(!EngineLab.Better(baseline, new EngineRun { Frames = 0 }), "A change that draws nothing is thrown away");
            Console.WriteLine(checks + " engine checks passed. Evidence: " + root); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
