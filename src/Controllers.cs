using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace PortForge
{
    /// <summary>The controller families PortForge draws and lays out. Automatic follows what the engine detects.</summary>
    public enum ControllerKind { Automatic, Xbox, DualSense, DualShock4, SwitchPro, JoyConPair, JoyConLeft, JoyConRight, Generic }

    /// <summary>How the game's buttons sit on a controller whose letters differ from the Switch's.</summary>
    public enum ButtonLayout { ByPosition, ByLabel }

    /// <summary>
    /// The player's controller choice, kept beside the app and written into every game's .ini when it is saved or
    /// played through PortForge. The engine reads the keys (gamepad_map, gamepad_swap_ab, gamepad_joycon_pair).
    /// </summary>
    public sealed class ControllerSettings
    {
        public ControllerKind Kind = ControllerKind.Automatic;
        public ButtonLayout Layout = ButtonLayout.ByPosition;
        public bool PairJoyCons = true;
        public static string PathOverride;
        public static string FilePath { get { return PathOverride ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "controller-settings.txt"); } }

        public static readonly string[] KindNames = {
            "Automatic (use what is connected)", "Xbox controller", "PlayStation DualSense (PS5)", "PlayStation DualShock 4 (PS4)",
            "Nintendo Switch Pro Controller", "Nintendo Joy-Cons (pair)", "Nintendo Joy-Con (left)", "Nintendo Joy-Con (right)", "Other controller" };

        public static ControllerSettings Load()
        {
            var s = new ControllerSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('='); if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                    ControllerKind kind; ButtonLayout layout;
                    if (key == "controller" && Enum.TryParse(value, out kind) && Enum.IsDefined(typeof(ControllerKind), kind)) s.Kind = kind;
                    else if (key == "layout" && Enum.TryParse(value, out layout) && Enum.IsDefined(typeof(ButtonLayout), layout)) s.Layout = layout;
                    else if (key == "pair_joycons") s.PairJoyCons = value != "0";
                }
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            return s;
        }
        public void Save()
        {
            string text = "controller=" + Kind + "\r\nlayout=" + Layout + "\r\npair_joycons=" + (PairJoyCons ? "1" : "0") + "\r\n";
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null); else File.Move(temp, FilePath);
        }

        /// <summary>Letters can only differ from the Switch's on Xbox-style controllers; the rest go by position.</summary>
        public static bool HasLetterChoice(ControllerKind kind) { return kind == ControllerKind.Xbox || kind == ControllerKind.Generic || kind == ControllerKind.Automatic; }

        public const string LetterMap = "south:A east:B west:X north:Y";
        /// <summary>Whether the player has saved a choice in the Controllers window; until then games keep their own.</summary>
        public static bool Chosen { get { return File.Exists(FilePath); } }

        /// <summary>The engine keys this choice sets on a game whose .ini holds existing. By position is the engine's
        /// own map (bottom button = B). A button map of the player's own, made in the game's menu, is never replaced.</summary>
        public Dictionary<string, string> Values(IDictionary<string, string> existing)
        {
            bool letters = Layout == ButtonLayout.ByLabel && HasLetterChoice(Kind);
            string current;
            if (existing == null || !existing.TryGetValue("gamepad_map", out current)) current = "";
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { {"gamepad_joycon_pair", PairJoyCons ? "1" : "0"} };
            if (letters && (current.Length == 0 || current == LetterMap)) values["gamepad_map"] = LetterMap;
            else if (!letters && current == LetterMap) values["gamepad_map"] = "";
            return values;
        }

        /// <summary>Adds the saved controller choice to settings being saved, for the keys this game's engine knows.</summary>
        public static void AddTo(IDictionary<string, string> settings, RuntimeCapabilities capabilities, IDictionary<string, string> existing)
        {
            if (!Chosen) return;
            foreach (var item in Load().Values(existing)) if (capabilities.Keys.Contains(item.Key)) settings[item.Key] = item.Value;
        }

        /// <summary>Writes the choice into each converted game's .ini now, with a backup; games that are running or
        /// whose engine has no controller settings are skipped and named.</summary>
        public List<string> ApplyTo(IEnumerable<string> games, CancellationToken token)
        {
            var report = new List<string>();
            foreach (string exe in games.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                string name = Path.GetFileNameWithoutExtension(exe);
                try
                {
                    if (GameDiagnostics.Running(exe)) { report.Add(name + ": open now, gets it the next time it starts from PortForge"); continue; }
                    var cap = RuntimeCapabilities.Read(exe, token);
                    if (!cap.Keys.Contains("gamepad_joycon_pair")) { report.Add(name + ": its engine has no controller settings, install the PortForge Engine first"); continue; }
                    string path = GameOptimization.ConfigPath(exe), text = File.Exists(path) ? File.ReadAllText(path) : "";
                    var existing = GameOptimization.ReadValues(text);
                    var values = Values(existing).Where(v => cap.Keys.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value);
                    if (values.All(v => existing.ContainsKey(v.Key) && existing[v.Key] == v.Value)) { report.Add(name + ": already set"); continue; }
                    string backup = File.Exists(path) ? path + ".before-portforge-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".bak" : null;
                    GameOptimization.AtomicWrite(path, new UTF8Encoding(false).GetBytes(GameOptimization.Merge(text, values)), backup);
                    report.Add(name + ": applied");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { report.Add(name + ": not changed (" + error.Message + ")"); }
            }
            return report;
        }

        /// <summary>The family of a controller from the type name the engine reports (SDL's).</summary>
        public static ControllerKind FromEngineType(string type, string name)
        {
            string t = (type ?? "").ToLowerInvariant(), n = (name ?? "").ToLowerInvariant();
            if (t.StartsWith("xbox")) return ControllerKind.Xbox;
            if (t == "ps5") return ControllerKind.DualSense;
            if (t == "ps4" || t == "ps3") return ControllerKind.DualShock4;
            if (t == "switchpro") return ControllerKind.SwitchPro;
            if (t == "joyconpair") return ControllerKind.JoyConPair;
            if (t == "joyconleft") return ControllerKind.JoyConLeft;
            if (t == "joyconright") return ControllerKind.JoyConRight;
            if (n.Contains("xbox") || n.Contains("xinput")) return ControllerKind.Xbox;
            if (n.Contains("dualsense")) return ControllerKind.DualSense;
            if (n.Contains("dualshock") || n.Contains("ps4")) return ControllerKind.DualShock4;
            if (n.Contains("pro controller")) return ControllerKind.SwitchPro;
            return ControllerKind.Generic;
        }

        /// <summary>For each button a Switch game shows, the one to press on this controller with this layout.</summary>
        public static string[][] GameButtonChart(ControllerKind kind, ButtonLayout layout)
        {
            string[] face = FaceLabels(kind); // south, east, west, north as printed on the controller
            bool letters = layout == ButtonLayout.ByLabel && (kind == ControllerKind.Xbox || kind == ControllerKind.Generic);
            // By position the Switch has B south, A east, Y west, X north.
            string a = letters ? face[0] : face[1], b = letters ? face[1] : face[0], x = letters ? face[2] : face[3], y = letters ? face[3] : face[2];
            string[] shoulders = ShoulderLabels(kind), menu = MenuLabels(kind);
            return new[] {
                new[] {"A", a}, new[] {"B", b}, new[] {"X", x}, new[] {"Y", y},
                new[] {"L", shoulders[0]}, new[] {"R", shoulders[1]}, new[] {"ZL", shoulders[2]}, new[] {"ZR", shoulders[3]},
                new[] {"+", menu[1]}, new[] {"-", menu[0]}, new[] {"Left stick click", shoulders[4]}, new[] {"Right stick click", shoulders[5]} };
        }
        public static string[] FaceLabels(ControllerKind kind)
        {
            switch (kind)
            {
                case ControllerKind.DualSense: case ControllerKind.DualShock4: return new[] {"Cross", "Circle", "Square", "Triangle"};
                case ControllerKind.SwitchPro: case ControllerKind.JoyConPair: case ControllerKind.JoyConRight: return new[] {"B", "A", "Y", "X"};
                case ControllerKind.JoyConLeft: return new[] {"Down", "Right", "Left", "Up"};
                default: return new[] {"A", "B", "X", "Y"};
            }
        }
        static string[] ShoulderLabels(ControllerKind kind)
        {
            switch (kind)
            {
                case ControllerKind.DualSense: case ControllerKind.DualShock4: return new[] {"L1", "R1", "L2", "R2", "L3", "R3"};
                case ControllerKind.SwitchPro: case ControllerKind.JoyConPair: case ControllerKind.JoyConLeft: case ControllerKind.JoyConRight:
                    return new[] {"L", "R", "ZL", "ZR", "Left stick", "Right stick"};
                default: return new[] {"LB", "RB", "LT", "RT", "Left stick", "Right stick"};
            }
        }
        static string[] MenuLabels(ControllerKind kind)
        {
            switch (kind)
            {
                case ControllerKind.DualSense: return new[] {"Create", "Options"};
                case ControllerKind.DualShock4: return new[] {"Share", "Options"};
                case ControllerKind.SwitchPro: case ControllerKind.JoyConPair: case ControllerKind.JoyConLeft: case ControllerKind.JoyConRight: return new[] {"-", "+"};
                default: return new[] {"View", "Menu"};
            }
        }
    }

    /// <summary>One controller as the engine last reported it (SDL's own values, before any player's map).</summary>
    public sealed class ControllerState
    {
        public int Slot;
        public string Type = "", Name = "";
        public uint Buttons;
        public int LeftX, LeftY, RightX, RightY, LeftTrigger, RightTrigger;
        public readonly MotionSample[] Motion = { new MotionSample(), new MotionSample(), new MotionSample() }; // the pad, its left half, its right half
        public DateTime Seen;
        public bool Held(ControllerButton button) { return (Buttons & (1u << (int)button)) != 0; }
        public ControllerState Copy()
        {
            var c = new ControllerState { Slot = Slot, Type = Type, Name = Name, Buttons = Buttons, LeftX = LeftX, LeftY = LeftY, RightX = RightX, RightY = RightY, LeftTrigger = LeftTrigger, RightTrigger = RightTrigger, Seen = Seen };
            for (int i = 0; i < 3; i++) c.Motion[i].CopyFrom(Motion[i]);
            return c;
        }
    }
    public sealed class MotionSample
    {
        public bool Valid;
        public double AccelX, AccelY, AccelZ, GyroX, GyroY, GyroZ;
        public void CopyFrom(MotionSample s) { Valid = s.Valid; AccelX = s.AccelX; AccelY = s.AccelY; AccelZ = s.AccelZ; GyroX = s.GyroX; GyroY = s.GyroY; GyroZ = s.GyroZ; }
    }
    /// <summary>SDL 3's gamepad buttons, by position (South is the bottom face button on every controller).</summary>
    public enum ControllerButton { South, East, West, North, Back, Guide, Start, LeftStick, RightStick, LeftShoulder, RightShoulder,
        DpadUp, DpadDown, DpadLeft, DpadRight, Misc1, RightPaddle1, LeftPaddle1, RightPaddle2, LeftPaddle2, Touchpad }

    /// <summary>
    /// Runs the bundled engine's --gamepad-monitor and keeps the latest state of each controller. Its lines are parsed
    /// on a reader thread; the window reads snapshots on its own timer.
    /// </summary>
    public sealed class ControllerMonitor : IDisposable
    {
        private readonly object gate = new object();
        private readonly Dictionary<int, ControllerState> pads = new Dictionary<int, ControllerState>();
        private readonly Dictionary<int, MotionTracker[]> trackers = new Dictionary<int, MotionTracker[]>();
        private Process process;
        private volatile bool disposed;
        public string Error { get; private set; }
        public bool Ready { get; private set; }

        public static string EnginePath()
        {
            string bundled = Path.Combine(EngineManager.DefaultFolder, "scarletnx.exe");
            return File.Exists(bundled) ? bundled : null;
        }
        public void Start(string engine)
        {
            if (engine == null || !File.Exists(engine)) { Error = "The PortForge Engine was not found in the app's Engine folder."; return; }
            var info = new ProcessStartInfo(engine, "--gamepad-monitor") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(engine) };
            process = Process.Start(info);
            process.ErrorDataReceived += delegate { };
            process.BeginErrorReadLine();
            var reader = new Thread(Read) { IsBackground = true, Name = "Controller monitor" };
            reader.Start();
        }
        private void Read()
        {
            try
            {
                string line;
                while (!disposed && (line = process.StandardOutput.ReadLine()) != null) Parse(line);
                if (!disposed) Error = "The engine's controller monitor stopped.";
            }
            catch (Exception error) { if (!disposed) Error = error.Message; }
        }
        /// <summary>One line of the monitor's output (public for the tests).</summary>
        public void Parse(string line)
        {
            if (line == "monitor ready") { Ready = true; return; }
            string[] p = line.Split(' ');
            int slot;
            if (p.Length >= 3 && p[0] == "pad" && Int32.TryParse(p[1], out slot))
            {
                lock (gate)
                {
                    if (p[2] == "connect" && p.Length >= 4)
                        pads[slot] = new ControllerState { Slot = slot, Type = p[3], Name = String.Join(" ", p.Skip(4)), Seen = DateTime.UtcNow };
                    if (p[2] == "connect") trackers[slot] = new[] { new MotionTracker(), new MotionTracker(), new MotionTracker() };
                    else if (p[2] == "disconnect") { pads.Remove(slot); trackers.Remove(slot); }
                }
                return;
            }
            if (p.Length < 9 + 21 || p[0] != "state" || !Int32.TryParse(p[1], out slot)) return;
            lock (gate)
            {
                ControllerState s;
                if (!pads.TryGetValue(slot, out s)) return;
                uint buttons;
                if (UInt32.TryParse(p[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out buttons)) s.Buttons = buttons;
                s.LeftX = Int(p[3]); s.LeftY = Int(p[4]); s.RightX = Int(p[5]); s.RightY = Int(p[6]); s.LeftTrigger = Int(p[7]); s.RightTrigger = Int(p[8]);
                for (int m = 0; m < 3; m++)
                {
                    int o = 9 + m * 7; var sample = s.Motion[m];
                    sample.Valid = p[o] == "1";
                    sample.AccelX = Num(p[o + 1]); sample.AccelY = Num(p[o + 2]); sample.AccelZ = Num(p[o + 3]);
                    sample.GyroX = Num(p[o + 4]); sample.GyroY = Num(p[o + 5]); sample.GyroZ = Num(p[o + 6]);
                }
                s.Seen = DateTime.UtcNow;
                MotionTracker[] t;
                if (trackers.TryGetValue(slot, out t)) for (int m = 0; m < 3; m++) t[m].Update(s.Motion[m], s.Seen);
            }
        }
        /// <summary>The rotation of a pad (part 0), or of its left (1) or right (2) half, since recentring; null without motion.</summary>
        public double[] View(int slot, int part)
        {
            lock (gate) { MotionTracker[] t; return trackers.TryGetValue(slot, out t) && t[part].Active ? t[part].View() : null; }
        }
        public void Recenter() { lock (gate) foreach (var t in trackers.Values) foreach (var one in t) one.Recenter(); }
        private static int Int(string s) { int v; return Int32.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0; }
        private static double Num(string s) { double v; return Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0; }
        public List<ControllerState> Snapshot() { lock (gate) return pads.Values.OrderBy(p => p.Slot).Select(p => p.Copy()).ToList(); }
        public void Dispose()
        {
            disposed = true;
            try { if (process != null && !process.HasExited) process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            if (process != null) process.Dispose();
        }
    }

    /// <summary>
    /// The controller's orientation from its gyroscope, with the accelerometer pulling the tilt back to gravity
    /// (a complementary filter, as Mahony's). Turning around the vertical drifts slowly with no compass, so the
    /// view can be recentred. Axes are SDL's: X right, Y out of the face, Z toward the player.
    /// </summary>
    public sealed class MotionTracker
    {
        private double[] r = Identity(), reference = Identity();
        private DateTime last = DateTime.MinValue, started;
        private bool settled;
        public bool Active { get; private set; }

        public void Reset() { r = Identity(); reference = Identity(); last = DateTime.MinValue; settled = false; Active = false; }
        public void Recenter() { reference = (double[])r.Clone(); }
        /// <summary>Where the controller thinks up is, in its own axes (for the tests).</summary>
        public double[] Up() { return new[] { r[3], r[4], r[5] }; }

        public void Update(MotionSample s, DateTime now)
        {
            if (!s.Valid) { Active = false; return; }
            if (!Active) { Active = true; last = now; started = now; settled = false; return; }
            double dt = (now - last).TotalSeconds; last = now;
            if (dt <= 0 || dt > 0.25) return;
            double gx = s.GyroX, gy = s.GyroY, gz = s.GyroZ;
            double an = Math.Sqrt(s.AccelX * s.AccelX + s.AccelY * s.AccelY + s.AccelZ * s.AccelZ);
            if (an > 2 && an < 20)
            {
                // the estimated up, in the controller's frame, is the world's Y row of r (r maps controller to world)
                double vx = r[3], vy = r[4], vz = r[5], ax = s.AccelX / an, ay = s.AccelY / an, az = s.AccelZ / an;
                double k = (now - started).TotalSeconds < 0.6 ? 8.0 : 1.0; // settle the tilt fast at first
                gx += k * (ay * vz - az * vy); gy += k * (az * vx - ax * vz); gz += k * (ax * vy - ay * vx);
            }
            Rotate(gx * dt, gy * dt, gz * dt);
            if (!settled && (now - started).TotalSeconds > 0.8) { Recenter(); settled = true; }
        }
        /// <summary>The rotation since the last recentring, in the controller's axes as they were then (so the view looks
        /// straight at its face as it was held), as a row-major 3x3.</summary>
        public double[] View()
        {
            var t = new double[9];
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) { double sum = 0; for (int k = 0; k < 3; k++) sum += reference[k * 3 + i] * r[k * 3 + j]; t[i * 3 + j] = sum; }
            return t;
        }
        private void Rotate(double x, double y, double z)
        {
            double angle = Math.Sqrt(x * x + y * y + z * z);
            if (angle < 1e-9) return;
            double kx = x / angle, ky = y / angle, kz = z / angle, c = Math.Cos(angle), s = Math.Sin(angle), v = 1 - c;
            var d = new[] { c + kx * kx * v, kx * ky * v - kz * s, kx * kz * v + ky * s,
                            ky * kx * v + kz * s, c + ky * ky * v, ky * kz * v - kx * s,
                            kz * kx * v - ky * s, kz * ky * v + kx * s, c + kz * kz * v };
            var n = new double[9];
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) { double sum = 0; for (int k = 0; k < 3; k++) sum += r[i * 3 + k] * d[k * 3 + j]; n[i * 3 + j] = sum; }
            r = Orthonormal(n);
        }
        private static double[] Orthonormal(double[] m)
        {
            // Gram-Schmidt on the columns keeps rounding from growing into a skew
            double[] a = { m[0], m[3], m[6] }, b = { m[1], m[4], m[7] };
            Normalize(a); double dot = a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
            for (int i = 0; i < 3; i++) b[i] -= dot * a[i];
            Normalize(b);
            double[] c = { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
            return new[] { a[0], b[0], c[0], a[1], b[1], c[1], a[2], b[2], c[2] };
        }
        private static void Normalize(double[] v) { double n = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]); if (n > 0) for (int i = 0; i < 3; i++) v[i] /= n; }
        private static double[] Identity() { return new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }; }
    }
}
