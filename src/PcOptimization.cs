using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace PortForge
{
    public sealed class PcOptimizationResult
    {
        public string Summary, Warning, ReportPath;
        public GraphicsProfile Profile;
        public bool Measured;
    }

    /// <summary>Local hardware matching followed by an optional, save-protected measurement of this game.</summary>
    public static class PcOptimization
    {
        public const string ReportName = "PortForge-PC-settings.txt";

        public static Dictionary<string,string> SettingsFor(HardwareProfile pc, GraphicsProfile current, RuntimeCapabilities cap)
        {
            if (cap == null || !cap.SupportsGraphics) throw new InvalidOperationException("This engine does not expose the required graphics settings.");
            var settings = pc.AutomaticBaseline(current).Values();
            if (cap.ProtectsRenderScale) {
                settings["resolution"] = "1"; settings["render_scale_safety"] = "1";
                if (pc.Tier == PcTier.Entry) { settings["docked"] = "0"; settings["upscaler"] = "1"; }
            }
            if (!cap.Keys.Contains("upscaler")) settings.Remove("upscaler");
            if (!cap.Keys.Contains("pixel_smoothing")) settings.Remove("pixel_smoothing");
            if (pc.LogicalCpus > 0)
            {
                bool nativeAuto=cap.Keys.Contains("frame_guard");
                if (cap.Keys.Contains("pipeline_threads")) settings["pipeline_threads"] = nativeAuto?"0":(pc.LogicalCpus <= 4 ? 1 : pc.PipelineThreads).ToString(CultureInfo.InvariantCulture);
                if (cap.Keys.Contains("jit_threads")) settings["jit_threads"] = nativeAuto?"0":Math.Max(1, Math.Min(4, (pc.LogicalCpus - 2) / 4)).ToString(CultureInfo.InvariantCulture);
                if (cap.Keys.Contains("async_shader_translation_threads")) settings["async_shader_translation_threads"] = nativeAuto?"0":Math.Max(1, Math.Min(4, (pc.LogicalCpus - 2) / 4)).ToString(CultureInfo.InvariantCulture);
            }
            // The engine's speed lock keeps every game at its own speed whatever the FPS limit; the hardware decides quality.
            if (cap.Keys.Contains("speed_lock")) settings["speed_lock"] = "1";
            return settings;
        }

        public static PcOptimizationResult Configure(string exe, HardwareProfile pc, bool measure, IProgress<string> progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); GameOptimization.ValidateGame(exe); GameOptimization.EnsureStopped(exe);
            pc = pc ?? HardwareProfile.Detect();
            var result = new PcOptimizationResult { ReportPath = Path.Combine(Path.GetDirectoryName(exe), ReportName) };
            var report = new StringBuilder("PortForge settings for this PC | " + DateTime.UtcNow.ToString("u") + "\r\n" + pc.Details + "\r\n\r\n");
            try
            {
                if (pc.Tier == PcTier.Unsupported) throw new InvalidOperationException(pc.Why);
                var cap = RuntimeCapabilities.Read(exe, token);
                var current = GameOptimization.Load(exe);
                var settings = SettingsFor(pc, current, cap);
                ApplyGlobalPreferences(settings, cap);
                progress.Report("Matching graphics and shader workers to this PC: " + pc.Summary);
                Apply(exe, settings, token);
                result.Profile = GameOptimization.Load(exe);
                report.AppendLine("Hardware baseline saved and verified: " + String.Join(", ", settings.Select(p => p.Key + "=" + p.Value)));
                if (measure)
                {
                    try
                    {
                        int target; if (!Int32.TryParse(current.FpsLimit, out target) || target < 10) target = 60;
                        var measured = GameOptimization.Tune(exe, result.Profile, target, progress, token, true, true);
                        GameOptimization.SaveAndVerify(exe, measured.Profile, cap);
                        result.Profile = measured.Profile; result.Measured = true;
                        report.AppendLine("\r\nMeasured profile applied. " + measured.Summary.Replace("Select Save settings to apply.", "Saved and verified automatically."));
                        report.AppendLine("Measurements: " + Path.GetFileName(measured.ReportPath));
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e)
                    {
                        result.Warning = "Automatic tuning could not finish; hardware baseline settings were kept. " + e.Message;
                        report.AppendLine("\r\n" + result.Warning);
                    }
                }
                else report.AppendLine("\r\nMeasurement is switched off. Settings are hardware estimates, not game performance measurements.");
                result.Summary = "Matched to this PC: " + (result.Profile.Docked ? "TV" : "handheld") + " mode at " + (result.Profile.Resolution * 100).ToString("0.#", CultureInfo.InvariantCulture) + "% internal scale" + (result.Measured ? "; measured settings saved." : "; hardware baseline saved (unmeasured).");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                result.Warning = "Automatic tuning could not finish; hardware matching was unavailable. " + e.Message;
                result.Summary = "Hardware matching unavailable; review Settings.";
                report.AppendLine(result.Warning);
            }
            report.AppendLine("\r\n" + result.Summary);
            report.AppendLine("Engine 1.4 and newer use frame_guard=1 by default and resolve automatic worker counts on the PC running the game. A copied game can therefore adapt its workers on each launch.");
            report.AppendLine("Engine 1.6 and newer use speed_lock=1 for the game's pace. Protected engines keep internal image scaling native; entry-level PC matching requests the game's handheld layout. Measurement compares TV and handheld layouts and retains TV when handheld brings no measured benefit. Some games render identically in both modes. FSR scales the finished image and adds GPU work; it does not remove CPU/loading stalls or create frames.");
            report.AppendLine("Engine 1.7 and newer report measured p99 frame time/1% low and GPU wait details, and reuse shader caches across compatible engine updates. These diagnostics and measurements do not guarantee future scenes will run without stutter.");
            report.AppendLine("Local hardware rules and measured samples; no AI account or hardware upload is needed. FPS, VSync, controllers and experimental shader preferences are preserved. The game selects its Vulkan device; the highest-rated Windows adapter is not proof of the active GPU. Later areas can behave differently; zero lag and universal compatibility are not guaranteed.");
            try { File.WriteAllText(result.ReportPath, report.ToString(), new UTF8Encoding(false)); }
            catch (IOException e) { result.Warning = (result.Warning ?? "") + " Could not save the PC settings report: " + e.Message; }
            catch (UnauthorizedAccessException e) { result.Warning = (result.Warning ?? "") + " Could not save the PC settings report: " + e.Message; }
            return result;
        }

        /// <summary>The saved global FPS limit, upscaling and PortForge FSR 3 choice, copied exactly: "game" stays the game's
        /// speed (it became 60 with FSR 3), Unlimited stays 0 (it was skipped) and FSR 3 off writes frame_generation=0 (a
        /// matched game kept generating).</summary>
        public static void ApplyGlobalPreferences(Dictionary<string,string> settings, RuntimeCapabilities cap)
        {
            if (settings == null) return;
            try
            {
                if (!File.Exists(GlobalSettings.FilePath)) return;
                var g = GlobalSettings.Load(GlobalSettings.FilePath);
                if (!g.Enabled) return;
                if (cap == null || cap.Keys.Contains("upscaler")) settings["upscaler"] = g.Graphics.Upscaler.ToString(CultureInfo.InvariantCulture);
                if (cap == null || cap.Keys.Contains("frame_generation")) settings["frame_generation"] = g.FrameGeneration ? "1" : "0";
            if (cap == null || cap.Keys.Contains("amd_frame_generation")) settings["amd_frame_generation"] = g.AmdFrameGeneration ? "1" : "0";
                if (cap == null || cap.Keys.Contains("fps_limit")) settings["fps_limit"] = g.Graphics.FpsLimit;
                if ((cap == null || cap.Keys.Contains("display_resolution")) && GlobalSettings.IsDisplayResolution(g.DisplayResolution))
                    settings["display_resolution"] = g.DisplayResolution;
                if (cap == null || cap.Keys.Contains("true_resolution")) settings["true_resolution"] = g.TrueResolution ? "1" : "0";
                if (cap == null || cap.Keys.Contains("low_latency")) settings["low_latency"] = g.LowLatency ? "1" : "0";
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void Apply(string exe, Dictionary<string,string> changes, CancellationToken token)
        {
            string path = GameOptimization.ConfigPath(exe);
            byte[] original = File.Exists(path) ? File.ReadAllBytes(path) : null;
            string text = original == null ? "" : Encoding.UTF8.GetString(original);
            token.ThrowIfCancellationRequested(); GameOptimization.EnsureStopped(exe);
            var current = GameOptimization.ReadValues(text);
            if (changes.All(p => current.ContainsKey(p.Key) && current[p.Key] == p.Value)) return;
            string backup = original == null ? null : path + ".before-pc-match-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".bak";
            try
            {
                GameOptimization.AtomicWrite(path, new UTF8Encoding(false).GetBytes(GameOptimization.Merge(text, changes)), backup);
                var values = GameOptimization.ReadValues(File.ReadAllText(path));
                foreach (var pair in changes)
                    if (!values.ContainsKey(pair.Key) || values[pair.Key] != pair.Value) throw new IOException("PC setting verification failed for " + pair.Key);
            }
            catch
            {
                if (original != null) GameOptimization.AtomicWrite(path, original, null);
                else if (File.Exists(path)) File.Delete(path);
                throw;
            }
        }
    }
}
