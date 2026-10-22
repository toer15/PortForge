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
            foreach (string key in new[] {"fps_limit", "resolution", "anisotropy", "shadows", "vsync", "docked", "fullscreen", "log", "compute", "vk_validation", "profile", "nvn_trace", "vk_gpu_profile", "mme_verify", "diag"})
                Console.WriteLine("Test\t" + key + "\t0\tint\t0\t1000\thelp\tlabel\t");
            return;
        }
        string explicitGame = Array.Find(args, a => a.StartsWith("--game="));
        if (explicitGame != null) Console.WriteLine("game_argument=" + explicitGame.Substring(7));
        string path = Path.ChangeExtension(System.Reflection.Assembly.GetExecutingAssembly().Location, ".ini");
        File.WriteAllText(path, "runtime wrote this while testing");
        File.WriteAllText(path + ".started", "started");
        if (Array.Exists(args, a => a.StartsWith("--script=")))
        {
            string seconds = Array.Find(args, a => a.StartsWith("--seconds=")) ?? "--seconds=5";
            string stem = Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string save = Path.Combine(Path.GetDirectoryName(path), stem + "_data", "save");
            Directory.CreateDirectory(save);
            File.WriteAllText(Path.Combine(save, "slot.dat"), "changed by scripted play");
            File.WriteAllText(Path.Combine(save, "extra.dat"), "created by scripted play");
            File.WriteAllText(Path.ChangeExtension(path, ".log"), "test run log");
            Thread.Sleep(Int32.Parse(seconds.Substring(10)) * 1000);
            Console.WriteLine("vulkan: 10 pipelines made (4 from the cache, 3 ms making, 0 ms waited for)");
            Console.WriteLine("frames=600 hash=1234");
            return;
        }
        Thread.Sleep(5000);
        Console.WriteLine("frames=600 hash=1234");
    }
}
