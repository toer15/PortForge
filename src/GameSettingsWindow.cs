using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PortForge
{
    public sealed class GameSettingsWindow : Form
    {
        protected override CreateParams CreateParams { get { return SmoothPaint.Composited(base.CreateParams); } }
        private static readonly Color Bg = Color.FromArgb(17, 21, 30), Surface = Color.FromArgb(28, 34, 46), Mint = Color.FromArgb(105, 226, 183), Muted = Color.FromArgb(173, 185, 203);
        private readonly TextBox game = new TextBox { AccessibleName = "Windows game executable", Dock = DockStyle.Fill };
        private readonly ComboBox fpsMode = Choice("FPS mode", "Game speed (recommended)", "Custom limit", "Unlimited"), filtering = Filtering();
        private readonly NumericUpDown fps = Number("Custom FPS limit", 10, 1000, 120, 10), scale = Number("Render scale percent", 25, 400, 100, 25);
        private readonly CheckBox vsync = Toggle("VSync (reduce tearing)", true), shadows = Toggle("Draw shadows", true), docked = Toggle("TV mode (off = handheld)", true), fullscreen = Toggle("Start fullscreen", false);
        private readonly Label badge = Label("READY", 9, Mint), status = Label("Choose a Windows game", 16, Color.White), message = Label("Browse to a generated game .exe, then load its settings.", 10, Muted);
        private readonly TextBox details = new TextBox { AccessibleName = "Tuning results", Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        private readonly Button save = Button("Save settings", true), tune = Button("Auto tune", true), launch = Button("Play game", false), stop = Button("Cancel test", false), report = Button("Open test report", false);
        private readonly TableLayoutPanel options = Stack();
        private readonly FlowLayoutPanel fileButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        private RuntimeCapabilities capabilities;
        private string loadedGame, reportPath;
        private CancellationTokenSource cancellation;
        private bool showingProfile,dirty;
        private readonly Label limiter=Label("Choose a game to check its saved FPS limit.",10,Mint);
        private readonly CheckBox watch=Toggle("Detect problems automatically while this window is open",true),autoRepair=Toggle("Apply supported repairs when the game is closed (may lower render scale)",true),autoAi=Toggle("Use Codex AI for new recorded problems (uses your signed-in account)",true);
        private readonly TextBox findings=new TextBox {AccessibleName="Detected problems",Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Top,Height=170,BackColor=Surface,ForeColor=Color.WhiteSmoke},history=new TextBox {AccessibleName="Repair and AI history",Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Top,Height=150,BackColor=Surface,ForeColor=Color.WhiteSmoke};
        private readonly Button scan=Button("Scan and fix",true),askAi=Button("Ask AI now",false),undo=Button("Undo last repair",false);
        private readonly TableLayoutPanel fixes=Stack();
        private readonly System.Windows.Forms.Timer healthTimer=new System.Windows.Forms.Timer {Interval=5000};
        private readonly string backendPath,reportsRoot;
        private readonly EnginePanel enginePanel=new EnginePanel();
        private string lastHealth;
        private string lastAiAttempt;
        private DateTime lastAiTime;
        private string displayedAi;
        private bool checkingInBackground;
        private UiLock inputLock;
        private readonly Label backgroundStatus = Label("Background checks run without interrupting your work.", 9, Muted);

        private static string MemoryFile(){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PortForge","last-game.txt");}
        public static void RememberGame(string exe)
        {
            foreach(string file in new[]{MemoryFile(),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"last-game.txt")})
            { try{Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,exe);}catch(IOException){}catch(UnauthorizedAccessException){} }
        }
        // The last game the player used, so the window is never empty after an update or restart.
        public static bool IgnoreRemembered;
        public static string RememberedGame()
        {
            if(IgnoreRemembered)return null;
            foreach(string file in new[]{MemoryFile(),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"last-game.txt")})
            { try{if(File.Exists(file)){string path=File.ReadAllText(file).Trim();if(File.Exists(path))return path;}}catch(IOException){}catch(UnauthorizedAccessException){} }
            try
            {
                string list=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"watched-games.txt");
                if(File.Exists(list))foreach(string line in File.ReadAllLines(list).Reverse()){string path=line.Trim();if(path.Length>0&&File.Exists(path))return path;}
            }catch(IOException){}catch(UnauthorizedAccessException){}
            return null;
        }
        public GameSettingsWindow(string initialGame,string backendFile=null)
        {
            backendPath=backendFile;
            if(!File.Exists(backendPath)&&File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Engine","scarletnx.exe")))backendPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Engine","scarletnx.exe");
            if(!File.Exists(backendPath)) {try {string locator=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"backend-location.txt");if(File.Exists(locator))backendPath=File.ReadAllText(locator).Trim();}catch(IOException){}}
            reportsRoot=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Diagnostics");
            Text = "PortForge | Graphics and performance"; AppIcon.Apply(this);
            ClientSize = new Size(880, 810); MinimumSize = new Size(810, 700); BackColor = Bg; ForeColor = Color.WhiteSmoke;
            Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(22) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            root.Controls.Add(Label("Graphics and performance", 22, Color.White), 0, 0);
            var file = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
            file.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); file.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 252));
            file.Controls.Add(Label("Windows game (.exe)", 10, Muted), 0, 0);
            file.Controls.Add(game, 0, 1); file.Controls.Add(fileButtons, 1, 1);
            foreach (string text in new[] {"Browse", "Clear", "Load"})
            {
                var b = Button(text, false); b.Size = new Size(76, 30); b.AccessibleName = text + " Windows game executable";
                if (text == "Browse") b.Click += async delegate { using (var picker = new OpenFileDialog { Filter = "Windows game|*.exe", CheckFileExists = true }) if (picker.ShowDialog(this) == DialogResult.OK) { game.Text = picker.FileName; await LoadGame(); } };
                if (text == "Clear") b.Click += delegate { game.Clear(); enginePanel.SetGame(null); };
                if (text == "Load") b.Click += async delegate { await LoadGame(); };
                fileButtons.Controls.Add(b);
            }
            root.Controls.Add(file, 0, 1);
            var tabs=new TabControl {Dock=DockStyle.Fill,AccessibleName="Game settings tabs"};
            var graphicsTab=new TabPage("Graphics and FPS") {BackColor=Bg,ForeColor=Color.WhiteSmoke};
            var fixesTab=new TabPage("Automatic fixes") {BackColor=Bg,ForeColor=Color.WhiteSmoke};
            var engineTab=new TabPage("Engine") {BackColor=Bg,ForeColor=Color.WhiteSmoke};engineTab.Controls.Add(enginePanel);
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(0, 8, 0, 0) };
            scroll.Controls.Add(options); graphicsTab.Controls.Add(scroll);tabs.TabPages.Add(graphicsTab);tabs.TabPages.Add(fixesTab);tabs.TabPages.Add(engineTab);root.Controls.Add(tabs,0,2);
            var fixesScroll=new Panel {Dock=DockStyle.Fill,AutoScroll=true};fixesScroll.Controls.Add(fixes);fixesTab.Controls.Add(fixesScroll);fixes.Padding=new Padding(10,12,22,8);
            fixes.Controls.Add(Label("Automatic checks and supported repairs",13,Mint));
            backgroundStatus.AccessibleName = "Background check status"; fixes.Controls.Add(backgroundStatus);
            foreach(var check in new[]{watch,autoRepair,autoAi})fixes.Controls.Add(check);
            var explanation=Label("Checks settings, missing runtime files and known renderer warnings, and tells shader-compile stalls from loading freezes. The stutter repair gives the engine more pipeline threads and pre-builds the shader cache by playing the opening of the game from a backed-up save. Repairs are backed up and verified. AI uses the installed Codex coding assistant and sends a small diagnostic summary; it cannot guarantee fixes for every world bug. Game files, saves and caches are kept.",9,Muted);
            fixes.SizeChanged+=delegate{explanation.MaximumSize=new Size(Math.Max(200,fixes.ClientSize.Width-34),0);};fixes.Controls.Add(explanation);
            var fixActions=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true};foreach(var button in new[]{scan,askAi,undo}){button.Size=new Size(160,32);fixActions.Controls.Add(button);}fixes.Controls.Add(fixActions);
            fixes.Controls.Add(Label("Detected problems",11,Mint));fixes.Controls.Add(findings);fixes.Controls.Add(Label("Repairs and AI engineering notes",11,Mint));fixes.Controls.Add(history);
            scan.Click+=async delegate{await CheckHealth(false);};askAi.Click+=async delegate{await CheckHealth(true);};
            undo.Click+=delegate{try{autoRepair.Checked=false;GameDiagnostics.RestoreLastRepair(loadedGame);ShowProfile(GameOptimization.Load(loadedGame));history.AppendText("Restored the last repair backup. Automatic repairs are switched off so the undo is kept.\r\n");SetStatus("DONE","Previous settings restored","Review the settings before starting the game.",Mint);}catch(Exception e){Error(e);}};
            options.Padding = new Padding(0, 0, 18, 0);
            options.Controls.Add(Label("Choose a starting point", 12, Mint));
            var presets = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            foreach (string text in new[] {"Match my PC", "Performance", "Balanced", "Quality", "Reset defaults"})
            {
                var b = Button(text, false); b.AutoSize = true; b.Height = 32;
                string preset = text;
                b.Click += delegate { try { ShowProfile(preset == "Reset defaults" ? new GraphicsProfile() : preset == "Match my PC" ? HardwareProfile.Detect().Recommended(ReadProfile()) : GraphicsProfile.Preset(preset, ReadProfile())); SetStatus("READY", "Settings changed", "Select Save settings to apply them next time you start the game.", Mint); } catch (Exception e) { Error(e); } };
                presets.Controls.Add(b);
            }
            options.Controls.Add(presets); var pcLine = Label("Checking this PC...", 9, Muted); pcLine.MaximumSize = new Size(700, 0); options.Controls.Add(pcLine); Shown += async delegate { var pc = await Task.Run(() => HardwareProfile.Detect()); pcLine.Text = "Your PC: " + pc.TierName + " (" + pc.Summary + "). " + pc.Why + " " + pc.Estimate + " Match my PC applies the settings this class of PC can afford; Auto tune measures the real game."; };
            var fpsRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
            fpsMode.Width = 235; fps.Width = 95; fpsRow.Controls.AddRange(new Control[] { fpsMode, fps });
            AddOption("FPS limit", fpsRow, delegate { fpsMode.SelectedIndex = 0; fps.Value = 120; });
            fpsMode.SelectedIndexChanged += delegate { fps.Visible=fps.Enabled = fpsMode.SelectedIndex == 1; Changed(); };
            fps.ValueChanged+=delegate{Changed();};
            var framePresets=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true};
            foreach(int value in new[]{30,60,120,144,165,240})
            {int cap=value;var button=Button(value+" FPS",false);button.AutoSize=true;button.Height=30;button.Click+=delegate{fpsMode.SelectedIndex=1;fps.Value=cap;vsync.Checked=false;Changed();};framePresets.Controls.Add(button);}
            options.Controls.Add(framePresets);options.Controls.Add(limiter);
            AddHint("Click an FPS button to set a custom cap and turn VSync off. Play game saves and verifies these settings automatically. Game speed uses the game's own timing; the hidden custom number is not active in that mode.");
            AddHint("A cap is a maximum. Higher FPS can change game speed or physics; some games need a game-specific patch. VSync may also limit FPS to your display's refresh rate.");
            AddOption("Render scale (%)", scale, delegate { scale.Value = 100; });
            AddHint("100% = the game's original resolution. Lower values reduce GPU work; higher values sharpen the picture and cost performance.");
            AddOption("Texture detail at angles", filtering, delegate { filtering.SelectedIndex = 0; });
            var checks = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
            checks.Controls.AddRange(new Control[] { vsync, shadows, docked, fullscreen }); options.Controls.Add(checks);
            options.Controls.Add(Label("Automatic tuning", 12, Mint));
            AddHint("Auto tune uses the custom FPS limit above as its target (60 in Game speed or Unlimited mode). It plays the first minute of the game with scripted button presses (A) from a backed-up save, so it measures real gameplay and loading. Later areas can be heavier. The Automatic fixes tab checks logs and provides Codex AI diagnosis.");
            details.Height = 105; details.MinimumSize = new Size(0, 100); details.BackColor = Surface; details.ForeColor = Color.WhiteSmoke; options.Controls.Add(details);
            report.AutoSize = true; report.Enabled = false; report.Click += delegate { if (File.Exists(reportPath)) Process.Start("notepad.exe", GameManifest.Quote(reportPath)); };
            options.Controls.Add(report);
            var card = Stack(); card.Dock = DockStyle.Fill; card.BackColor = Surface; card.Padding = new Padding(12); card.Margin = new Padding(0, 8, 0, 0);
            card.Controls.Add(badge); card.Controls.Add(status); card.Controls.Add(message);
            card.SizeChanged += delegate { message.MaximumSize = new Size(Math.Max(200, card.ClientSize.Width - 30), 0); };
            root.Controls.Add(card, 0, 3);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
            foreach (var b in new[] {save, tune, launch, stop}) { b.Size = new Size(145, 34); actions.Controls.Add(b); }
            save.Enabled = tune.Enabled = launch.Enabled = stop.Enabled = false;
            save.Click += delegate { try { GameOptimization.SaveAndVerify(loadedGame, ReadProfile(), capabilities);ShowProfile(GameOptimization.Load(loadedGame)); SetStatus("DONE", "Settings saved and verified", limiter.Text, Mint); } catch (Exception e) { Error(e); } };
            tune.Click += async delegate { await Tune(); };
            launch.Click += async delegate { await Play(); };
            stop.Click += delegate { if (cancellation != null) { cancellation.Cancel(); stop.Enabled = false; SetStatus("STOPPING", "Cancelling test", "Stopping the test process and restoring your original settings.", Muted); } };
            root.Controls.Add(actions, 0, 4); Controls.Add(root);
            game.TextChanged += delegate { capabilities = null; loadedGame = null;dirty=false;lastHealth=displayedAi=null;findings.Clear();history.Clear();save.Enabled = tune.Enabled = launch.Enabled = scan.Enabled=askAi.Enabled=undo.Enabled=false; SetStatus("READY", "Load the game settings", "Select Load or Browse to check this game's engine.", Muted); };
            game.Text = (initialGame != null && File.Exists(initialGame)) ? initialGame : (RememberedGame() ?? "");
            ShowProfile(new GraphicsProfile());
            scale.ValueChanged+=delegate{Changed();};filtering.SelectedIndexChanged+=delegate{Changed();};foreach(var check in new[]{vsync,shadows,docked,fullscreen})check.CheckedChanged+=delegate{Changed();};
            Shown += async delegate { if (File.Exists(game.Text)) await LoadGame(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (cancellation != null) { e.Cancel = true; cancellation.Cancel(); } };
            healthTimer.Tick+=async delegate{if(Visible&&watch.Checked)await RefreshHealthInBackground();};healthTimer.Start();
        }
        private async Task LoadGame()
        {
            if (cancellation != null) return;
            string exe = game.Text.Trim();
            try { if(!File.Exists(exe))throw new ArgumentException("Choose the generated game's .exe file.");NativeConversion.RequireNativeX64(exe); } catch (Exception e) { Error(e); return; }
            cancellation = new CancellationTokenSource();
            loadedGame=Path.GetFullPath(exe);
            Busy(true); SetStatus("WORKING", "Checking game settings", "Reading the controls supported by this game's engine.", Muted);
            try
            {
                var token = cancellation.Token;
                var initial=await Task.Run(()=>GameDiagnostics.Scan(exe,null,backendPath));
                findings.Text=initial.Text();
                if(autoRepair.Checked&&!initial.Running&&initial.Issues.Any(i=>i.Fix=="restore_runtime"))history.AppendText(await Task.Run(()=>GameDiagnostics.Repair(exe,null,backendPath,new[]{"restore_runtime"}))+Environment.NewLine);
                capabilities = await Task.Run(() => RuntimeCapabilities.Read(exe, token));
                ShowProfile(GameOptimization.Load(exe)); loadedGame = Path.GetFullPath(exe);
                RememberGame(loadedGame);
                if(LiveDoctor.Current!=null)LiveDoctor.Current.Watch(loadedGame);
                enginePanel.SetGame(loadedGame);
                SetStatus("READY", "Ready to tune", "Choose an FPS preset above. Play game saves and verifies your selected limit. Automatic fixes checks the recorded log.", Mint);
            }
            catch (OperationCanceledException) { SetStatus("CANCELLED", "Check cancelled", "No settings were changed.", Muted); }
            catch (Exception e) { capabilities = null; Error(e); }
            finally { cancellation.Dispose(); cancellation = null; Busy(false); }
            if(loadedGame!=null&&capabilities!=null)await RefreshHealthInBackground();
        }
        private async Task Tune()
        {
            if (loadedGame == null || cancellation != null) return;
            GraphicsProfile desired;
            try { desired = ReadProfile(); GameOptimization.EnsureStopped(loadedGame); } catch (Exception e) { Error(e); return; }
            cancellation = new CancellationTokenSource(); Busy(true); details.Clear(); report.Enabled = false;
            SetStatus("WORKING", "Measuring performance", "Testing temporary profiles. Your original settings are restored when the test ends.", Muted);
            try
            {
                string exe = loadedGame; int goal; if(!Int32.TryParse(desired.FpsLimit,out goal)||goal==0)goal=60; var token = cancellation.Token;
                var updates = new Progress<string>(line => { if (cancellation == null || token.IsCancellationRequested) return; details.AppendText(line + Environment.NewLine); message.Text = line; });
                var result = await Task.Run(() => GameOptimization.Tune(exe, desired, goal, updates, token));
                ShowProfile(result.Profile); reportPath = result.ReportPath; report.Enabled = File.Exists(reportPath);
                details.AppendText(result.Summary + Environment.NewLine);
                SetStatus("DONE", "Tuning recommendation ready", "The suggested graphics are shown above. Select Save settings to use them. See Tuning results for measurements and limits.", Mint);
            }
            catch (OperationCanceledException) { SetStatus("CANCELLED", "Test cancelled", "Your original settings have been restored. No recommendation was saved.", Muted); }
            catch (Exception e) { Error(e); }
            finally { cancellation.Dispose(); cancellation = null; Busy(false); }
        }
        private void Busy(bool busy)
        {
            if (busy && inputLock == null) inputLock = UiLock.Lock(this);
            if (!busy && inputLock != null) { inputLock.Unlock(); inputLock = null; }
            save.Enabled = tune.Enabled = launch.Enabled = !busy && loadedGame != null && capabilities != null;
            scan.Enabled=undo.Enabled=!busy&&loadedGame!=null;askAi.Enabled=!busy&&loadedGame!=null&&CodexDiagnosis.Find()!=null;
            stop.Enabled = busy;
        }
        // The timer only reads. The main doctor's worker owns automatic repairs and optional AI reviews.
        // Never reserve the foreground operation, reload settings, or repaint its status card for a timer tick.
        private async Task RefreshHealthInBackground()
        {
            if (checkingInBackground || loadedGame == null || cancellation != null || enginePanel.IsWorking) return;
            string exe = loadedGame; var cap = capabilities;
            checkingInBackground = true;
            try
            {
                var health = await Task.Run(() => GameDiagnostics.Scan(exe, cap, backendPath));
                if (IsDisposed || Disposing || loadedGame != exe || cancellation != null || enginePanel.IsWorking) return;
                string text = health.Text();
                if (lastHealth != text)
                {
                    int start = findings.SelectionStart, length = findings.SelectionLength;
                    findings.Text = lastHealth = text;
                    findings.Select(Math.Min(start, findings.TextLength), Math.Min(length, Math.Max(0, findings.TextLength - start)));
                }
                string summary = "Background check: " + health.Summary;
                if (backgroundStatus.Text != summary) backgroundStatus.Text = summary;
            }
            catch (Exception e)
            {
                if (!IsDisposed && !Disposing && loadedGame == exe && cancellation == null)
                {
                    string text = "Background check: " + e.Message;
                    if (backgroundStatus.Text != text) backgroundStatus.Text = text;
                }
            }
            finally { checkingInBackground = false; }
        }
        private void Changed()
        {
            if(showingProfile)return;
            dirty=true;UpdateLimiter();
        }
        private void UpdateLimiter()
        {
            if(loadedGame==null){limiter.Text="Load a game to check and change its FPS limit.";return;}
            string value=fpsMode.SelectedIndex==0?"GAME SPEED (game's own timing)":fpsMode.SelectedIndex==2?"UNLIMITED":fps.Value.ToString("0")+" FPS maximum";
            limiter.Text=(dirty?"Selected limit: ":"Saved limit: ")+value+"; VSync "+(vsync.Checked?"on":"off")+". "+(dirty?"Play saves this automatically.":"Verified settings are used on next start.");
            limiter.MaximumSize=new Size(Math.Max(200,options.ClientSize.Width-24),0);
        }
        private async Task Play()
        {
            if(cancellation!=null||loadedGame==null||capabilities==null)return;
            GraphicsProfile desired=ReadProfile();cancellation=new CancellationTokenSource();Busy(true);
            try
            {
                GameOptimization.EnsureStopped(loadedGame);
                await Task.Run(()=>GameOptimization.SaveAndVerify(loadedGame,desired,capabilities));
                if(autoRepair.Checked)history.AppendText(await Task.Run(()=>GameDiagnostics.Repair(loadedGame,capabilities,backendPath))+Environment.NewLine);
                ShowProfile(GameOptimization.Load(loadedGame));
                cancellation.Token.ThrowIfCancellationRequested();
                using(var process=GameOptimization.SaveAndStart(loadedGame,ReadProfile(),capabilities)){}
                SetStatus("PLAYING","Game started",limiter.Text+" Automatic checks continue while this window is open.",Mint);
            }
            catch(OperationCanceledException){SetStatus("CANCELLED","Game start cancelled","Any verified settings changes are kept; the game was not launched.",Muted);}
            catch(Exception e){Error(e);}
            finally{cancellation.Dispose();cancellation=null;Busy(false);}
        }
        private async Task CheckHealth(bool forceAi,bool quiet=false)
        {
            if (quiet) { await RefreshHealthInBackground(); return; }
            if(loadedGame==null||cancellation!=null)return;
            cancellation=new CancellationTokenSource();var token=cancellation.Token;if(!quiet)Busy(true);
            var repairProgress=new Progress<string>(m=>SetStatus("WORKING","Applying supported repairs",m,Muted));
            try
            {
                var health=await Task.Run(()=>GameDiagnostics.Scan(loadedGame,capabilities,backendPath));
                if(autoRepair.Checked&&!dirty&&!health.Running&&health.Issues.Any(i=>i.Fix!=null))
                {
                    Busy(true);SetStatus("WORKING","Applying supported repairs","Backing up settings and verifying each supported change.",Muted);
                    history.AppendText(await Task.Run(()=>GameDiagnostics.Repair(loadedGame,capabilities,backendPath,null,true,repairProgress,token))+Environment.NewLine);
                    if(capabilities!=null)ShowProfile(GameOptimization.Load(loadedGame));
                    health=await Task.Run(()=>GameDiagnostics.Scan(loadedGame,capabilities,backendPath));
                }
                string signature=CodexDiagnosis.Fingerprint(health),cache=signature==null?null:Path.Combine(reportsRoot,"last-ai-"+signature+".json");
                if(!forceAi&&File.Exists(cache)&&displayedAi!=cache)
                {var saved=CodexDiagnosis.Parse(File.ReadAllText(cache),health);history.AppendText("PREVIOUS AI DIAGNOSIS (verify in gameplay): "+saved.summary+"\r\nENGINEERING NOTE: "+saved.engineering_note+Environment.NewLine);displayedAi=cache;}
                bool aiEligible=forceAi||(autoAi.Checked&&!dirty&&!health.Running&&signature!=null&&!File.Exists(cache)&&(lastAiAttempt!=signature||(DateTime.UtcNow-lastAiTime).TotalMinutes>10));
                if(aiEligible&&CodexDiagnosis.Find()!=null)
                {
                    Busy(true);lastAiAttempt=signature;lastAiTime=DateTime.UtcNow;
                    SetStatus("WORKING","AI is reviewing the evidence","Codex is diagnosing the recorded problems. Supported fixes are validated locally; engine bugs may still need a code fix.",Muted);
                    var result=await Task.Run(()=>CodexDiagnosis.Review(health,reportsRoot,token));
                    history.AppendText("\r\nAI DIAGNOSIS (verify in gameplay): "+result.summary+"\r\nENGINEERING NOTE: "+result.engineering_note+Environment.NewLine);
                    if(cache!=null){File.Copy(result.ReportPath,cache,true);displayedAi=cache;}
                    if(autoRepair.Checked&&!dirty&&!GameDiagnostics.Running(loadedGame)&&result.recommended_rules.Length>0)
                    {history.AppendText(await Task.Run(()=>GameDiagnostics.Repair(loadedGame,capabilities,backendPath,result.recommended_rules,true,repairProgress,token))+Environment.NewLine);if(capabilities!=null)ShowProfile(GameOptimization.Load(loadedGame));}
                    health=await Task.Run(()=>GameDiagnostics.Scan(loadedGame,capabilities,backendPath));
                }
                bool changed=lastHealth!=health.Text();if(changed)findings.Text=lastHealth=health.Text();
                if(!quiet||changed)
                {
                    bool engine=health.Issues.Any(i=>new[]{"occlusion","renderer_failure","runtime_failure"}.Contains(i.Id));
                    SetStatus(engine?"NEEDS ATTENTION":health.Running?"MONITORING":"CHECKED",engine?"Engine issue detected":health.Running?"Watching the recorded log":"Checks complete",health.Summary+(health.Running&&health.Issues.Any(i=>i.Fix!=null)?" Repairs wait until you close the game.":""),engine?Color.FromArgb(255,199,120):Mint);
                }
                if(history.TextLength>60000)history.Text=history.Text.Substring(history.TextLength-30000);
            }
            catch(OperationCanceledException){SetStatus("CANCELLED","Checks stopped","Any already verified repairs are kept. Your running game was left alone.",Muted);}
            catch(Exception e){history.AppendText(e.Message+Environment.NewLine);Error(e);}
            finally{cancellation.Dispose();cancellation=null;Busy(false);}
        }
        private GraphicsProfile ReadProfile()
        {
            return new GraphicsProfile { FpsLimit = fpsMode.SelectedIndex == 0 ? "game" : fpsMode.SelectedIndex == 2 ? "0" : ((int)fps.Value).ToString(System.Globalization.CultureInfo.InvariantCulture), Resolution = scale.Value / 100, Anisotropy = filtering.SelectedIndex, VSync = vsync.Checked, Shadows = shadows.Checked, Docked = docked.Checked, Fullscreen = fullscreen.Checked };
        }
        private void ShowProfile(GraphicsProfile p)
        {
            showingProfile=true;
            fpsMode.SelectedIndex = p.FpsLimit == "game" ? 0 : p.FpsLimit == "0" ? 2 : 1;
            if (fpsMode.SelectedIndex == 1) fps.Value = Int32.Parse(p.FpsLimit);
            fps.Visible=fps.Enabled = fpsMode.SelectedIndex == 1; scale.Value = p.Resolution * 100;
            filtering.SelectedIndex = p.Anisotropy;
            vsync.Checked = p.VSync; shadows.Checked = p.Shadows; docked.Checked = p.Docked; fullscreen.Checked = p.Fullscreen;
            showingProfile=false;dirty=false;UpdateLimiter();
        }
        private void AddOption(string text, Control control, Action reset)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 5, 0, 5) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
            row.Controls.Add(Label(text, 10, Color.WhiteSmoke), 0, 0); control.Dock = DockStyle.Top; row.Controls.Add(control, 1, 0);
            var clear = Button("Clear", false); clear.AccessibleName = "Clear " + text; clear.Size = new Size(62, 30); clear.Click += delegate { reset(); }; row.Controls.Add(clear, 2, 0); options.Controls.Add(row);
        }
        private void AddHint(string text)
        {
            var hint = Label(text, 9, Muted); hint.Margin = new Padding(0, 0, 0, 10);
            options.SizeChanged += delegate { hint.MaximumSize = new Size(Math.Max(200, options.ClientSize.Width - 24), 0); }; options.Controls.Add(hint);
        }
        private void SetStatus(string state, string title, string text, Color color) { badge.Text = state; status.Text = title; message.Text = text; badge.ForeColor = status.ForeColor = color; }
        private void Error(Exception e) { SetStatus("ERROR", "Could not finish", e.Message, Color.FromArgb(255, 157, 148)); }
        private static NumericUpDown Number(string label, int min, int max, int value, int step) { return new NumericUpDown { AccessibleName = label, Minimum = min, Maximum = max, Value = value, Increment = step, Width = 110 }; }
        private static ComboBox Choice(string label, params string[] values) { var box = new ComboBox { AccessibleName = label, DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 }; box.Items.AddRange(values); box.SelectedIndex = 0; return box; }
        private static ComboBox Filtering() { var values = new string[17]; values[0] = "Game default"; for (int i = 1; i <= 16; i++) values[i] = i + "x"; return Choice("Texture filtering", values); }
        private static CheckBox Toggle(string text, bool value) { return new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(0, 6, 22, 6) }; }
        private static Label Label(string text, float size, Color color) { return new Label { Text = text, Font = new Font("Segoe UI", size), ForeColor = color, AutoSize = true, Margin = new Padding(0, 0, 0, 5) }; }
        private static TableLayoutPanel Stack() { return new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = new Padding(0) }; }
        private static Button Button(string text, bool primary) { var b = new ReadableButton { Text = text, AccessibleName = text, FlatStyle = FlatStyle.Flat, BackColor = primary ? Mint : Surface, ForeColor = primary ? Bg : Color.WhiteSmoke, Cursor = Cursors.Hand }; b.EnabledChanged += delegate { b.BackColor = b.Enabled ? (primary ? Mint : Surface) : Color.FromArgb(34, 38, 48); b.ForeColor = b.Enabled ? (primary ? Bg : Color.WhiteSmoke) : Color.FromArgb(120, 126, 140); }; b.FlatAppearance.BorderSize = 0; return b; }
        protected override void Dispose(bool disposing){if(disposing){healthTimer.Stop();healthTimer.Dispose();if(inputLock!=null){inputLock.Unlock();inputLock=null;}}base.Dispose(disposing);}
    }
}
