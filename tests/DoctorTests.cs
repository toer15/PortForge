using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using PortForge;

internal static class DoctorTests
{
    private static int checks;
    private static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
    private static bool Wait(Func<bool> condition, int seconds) { var end = DateTime.UtcNow.AddSeconds(seconds); while (DateTime.UtcNow < end) { if (condition()) return true; Thread.Sleep(100); } return condition(); }

    private static int Main(string[] args)
    {
        try
        {
            // Lines in the format of a real gameplay session's log.
            string compile = "[ 110.07] vulkan: a 977 ms frame: 0 shaders translated (0 ms), 2 pipelines made (980 ms waited for), 0 render targets made, 0 texture uploads (0 KB), 0 ms in finishes\n" +
                             "[ 122.69] vulkan: a 292 ms frame: 0 shaders translated (0 ms), 0 pipelines made (291 ms waited for), 0 render targets made, 0 texture uploads (0 KB), 0 ms in finishes\n" +
                             "[ 135.00] vulkan: a 280 ms frame: 0 shaders translated (0 ms), 0 pipelines made (278 ms waited for), 0 render targets made, 0 texture uploads (0 KB), 0 ms in finishes\n";
            string loading = "[  27.76] vulkan: a 358 ms frame: 0 shaders translated (0 ms), 0 pipelines made (0 ms waited for), 36 render targets made, 8 texture uploads (7478 KB), 0 ms in finishes\n";
            var slow = new StringBuilder();
            for (int i = 0; i < 12; i++) slow.Append("[ " + (291 + i * 0.6).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "] vulkan: a " + (250 + i * 20) + " ms frame: 0 shaders translated (0 ms), 0 pipelines made (0 ms waited for), 0 render targets made, 0 texture uploads (0 KB), 0 ms in finishes\n");
            string memory = "[ 400.00] vulkan: memory used and budgeted: heap 0 163 of 15181 MB (128 ours), heap 1 15000 of 15419 MB (2016 ours)\n";
            string crash = "[ 500.00] FATAL: guest abort in a service\n";

            var watch = new LogWatch();
            var events = watch.Feed(loading + compile, "Game");
            Check(watch.CompileFrames == 3 && watch.LoadingFrames == 1 && watch.CompileWaitMs == 980 + 291 + 278 && watch.WorstMs == 977, "Live log reading sorts stalls into shader stutter and loading freezes");
            Check(events.Count(e => e.Kind == "compile") == 2 && events.Count(e => e.Kind == "loading") == 1, "Shader stutter is reported at most once every 20 seconds, not for every frame");
            Check(events.First(e => e.Kind == "compile").Text.Contains("977 ms"), "The report gives the real freeze length");
            var streak = watch.Feed(slow.ToString(), "Game");
            Check(streak.Count(e => e.Kind == "slowdown") == 1 && watch.UnexplainedFrames == 12, "A burst of slow frames with no new work is reported once as a slowdown");
            Check(watch.Feed(memory, "Game").Any(e => e.Kind == "memory" && e.Text.Contains("15000 of 15419")), "Graphics memory close to full is noticed on the fullest heap");
            Check(watch.Feed(crash, "Game").Any(e => e.Kind == "crash") && watch.Crashed, "A crash line is reported right away");

            var chunked = new LogWatch(); var found = new List<DoctorEvent>();
            string text = loading + compile;
            for (int i = 0; i < text.Length; i += 7) found.AddRange(chunked.Feed(text.Substring(i, Math.Min(7, text.Length - i)), "Game"));
            Check(chunked.CompileFrames == 3 && chunked.LoadingFrames == 1 && found.Count == 3, "Lines split across reads are joined before they are judged");
            Check(new LogWatch().Feed("[ 1.00] vulkan: a 120 ms frame: 0 shaders translated (0 ms), 0 pipelines made (0 ms waited for), 0 render targets made, 0 texture uploads (0 KB), 0 ms in finishes\n", "Game").Count == 0, "A single slow frame does not raise an alarm");

            var health = GameDiagnostics.Inspect("fps_limit=game\n", slow.ToString(), null);
            Check(health.Issues.Any(i => i.Id == "system_slowdown" && i.Fix == null) && !health.Issues.Any(i => i.Id == "pipeline_stalls"), "A slowdown with no game work is explained without a fake repair");

            string busy = LiveDoctor.WhatElseIsBusy(Process.GetCurrentProcess().MainModule.FileName);
            Check(busy.Contains("CPU"), "The doctor can sample what else uses the CPU during a slowdown");

            // End to end: a watched game runs, the doctor reads its log live, and repairs it when it closes.
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "doctor-test-" + Guid.NewGuid().ToString("N"));
            string games = Path.Combine(root, "Games"); Directory.CreateDirectory(games);
            string exe = Path.Combine(games, "Doctor game.exe"); File.Copy(args[0], exe); File.Copy(args[0], Path.Combine(games, "Doctor game_code.dll"));
            foreach (string lib in GameDiagnostics.Libraries) File.Copy(args[0], Path.Combine(games, lib));
            string save = Path.Combine(games, "Doctor game_data", "save"); Directory.CreateDirectory(save); File.WriteAllText(Path.Combine(save, "slot.dat"), "player progress");
            File.WriteAllText(Path.ChangeExtension(exe, ".ini"), "fps_limit=120\r\nlog=1\r\n");
            File.WriteAllText(Path.ChangeExtension(exe, ".log"), compile);
            GameOptimization.GameplaySeconds = 2;
            var seen = new List<DoctorEvent>();
            using (var doctor = new LiveDoctor(root) { UseAi = false })
            {
                doctor.Reported += e => { lock (seen) seen.Add(e); };
                doctor.Watch(exe);
                Check(doctor.Games.Contains(Path.GetFullPath(exe)) && File.ReadAllText(Path.Combine(root, "watched-games.txt")).Contains("Doctor game.exe"), "Watched games are remembered for the next start");
                // The doctor's first look at a closed game with a recorded stall repairs it straight away.
                Check(Wait(() => { lock (seen) return seen.Any(e => e.Kind == "fixed"); }, 40), "Pending repairs from an earlier session are applied when the doctor starts");
                lock (seen) seen.Clear();
                File.WriteAllText(Path.ChangeExtension(exe, ".log"), "");
                using (var game = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = games }))
                {
                    Check(Wait(() => { lock (seen) return seen.Any(e => e.Kind == "start"); }, 10), "The doctor notices when the game starts");
                    File.AppendAllText(Path.ChangeExtension(exe, ".log"), "[ 310.00] vulkan: a 640 ms frame: 3 shaders translated (40 ms), 4 pipelines made (600 ms waited for), 0 render targets made, 0 texture uploads (0 KB), 0 ms in finishes\n");
                    Check(Wait(() => { lock (seen) return seen.Any(e => e.Kind == "compile" && e.Text.Contains("640 ms")); }, 10), "Stutter written while the game runs is reported live");
                    Check(File.ReadAllText(Path.ChangeExtension(exe, ".ini")).Contains("fps_limit=120") || File.ReadAllText(Path.ChangeExtension(exe, ".ini")).Contains("runtime wrote"), "The running game's settings are not touched");
                    game.WaitForExit(15000);
                }
                Check(Wait(() => { lock (seen) return seen.Any(e => e.Kind == "end"); }, 15), "The doctor notices when the game closes and summarises the session");
                Check(Wait(() => { lock (seen) return seen.Any(e => e.Kind == "fixed" && e.Text.Contains("Shader cache warmed")); }, 60), "The new stutter is repaired automatically after the game closes");
                Check(File.ReadAllText(Path.Combine(save, "slot.dat")) == "player progress", "Automatic repairs never change the player's save");
                Check(File.ReadAllText(Path.Combine(games, "PortForge-doctor.log")).Contains("Fixed automatically"), "Everything the doctor did is written to the game's doctor log");
            }
            GameOptimization.GameplaySeconds = 45;

            // On any PC: the doctor puts the shipped PortForge Engine under a watched game by itself, and respects a "keep original" choice.
            string enginePkg = Path.Combine(root, "Engine"); Directory.CreateDirectory(enginePkg);
            File.Copy(args[0], Path.Combine(enginePkg, "scarletnx.exe")); File.WriteAllText(Path.Combine(enginePkg, "ENGINE.txt"), "version=1.0\nbase=0de0a87\nfeatures=Smart async shaders\n");
            EngineManager.FolderOverride = enginePkg;
            string other = Path.Combine(root, "Other"); Directory.CreateDirectory(other);
            string otherExe = Path.Combine(other, "Other game.exe"); File.Copy(args[1], otherExe); File.Copy(args[1], Path.Combine(other, "Other game_code.dll"));
            byte[] originalEngine = File.ReadAllBytes(otherExe);
            var engineEvents = new List<DoctorEvent>();
            using (var doctor = new LiveDoctor(Path.Combine(root, "app2")) { UseAi = false })
            {
                doctor.Reported += e => { lock (engineEvents) engineEvents.Add(e); };
                Directory.CreateDirectory(Path.Combine(root, "app2"));
                doctor.Watch(otherExe);
                Check(Wait(() => { lock (engineEvents) return engineEvents.Any(e => e.Kind == "fixed" && e.Title == "PortForge Engine installed"); }, 40), "The doctor installs the PortForge Engine under a watched game by itself");
            }
            Check(EngineManager.IsInstalled(otherExe, EngineManager.Find()), "The game really runs on the PortForge Engine afterwards");
            EngineManager.Restore(otherExe); EngineManager.Decline(otherExe, "The player restored the original engine.");
            engineEvents.Clear();
            using (var doctor = new LiveDoctor(Path.Combine(root, "app2")) { UseAi = false })
            {
                doctor.Reported += e => { lock (engineEvents) engineEvents.Add(e); };
                Thread.Sleep(6000);
            }
            Check(File.ReadAllBytes(otherExe).SequenceEqual(originalEngine) && !engineEvents.Any(e => e.Kind == "engine"), "A player's choice to keep the original engine is respected");
            EngineManager.FolderOverride = null;
            Check(LiveDoctor.AiProblem("ERROR: You've hit your usage limit. Upgrade to Pro").Contains("usage limit") && LiveDoctor.AiProblem("401 Unauthorized").Contains("not signed in") && LiveDoctor.AiProblem("something else").Contains("could not finish"), "Codex problems are explained in plain words");
            // Every kind of PC is sorted by rules that can be checked without owning it.
            Func<string, long, GpuInfo[]> one = (name, mb) => new[] { new GpuInfo { Name = name, VramMb = mb } };
            Check(HardwareProfile.Classify("cpu", 16, 32, one("NVIDIA GeForce RTX 4090", 24576), false).Tier == PcTier.Unsupported, "A PC without a Vulkan driver is reported as unsupported with the reason");
            Check(HardwareProfile.Classify("cpu", 12, 16, one("Intel(R) UHD Graphics 770", 128), true).Tier == PcTier.Entry, "Integrated graphics is an entry-level PC");
            Check(HardwareProfile.Classify("cpu", 8, 16, one("AMD Radeon(TM) Graphics", 512), true).Tier == PcTier.Entry, "An AMD integrated GPU is an entry-level PC");
            Check(HardwareProfile.Classify("cpu", 8, 16, one("NVIDIA GeForce GTX 1060 6GB", 6144), true).Tier == PcTier.Mid, "A 6 GB discrete card is a mid-range PC");
            Check(HardwareProfile.Classify("cpu", 12, 16, one("NVIDIA GeForce RTX 3060", 12288), true).Tier == PcTier.High, "A large-memory card of a lower class is high-end, not enthusiast");
            Check(HardwareProfile.Classify("cpu", 16, 32, one("AMD Radeon RX 9070 XT", 16304), true).Tier == PcTier.Enthusiast, "A top-class card with 16 GB is an enthusiast PC");
            Check(HardwareProfile.Classify("cpu", 16, 8, one("NVIDIA GeForce RTX 4090", 24576), true).Tier == PcTier.Mid && HardwareProfile.Classify("cpu", 16, 6, one("NVIDIA GeForce RTX 4090", 24576), true).Tier == PcTier.Entry, "Little RAM holds a strong card back");
            Check(HardwareProfile.Classify("cpu", 2, 32, one("NVIDIA GeForce RTX 4090", 24576), true).Tier == PcTier.Entry, "Two CPU threads hold a strong card back");
            var discreteAndIntegrated = HardwareProfile.Classify("cpu", 16, 32, new[] { new GpuInfo { Name = "AMD Radeon(TM) Graphics", VramMb = 512 }, new GpuInfo { Name = "NVIDIA GeForce RTX 4070", VramMb = 12288 } }, true);
            Check(discreteAndIntegrated.BestGpu.Name.Contains("4070"), "With two graphics chips the discrete one is the one rated");
            Check(HardwareProfile.PipelineThreadsFor(16) == 6 && HardwareProfile.PipelineThreadsFor(4) == 2 && HardwareProfile.PipelineThreadsFor(64) == 8, "Pipeline workers scale with the CPU between two and eight");
            Check(HardwareProfile.CompilerJobsFor(2) == 2 && HardwareProfile.CompilerJobsFor(8) == 7 && HardwareProfile.CompilerJobsFor(16) == 15 && HardwareProfile.CompilerJobsFor(64) == 32, "Conversion compiler jobs use almost every CPU thread, capped at thirty-two");
            var chosen = new GraphicsProfile { FpsLimit = "120", VSync = false, Fullscreen = true };
            var matched = HardwareProfile.Classify("cpu", 16, 32, one("AMD Radeon RX 9070 XT", 16304), true).Recommended(chosen);
            Check(matched.Resolution == 2m && matched.Anisotropy == 16 && matched.FpsLimit == "120" && !matched.VSync && matched.Fullscreen, "Matching the PC changes quality only and keeps the player's frame limit, VSync and display mode");
            var here = HardwareProfile.Detect();
            Check(here.LogicalCpus > 0 && here.Summary.Length > 0 && here.Estimate.Length > 0, "This PC can be read and described");
            // Games nobody told the doctor about are found by what a converted game looks like, wherever it was saved.
            string finds = Path.Combine(root, "finds"), deep = Path.Combine(finds, "Games", "Some Game"), ignored = Path.Combine(deep, "Some Game_data", "nested");
            Directory.CreateDirectory(ignored);
            foreach (string stem in new[] { Path.Combine(deep, "Some Game"), Path.Combine(ignored, "Hidden") }) { File.WriteAllText(stem + ".exe", "x"); File.WriteAllText(stem + "_code.dll", "x"); File.WriteAllText(stem + ".abi", "x"); }
            File.WriteAllText(Path.Combine(finds, "Games", "NotAGame.exe"), "x");
            var discovered = LiveDoctor.FindConvertedGames(new[] { finds });
            Check(discovered.Count == 1 && discovered[0].EndsWith("Some Game.exe"), "A converted game is found by its .abi and _code.dll beside its .exe, without opening it first");
            Check(LiveDoctor.FindConvertedGames(new[] { finds }, 1).Count == 0, "Discovery does not go deeper than the depth it was given");
            Console.WriteLine(checks + " doctor checks passed. Evidence: " + root); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
