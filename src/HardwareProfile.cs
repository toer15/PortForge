using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PortForge
{
    /// <summary>The graphics cards Windows itself lists through DXGI, with their true dedicated memory (WMI caps it at 4 GB).</summary>
    internal static class DxgiProbe
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct AdapterDesc1
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
            public uint VendorId, DeviceId, SubSysId, Revision;
            public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
            public long AdapterLuid;
            public uint Flags;
        }
        [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIFactory1
        {
            [PreserveSig] int SetPrivateData(); [PreserveSig] int SetPrivateDataInterface(); [PreserveSig] int GetPrivateData(); [PreserveSig] int GetParent();
            [PreserveSig] int EnumAdapters(); [PreserveSig] int MakeWindowAssociation(); [PreserveSig] int GetWindowAssociation(); [PreserveSig] int CreateSwapChain(); [PreserveSig] int CreateSoftwareAdapter();
            [PreserveSig] int EnumAdapters1(uint index, [MarshalAs(UnmanagedType.Interface)] out IDXGIAdapter1 adapter);
        }
        [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIAdapter1
        {
            [PreserveSig] int SetPrivateData(); [PreserveSig] int SetPrivateDataInterface(); [PreserveSig] int GetPrivateData(); [PreserveSig] int GetParent();
            [PreserveSig] int EnumOutputs(); [PreserveSig] int GetDesc(); [PreserveSig] int CheckInterfaceSupport();
            [PreserveSig] int GetDesc1(out AdapterDesc1 desc);
        }
        [DllImport("dxgi.dll", ExactSpelling = true)]
        private static extern int CreateDXGIFactory1(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IDXGIFactory1 factory);

        public static List<GpuInfo> Adapters()
        {
            var list = new List<GpuInfo>();
            try
            {
                Guid id = typeof(IDXGIFactory1).GUID; IDXGIFactory1 factory;
                if (CreateDXGIFactory1(ref id, out factory) != 0 || factory == null) return list;
                for (uint i = 0; i < 16; i++)
                {
                    IDXGIAdapter1 adapter;
                    if (factory.EnumAdapters1(i, out adapter) != 0 || adapter == null) break;
                    AdapterDesc1 d;
                    if (adapter.GetDesc1(out d) == 0 && (d.Flags & 2) == 0 && !String.IsNullOrWhiteSpace(d.Description))
                        list.Add(new GpuInfo { Name = d.Description.Trim(), VramMb = (long)((ulong)d.DedicatedVideoMemory / 1048576UL) });
                    Marshal.ReleaseComObject(adapter);
                }
                Marshal.ReleaseComObject(factory);
            }
            catch (Exception) { }
            return list;
        }
    }

    public sealed class GpuInfo
    {
        public string Name = "", Driver = "";
        public long VramMb;
        public bool Integrated;
    }

    public enum PcTier { Unknown, Unsupported, Entry, Mid, High, Enthusiast }

    /// <summary>
    /// What this PC is and how much a game can ask of it. The tiers come from rules on the graphics card (integrated or not, its memory, its
    /// model class), RAM and CPU threads. They are estimates of a class of hardware, not measurements: Auto tune measures the real game.
    /// </summary>
    public sealed class HardwareProfile
    {
        public string Cpu = "", Summary = "", Why = "", Estimate = "";
        public int LogicalCpus, RamGb;
        public bool Vulkan = true;
        public List<GpuInfo> Gpus = new List<GpuInfo>();
        public GpuInfo BestGpu;
        public PcTier Tier = PcTier.Unknown;

        public string TierName
        {
            get
            {
                switch (Tier)
                {
                    case PcTier.Unsupported: return "Not supported";
                    case PcTier.Entry: return "Entry";
                    case PcTier.Mid: return "Mid-range";
                    case PcTier.High: return "High-end";
                    case PcTier.Enthusiast: return "Enthusiast";
                    default: return "Unknown";
                }
            }
        }

        static readonly Regex Weak = new Regex(@"GeForce\s+(GT\s*\d+|GTX\s*(6|7|9)\d\d\b|GTX\s*10(30|50)\b|GTX\s*16(30|50)\b)|Radeon\s+(R[579]\s|HD\s*\d|RX\s*(4|5)[0-5]\d\b)|Intel\(R\)\s+(UHD|HD|Iris)", RegexOptions.IgnoreCase);
        static readonly Regex IntegratedName = new Regex(@"Intel\(R\)\s+(UHD|HD|Iris|Graphics)|Radeon\(TM\)\s+(Vega\s+\d+\s+)?Graphics|Radeon\s+(Vega\s+\d+|[5-8]\d0M|R\d\s)|AMD\s+Radeon\(TM\)\s+Graphics|Adreno|Mali", RegexOptions.IgnoreCase);
        static readonly Regex TopNvidia = new Regex(@"RTX\s*(30|40|50)(70|80|90)", RegexOptions.IgnoreCase);
        static readonly Regex TopAmd = new Regex(@"RX\s*(6[89]00|7[89]00|79\d0|9070|9080)", RegexOptions.IgnoreCase);

        /// <summary>The engine's pipeline workers for this CPU: about three eighths of its threads, two to eight, so the game keeps the rest.</summary>
        public static int PipelineThreadsFor(int logicalCpus) { return Math.Max(2, Math.Min(8, logicalCpus * 3 / 8)); }

        public int PipelineThreads { get { return PipelineThreadsFor(LogicalCpus); } }

        public static bool LooksIntegrated(string name, long vramMb) { return IntegratedName.IsMatch(name ?? "") && !Regex.IsMatch(name ?? "", @"\bArc\b", RegexOptions.IgnoreCase) || (vramMb > 0 && vramMb < 1024 && !Weak.IsMatch(name ?? "") && !Regex.IsMatch(name ?? "", @"(RTX|GTX|RX\s*\d|Arc)", RegexOptions.IgnoreCase)); }

        /// <summary>Sorts a PC into a tier. Pure, so every rule can be tested without the hardware.</summary>
        public static HardwareProfile Classify(string cpu, int logicalCpus, int ramGb, IList<GpuInfo> gpus, bool vulkan)
        {
            var p = new HardwareProfile { Cpu = cpu ?? "", LogicalCpus = logicalCpus, RamGb = ramGb, Vulkan = vulkan };
            p.Gpus.AddRange((gpus ?? new GpuInfo[0]).GroupBy(g => g.Name + "|" + g.VramMb).Select(g => g.First()));
            foreach (var g in p.Gpus) g.Integrated = g.Integrated || LooksIntegrated(g.Name, g.VramMb);
            // The best card is the one the game should use: discrete before integrated, then the most memory.
            p.BestGpu = p.Gpus.OrderBy(g => g.Integrated ? 1 : 0).ThenByDescending(g => g.VramMb).FirstOrDefault();
            var reasons = new List<string>();
            if (!vulkan) { p.Tier = PcTier.Unsupported; p.Why = "Windows has no Vulkan graphics driver (vulkan-1.dll). Update the graphics driver to run converted games."; p.Estimate = "Games cannot run until the graphics driver supports Vulkan."; p.Summary = Describe(p); return p; }
            if (p.BestGpu == null) { p.Tier = PcTier.Unknown; p.Why = "The graphics card could not be read."; p.Estimate = "No estimate. Auto tune measures the real game."; p.Summary = Describe(p); return p; }
            var gpu = p.BestGpu;
            PcTier tier;
            if (gpu.Integrated || Weak.IsMatch(gpu.Name)) { tier = PcTier.Entry; reasons.Add(gpu.Integrated ? "integrated graphics" : "an older or entry-level graphics card"); }
            else if (gpu.VramMb <= 0) { tier = PcTier.Mid; reasons.Add("graphics memory unknown"); }
            else if (gpu.VramMb < 4000) { tier = PcTier.Entry; reasons.Add("under 4 GB of graphics memory"); }
            else if (gpu.VramMb < 7500) { tier = PcTier.Mid; reasons.Add("4 to 7 GB of graphics memory"); }
            else if (gpu.VramMb < 11500) { tier = PcTier.High; reasons.Add("8 to 11 GB of graphics memory"); }
            else if (TopNvidia.IsMatch(gpu.Name) || TopAmd.IsMatch(gpu.Name)) { tier = PcTier.Enthusiast; reasons.Add("a top-class card with " + Math.Round(gpu.VramMb / 1024.0) + " GB of graphics memory"); }
            else { tier = PcTier.High; reasons.Add("a large-memory card of a lower class"); }
            // The rest of the PC can hold a strong card back.
            if (ramGb > 0 && ramGb < 8 && tier > PcTier.Entry) { tier = PcTier.Entry; reasons.Add("only " + ramGb + " GB of RAM"); }
            else if (ramGb > 0 && ramGb < 16 && tier > PcTier.Mid) { tier = PcTier.Mid; reasons.Add(ramGb + " GB of RAM"); }
            if (logicalCpus > 0 && logicalCpus < 4 && tier > PcTier.Entry) { tier = PcTier.Entry; reasons.Add("only " + logicalCpus + " CPU threads"); }
            else if (logicalCpus > 0 && logicalCpus < 6 && tier > PcTier.Mid) { tier = PcTier.Mid; reasons.Add(logicalCpus + " CPU threads"); }
            p.Tier = tier;
            p.Why = "Because of " + String.Join(", ", reasons.ToArray()) + ".";
            switch (tier)
            {
                case PcTier.Entry: p.Estimate = "Estimate: lighter games should run well; demanding 3D games will need a lower resolution and may stay under 30 FPS."; break;
                case PcTier.Mid: p.Estimate = "Estimate: most games should reach 30 to 60 FPS at the console's own resolution."; break;
                case PcTier.High: p.Estimate = "Estimate: most games should hold 60 FPS, and a higher resolution is affordable."; break;
                default: p.Estimate = "Estimate: games should reach their own frame cap with room for a much higher resolution; high refresh rates work where the game allows them."; break;
            }
            p.Summary = Describe(p);
            return p;
        }

        private static string Describe(HardwareProfile p)
        {
            var g = p.BestGpu;
            return (g != null ? g.Name + (g.VramMb > 0 ? " (" + Math.Round(g.VramMb / 1024.0, g.VramMb >= 10240 ? 0 : 1).ToString(CultureInfo.InvariantCulture) + " GB)" : "") : "Graphics card unknown") + ", " + p.RamGb + " GB RAM, " + p.LogicalCpus + " CPU threads";
        }

        /// <summary>The settings this tier can afford. Frame limit, VSync, display mode and everything else the player chose are kept.</summary>
        public GraphicsProfile Recommended(GraphicsProfile current)
        {
            var r = current == null ? new GraphicsProfile() : current.Copy();
            switch (Tier)
            {
                case PcTier.Entry: r.Resolution = .75m; r.Anisotropy = 0; r.Shadows = true; break;
                case PcTier.Mid: r.Resolution = 1m; r.Anisotropy = 4; r.Shadows = true; break;
                case PcTier.High: r.Resolution = 1.5m; r.Anisotropy = 8; r.Shadows = true; break;
                case PcTier.Enthusiast: r.Resolution = 2m; r.Anisotropy = 16; r.Shadows = true; break;
                default: break;
            }
            return r;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MemoryStatus
        {
            public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            public uint MemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus buffer);

        /// <summary>Reads this PC: the CPU and graphics cards from the Windows registry, RAM from Windows, and whether a Vulkan driver is installed.</summary>
        public static HardwareProfile Detect()
        {
            string cpu = "";
            try { using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")) cpu = key == null ? "" : Convert.ToString(key.GetValue("ProcessorNameString")).Trim(); } catch (Exception) { }
            int ram = 0;
            try { var status = new MemoryStatus(); if (GlobalMemoryStatusEx(status)) ram = (int)Math.Round(status.TotalPhys / 1073741824.0); } catch (Exception) { }
            var gpus = DxgiProbe.Adapters();
            if (gpus.Count == 0)
            try
            {
                using (var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11cd-bfc1-08002be10318}"))
                    if (cls != null)
                        foreach (string name in cls.GetSubKeyNames().Where(n => Regex.IsMatch(n, @"^\d{4}$")))
                            using (var sub = cls.OpenSubKey(name))
                            {
                                string desc = sub == null ? null : sub.GetValue("DriverDesc") as string;
                                if (String.IsNullOrWhiteSpace(desc) || Regex.IsMatch(desc, @"Basic Display|Remote|Virtual|Mirror|Parsec|Indirect", RegexOptions.IgnoreCase)) continue;
                                long vram = 0; object raw = sub.GetValue("HardwareInformation.qwMemorySize");
                                if (raw is long) vram = (long)raw / 1048576;
                                else if (raw is byte[] && ((byte[])raw).Length >= 8) vram = BitConverter.ToInt64((byte[])raw, 0) / 1048576;
                                else { object old = sub.GetValue("HardwareInformation.MemorySize"); if (old is byte[] && ((byte[])old).Length >= 4) vram = (long)BitConverter.ToUInt32((byte[])old, 0) / 1048576; else if (old is int) vram = (long)(uint)(int)old / 1048576; }
                                gpus.Add(new GpuInfo { Name = desc.Trim(), Driver = Convert.ToString(sub.GetValue("DriverVersion")), VramMb = vram });
                            }
            }
            catch (Exception) { }
            bool vulkan = false;
            try { vulkan = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "vulkan-1.dll")); } catch (Exception) { }
            return Classify(cpu, Environment.ProcessorCount, ram, gpus, vulkan);
        }
    }
}
