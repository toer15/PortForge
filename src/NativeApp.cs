using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PortForge
{
    internal static class NativeProgram
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new NativeWindow());
        }
    }

    public sealed class NativeWindow : Form
    {
        protected override CreateParams CreateParams { get { return SmoothPaint.Composited(base.CreateParams); } }
        private static readonly Color Background = Color.FromArgb(17, 21, 30), Surface = Color.FromArgb(28, 34, 46),
            Muted = Color.FromArgb(173, 185, 203), Mint = Color.FromArgb(105, 226, 183), Red = Color.FromArgb(255, 157, 148), Blue = Color.FromArgb(140, 187, 255);
        private readonly TextBox name = Box("Game name"), input = Box("Game file"), backend = Box("Conversion engine"),
            output = Box("Save folder"), keys = Box("prod.keys file"), titleKeys = Box("title.keys file"), log = Box("Technical details");
        private readonly Button convert = Button("Start conversion", true), cancel = Button("Cancel", false),
            statusAction = Button("Open game folder", true), extraToggle = Button("Extra settings  +", false),
            sample = Button("Try a test file", false), openLog = Button("Open full log", false), copyLog = Button("Copy details", false), clearLog = Button("Clear details", false);
        private readonly Label statusBadge = new Label(), statusTitle = new Label(), statusMessage = new Label();
        private readonly Panel statusCard = new Panel(), leftScroll = new Panel();
        private readonly TableLayoutPanel fields = Stack(), extraFields = Stack();
        private UiLock fieldLock;
        private readonly ProgressBar progress = new ProgressBar();
        private readonly ToolTip tips = new ToolTip();
        private TabControl mainTabs;
        private readonly Button downloads = Button("Downloads", false), updates = Button("Check for updates", false), doctorButton = Button("AI Doctor", false);
        private readonly Label doctorStatus = new Label();
        private readonly Label hardwareInfo = Caption("Checking this PC in the background...", 9, Muted, false);
        private HardwareProfile detectedHardware;
        private readonly CheckBox doctorAuto = new CheckBox { Text = "Fix automatically", Checked = true, AutoSize = true, AccessibleName = "AI Doctor fixes automatically" },
            doctorAi = new CheckBox { Text = "Extra AI review (Codex)", Checked = true, AutoSize = true, AccessibleName = "AI Doctor extra review with Codex" };
        private readonly System.Collections.Generic.List<DoctorEvent> doctorEvents = new System.Collections.Generic.List<DoctorEvent>();
        private readonly NotifyIcon tray = new NotifyIcon();
        private LiveDoctor doctor;
        private CloudSaveMonitor cloudMonitor;
        private readonly System.Windows.Forms.Timer doctorUiTimer = new System.Windows.Forms.Timer { Interval = 500 };
        private DoctorEvent pendingDoctorEvent, pendingDoctorNotice;
        private DateTime lastDoctorNotice = DateTime.MinValue;
        /// <summary>Tests turn this off so a test window never watches or repairs real games.</summary>
        public static bool StartDoctorOnShow = true;
        private readonly CheckBox matchHardware = new CheckBox { Text = "Match new games to this PC", Checked = true, AutoSize = true, AccessibleName = "Match games to this PC", Margin = new Padding(0, 0, 0, 6) };
        private readonly CheckBox autoTune = new CheckBox { Text = "Measure game settings (takes a few minutes)", Checked = true, AutoSize = true, AccessibleName = "Automatically tune new games", Margin = new Padding(0, 0, 0, 15) };
        private CancellationTokenSource cancellation;
        private string result, lastLogPath, actionField, recoveryFolder;
        private bool initializing = true;
        private int operation;

        public NativeWindow()
        {
            Text = "PortForge 0.36 | Switch to PC"; AppIcon.Apply(this);
            ClientSize = new Size(1120, 750);
            MinimumSize = new Size(1040, 720);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Background;
            ForeColor = Color.WhiteSmoke;
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(26, 20, 26, 16) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            var header = Stack();
            header.Controls.Add(Caption("PortForge", 26, Color.White, true));
            header.Controls.Add(Caption("Create Windows game files from your Switch game.", 11, Muted, false));
            var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 600));
            heading.Controls.Add(header, 0, 0);
            var headerActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            downloads.AccessibleName = "Downloads"; downloads.Size = new Size(120, 40);
            downloads.Click += async delegate
            {
                using (var window = new DownloadWindow())
                if (window.ShowDialog(this) == DialogResult.OK)
                {
                    input.Text = window.SelectedInput;
                    string suggested = Path.GetFileNameWithoutExtension(input.Text);
                    foreach (char invalid in Path.GetInvalidFileNameChars()) suggested = suggested.Replace(invalid, '_');
                    suggested = suggested.Trim().TrimEnd('.');
                    if (suggested.Length == 0) suggested = "Downloaded Game";
                    if (System.Text.RegularExpressions.Regex.IsMatch(suggested.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) suggested = "Game " + suggested;
                    name.Text = suggested.Length > 60 ? suggested.Substring(0,60) : suggested;
                    if (Path.IsPathRooted(output.Text))
                    {
                        string originalName = name.Text; int number = 2;
                        while (Directory.Exists(Path.Combine(output.Text,name.Text)) || File.Exists(Path.Combine(output.Text,name.Text))) name.Text = originalName.Substring(0,Math.Min(52,originalName.Length)) + " (" + number++ + ")";
                    }
                    if (window.ConvertWhenReady && cancellation == null) await Compile();
                }
            };
            updates.AccessibleName = "Check for updates"; updates.Size = new Size(170, 40);
            updates.Click += delegate
            {
                using (var window = new UpdateWindow(cancellation != null))
                    if (window.ShowDialog(this) == DialogResult.Yes && window.InstallStarted) Close();
            };
            var cloud=Button("Cloud saves",false);cloud.Size=new Size(130,40);cloud.Click+=delegate{string exe=Directory.Exists(result)?Path.Combine(result,Path.GetFileName(result)+".exe"):GameSettingsWindow.RememberedGame();using(var window=new CloudSaveWindow(exe))window.ShowDialog(this);};
            var controllers = Button("Controllers", false); controllers.AccessibleName = "Controllers"; controllers.Size = new Size(130, 40);
            controllers.Click += delegate { using (var window = new ControllerWindow()) window.ShowDialog(this); };
            headerActions.Controls.Add(downloads); headerActions.Controls.Add(cloud); headerActions.Controls.Add(controllers); headerActions.Controls.Add(updates);
            heading.Controls.Add(headerActions, 1, 0); root.Controls.Add(heading, 0, 0);
            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            root.Controls.Add(columns, 0, 1);

            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            columns.Controls.Add(left, 0, 0);
            leftScroll.Dock = DockStyle.Fill;
            leftScroll.AutoScroll = true;
            leftScroll.Margin = new Padding(0);
            fields.Padding = new Padding(0, 0, 14, 0);
            leftScroll.Controls.Add(fields);
            left.Controls.Add(leftScroll, 0, 0);
            fields.Controls.Add(Section("Your PC"));
            hardwareInfo.AccessibleName = "Detected hardware"; fields.Controls.Add(hardwareInfo);
            var hardwareHint = Caption("Automatic matching uses this PC's hardware and game measurements. It works without an AI account.", 9, Muted, false);
            hardwareHint.Margin = new Padding(0, 0, 0, 14); fields.Controls.Add(hardwareHint);
            fields.SizeChanged += delegate { int width = Math.Max(220, fields.ClientSize.Width - 18); hardwareInfo.MaximumSize = hardwareHint.MaximumSize = new Size(width, 0); };
            fields.Controls.Add(Section("1   Choose your game"));
            AddField(fields, "Game file", input, "Select an .nsp, .xci or .nro file. Use Folder if your game is already extracted.",
                delegate { PickFile(input, "Switch game files|*.nro;*.nsp;*.xci;*.dnsp;*.dxci|All files|*.*"); }, true);
            AddField(fields, "Game name", name, "The name to use for your Windows game.", null, false);
            name.Text = "My Switch Game";
            fields.Controls.Add(Section("2   Choose where to save"));
            AddField(fields, "Save folder", output, "A new folder with your game name will be created here.", delegate { PickFolder(output); }, false);
            output.Text = AppPaths.DefaultGamesFolder;
            fields.Controls.Add(matchHardware); fields.Controls.Add(autoTune);

            extraToggle.Height = 34;
            extraToggle.Dock = DockStyle.Top;
            extraToggle.TextAlign = ContentAlignment.MiddleLeft;
            extraToggle.Margin = new Padding(0, 0, 0, 10);
            extraToggle.AccessibleName = "Show extra settings";
            extraToggle.Click += delegate { ShowExtra(!extraFields.Visible); };
            fields.Controls.Add(extraToggle);
            AddField(extraFields, "Conversion engine", backend, "Select scarletnx.exe. The installed engine is usually selected for you.",
                delegate { PickFile(backend, "ScarletNX conversion engine|scarletnx.exe"); }, false);
            AddField(extraFields, "prod.keys file", keys, "Needed for encrypted game files. Select your own local prod.keys file.",
                delegate { PickFile(keys, "Key files|*.keys|All files|*.*"); }, false);
            AddField(extraFields, "title.keys file", titleKeys, "Some games also need this key file. Leave it empty unless needed.",
                delegate { PickFile(titleKeys, "Key files|*.keys|All files|*.*"); }, false);
            sample.Dock = DockStyle.Top;
            sample.Height = 34;
            sample.Click += delegate { LoadTest(); };
            extraFields.Controls.Add(sample);
            fields.Controls.Add(extraFields);
            extraFields.Visible = false;

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 12, 0, 0) };
            convert.Size = new Size(200, 40);
            convert.Click += async delegate { await Compile(); };
            cancel.Size = new Size(95, 40);
            cancel.Enabled = false;
            cancel.Click += delegate { CancelConversion(); };
            actions.Controls.AddRange(new Control[] { convert, cancel });
            left.Controls.Add(actions, 0, 1);

            var activity = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
            activity.RowStyles.Add(new RowStyle(SizeType.Absolute, 260));
            activity.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            activity.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            activity.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            columns.Controls.Add(activity, 2, 0);
            statusCard.Dock = DockStyle.Fill;
            statusCard.BackColor = Surface;
            statusCard.Margin = new Padding(0);
            statusBadge.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            statusTitle.Font = new Font("Segoe UI", 18, FontStyle.Bold);
            statusMessage.Font = new Font("Segoe UI", 10);
            statusMessage.ForeColor = Color.WhiteSmoke;
            statusAction.Height = 34;
            statusAction.Visible = false;
            statusAction.Click += delegate { StatusAction(); };
            progress.Height = 4;
            progress.Style = ProgressBarStyle.Marquee;
            progress.MarqueeAnimationSpeed = 30;
            progress.Visible = false;
            statusCard.Controls.AddRange(new Control[] { statusBadge, statusTitle, statusMessage, statusAction, progress });
            statusCard.Resize += delegate { LayoutStatus(); };
            activity.Controls.Add(statusCard, 0, 0);
            var detailsLabel = Caption("Technical details", 10, Muted, false);
            detailsLabel.Margin = new Padding(0, 14, 0, 4);
            activity.Controls.Add(detailsLabel, 0, 1);
            log.Dock = DockStyle.Fill;
            log.Multiline = true;
            log.ReadOnly = true;
            log.WordWrap = true;
            log.ScrollBars = ScrollBars.Vertical;
            log.Font = new Font("Consolas", 9);
            log.Margin = new Padding(0);
            activity.Controls.Add(log, 0, 2);
            var detailActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 8, 0, 0) };
            foreach (var button in new[] { openLog, copyLog, clearLog }) { button.Size = new Size(125, 30); button.Font = new Font("Segoe UI", 9); }
            openLog.Enabled = false;
            openLog.Click += delegate { if (File.Exists(lastLogPath)) Process.Start("notepad.exe", GameManifest.Quote(lastLogPath)); };
            copyLog.Click += delegate { if (log.TextLength > 0) { try { Clipboard.SetText(log.Text); } catch { AppendLog("Could not copy to the clipboard. Use Open full log instead."); } } };
            clearLog.Click += delegate { log.Clear(); };
            detailActions.Controls.AddRange(new Control[] { openLog, copyLog, clearLog });
            activity.Controls.Add(detailActions, 0, 3);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 8, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            doctorStatus.AutoSize = false; doctorStatus.Dock = DockStyle.Fill; doctorStatus.TextAlign = ContentAlignment.MiddleLeft; doctorStatus.AutoEllipsis = true;
            doctorStatus.ForeColor = Muted; doctorStatus.Font = new Font("Segoe UI", 9); doctorStatus.AccessibleName = "AI Doctor status";
            doctorStatus.Text = "AI Doctor: starting. Experimental conversion: some games may not convert or run.";
            doctorAuto.Margin = new Padding(10, 7, 10, 0); doctorAi.Margin = new Padding(0, 7, 10, 0);
            doctorButton.AccessibleName = "Open AI Doctor"; doctorButton.Size = new Size(130, 32);
            doctorButton.Click += delegate { using (var window = new DoctorWindow(doctorEvents, doctor)) window.ShowDialog(this); };
            doctorAuto.CheckedChanged += delegate { if (doctor != null) doctor.AutoFix = doctorAuto.Checked; };
            doctorAi.CheckedChanged += delegate { if (doctor != null) doctor.UseAi = doctorAi.Checked; };
            footer.Controls.Add(doctorStatus, 0, 0); footer.Controls.Add(doctorAuto, 1, 0); footer.Controls.Add(doctorAi, 2, 0); footer.Controls.Add(doctorButton, 3, 0);
            root.Controls.Add(footer, 0, 2);
            var tabs = new TabControl { Dock = DockStyle.Fill, AccessibleName = "PortForge tabs" };
            mainTabs = tabs;
            var convertTab = new TabPage("Convert") { BackColor = Background };
            var settingsTab = new TabPage("Settings") { BackColor = Background };
            convertTab.Controls.Add(root); settingsTab.Controls.Add(new GlobalSettingsPanel());
            tabs.TabPages.Add(convertTab); tabs.TabPages.Add(settingsTab); Controls.Add(tabs);

            LocateBackend();
            foreach (var box in new[] { input, name, output, backend, keys, titleKeys }) box.TextChanged += delegate { InputsChanged(); };
            initializing = false;
            ShowStatus("READY", "Ready when you are", "Choose a game file and a save folder, then select Start conversion.", Muted);
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            { if (cancellation != null) { e.Cancel = true; CancelConversion(); } };
            Shown += delegate { AppPaths.RefreshRegistration(); };
            Shown += async delegate { await QuietUpdateCheck(); };
            Shown += async delegate { await DetectHardware(); };
            Shown += delegate { if (StartDoctorOnShow) StartDoctor(); };
            FormClosed += delegate { doctorUiTimer.Stop(); doctorUiTimer.Dispose(); if (doctor != null) doctor.Dispose(); if(cloudMonitor!=null)cloudMonitor.Dispose(); tray.Visible = false; tray.Dispose(); };
        }

        private void StartDoctor()
        {
            cloudMonitor=new CloudSaveMonitor(()=>doctor==null?new string[0]:doctor.Games);
            tray.Icon = AppIcon.Get() ?? Icon ?? SystemIcons.Application; tray.Text = "PortForge AI Doctor"; tray.Visible = true;
            tray.BalloonTipClicked += delegate { doctorButton.PerformClick(); };
            doctor = new LiveDoctor(AppDomain.CurrentDomain.BaseDirectory) { AutoFix = doctorAuto.Checked, UseAi = doctorAi.Checked };
            doctor.StartDiscovery(LiveDoctor.DefaultDiscoveryRoots(AppDomain.CurrentDomain.BaseDirectory));
            doctor.Reported += e =>
            {
                lock (doctorEvents)
                {
                    doctorEvents.Add(e); if (doctorEvents.Count > 500) doctorEvents.RemoveAt(0);
                    pendingDoctorEvent = e;
                    if (e.Fixed || e.Important) pendingDoctorNotice = e;
                }
            };
            doctorUiTimer.Tick += delegate { FlushDoctorUpdates(); }; doctorUiTimer.Start();
            int count = doctor.Games.Length;
            doctorStatus.Text = "AI Doctor: " + (count == 0 ? "ready. It starts watching a game once you convert one or open it in Settings / Game tools." : "watching " + count + " game(s). Problems are explained live and fixed when the game closes.");
        }

        private async Task DetectHardware()
        {
            try
            {
                var pc = await Task.Run(() => HardwareProfile.Detect());
                if (IsDisposed || Disposing) return;
                detectedHardware = pc; hardwareInfo.Text = pc.Details;
            }
            catch (Exception)
            {
                if (!IsDisposed && !Disposing) hardwareInfo.Text = "Hardware could not be read yet. Conversion will retry the hardware check.";
            }
        }

        private void FlushDoctorUpdates()
        {
            DoctorEvent latest, notice = null;
            lock (doctorEvents)
            {
                latest = pendingDoctorEvent; pendingDoctorEvent = null;
                if ((DateTime.UtcNow - lastDoctorNotice).TotalSeconds >= 30)
                { notice = pendingDoctorNotice; pendingDoctorNotice = null; }
            }
            if (latest != null)
            {
                string text = "AI Doctor: " + latest;
                if (doctorStatus.Text != text) doctorStatus.Text = text;
                Color color = latest.Fixed ? Mint : latest.Important ? Color.FromArgb(255, 199, 120) : Muted;
                if (doctorStatus.ForeColor != color) doctorStatus.ForeColor = color;
            }
            if (notice != null)
            {
                lastDoctorNotice = DateTime.UtcNow;
                tray.ShowBalloonTip(6000, "PortForge AI Doctor: " + notice.Title, (notice.Game.Length > 0 ? notice.Game + ": " : "") + (notice.Text.Length > 220 ? notice.Text.Substring(0, 220) + "..." : notice.Text), notice.Kind == "crash" || notice.Kind == "error" ? ToolTipIcon.Warning : ToolTipIcon.Info);
            }
        }

        private async Task QuietUpdateCheck()
        {
            try
            {
                var found = await Task.Run(() => AppUpdates.Check(AppUpdates.ApiRoot, AppUpdates.Repository, AppUpdates.CurrentVersion, CancellationToken.None));
                if (found.IsNewer && !IsDisposed) updates.Text = "Update available: " + found.Latest.ToString(3);
            }
            catch (Exception) { }
        }

        private void AddField(TableLayoutPanel parent, string label, TextBox box, string hint, Action browse, bool folder)
        {
            var block = Stack();
            block.Margin = new Padding(0, 0, 0, 16);
            block.Controls.Add(Caption(label, 10, Color.WhiteSmoke, true));
            int count = 2 + (browse == null ? 0 : 1) + (folder ? 1 : 0);
            var row = new TableLayoutPanel { ColumnCount = count, RowCount = 1, Dock = DockStyle.Top, Height = 35, Margin = new Padding(0, 6, 0, 5) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            box.Dock = DockStyle.Fill;
            box.AutoSize = false;
            box.Margin = new Padding(0);
            row.Controls.Add(box, 0, 0);
            int column = 1;
            if (browse != null)
            {
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
                var button = Button("Browse", false);
                button.AccessibleName = "Browse " + label.ToLowerInvariant();
                button.Click += delegate { browse(); };
                PlaceButton(row, button, column++);
            }
            if (folder)
            {
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
                var button = Button("Folder", false);
                button.AccessibleName = "Choose extracted game folder";
                button.Click += delegate { PickFolder(box); };
                PlaceButton(row, button, column++);
            }
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            var clear = Button("Clear", false);
            clear.AccessibleName = "Clear " + label.ToLowerInvariant();
            clear.Enabled = box.TextLength > 0;
            box.TextChanged += delegate { clear.Enabled = box.TextLength > 0; };
            clear.Click += delegate { box.Clear(); box.Focus(); };
            tips.SetToolTip(clear, "Clear only the " + label.ToLowerInvariant() + " field");
            PlaceButton(row, clear, column);
            block.Controls.Add(row);
            var help = Caption(hint, 9, Muted, false);
            block.SizeChanged += delegate { help.MaximumSize = new Size(Math.Max(180, block.ClientSize.Width), 0); };
            block.Controls.Add(help);
            parent.Controls.Add(block);
        }

        private static void PlaceButton(TableLayoutPanel row, Button button, int column)
        { button.Dock = DockStyle.Fill; button.Margin = new Padding(6, 0, 0, 0); button.Font = new Font("Segoe UI", 9); row.Controls.Add(button, column, 0); }

        private void ShowExtra(bool show)
        { extraFields.Visible = show; extraToggle.Text = show ? "Extra settings  -" : "Extra settings  +"; extraToggle.AccessibleName = show ? "Hide extra settings" : "Show extra settings"; }

        private void LayoutStatus()
        {
            int width = Math.Max(120, statusCard.ClientSize.Width - 36);
            statusBadge.SetBounds(18, 17, width, 22);
            statusTitle.SetBounds(18, 48, width, 56);
            statusMessage.SetBounds(18, 112, width, 86);
            statusAction.SetBounds(18, 207, Math.Min(width, 205), 34);
            progress.SetBounds(0, statusCard.ClientSize.Height - 4, statusCard.ClientSize.Width, 4);
        }

        private void ShowStatus(string badge, string title, string message, Color color)
        {
            statusBadge.Text = badge;
            statusBadge.ForeColor = statusTitle.ForeColor = color;
            statusTitle.Text = title;
            statusMessage.Text = message;
            statusAction.Visible = false;
            actionField = null;
        }

        private void InputsChanged()
        {
            if (initializing || cancellation != null) return;
            result = null;
            ShowStatus("READY", "Ready when you are", "Your choices have been updated. Select Start conversion when you are ready.", Muted);
        }

        private bool CheckRequired()
        {
            foreach (var box in new[] { input, name, output, backend })
            {
                if (!String.IsNullOrWhiteSpace(box.Text)) continue;
                string label = box.AccessibleName.ToLowerInvariant();
                ShowStatus("ERROR", "Choose a " + label, "Fill in the " + label + " field, then select Start conversion.", Red);
                AppendLog("ERROR: The " + label + " field is empty.");
                if (box == backend) ShowExtra(true);
                box.Focus();
                return false;
            }
            return true;
        }

        private async Task Compile()
        {
            if (cancellation != null || !CheckRequired()) return;
            var request = new NativeRequest { Name = name.Text.Trim(), Input = input.Text.Trim(), Backend = backend.Text.Trim(), Output = output.Text.Trim(), ProductKeys = keys.Text.Trim(), TitleKeys = titleKeys.Text.Trim(), AutoOptimize = autoTune.Checked, MatchHardware = matchHardware.Checked, Hardware = detectedHardware };
            cancellation = new CancellationTokenSource();
            CancellationToken token = cancellation.Token;
            int current = ++operation;
            result = lastLogPath = recoveryFolder = null;
            fieldLock = UiLock.Lock(fields); convert.Enabled = openLog.Enabled = false;
            downloads.Enabled = false;
            cancel.Enabled = progress.Visible = true;
            ShowStatus("WORKING", "Starting conversion", "Checking your game and preparing the conversion. You can cancel at any time.", Blue);
            log.Clear();
            AppendLog("STARTED: " + request.Name);
            try
            {
                var updates = new Progress<string>(line =>
                {
                    if (current != operation || cancellation == null) return;
                    if (line.StartsWith("Build log: ")) { lastLogPath = line.Substring(11).Trim(); }
                    AppendLog(line);
                });
                var stages = new Progress<ConversionStatus>(stage =>
                {
                    if (current == operation && cancellation != null && !token.IsCancellationRequested)
                        ShowStatus("WORKING", stage.Title, stage.Message, Blue);
                });
                result = await Task.Run(() => NativeConversion.Convert(request, updates, token, stages));
                try{await Task.Run(()=>CloudSaves.Service.RegisterGame(Path.Combine(result,request.Name+".exe")));}catch(Exception e){AppendLog("Cloud saves setup will retry: "+e.Message);}
                try { if (result != null && Directory.Exists(result)) { string built = Path.Combine(result, Path.GetFileName(result) + ".exe"); if (File.Exists(built)) GameSettingsWindow.RememberGame(built); } } catch (IOException) { } catch (UnauthorizedAccessException) { }
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last-game.txt"), Path.Combine(result, request.Name + ".exe")); } catch { }
                if (doctor != null) doctor.Watch(Path.Combine(result, request.Name + ".exe"));
                ShowStatus("DONE", "Your files are ready", "The Windows game files were created successfully. Open the game folder to find " + request.Name + ".exe. Whether the game runs depends on game support.", Mint);
                if (request.PcSummary != null) ShowStatus("DONE", "Your game settings are ready", request.PcSummary + " See PortForge-PC-settings.txt in the game folder for the hardware and settings used. Game compatibility can vary.", Mint);
                if (request.TuningWarning != null)
                    ShowStatus("DONE / CHECK SETTINGS", "Your files are ready", (request.PcSummary ?? "The game files were created.") + " Automatic tuning could not finish. Open full log for the reason, or use the Settings tab.", Blue);
                statusAction.Text = "Open game folder";
                statusAction.Visible = true;
                AppendLog("DONE: Files created in " + result);
            }
            catch (OperationCanceledException)
            {
                ShowStatus("CANCELLED", "Conversion cancelled", "The conversion stopped. No completed game folder was created. You can change your choices and try again.", Muted);
                AppendLog("CANCELLED: No completed game was created.");
            }
            catch (Exception error)
            {
                var failure = error as ConversionFailure;
                if (failure != null)
                {
                    if (!String.IsNullOrEmpty(failure.LogPath)) lastLogPath = failure.LogPath;
                    ShowStatus("ERROR", failure.UserTitle, failure.NextStep, Red);
                    if (failure.Field == "keys" || failure.Field == "titleKeys")
                    {
                        ShowExtra(true);
                        actionField = failure.Field;
                        statusAction.Text = failure.Field == "keys" ? "Choose prod.keys file" : "Choose title.keys file";
                        statusAction.Visible = true;
                    }
                    else if (!String.IsNullOrEmpty(failure.RecoveryFolder))
                    {
                        recoveryFolder = failure.RecoveryFolder;
                        statusAction.Text = "Open saved work";
                        statusAction.Visible = true;
                    }
                    AppendLog("ERROR: " + failure.UserTitle + Environment.NewLine + failure.NextStep);
                    AppendLog(failure.Message);
                }
                else
                {
                    ShowStatus("ERROR", error is ArgumentException ? "Check your choices" : "Conversion stopped", error.Message, Red);
                    AppendLog("ERROR: " + error.Message);
                }
            }
            finally
            {
                cancellation.Dispose(); cancellation = null;
                if (fieldLock != null) { fieldLock.Unlock(); fieldLock = null; }
                cancel.Enabled = progress.Visible = false;
                openLog.Enabled = File.Exists(lastLogPath);
                convert.Enabled = true;
                downloads.Enabled = true;
            }
        }

        private void CancelConversion()
        {
            if (cancellation == null) return;
            cancellation.Cancel();
            cancel.Enabled = false;
            ShowStatus("STOPPING", "Cancelling conversion", "Stopping the conversion and cleaning up temporary files. Please wait.", Muted);
        }

        private void StatusAction()
        {
            if (actionField != null)
            {
                TextBox box = actionField == "keys" ? keys : titleKeys;
                ShowExtra(true);
                PickFile(box, "Key files|*.keys|All files|*.*");
            }
            else if (Directory.Exists(recoveryFolder)) Process.Start("explorer.exe", GameManifest.Quote(recoveryFolder));
            else if (Directory.Exists(result)) Process.Start("explorer.exe", GameManifest.Quote(result));
        }

        private void AppendLog(string line)
        {
            if (log.TextLength > 120000) log.Text = log.Text.Substring(log.TextLength - 60000);
            log.AppendText(line + Environment.NewLine);
        }

        private void LoadTest()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NativeTest", "original-test.nro");
            if (!File.Exists(path)) { ShowStatus("ERROR", "Test file missing", "Extract the whole PortForge ZIP so the NativeTest folder is beside the app.", Red); return; }
            input.Text = path;
            name.Text = "PortForge Test " + DateTime.Now.ToString("HHmmss");
            ShowStatus("READY", "Test file selected", "This small test checks the conversion process. It writes a log and closes; it is not a playable game. Select Start conversion to try it.", Muted);
        }

        private void LocateBackend()
        {
            string locator = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backend-location.txt");
            string engine = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Engine", "scarletnx.exe");
            string bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backends", "ScarletNX", "bin", "scarletnx.exe");
            // The PortForge Engine ships with the app, so every PC converts with it and every new game gets its stutter fix
            if (File.Exists(engine)) backend.Text = engine;
            else if (File.Exists(bundled)) backend.Text = bundled;
            else if (File.Exists(locator))
            { string path = File.ReadAllText(locator).Trim(); if (File.Exists(path)) backend.Text = path; }
        }

        private void PickFile(TextBox box, string filter)
        { using (var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true }) if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.FileName; }
        private void PickFolder(TextBox box)
        { using (var dialog = new FolderBrowserDialog()) if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.SelectedPath; }
        private static TextBox Box(string label)
        { return new TextBox { AccessibleName = label, BackColor = Surface, ForeColor = Color.WhiteSmoke, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10) }; }
        private static TableLayoutPanel Stack()
        { return new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = new Padding(0) }; }
        private static Label Caption(string text, float size, Color color, bool bold)
        { return new Label { Text = text, AutoSize = true, ForeColor = color, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(0, 0, 0, 4) }; }
        private static Label Section(string text)
        { var label = Caption(text, 12, Mint, true); label.Margin = new Padding(0, 0, 0, 14); return label; }
        private static Button Button(string text, bool primary)
        {
            var button = new ReadableButton { Text = text, FlatStyle = FlatStyle.Flat, BackColor = primary ? Mint : Surface, ForeColor = primary ? Background : Color.WhiteSmoke, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 10, primary ? FontStyle.Bold : FontStyle.Regular) };
            button.EnabledChanged += delegate { button.BackColor = button.Enabled ? (primary ? Mint : Surface) : Color.FromArgb(34, 38, 48); button.ForeColor = button.Enabled ? (primary ? Background : Color.WhiteSmoke) : Color.FromArgb(120, 126, 140); };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }
        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
    }
}
