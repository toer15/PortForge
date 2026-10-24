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
        public GraphicsProfile Graphics = new GraphicsProfile();
        public static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PortForge", "global-settings.ini"); } }
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
            if (v.TryGetValue("fps_limit", out x)) s.Graphics.FpsLimit = x;
            if (v.TryGetValue("upscaler", out x) && Int32.TryParse(x, out n)) s.Graphics.Upscaler = n;
            if (v.TryGetValue("anisotropy", out x) && Int32.TryParse(x, out n)) s.Graphics.Anisotropy = n;
            if (v.TryGetValue("docked", out x)) s.Graphics.Docked = x == "1";
            if (v.TryGetValue("fullscreen", out x)) s.Graphics.Fullscreen = x == "1";
            if (v.TryGetValue("vsync", out x)) s.Graphics.VSync = x == "1";
            if (v.TryGetValue("pixel_smoothing", out x)) s.Graphics.PixelSmoothing = x == "1";
            s.Graphics.Validate();
            return s;
        }
        public void Save(string path)
        {
            Graphics.Validate();
            var v = Graphics.Values();
            // Keep the established game-graphics safety policy. No arbitrary render-target resizing.
            v["resolution"] = "1"; v["render_scale_safety"] = "1";
            v["global_enabled"] = Enabled ? "1" : "0";
            v["hide_cursor"] = HideCursor ? "1" : "0";
            v["save_indicator"] = SaveIndicator ? "1" : "0";
            v["frametime_graph"] = FrameTimeGraph ? "1" : "0";
            v["frame_trace"] = FrameTrace ? "1" : "0";
            v["frame_generation"] = FrameGeneration ? "1" : "0";
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
