using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PortForge
{
    /// <summary>The Engine tab: puts the PortForge Engine under a converted game, measures what it changes, and lets the AI work on it.</summary>
    public sealed class EnginePanel : UserControl
    {
        private static readonly Color Bg = Color.FromArgb(17, 21, 30), Surface = Color.FromArgb(28, 34, 46), Mint = Color.FromArgb(105, 226, 183), Muted = Color.FromArgb(173, 185, 203), Amber = Color.FromArgb(255, 199, 120), Red = Color.FromArgb(255, 157, 148);
        private readonly Label status = Caption("Load a game above to see its engine.", 12, Color.White), detail = Caption("", 9, Muted);
        private readonly CheckBox asyncShaders = new CheckBox { Text = "Smart async shaders (reduce shader stalls; objects may briefly appear late)", Checked = true, AutoSize = true, AccessibleName = "Smart async shaders" };
        private readonly CheckBox asyncTranslation = new CheckBox { Text = "Background shader translation (experimental; may cause visual errors)", AutoSize = true, AccessibleName = "Background shader translation" };
        private readonly CheckBox frameGuard = new CheckBox { Text = "Automatic frame guard (shared shader wait budget)", Checked = true, AutoSize = true, AccessibleName = "Automatic frame guard" };
        private readonly Button autoFix = Action("Auto fix everything", true), install = Action("Install PortForge Engine", false), restore = Action("Restore original engine", false), measure = Action("Measure the stutter fix", false),
            lab = Action("Let AI improve the engine", true), stop = Action("Stop", false);
        private readonly TextBox results = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 230, Dock = DockStyle.Top, BackColor = Surface, ForeColor = Color.WhiteSmoke, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Engine results" };
        private readonly TableLayoutPanel stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(10, 12, 22, 8) };
        private CancellationTokenSource cancellation;
        private string game;
        private bool loading;
        private EnginePackage package;

        public EnginePanel()
        {
            BackColor = Bg; ForeColor = Color.WhiteSmoke; Dock = DockStyle.Fill; AutoScroll = true;
            stack.Controls.Add(Caption("PortForge Engine", 13, Mint));
            var about = Caption("The PortForge Engine builds graphics pipelines in the background to reduce compilation stalls. Some objects may briefly appear late. It waits for work needed by menus, loading screens and game data. Loading and game-specific freezes can remain. The original engine is kept and can be put back at any time.", 9, Muted);
            stack.Controls.Add(about); stack.Controls.Add(status); stack.Controls.Add(detail);
            var fixAbout = Caption("Auto fix everything: puts the PortForge Engine under this game, applies every repair the app knows, matches the engine to this PC, then plays the game for 20 seconds to check it works. Everything keeps a backup, and an engine that breaks the game is taken out again. Close the game first.", 9, Muted);
            stack.Controls.Add(fixAbout);
            var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            foreach (var b in new[] { autoFix, install, restore, measure }) row.Controls.Add(b);
            stack.Controls.Add(row); stack.Controls.Add(frameGuard); stack.Controls.Add(asyncShaders); stack.Controls.Add(asyncTranslation);
            stack.Controls.Add(Caption("AI Engine Lab", 13, Mint));
            var labAbout = Caption("The AI (Codex on this PC) studies the game's recorded problems, writes a change to the engine's code, and PortForge builds it and measures it on the game with scripted play. A change is kept only when it measurably reduces stutter without new errors; otherwise it is thrown away. Needs the engine source and build tools on this PC.", 9, Muted);
            stack.Controls.Add(labAbout);
            var labRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            labRow.Controls.Add(lab); labRow.Controls.Add(stop); stack.Controls.Add(labRow);
            stack.Controls.Add(Caption("Results", 11, Mint)); stack.Controls.Add(results);
            SizeChanged += delegate { int w = Math.Max(240, ClientSize.Width - 50); foreach (var l in new[] { about, labAbout, detail, status, fixAbout }) l.MaximumSize = new Size(w, 0); };
            Controls.Add(stack);
            autoFix.Click += async delegate { await Work("Fixing everything", (p, t) => AutoFix.Run(game, p, t, package)); };
            install.Click += async delegate { await Work("Installing the PortForge Engine", (p, t) => EngineManager.Install(game, package, p, t)); };
            restore.Click += async delegate { await Work("Restoring the original engine", (p, t) => { EngineManager.Restore(game); EngineManager.Decline(game, "The player restored the original engine."); return "The original engine is back in place. The AI Doctor won't install the PortForge Engine for this game again unless you select Install."; }); };
            measure.Click += async delegate { await Work("Measuring", Measure); };
            lab.Click += async delegate { await Work("AI Engine Lab", (p, t) => EngineLab.Improve(game, p, t)); };
            stop.Click += delegate { if (cancellation != null) cancellation.Cancel(); };
            asyncShaders.CheckedChanged += delegate { if (!loading) SaveAsync(); };
            asyncTranslation.CheckedChanged += delegate { if (!loading) SaveTranslation(); };
            frameGuard.CheckedChanged += delegate { if (!loading) SaveFrameGuard(); };
            SetGame(null);
        }

        public void SetGame(string exe)
        {
            game = exe != null && File.Exists(exe) ? Path.GetFullPath(exe) : null;
            package = EngineManager.Find();
            UpdateState();
        }

        private void UpdateState()
        {
            bool idle = cancellation == null, has = game != null, installed = has && EngineManager.IsInstalled(game, package);
            status.Text = !has ? "No game is loaded yet. Click Browse at the top, choose your converted game's .exe, then click Load." : EngineManager.Describe(game, package);
            detail.Text = package == null ? "The PortForge Engine is not included in this copy of the app (Engine folder missing)." : package.Name + (package.Base.Length > 0 ? ", based on ScarletNX " + package.Base : "") + (package.Features.Length > 0 ? ". " + package.Features : "") + ". " + EngineLab.Availability();
            autoFix.Enabled = idle && has;
            install.Enabled = idle && has && package != null && !installed;
            restore.Enabled = idle && has && File.Exists(Path.Combine(Path.GetDirectoryName(game ?? "C:\\"), EngineManager.BackupFolderName, Path.GetFileName(game ?? "x")));
            measure.Enabled = idle && has && package != null;
            lab.Enabled = idle && has && EngineLab.Available();
            stop.Enabled = !idle; asyncShaders.Enabled = idle && installed; asyncTranslation.Enabled = idle && installed;
            Version version;frameGuard.Enabled=idle&&installed&&Version.TryParse(package.Version,out version)&&version>=new Version(1,4);
            loading = true;
            try
            {
                string value;
                var values = has && File.Exists(GameOptimization.ConfigPath(game)) ? GameOptimization.ReadValues(File.ReadAllText(GameOptimization.ConfigPath(game))) : new Dictionary<string, string>();
                asyncShaders.Checked = !(values.TryGetValue("async_shaders", out value) && value == "0");
                asyncTranslation.Checked = values.TryGetValue("async_translation", out value) && value == "1";
                frameGuard.Checked = !(values.TryGetValue("frame_guard", out value) && value == "0");
            }
            catch (IOException) { }
            finally { loading = false; }
        }

        private void SaveAsync()
        {
            try
            {
                GameOptimization.EnsureStopped(game);
                string path = GameOptimization.ConfigPath(game), text = File.Exists(path) ? File.ReadAllText(path) : "";
                GameOptimization.AtomicWrite(path, new UTF8Encoding(false).GetBytes(GameOptimization.Merge(text, new Dictionary<string, string> { { "async_shaders", asyncShaders.Checked ? "1" : "0" } })), path + ".before-portforge-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".bak");
                Say("Smart async shaders " + (asyncShaders.Checked ? "on" : "off") + ". It applies the next time the game starts.");
            }
            catch (Exception e) { Say("Could not change the setting: " + e.Message); loading = true; asyncShaders.Checked = !asyncShaders.Checked; loading = false; }
        }

        private void SaveTranslation()
        {
            try
            {
                GameOptimization.EnsureStopped(game);
                string path = GameOptimization.ConfigPath(game), text = File.Exists(path) ? File.ReadAllText(path) : "";
                GameOptimization.AtomicWrite(path, new UTF8Encoding(false).GetBytes(GameOptimization.Merge(text, new Dictionary<string, string> { { "async_translation", asyncTranslation.Checked ? "1" : "0" } })), path + ".before-portforge-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".bak");
                Say("Background shader translation " + (asyncTranslation.Checked ? "on" : "off") + ". Applies on next start. If objects appear incorrectly, turn it off for this game.");
            }
            catch (Exception e) { Say("Could not change the setting: " + e.Message); loading = true; asyncTranslation.Checked = !asyncTranslation.Checked; loading = false; }
        }

        private void SaveFrameGuard()
        {
            try
            {
                GameOptimization.EnsureStopped(game);string path=GameOptimization.ConfigPath(game),text=File.Exists(path)?File.ReadAllText(path):"";
                GameOptimization.AtomicWrite(path,new UTF8Encoding(false).GetBytes(GameOptimization.Merge(text,new Dictionary<string,string>{{"frame_guard",frameGuard.Checked?"1":"0"}})),path+".before-frame-guard-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".bak");
                Say("Frame guard "+(frameGuard.Checked?"on":"off")+". Applies at the next game launch.");
            }
            catch(Exception e){Say("Could not change frame guard: "+e.Message);loading=true;frameGuard.Checked=!frameGuard.Checked;loading=false;}
        }

        private string Measure(IProgress<string> progress, CancellationToken token)
        {
            string runtime = EngineManager.IsInstalled(game, package) ? game : package.Exe;
            var runs = EngineManager.CompareAsync(runtime, game, progress, token);
            var off = runs[0]; var on = runs[1];
            string verdict = on.Crashed ? "The game crashed with async shaders: keep them off for this game." :
                on.WorstMs < off.WorstMs || on.SlowFrames < off.SlowFrames ? "Smart async shaders reduced stutter in this test." :
                "No measurable difference in this test: the scenes it reached had little shader work.";
            return off.Summary + "\r\n" + on.Summary + "\r\n" + verdict + " (Both runs used an empty shader cache. Your own cache, save and settings were put back.)";
        }

        private async Task Work(string title, Func<IProgress<string>, CancellationToken, string> job)
        {
            if (cancellation != null || game == null) return;
            cancellation = new CancellationTokenSource(); var token = cancellation.Token; UpdateState();
            var progress = new Progress<string>(Say);
            Say(title + "...");
            try { Say(await Task.Run(() => job(progress, token))); }
            catch (OperationCanceledException) { Say("Stopped. Nothing was changed that wasn't put back."); }
            catch (Exception e) { Say("Could not finish: " + e.Message); }
            finally { cancellation.Dispose(); cancellation = null; UpdateState(); }
        }

        private void Say(string text) { results.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine); }
        public string ResultsText { get { return results.Text; } }
        public bool IsWorking { get { return cancellation != null; } }

        private static Label Caption(string text, float size, Color color) { return new Label { Text = text, AutoSize = true, ForeColor = color, Font = new Font("Segoe UI", size), Margin = new Padding(0, 4, 0, 8) }; }
        private static Button Action(string text, bool primary)
        {
            var b = new ReadableButton { Text = text, AccessibleName = text, AutoSize = true, MinimumSize = new Size(170, 34), FlatStyle = FlatStyle.Flat, BackColor = primary ? Mint : Surface, ForeColor = primary ? Bg : Color.WhiteSmoke, Margin = new Padding(0, 4, 8, 4) };
            b.EnabledChanged += delegate { b.BackColor = b.Enabled ? (primary ? Mint : Surface) : Color.FromArgb(34, 38, 48); b.ForeColor = b.Enabled ? (primary ? Bg : Color.WhiteSmoke) : Color.FromArgb(120, 126, 140); }; b.FlatAppearance.BorderSize = 0; return b;
        }
    }
}
