using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PortForge
{
    // Read by every updated runtime, including games started directly from Explorer.
    public sealed class GlobalSettings
    {
        public bool Enabled = true, HideCursor = true, SaveIndicator = true, FrameTimeGraph, FrameTrace, FrameGeneration;
        /// <summary>auto, 720, 1080, 1440, or 2160: the window and swapchain height.</summary>
        public string DisplayResolution = "auto";
        /// <summary>Engine 1.41: screen-shaped render targets are scaled toward the selected resolution relative to
        /// the game's typical output (1080 TV, 720 handheld). Dynamic-resolution games may differ.</summary>
        public bool TrueResolution = true;
        /// <summary>Engine 1.41: three swapchain images with VSync instead of five, the game ahead of ordinary programs.</summary>
        public bool LowLatency = true;
        /// <summary>0 Linear, 1 FSR 1, 2 FSR 3 frame generation (FSR 1 + AMD FidelityFX SDK frame interpolation).</summary>
        public int UpscaleMode;
        /// <summary>Engine 1.38+: generated pictures come from AMD FidelityFX SDK 1.1.4's optical flow and frame
        /// interpolation (amd_frame_generation=1). Off falls back to PortForge's earlier motion search.</summary>
        public bool AmdFrameGeneration = true;
        public GraphicsProfile Graphics = new GraphicsProfile();
        /// <summary>Tests point this at a scratch file so they never touch the user's own settings.</summary>
        public static string PathOverride;
        public static string FilePath { get { return String.IsNullOrEmpty(PathOverride) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PortForge", "global-settings.ini") : PathOverride; } }

        public bool IsFsr3Style { get { return UpscaleMode == 2 || (FrameGeneration && Graphics.Upscaler == 1 && UpscaleMode != 0); } }

        public static GlobalSettings Load(string path)
        {
            var s = new GlobalSettings();
            if (!File.Exists(path)) return s;
            var v = GameOptimization.ReadValues(File.ReadAllText(path));
            string x; int n;
            if (v.TryGetValue("global_enabled", out x)) s.Enabled = x == "1";
            if (v.TryGetValue("hide_cursor", out x)) s.HideCursor = x == "1";
            if (v.TryGetValue("save_indicator", out x)) s.SaveIndicator = x == "1";
            if (v.TryGetValue("frametime_graph", out x)) s.FrameTimeGraph = x == "1";
            if (v.TryGetValue("frame_trace", out x)) s.FrameTrace = x == "1";
            if (v.TryGetValue("frame_generation", out x)) s.FrameGeneration = x == "1";
            if (v.TryGetValue("display_resolution", out x) && IsDisplayResolution(x)) s.DisplayResolution = x;
            if (v.TryGetValue("true_resolution", out x)) s.TrueResolution = x != "0";
            if (v.TryGetValue("low_latency", out x)) s.LowLatency = x != "0";
            if (v.TryGetValue("amd_frame_generation", out x)) s.AmdFrameGeneration = x != "0";
            if (v.TryGetValue("upscale_mode", out x) && Int32.TryParse(x, out n) && n >= 0 && n <= 2) s.UpscaleMode = n;
            if (v.TryGetValue("fps_limit", out x)) s.Graphics.FpsLimit = x;
            if (v.TryGetValue("upscaler", out x) && Int32.TryParse(x, out n)) s.Graphics.Upscaler = n;
            if (v.TryGetValue("anisotropy", out x) && Int32.TryParse(x, out n)) s.Graphics.Anisotropy = n;
            if (v.TryGetValue("docked", out x)) s.Graphics.Docked = x == "1";
            if (v.TryGetValue("fullscreen", out x)) s.Graphics.Fullscreen = x == "1";
            if (v.TryGetValue("vsync", out x)) s.Graphics.VSync = x == "1";
            if (v.TryGetValue("pixel_smoothing", out x)) s.Graphics.PixelSmoothing = x == "1";
            // Older files: derive UpscaleMode from upscaler + frame_generation when upscale_mode is missing.
            if (!v.ContainsKey("upscale_mode"))
                s.UpscaleMode = s.FrameGeneration && s.Graphics.Upscaler == 1 ? 2 : s.Graphics.Upscaler == 1 ? 1 : 0;
            s.ApplyUpscaleMode();
            s.Graphics.Validate();
            return s;
        }

        public static GlobalSettings LoadEnabledOrNull()
        {
            if (!File.Exists(FilePath)) return null;
            var settings = Load(FilePath);
            return settings.Enabled ? settings : null;
        }

        public void ApplyUpscaleMode()
        {
            if (UpscaleMode < 0 || UpscaleMode > 2) UpscaleMode = 0;
            if (UpscaleMode == 0) { Graphics.Upscaler = 0; FrameGeneration = false; }
            else if (UpscaleMode == 1) { Graphics.Upscaler = 1; FrameGeneration = false; }
            else { Graphics.Upscaler = 1; FrameGeneration = true; }
        }

        public static bool IsDisplayResolution(string value)
        {
            return value == "auto" || value == "720" || value == "1080" || value == "1440" || value == "2160";
        }

        /// <summary>The engine adjusts screen-shaped render targets (render_scale.h pf_true_scale) when the selected
        /// display height differs from the game's typical output, 1080 lines in TV mode and 720 handheld.</summary>
        public bool TrueResolutionActive
        {
            get
            {
                int height;
                if (!Enabled || !TrueResolution || !Int32.TryParse(DisplayResolution, NumberStyles.Integer, CultureInfo.InvariantCulture, out height)) return false;
                return height != (Graphics.Docked ? 1080 : 720);
            }
        }

        /// <summary>Approximate screen-target size for Settings; dynamic-resolution games may produce different sizes.</summary>
        public string RenderResolutionNote()
        {
            int height;
            if (!Int32.TryParse(DisplayResolution, NumberStyles.Integer, CultureInfo.InvariantCulture, out height)) return "Auto keeps the game's own resolution and window.";
            int own = Graphics.Docked ? 1080 : 720;
            if (TrueResolution)
                return "Screen-shaped targets render at " + (height * 16 / 9) + "x" + height + ". The engine learns each game's own output height in its first run (many games draw 900 lines, not " + own + ") and scales from that from the next start. Dynamic-resolution games and data-shaped targets may differ.";
            return "The game keeps its typical " + own + "p " + (Graphics.Docked ? "TV" : "handheld") + " targets; the " + height + "p display scales the finished image.";
        }

        /// <summary>FPS limit and FSR 3 change pictures on screen. The game keeps its own pace.</summary>
        public string EffectiveDisplayFpsNote()
        {
            int fps;
            if (!FrameGeneration || !Int32.TryParse(Graphics.FpsLimit, NumberStyles.Integer, CultureInfo.InvariantCulture, out fps) || fps <= 0)
                return "";
            return "Limit " + fps + " caps the pictures on screen. FSR 3 frame generation adds AMD-generated pictures between game frames up to " + fps + " (twice the game's frames, or up to four times them when the limit is above twice them, and your monitor's refresh with VSync at most). No frame is repeated. The game keeps its own speed.";
        }

        public void Save(string path)
        {
            ApplyUpscaleMode();
            Graphics.Validate();
            if (!IsDisplayResolution(DisplayResolution)) DisplayResolution = "auto";
            var v = Graphics.Values();
            // resolution= stays 1 under protection; true_resolution scales the game's screen-sized targets toward
            // display_resolution (square, small and odd-shaped ones, which may hold data, stay native).
            v["resolution"] = "1";
            v["render_scale_safety"] = "1";
            v["display_resolution"] = DisplayResolution;
            v["true_resolution"] = TrueResolution ? "1" : "0";
            v["low_latency"] = LowLatency ? "1" : "0";
            v["upscale_mode"] = UpscaleMode.ToString(CultureInfo.InvariantCulture);
            v["global_enabled"] = Enabled ? "1" : "0";
            v["hide_cursor"] = HideCursor ? "1" : "0";
            v["save_indicator"] = SaveIndicator ? "1" : "0";
            v["frametime_graph"] = FrameTimeGraph ? "1" : "0";
            v["frame_trace"] = FrameTrace ? "1" : "0";
            v["frame_generation"] = FrameGeneration ? "1" : "0";
            v["amd_frame_generation"] = AmdFrameGeneration ? "1" : "0";
            v["portforge_fsr3"] = UpscaleMode == 2 ? "1" : "0";
            v["speed_lock"] = "1";
            v["pipeline_threads"] = "0";
            var text = new StringBuilder("; PortForge settings for every game. Applied at the next launch.\r\n[PortForge]\r\n");
            foreach (var pair in v) text.Append(pair.Key).Append('=').Append(pair.Value).Append("\r\n");
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(text.ToString());
                    stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
