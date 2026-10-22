using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;
using System.Threading.Tasks;
using PortForge;

internal static class NativeInterfaceTests
{
    private static int checks;
    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (Control nested in All(child)) yield return nested; }
    }
    private static void Check(bool value, string text) { if (!value) throw new Exception("FAIL: " + text); checks++; Console.WriteLine("PASS: " + text); }
    private static void PumpUntil(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition()) { if (watch.ElapsedMilliseconds > 15000) throw new Exception("UI operation timed out."); Application.DoEvents(); Thread.Sleep(10); }
        Application.DoEvents();
    }
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        int result = 1;
        using (var host = new Form { ShowInTaskbar=false, Opacity=0, StartPosition=FormStartPosition.Manual, Location=new Point(-10000,-10000) })
        {
            host.Shown += delegate { host.BeginInvoke(new Action(delegate { try { result=RunChecks(args); } finally { host.Close(); } })); };
            Application.Run(host);
        }
        return result;
    }
    private static int RunChecks(string[] args)
    {
        try
        {
            PortForge.NativeWindow.StartDoctorOnShow = false;
            using (var form = new PortForge.NativeWindow())
            {
                form.ShowInTaskbar = false;
                form.Opacity = 0;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-10000, -10000);
                form.Show();
                Application.DoEvents();
                var controls = All(form).ToList();
                PumpUntil(() => controls.OfType<Label>().Single(l => l.AccessibleName == "Detected hardware").Text.Contains("CPU:"));
                string hardware = controls.OfType<Label>().Single(l => l.AccessibleName == "Detected hardware").Text;
                Check(hardware.Contains("Graphics:") && hardware.Contains("RAM:") && controls.OfType<CheckBox>().Single(c => c.AccessibleName == "Match games to this PC").Checked && controls.OfType<CheckBox>().Single(c => c.AccessibleName == "Automatically tune new games").Checked,
                    "Startup detects and shows this PC's CPU, graphics and RAM with matching and measurement enabled");
                Check(controls.OfType<Button>().Any(b => b.Text == "Graphics and FPS"), "Graphics controls are directly accessible from the main screen");
                Check(controls.OfType<Button>().Any(b => b.Text == "Downloads"), "Downloads are directly accessible from the main screen");
                var boxes = controls.OfType<TextBox>().ToDictionary(box => box.AccessibleName);
                Func<string, Button> button = label => controls.OfType<Button>().Single(b => b.AccessibleName == label || b.Text == label);
                button("Show extra settings").PerformClick();
                foreach (string label in new[] { "Game file", "Game name", "Save folder", "Conversion engine", "prod.keys file", "title.keys file" })
                {
                    boxes[label].Text = "temporary value";
                    button("Clear " + label.ToLowerInvariant()).PerformClick();
                    Check(boxes[label].Text == "", "Clear button empties only " + label);
                }
                button("Start conversion").PerformClick();
                Check(controls.OfType<Label>().Any(l => l.Text == "ERROR") && controls.OfType<Label>().Any(l => l.Text == "Choose a game file"), "Missing required field appears as a visible error above the log");

                string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "interface-test-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                byte[] fixture = new byte[16];
                System.Text.Encoding.ASCII.GetBytes("PFS0").CopyTo(fixture, 0);
                string game = Path.Combine(root, "encrypted.nsp");
                File.WriteAllBytes(game, fixture);
                boxes["Game file"].Text = game;
                boxes["Game name"].Text = "UI key error";
                boxes["Save folder"].Text = root;
                boxes["Conversion engine"].Text = args[0];
                button("Hide extra settings").PerformClick();
                button("Start conversion").PerformClick();
                PumpUntil(() => button("Start conversion").Enabled);
                Check(controls.OfType<Label>().Any(l => l.Text == "Key file needed") && controls.OfType<Label>().Any(l => l.Text.Contains("Choose your prod.keys file")), "Engine key error is explained in the status card");
                Check(button("Choose prod.keys file").Visible && button("Hide extra settings").Visible, "Key error reveals settings and offers a direct Choose key file button");
                Check(button("Open full log").Enabled && boxes["Technical details"].Text.Contains("ERROR: Key file needed"), "Error state keeps a readable error summary and a full-log action");
                button("Clear details").PerformClick();
                Check(boxes["Technical details"].TextLength == 0 && controls.OfType<Label>().Any(l => l.Text == "ERROR"), "Clearing technical details preserves the error status");
                form.Size = form.MinimumSize;
                Application.DoEvents();
                Check(form.ClientRectangle.Contains(form.PointToClient(button("Start conversion").PointToScreen(Point.Empty))), "Start button stays visible at the minimum window size");
                using (var panel = new Panel())
                {
                    var text = new TextBox { Text = "readable", ForeColor = Color.WhiteSmoke, BackColor = Color.FromArgb(28,34,46) };
                    var label = new Label { Text = "Status", ForeColor = Color.WhiteSmoke };
                    var choice = new ComboBox(); choice.Items.Add("one"); choice.SelectedIndex = 0;
                    var check = new CheckBox { Checked = true }; var off = new Button { Enabled = false };
                    panel.Controls.AddRange(new Control[] { text, label, choice, check, off });
                    using (var inputLock = UiLock.Lock(panel))
                    {
                        Check(panel.Enabled && label.Enabled && choice.Enabled && check.Enabled && text.ReadOnly && text.ForeColor == Color.WhiteSmoke,
                            "Working inputs keep panels, labels and their normal text colors enabled");
                        var key = Message.Create(choice.Handle, 0x100, (IntPtr)Keys.Down, IntPtr.Zero);
                        var click = Message.Create(check.Handle, 0x201, IntPtr.Zero, IntPtr.Zero);
                        Check(inputLock.PreFilterMessage(ref key) && inputLock.PreFilterMessage(ref click), "Working inputs block keyboard and mouse edits without dimming the controls");
                    }
                    Check(!text.ReadOnly && !off.Enabled, "Unlock restores prior editability and keeps previously disabled buttons disabled");
                }
                using (var readable = new ReadableButton { Text = "Working", Enabled = false, Size = new Size(120,34), BackColor = Color.FromArgb(34,38,48) })
                using (var bitmap = new Bitmap(120,34))
                {
                    readable.CreateControl();
                    readable.DrawToBitmap(bitmap, new Rectangle(0,0,120,34));
                    int light = 0; for(int y=0;y<34;y++)for(int x=0;x<120;x++){Color pixel=bitmap.GetPixel(x,y);if(pixel.R>130&&pixel.G>130&&pixel.B>130)light++;}
                    Check(light > 10, "A disabled button actually paints a light readable label on the dark background");
                }
            GameSettingsWindow.IgnoreRemembered = true;
            using (var settingsForm = new GameSettingsWindow(null))
            {
                settingsForm.ShowInTaskbar = false; settingsForm.Opacity = 0; settingsForm.StartPosition = FormStartPosition.Manual; settingsForm.Location = new Point(-10000, -10000); settingsForm.Show(); Application.DoEvents();
                var settingsControls = All(settingsForm).ToList();
                Func<string, Button> settingsButton = label => settingsControls.OfType<Button>().Single(b => b.AccessibleName == label);
                var fpsMode = settingsControls.OfType<ComboBox>().Single(b => b.AccessibleName == "FPS mode");
                var fps = settingsControls.OfType<NumericUpDown>().Single(b => b.AccessibleName == "Custom FPS limit");
                var scale = settingsControls.OfType<NumericUpDown>().Single(b => b.AccessibleName == "Render scale percent");
                var filter = settingsControls.OfType<ComboBox>().Single(b => b.AccessibleName == "Texture filtering");
                Check(fpsMode.SelectedIndex == 0 && !fps.Enabled && fps.Minimum == 10 && fps.Maximum == 1000, "FPS control starts at game timing and exposes the supported custom range");
                fpsMode.SelectedIndex = 1; fps.Value = 240; settingsButton("Performance").PerformClick();
                Check(fps.Enabled && fps.Value == 240 && scale.Value == 75, "Performance preset lowers render work and preserves the custom frame cap");
                settingsButton("Clear FPS limit").PerformClick(); settingsButton("Clear Render scale (%)").PerformClick();
                Check(fpsMode.SelectedIndex == 0 && !fps.Enabled && scale.Value == 100, "Clear restores safe defaults for frame timing and resolution");
                filter.SelectedIndex = 7;
                settingsButton("Clear Texture detail at angles").PerformClick();
                Check(filter.SelectedIndex == 0 && !settingsControls.Any(c=>c.AccessibleName=="Test target FPS"), "Texture filtering resets independently and there is no misleading second FPS target");
                settingsButton("120 FPS").PerformClick();
                Check(fpsMode.SelectedIndex==1&&fps.Value==120&&!settingsControls.OfType<CheckBox>().Single(c=>c.Text=="VSync (reduce tearing)").Checked,"One-click 120 FPS selects the real custom limiter and removes a possible VSync cap");
                Check(settingsControls.OfType<TabControl>().Any(t=>t.TabPages.Cast<TabPage>().Any(p=>p.Text=="Automatic fixes")),"Automatic fixes has its own tab with diagnostics and AI controls");
                var gameBox = settingsControls.OfType<TextBox>().Single(b => b.AccessibleName == "Windows game executable"); gameBox.Text = "old game"; settingsButton("Clear Windows game executable").PerformClick();
                Check(gameBox.TextLength == 0 && !settingsButton("Save settings").Enabled && !settingsButton("Auto tune").Enabled, "Clearing the game invalidates loaded settings and disables save and test");
                settingsButton("Load Windows game executable").PerformClick();
                PumpUntil(() => settingsControls.OfType<Label>().Any(l => l.Text == "ERROR"));
                if (!settingsControls.OfType<Label>().Any(l => l.Text.Contains("Choose the generated game's")))
                    Console.Error.WriteLine("Settings error diagnostics: " + String.Join(" | ",settingsControls.OfType<Label>().Select(l=>l.Text)));
                Check(settingsControls.OfType<Label>().Any(l => l.Text.Contains("Choose the generated game's")), "An invalid executable gets a clear, visible settings error");
                settingsForm.Size = settingsForm.MinimumSize; Application.DoEvents();
                Check(settingsForm.ClientRectangle.Contains(settingsForm.PointToClient(settingsButton("Auto tune").PointToScreen(Point.Empty))) && settingsForm.ClientRectangle.Contains(settingsForm.PointToClient(settingsButton("Save settings").PointToScreen(Point.Empty))), "Settings actions stay visible at minimum size while controls scroll");
                string passiveGame = Path.Combine(root, "background.exe"); File.Copy(args[0], passiveGame);
                string passiveIni = "fps_limit=120\nlog=2\ncompute=0\n"; File.WriteAllText(Path.ChangeExtension(passiveGame,".ini"), passiveIni);
                var binding = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(GameSettingsWindow).GetField("loadedGame", binding).SetValue(settingsForm, passiveGame);
                var passiveCaps = new RuntimeCapabilities(); passiveCaps.Keys.UnionWith(new[] { "log", "compute" });
                typeof(GameSettingsWindow).GetField("capabilities", binding).SetValue(settingsForm, passiveCaps);
                typeof(GameSettingsWindow).GetField("dirty", binding).SetValue(settingsForm, true);
                var refreshHealth = typeof(GameSettingsWindow).GetMethod("RefreshHealthInBackground", binding);
                var background = (Task)refreshHealth.Invoke(settingsForm, null);
                Check(gameBox.Enabled && !gameBox.ReadOnly && fpsMode.Enabled && typeof(GameSettingsWindow).GetField("cancellation", binding).GetValue(settingsForm) == null,
                    "Background check leaves inputs editable and does not reserve the foreground operation");
                PumpUntil(() => background.IsCompleted); background.GetAwaiter().GetResult();
                Check(File.ReadAllText(Path.ChangeExtension(passiveGame,".ini")) == passiveIni && fps.Value == 120 && (bool)typeof(GameSettingsWindow).GetField("dirty", binding).GetValue(settingsForm) && settingsControls.OfType<Label>().Any(l => l.Text == "ERROR"),
                    "Background results preserve unsaved settings, repairable INI values and the foreground status");
                var findings = settingsControls.OfType<TextBox>().Single(b => b.AccessibleName == "Detected problems");
                int repaints = 0; findings.TextChanged += delegate { repaints++; };
                background = (Task)refreshHealth.Invoke(settingsForm, null); PumpUntil(() => background.IsCompleted); background.GetAwaiter().GetResult();
                Check(repaints == 0, "An unchanged background result does not rewrite the results box");
                background = (Task)refreshHealth.Invoke(settingsForm, null);
                settingsForm.Close();
                PumpUntil(() => background.IsCompleted); background.GetAwaiter().GetResult();
                Check(settingsForm.IsDisposed, "Closing the window during a background scan completes without updating disposed controls");
            }
            using (var downloadForm = new DownloadWindow())
            using (var server = new DownloadServer(File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"NativeTest","original-test.nro"))))
            {
                downloadForm.ShowInTaskbar=false; downloadForm.Opacity=0; downloadForm.StartPosition=FormStartPosition.Manual; downloadForm.Location=new Point(-10000,-10000); downloadForm.Show(); Application.DoEvents();
                var downloadControls=All(downloadForm).ToList();
                var downloadBoxes=downloadControls.OfType<TextBox>().ToDictionary(b=>b.AccessibleName);
                Func<string,Button> downloadButton=label=>downloadControls.OfType<Button>().Single(b=>b.AccessibleName==label);
                foreach(string label in new[]{"Direct download link","Download save path","Expected SHA-256"})
                {downloadBoxes[label].Text="temporary";downloadButton("Clear "+label).PerformClick();Check(downloadBoxes[label].TextLength==0,"Download Clear empties only "+label);}
                downloadButton("Download / Resume").PerformClick();
                Check(downloadControls.OfType<Label>().Any(l=>l.Text=="ERROR"),"Invalid download input appears in the persistent error panel");
                var autoConvert=downloadControls.OfType<CheckBox>().Single(c=>c.Text=="Start conversion when the game is ready");
                Check(autoConvert.Checked && downloadControls.OfType<CheckBox>().All(c=>c.Checked),"Parallel downloading, ZIP preparation and conversion handoff start enabled");
                autoConvert.Checked=false;
                string saved=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-download-"+Guid.NewGuid().ToString("N"),"test.nro");
                downloadBoxes["Direct download link"].Text=server.BaseUrl+"/game.nro";downloadBoxes["Download save path"].Text=saved;
                downloadButton("Download / Resume").PerformClick();
                try { PumpUntil(()=>downloadButton("Download / Resume").Enabled); }
                catch { Console.Error.WriteLine("Download UI diagnostics: "+String.Join(" | ",downloadControls.OfType<Label>().Select(l=>l.Text))+" | "+downloadBoxes["Download details"].Text); throw; }
                Check(File.Exists(saved) && File.ReadAllBytes(saved).SequenceEqual(server.Data) && downloadControls.OfType<Label>().Any(l=>l.Text=="DONE"),"The download screen saves an actual HTTP response and shows completion");
                downloadForm.Size=downloadForm.MinimumSize;Application.DoEvents();
                Check(downloadForm.ClientRectangle.Contains(downloadForm.PointToClient(downloadButton("Download / Resume").PointToScreen(Point.Empty))) && downloadForm.ClientRectangle.Contains(downloadForm.PointToClient(downloadButton("Use selected game").PointToScreen(Point.Empty))),"Download actions remain visible at minimum window size");
                downloadButton("Use selected game").PerformClick();
                Check(downloadForm.SelectedInput==saved && !downloadForm.ConvertWhenReady,"The verified download is handed back to conversion with the player's automation choice");
            }
                form.Close();
            }
            Console.WriteLine(checks + " interface checks passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
