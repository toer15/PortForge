using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PortForge;

internal static class OptimizationTests
{
    private static int checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); checks++; Console.WriteLine("PASS: " + message); }
    private static void Fails(Action action, string message) { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } catch (IOException) { rejected = true; } catch (InvalidOperationException) { rejected = true; } Check(rejected, message); }
    private sealed class Updates : IProgress<string> { public void Report(string s) { } }
    private static int Main(string[] args)
    {
        try
        {
            string root = Path.Combine(Path.GetDirectoryName(args[0]), "optimization-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string exe = Path.Combine(root, "Tuning game.exe"), config = Path.ChangeExtension(exe, ".ini");
            File.Copy(args[0], exe); File.Copy(args[0], Path.Combine(root, "Tuning game_code.dll"));
            var cap = RuntimeCapabilities.Read(exe, CancellationToken.None);
            Check(cap.SupportsGraphics, "Capabilities come from the selected game's runtime");
            Fails(() => RuntimeCapabilities.Parse("unknown\tsetting\t0\tint\t0\t1\tx\ty"), "Unsupported runtime cannot silently save ineffective graphics settings");
            foreach (string value in new[] {"game", "0", "10", "120", "1000"}) new GraphicsProfile { FpsLimit = value }.Validate();
            Check(true, "Native timing, unlimited and custom FPS limits are accepted");
            foreach (string value in new[] {"-1", "9", "1001", "120 --bad", "1.5"}) Fails(() => new GraphicsProfile { FpsLimit = value }.Validate(), "Invalid FPS value rejected: " + value);
            Fails(() => new GraphicsProfile { Resolution = 4.01m }.Validate(), "Invalid render scale rejected");
            string original = "; keep this comment\r\n[Graphics]\r\nresolution=1 ; user's comment\r\nresolution=1\r\n[Controllers]\r\ngamepad_map=A:B\r\n[Game]\r\nsource=C:\\Games\\data\r\nunknown_key=keep\r\n";
            byte[] bytes = new UTF8Encoding(false).GetBytes(original); File.WriteAllBytes(config, bytes);
            var profile = new GraphicsProfile { FpsLimit = "144", Resolution = 1.25m, Anisotropy = 3, VSync = false, Shadows = false, Docked = false };
            var previousCulture = Thread.CurrentThread.CurrentCulture; Thread.CurrentThread.CurrentCulture = new CultureInfo("nb-NO");
            string backup;
            try { backup = GameOptimization.Save(exe, profile, cap); } finally { Thread.CurrentThread.CurrentCulture = previousCulture; }
            string saved = File.ReadAllText(config);
            Check(saved.Contains("resolution=1.25") && !saved.Contains("resolution=1 ;"), "Render scale writes invariant decimals and replaces duplicate keys");
            Check(saved.Contains("; user's comment") && saved.Contains("gamepad_map=A:B") && saved.Contains("unknown_key=keep") && saved.Contains("source=C:\\Games\\data"), "Saving graphics preserves comments, controller mapping, source and unrelated settings");
            Check(File.ReadAllBytes(backup).SequenceEqual(bytes), "Saving keeps an exact backup of the previous configuration");
            var read = GameOptimization.Load(exe);
            Check(read.FpsLimit == "144" && read.Resolution == 1.25m && read.Anisotropy == 3 && !read.VSync && !read.Shadows && !read.Docked, "Per-game settings round-trip without losing non-preset values");
            var quality = GraphicsProfile.Preset("Quality", profile);
            Check(quality.FpsLimit == "144" && !quality.VSync && !quality.Docked, "Quality presets preserve timing and display choices");
            var sample = GameOptimization.ParseSample("sample", profile, "vulkan: 3 frames over 50 ms in the last 600\nvulkan: frame-time p99 18.250 ms (1% low 54.8 FPS) across 600 frames\nvulkan: frame-time p99 20.125 ms (1% low 49.7 FPS) across 600 frames\nframes=1160 hash=ab12\n", 20);
            Check(sample.Fps == 58 && sample.SlowFrames == 3 && sample.HasFramePercentile && sample.P99FrameMs == 20.125m, "Measured FPS, worst-block p99 frame time and stalls parse from runtime output");
            Fails(() => GameOptimization.ParseSample("bad", profile, "frames=0 hash=0", 20), "A no-frame or failed startup never becomes a valid tuning result");
            Fails(() => GameOptimization.ParseSample("crashed",profile,"FATAL: guest crash\nframes=1200 hash=1",20),"A crash report cannot become a tuning recommendation even when frames were recorded");
            var low = new BenchmarkSample { Success = true, Frames = 1200, Fps = 60, Profile = new GraphicsProfile { Resolution = .75m } };
            var high = new BenchmarkSample { Success = true, Frames = 1180, Fps = 59, Profile = new GraphicsProfile { Resolution = 1.5m } };
            Check(GameOptimization.Select(new[] {low, high}, 60) == high, "Automatic selection keeps higher detail when it meets the target");
            Check(GameOptimization.Select(new[] {low, high}, 60, 1) == low, "Automatic tuning does not raise render scale above the player's choice from a menu-only sample");
            high.Fps = 38;
            Check(GameOptimization.Select(new[] {low, high}, 60) == low, "Automatic selection rejects a slower high-resolution profile");
            low.Fps = 30; high.Fps = 29.8;
            Check(GameOptimization.Select(new[] {low, high}, 60) == high, "A game's lower intrinsic ceiling does not force lower resolution without measured benefit");
            File.WriteAllBytes(config, bytes);
            using (var cts = new CancellationTokenSource())
            {
                var task = Task.Run(() => GameOptimization.Tune(exe, profile, 60, new Updates(), cts.Token, false));
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (!File.Exists(config + ".started") && !task.IsCompleted && watch.ElapsedMilliseconds < 10000) Thread.Sleep(20);
                Check(File.Exists(config + ".started"), "Cancellation fixture enters a real test process and writes its INI");
                Fails(() => GameOptimization.Save(exe, profile, cap), "Changing settings while the game is running is blocked");
                cts.Cancel(); bool cancelled = false;
                try { task.GetAwaiter().GetResult(); } catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && File.ReadAllBytes(config).SequenceEqual(bytes), "Cancelling kills only the test process and restores the exact original configuration");
                Check(!Directory.GetFiles(root, "*.tuning-recovery-*.bak").Any(), "Successful restoration clears the temporary recovery snapshot");
            }
            Fails(() => GameOptimization.Tune(exe, profile, 60, new Updates(), CancellationToken.None, false), "An engine exiting before its requested duration is rejected");
            Check(File.ReadAllBytes(config).SequenceEqual(bytes), "Failed tuning also restores the exact original configuration");
            var slowProbe = Task.Run(() => GameOptimization.Run(exe, "--settings --slow-probe", 10, CancellationToken.None));
            var probeWatch = System.Diagnostics.Stopwatch.StartNew();
            while (System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)).Length == 0 && !slowProbe.IsCompleted && probeWatch.ElapsedMilliseconds < 3000) Thread.Sleep(20);
            GameOptimization.EnsureStopped(exe);
            Check(!GameDiagnostics.Running(exe) && !slowProbe.IsCompleted, "A live settings probe is not mistaken for a running game");
            slowProbe.GetAwaiter().GetResult();
            GameOptimization.SaveAndVerify(exe, profile, cap);
            using (var firstPlay = GameOptimization.StartForPlayer(exe))
            {
                var startedWatch = System.Diagnostics.Stopwatch.StartNew();
                while (!File.ReadAllText(config).Contains("runtime wrote") && startedWatch.ElapsedMilliseconds < 3000) Thread.Sleep(20);
                byte[] runningSettings = File.ReadAllBytes(config);
                using (var repeatedPlay = GameOptimization.SaveAndStart(exe, profile, cap))
                    Check(firstPlay.Id == repeatedPlay.Id && File.ReadAllBytes(config).SequenceEqual(runningSettings), "Repeated Play returns the existing process without changing its live settings");
                firstPlay.Kill(); firstPlay.WaitForExit();
            }
            Check(!GameDiagnostics.Running(exe), "An exited game does not prevent a new launch");
            File.WriteAllBytes(config, bytes);
            var fsr = profile.Copy();fsr.Upscaler = 0;GameOptimization.SaveAndVerify(exe,fsr,cap);
            Check(GameOptimization.Load(exe).Upscaler == 0, "The upscaler off switch survives saving and verification");
            fsr.Upscaler = 1;GameOptimization.SaveAndVerify(exe,fsr,cap);
            Check(GameOptimization.Load(exe).Upscaler == 1, "AMD FSR 1 round-trips through the per-game configuration");
            var legacy = new RuntimeCapabilities();foreach(string key in cap.Keys.Where(k=>k!="upscaler"))legacy.Keys.Add(key);
            File.WriteAllBytes(config,bytes);GameOptimization.SaveAndVerify(exe,fsr,legacy);
            Check(!GameOptimization.ReadValues(File.ReadAllText(config)).ContainsKey("upscaler"), "An older engine is not given an unsupported upscaler setting");
            File.WriteAllBytes(config,bytes);
            // Gameplay workload: the scripted play-through must use the guarded save and report gameplay results.
            GameOptimization.GameplaySeconds = 2;
            string saveFolder = Path.Combine(root, "Tuning game_data", "save"); Directory.CreateDirectory(saveFolder); File.WriteAllText(Path.Combine(saveFolder, "slot.dat"), "my progress");
            byte[] logBytes = Encoding.UTF8.GetBytes("my last session log"); File.WriteAllBytes(Path.ChangeExtension(exe, ".log"), logBytes);
            var gameplayResult = GameOptimization.Tune(exe, profile, 60, new Updates(), CancellationToken.None);
            Check(gameplayResult.Samples.Count == 4 && gameplayResult.Summary.Contains("gameplay") && File.ReadAllText(gameplayResult.ReportPath).Contains("scripted button presses"), "Automatic tuning measures scripted gameplay and says so in its report");
            Check(File.ReadAllText(Path.Combine(saveFolder, "slot.dat")) == "my progress" && !File.Exists(Path.Combine(saveFolder, "extra.dat")), "Gameplay tuning cannot change or add to the game save");
            Check(File.ReadAllBytes(Path.ChangeExtension(exe, ".log")).SequenceEqual(logBytes) && File.ReadAllBytes(config).SequenceEqual(bytes), "Gameplay tuning puts back the player log and exact settings");
            Check(!Directory.GetDirectories(root, "PortForge-test-backup-*").Any(), "Gameplay tuning removes its temporary backups");
            var fastGpu = new[] {new GpuInfo {Name="NVIDIA GeForce RTX 4090",VramMb=24576}};
            var strong = HardwareProfile.Classify("Synthetic strong CPU",16,32,fastGpu,true);
            var entry = HardwareProfile.Classify("Synthetic small CPU",4,8,new[]{new GpuInfo{Name="Intel(R) UHD Graphics 770",VramMb=128}},true);
            var picked = new GraphicsProfile {FpsLimit="120",VSync=false,Fullscreen=true,Resolution=2m,Anisotropy=16,Shadows=false,Docked=false};
            var strongPlan=PcOptimization.SettingsFor(strong,picked,cap);var entryPlan=PcOptimization.SettingsFor(entry,picked,cap);
            Check(strongPlan["resolution"]=="1"&&entryPlan["resolution"]=="0.75"&&strongPlan["pipeline_threads"]=="6"&&entryPlan["pipeline_threads"]=="1"&&entryPlan["jit_threads"]=="1", "Automatic profiles scale quality and worker counts for strong and small PCs without automatic upscaling");
            Check(strongPlan["fps_limit"]=="120"&&strongPlan["vsync"]=="0"&&strongPlan["fullscreen"]=="1"&&strongPlan["shadows"]=="0"&&!strongPlan.ContainsKey("async_translation"), "PC matching preserves timing, display choices and experimental shader preferences");
            var noWorkers=new RuntimeCapabilities();noWorkers.Keys.UnionWith(picked.Values().Keys);
            Check(!PcOptimization.SettingsFor(strong,picked,noWorkers).ContainsKey("pipeline_threads"),"Unsupported engine worker settings are not written");
            var guarded=new RuntimeCapabilities();guarded.Keys.UnionWith(cap.Keys);guarded.Keys.Add("frame_guard");
            var guardedPlan=PcOptimization.SettingsFor(strong,picked,guarded);
            Check(guardedPlan["pipeline_threads"]=="0"&&guardedPlan["jit_threads"]=="0"&&!guardedPlan.ContainsKey("async_translation"),"Frame-guard engines adapt workers on each launch while preserving experimental shader choices");
            Check(!guardedPlan.ContainsKey("speed_lock"),"Engines without a speed lock are not given the setting");
            var lockedCap=new RuntimeCapabilities();lockedCap.Keys.UnionWith(guarded.Keys);lockedCap.Keys.Add("speed_lock");
            var lockedPlan=PcOptimization.SettingsFor(strong,picked,lockedCap);
            Check(lockedPlan["speed_lock"]=="1"&&lockedPlan["fps_limit"]=="120","PC matching keeps every game at its own speed while preserving the player's FPS cap");
            var steady=new BenchmarkSample {Success=true,Frames=1200,Fps=60,SlowFrames=1,WorstFrameMs=70,Profile=new GraphicsProfile{Resolution=.75m}};
            var hitching=new BenchmarkSample {Success=true,Frames=1200,Fps=60,SlowFrames=12,WorstFrameMs=500,Profile=new GraphicsProfile{Resolution=1m}};
            Check(GameOptimization.SelectSmooth(new[]{steady,hitching},60,1)==steady,"Automatic matching chooses fewer freezes when average FPS is equal");
            hitching.SlowFrames=1;hitching.WorstFrameMs=800;
            Check(GameOptimization.SelectSmooth(new[]{steady,hitching},60,1)==steady,"A much longer worst freeze can lower the selected resolution despite equal FPS and stall counts");
            hitching.WorstFrameMs=100;
            Check(GameOptimization.SelectSmooth(new[]{steady,hitching},60,1)==hitching,"Small timing differences do not unnecessarily lower image quality");
            var stableP99=new BenchmarkSample {Success=true,Frames=1200,Fps=60,SlowFrames=1,WorstFrameMs=100,P99FrameMs=16.8m,HasFramePercentile=true,Profile=new GraphicsProfile{Resolution=.75m}};
            var unstableP99=new BenchmarkSample {Success=true,Frames=1200,Fps=60,SlowFrames=1,WorstFrameMs=100,P99FrameMs=20.2m,HasFramePercentile=true,Profile=new GraphicsProfile{Resolution=1.25m}};
            var similarP99=new BenchmarkSample {Success=true,Frames=1200,Fps=60,SlowFrames=1,WorstFrameMs=100,P99FrameMs=17.1m,HasFramePercentile=true,Profile=new GraphicsProfile{Resolution=1m}};
            Check(GameOptimization.Select(new[]{stableP99,unstableP99},60)==stableP99,"Auto tune prefers the measured lower-p99 profile when average FPS is equal");
            Check(GameOptimization.SelectSmooth(new[]{stableP99,unstableP99},60,2)==stableP99,"PC matching rejects a profile with materially worse measured p99 frame time");
            Check(GameOptimization.SelectSmooth(new[]{stableP99,similarP99},60,2)==similarP99,"PC matching keeps higher image quality when p99 differs by less than the measurement tolerance");
            string pcIni="fps_limit=120\nvsync=0\nresolution=2\nanisotropy=16\ngamepad_map=A:B\nasync_translation=0\npc_profile_test=1\n";
            File.WriteAllText(config,pcIni);
            var baseline=PcOptimization.Configure(exe,strong,false,new Updates(),CancellationToken.None);
            var baselineValues=GameOptimization.ReadValues(File.ReadAllText(config));
            Check(baseline.Warning==null&&!baseline.Measured&&baselineValues["resolution"]=="1"&&baselineValues["pipeline_threads"]=="6"&&baselineValues["jit_threads"]=="3"&&baselineValues["gamepad_map"]=="A:B"&&baselineValues["async_translation"]=="0", "A real configuration write applies and verifies only supported PC settings while preserving other choices");
            Check(File.ReadAllText(baseline.ReportPath).Contains("Synthetic strong CPU")&&File.ReadAllText(baseline.ReportPath).Contains("not game performance measurements"),"The hardware report distinguishes an unmeasured baseline from game measurements");
            var measured=PcOptimization.Configure(exe,strong,true,new Updates(),CancellationToken.None);
            var measuredValues=GameOptimization.ReadValues(File.ReadAllText(config));
            Check(measured.Warning==null&&measured.Measured&&measuredValues["resolution"]=="0.75"&&measuredValues["fps_limit"]=="120"&&measuredValues["vsync"]=="0", "End-to-end PC matching measures actual fixture processes and saves the smoother profile without changing the frame cap");
            string measuredReport=File.ReadAllText(Directory.GetFiles(root,"PortForge-performance-*.txt").OrderByDescending(x=>x).First());
            Check(System.Text.RegularExpressions.Regex.Matches(measuredReport,"save_before=my progress").Count==4&&File.ReadAllText(Path.Combine(saveFolder,"slot.dat"))=="my progress"&&!File.Exists(Path.Combine(saveFolder,"extra.dat")),"Warm-up and all three comparisons start from the same save and restore it afterwards");
            using(var cancelled=new CancellationTokenSource())
            {cancelled.Cancel();bool stopped=false;string before=File.ReadAllText(config);try{PcOptimization.Configure(exe,strong,true,new Updates(),cancelled.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped&&File.ReadAllText(config)==before,"Cancelling PC matching before it starts writes no settings");}
            File.Delete(Path.ChangeExtension(exe,".log"));
            using(new GameStateGuard(exe))File.WriteAllText(Path.ChangeExtension(exe,".log"),"temporary benchmark log");
            Check(!File.Exists(Path.ChangeExtension(exe,".log")),"A benchmark does not leave a session log behind when none existed before it");
            var protectedCap = new RuntimeCapabilities(); protectedCap.Keys.UnionWith(cap.Keys); protectedCap.Keys.Add("render_scale_safety");
            var performance = GraphicsProfile.Preset("Performance", profile, true);
            Check(!performance.Docked && performance.Upscaler == 1 && performance.Resolution == 1 && performance.FpsLimit == profile.FpsLimit && performance.Fullscreen == profile.Fullscreen && performance.VSync == profile.VSync,
                "Protected Performance requests the game's handheld layout with FSR while preserving timing and fullscreen");
            Check(GraphicsProfile.Preset("Balanced", performance, true).Docked && GraphicsProfile.Preset("Quality", performance, true).Docked,
                "Protected Balanced and Quality can restore TV rendering after Performance");
            GameOptimization.SaveAndVerify(exe, performance, protectedCap);
            var protectedValues = GameOptimization.ReadValues(File.ReadAllText(config));
            Check(protectedValues["resolution"] == "1" && protectedValues["render_scale_safety"] == "1" && GameOptimization.Load(exe).FpsLimit == performance.FpsLimit, "Saving Performance on a protected engine preserves native game images and the player's frame cap");
            var protectedEntry = PcOptimization.SettingsFor(entry, performance, protectedCap);
            Check(protectedEntry["resolution"] == "1" && protectedEntry["render_scale_safety"] == "1" && protectedEntry["docked"] == "0" && protectedEntry["upscaler"] == "1", "Entry-level PC matching selects handheld plus FSR without scaling intermediate game data");
            string protectedIni = "fps_limit=120\nvsync=0\nresolution=0.75\npc_profile_test=1\nfixture_graphics_protection=1\n";
            File.WriteAllText(config, protectedIni);
            var protectedTune = GameOptimization.Tune(exe, performance, 60, new Updates(), CancellationToken.None, true, true);
            Check(protectedTune.Samples.Count == 2 && protectedTune.Samples.All(s=>s.Profile.Resolution==1) && protectedTune.Samples.Select(s=>s.Profile.Docked).Distinct().Count()==2 && File.ReadAllText(config)==protectedIni, "Protected Auto tune compares actual TV and handheld processes, skips duplicate modes and restores the exact settings");
            Check(!protectedTune.Profile.Docked && protectedTune.Samples.All(s=>s.Diagnostic.Contains("tested_mode=")) && !protectedTune.Summary.Contains("FSR speedup"), "Measured handheld p99 improvement selects handheld without claiming measured FSR cost");
            var tiedHandheld = new BenchmarkSample {Success=true,Frames=1200,Fps=60,Profile=new GraphicsProfile{Docked=false}};
            var tiedTv = new BenchmarkSample {Success=true,Frames=1200,Fps=60,Profile=new GraphicsProfile{Docked=true}};
            Check(GameOptimization.SelectSmooth(new[]{tiedHandheld,tiedTv},60,1)==tiedTv, "TV image quality wins when handheld brings no measured smoothness benefit");
            Console.WriteLine(checks + " optimization checks passed. Evidence: " + root); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
