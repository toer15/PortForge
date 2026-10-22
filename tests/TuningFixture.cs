using System;
using System.IO;
using System.Threading;

internal static class TuningFixture
{
    private static void Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        if (Array.IndexOf(args, "--settings") >= 0)
        {
            string settingsPath = Path.ChangeExtension(System.Reflection.Assembly.GetExecutingAssembly().Location, ".ini");
            if (File.Exists(settingsPath) && File.ReadAllText(settingsPath).Contains("fixture_graphics_protection=1"))
                Console.WriteLine("Test\trender_scale_safety\t0\tint\t0\t1\thelp\tlabel\t");
            if (Array.IndexOf(args, "--slow-probe") >= 0) Thread.Sleep(1500);
            foreach (string key in new[] {"fps_limit", "resolution", "anisotropy", "upscaler", "shadows", "vsync", "docked", "fullscreen", "log", "compute", "vk_validation", "profile", "nvn_trace", "vk_gpu_profile", "mme_verify", "diag", "pipeline_threads", "jit_threads"})
                Console.WriteLine("Test\t" + key + "\t0\tint\t0\t1000\thelp\tlabel\t");
            return;
        }
        string explicitGame = Array.Find(args, a => a.StartsWith("--game="));
        if (explicitGame != null) Console.WriteLine("game_argument=" + explicitGame.Substring(7));
        string path = Path.ChangeExtension(System.Reflection.Assembly.GetExecutingAssembly().Location, ".ini");
        bool pcTest = File.Exists(path) && File.ReadAllText(path).Contains("pc_profile_test=1");
        string originalConfig = File.Exists(path) ? File.ReadAllText(path) : "";
        File.WriteAllText(path, "runtime wrote this while testing");
        File.WriteAllText(path + ".started", "started");
        if (Array.Exists(args, a => a.StartsWith("--script=")))
        {
            string seconds = Array.Find(args, a => a.StartsWith("--seconds=")) ?? "--seconds=5";
            string stem = Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string save = Path.Combine(Path.GetDirectoryName(path), stem + "_data", "save");
            Directory.CreateDirectory(save);
            if (pcTest) Console.WriteLine("save_before=" + File.ReadAllText(Path.Combine(save,"slot.dat")));
            File.WriteAllText(Path.Combine(save, "slot.dat"), "changed by scripted play");
            File.WriteAllText(Path.Combine(save, "extra.dat"), "created by scripted play");
            File.WriteAllText(Path.ChangeExtension(path, ".log"), "test run log");
            Thread.Sleep(Int32.Parse(seconds.Substring(10)) * 1000);
            Console.WriteLine("vulkan: 10 pipelines made (4 from the cache, 3 ms making, 0 ms waited for)");
            if (pcTest)
            {
                string resolution = Array.Find(args, a => a.StartsWith("--resolution="));
                decimal scale = resolution == null ? 1 : Decimal.Parse(resolution.Substring(13), System.Globalization.CultureInfo.InvariantCulture);
                int slow = scale >= 1 ? 8 : scale >= .75m ? 1 : 0, worst = scale >= 1 ? 400 : scale >= .75m ? 60 : 0;
                Console.WriteLine("vulkan: " + slow + " frames over 50 ms in the last 600, the slowest " + worst + " ms");
                decimal p99 = scale >= 1 ? 28m : scale >= .75m ? 16.7m : 16.4m;
                Console.WriteLine("vulkan: frame-time p99 " + p99.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " ms (1% low " + (1000m / p99).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " FPS) across 600 frames");
                File.WriteAllText(path, originalConfig);
                Console.WriteLine("frames=" + (Int32.Parse(seconds.Substring(10))*60) + " hash=1234");
            }
            else Console.WriteLine("frames=600 hash=1234");
            return;
        }
        Thread.Sleep(5000);
        Console.WriteLine("frames=600 hash=1234");
    }
}
