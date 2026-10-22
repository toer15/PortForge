using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PortForge
{
    public sealed class UpdateWindow : Form
    {
        private static readonly Color Bg = Color.FromArgb(17, 21, 30), Surface = Color.FromArgb(28, 34, 46), Mint = Color.FromArgb(105, 226, 183),
            Muted = Color.FromArgb(173, 185, 203), Red = Color.FromArgb(255, 157, 148), Blue = Color.FromArgb(140, 187, 255);
        private readonly Label badge = Label("CHECKING", 9, Mint), title = Label("Checking for updates", 18, Color.White), message = Label("", 10, Muted), version = Label("", 10, Muted);
        private readonly TextBox notes = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Surface, ForeColor = Color.WhiteSmoke, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "What is new" };
        private readonly Button check = Action("Check again", false), install = Action("Install update", true), page = Action("Open release page", false), close = Action("Close", false);
        private readonly ProgressBar bar = new ProgressBar { Dock = DockStyle.Fill };
        private readonly bool busy;
        private readonly string apiRoot, repository;
        private readonly Version current;
        private CancellationTokenSource cancellation;
        private UpdateInfo info;
        public bool InstallStarted { get; private set; }
        public UpdateInfo Latest { get { return info; } }
        public string StatusTitle { get { return title.Text; } }
        public string StatusMessage { get { return message.Text; } }
        public bool CanInstall { get { return install.Enabled; } }

        public UpdateWindow(bool conversionRunning) : this(conversionRunning, AppUpdates.ApiRoot, AppUpdates.Repository, AppUpdates.CurrentVersion) { }

        public UpdateWindow(bool conversionRunning, string apiRoot, string repository, Version current)
        {
            busy = conversionRunning; this.apiRoot = apiRoot; this.repository = repository; this.current = current;
            Text = "PortForge | Updates"; AppIcon.Apply(this); ClientSize = new Size(640, 520); MinimumSize = new Size(560, 460); StartPosition = FormStartPosition.CenterParent;
            BackColor = Bg; ForeColor = Color.WhiteSmoke; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(22) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            var card = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Surface, Padding = new Padding(14) };
            card.Controls.Add(badge); card.Controls.Add(title); card.Controls.Add(message);
            card.SizeChanged += delegate { message.MaximumSize = new Size(Math.Max(200, card.ClientSize.Width - 30), 0); };
            root.Controls.Add(card, 0, 0);
            root.Controls.Add(bar, 0, 1); root.Controls.Add(Label("What is new in the latest version", 10, Muted), 0, 2);
            root.Controls.Add(notes, 0, 3);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
            foreach (var b in new[] { install, check, page, close }) actions.Controls.Add(b);
            root.Controls.Add(actions, 0, 4); Controls.Add(root);
            version.Text = "You have version " + current.ToString(3) + ".";
            install.Enabled = false; page.Enabled = false;
            check.Click += async delegate { await CheckNow(); };
            install.Click += async delegate { await InstallNow(); };
            page.Click += delegate { if (info != null && info.PageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) Process.Start(info.PageUrl); };
            close.Click += delegate { Close(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (cancellation != null) cancellation.Cancel(); };
            Shown += async delegate { await CheckNow(); };
        }

        private static Label Label(string text, float size, Color color) { return new Label { Text = text, AutoSize = true, ForeColor = color, Font = new Font("Segoe UI", size), Margin = new Padding(0, 2, 0, 6) }; }
        private static Button Action(string text, bool primary)
        {
            return new Button { Text = text, AccessibleName = text, Size = new Size(150, 34), FlatStyle = FlatStyle.Flat, ForeColor = primary ? Color.Black : Color.White, BackColor = primary ? Mint : Color.FromArgb(48, 58, 76), UseVisualStyleBackColor = false, Margin = new Padding(0, 0, 8, 0) };
        }
        private void Status(string kind, string heading, string text, Color color)
        {
            badge.Text = kind; badge.ForeColor = color; title.Text = heading; message.Text = text;
        }

        public async Task CheckNow()
        {
            if (cancellation != null) return;
            cancellation = new CancellationTokenSource(); var token = cancellation.Token;
            install.Enabled = page.Enabled = check.Enabled = false; bar.Style = ProgressBarStyle.Marquee;
            Status("CHECKING", "Checking for updates", version.Text + " Asking GitHub for the newest version...", Blue);
            try
            {
                info = await Task.Run(() => AppUpdates.Check(apiRoot, repository, current, token));
                notes.Text = (info.Notes ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
                page.Enabled = info.PageUrl.Length > 0;
                if (!info.IsNewer) Status("UP TO DATE", "You have the newest version", version.Text + " The newest published version is " + info.Latest.ToString(3) + ".", Mint);
                else if (!info.HasPackage) Status("UPDATE", "Version " + info.Latest.ToString(3) + " is available", "This release has no Windows download attached. Use Open release page to get it.", Blue);
                else if (busy) Status("UPDATE", "Version " + info.Latest.ToString(3) + " is available", "A conversion is running. Close this window, wait for it to finish, then install the update.", Blue);
                else { Status("UPDATE", "Version " + info.Latest.ToString(3) + " is available", version.Text + " Install update downloads it, closes PortForge, replaces the program files and opens it again. Your games, settings and downloads are kept.", Blue); install.Enabled = true; }
            }
            catch (Exception e)
            {
                if (token.IsCancellationRequested) return;
                Status("ERROR", "Could not check for updates", e.Message, Red);
            }
            finally { bar.Style = ProgressBarStyle.Continuous; check.Enabled = true; cancellation = null; }
        }

        private async Task InstallNow()
        {
            if (cancellation != null || info == null || !info.IsNewer || busy) return;
            cancellation = new CancellationTokenSource(); var token = cancellation.Token;
            install.Enabled = check.Enabled = false; bar.Style = ProgressBarStyle.Continuous; bar.Value = 0;
            Status("DOWNLOADING", "Downloading version " + info.Latest.ToString(3), "Please keep PortForge open.", Blue);
            try
            {
                string work = Path.Combine(Path.GetTempPath(), "PortForge-updates");
                string folder = await Task.Run(() => AppUpdates.Download(info, work, p => BeginInvoke((Action)delegate { bar.Value = Math.Max(0, Math.Min(100, p)); }), token));
                AppUpdates.StartInstall(folder, AppDomain.CurrentDomain.BaseDirectory, Process.GetCurrentProcess().Id, true);
                InstallStarted = true;
                Status("DONE", "Installing the update", "PortForge will close and open again in a few seconds.", Mint);
                DialogResult = DialogResult.Yes;
            }
            catch (Exception e)
            {
                if (token.IsCancellationRequested) return;
                Status("ERROR", "Update not installed", e.Message + " Your current version was not changed.", Red);
                install.Enabled = true;
            }
            finally { check.Enabled = true; cancellation = null; }
        }
    }
}
