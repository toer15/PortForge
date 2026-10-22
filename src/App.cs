using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PortForge
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new BuilderWindow());
        }
    }

    public sealed class BuilderWindow : Form
    {
        private static readonly Color Background = Color.FromArgb(17, 21, 30);
        private static readonly Color Surface = Color.FromArgb(28, 34, 46);
        private static readonly Color Muted = Color.FromArgb(160, 173, 193);
        private static readonly Color Accent = Color.FromArgb(105, 226, 183);
        private readonly TextBox title = Input();
        private readonly TextBox runtime = Input();
        private readonly TextBox game = Input();
        private readonly TextBox output = Input();
        private readonly TextBox arguments = Input();
        private readonly ComboBox profile = new ComboBox();
        private readonly CheckBox portable = new CheckBox();
        private readonly Label status = new Label();
        private readonly Label profileNote = new Label();
        private readonly TextBox command = Input();
        private readonly Button build = Button("Build Windows app", true);
        private readonly Button inspect = Button("Check package", false);
        private readonly Button cancel = Button("Cancel", false);
        private readonly Button open = Button("Open export", false);
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Panel inputs = new Panel();
        private CancellationTokenSource cancellation;
        private string lastExport;

        public BuilderWindow()
        {
            Text = "PortForge | Console to desktop";
            ClientSize = new Size(1090, 880);
            MinimumSize = new Size(1010, 760);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Background;
            ForeColor = Color.WhiteSmoke;
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PortForge.ico"))
                if (stream != null) Icon = new Icon(stream);

            var sidebar = new Sidebar { Dock = DockStyle.Left, Width = 232, BackColor = Color.FromArgb(12, 16, 24) };
            var demo = Button("Load packaging demo", false);
            demo.SetBounds(22, 364, 186, 38);
            demo.Click += delegate { LoadDemo(); };
            sidebar.Controls.Add(demo);

            var main = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(28, 24, 28, 24) };
            var content = new Panel { Dock = DockStyle.Top, Height = 824 };
            content.Controls.Add(Label("Give your game a home on PC.", 0, 0, 760, 36, 22, Color.White, true));
            content.Controls.Add(Label("Choose your files. Set the launch options. Export a Windows app.", 0, 44, 760, 26, 10, Muted, false));
            inputs.SetBounds(0, 87, 760, 533);
            inputs.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            content.Controls.Add(inputs);

            inputs.Controls.Add(Label("01   GAME & RUNTIME", 0, 0, 740, 24, 10, Accent, true));
            Field("App name", title, 29, null);
            title.Text = "My Game";

            inputs.Controls.Add(Label("Runtime profile", 0, 91, 280, 20, 9, Muted, false));
            profile.SetBounds(0, 114, 360, 30);
            profile.DropDownStyle = ComboBoxStyle.DropDownList;
            profile.BackColor = Surface;
            profile.ForeColor = Color.WhiteSmoke;
            profile.FlatStyle = FlatStyle.Flat;
            profile.Items.AddRange(new object[] { "Custom emulator / runtime", "Dolphin (GameCube / Wii)", "Nintendo Switch (custom runtime)", "PS5 (custom runtime)" });
            profile.SelectedIndexChanged += delegate { SetProfile(); };
            inputs.Controls.Add(profile);
            profileNote.SetBounds(374, 111, 354, 40);
            profileNote.Font = new Font("Segoe UI", 9);
            profileNote.ForeColor = Muted;
            inputs.Controls.Add(profileNote);
            Field("Runtime executable (.exe)", runtime, 155, delegate { BrowseFile(runtime, "Windows executable|*.exe"); });
            Field("Game file or folder", game, 217, delegate { BrowseFile(game, "Game files|*.*"); });
            var gameFolder = Button("Folder", false);
            gameFolder.SetBounds(640, 240, 88, 31);
            gameFolder.Click += delegate { BrowseFolder(game); };
            inputs.Controls.Add(gameFolder);
            game.Width = 527;
            foreach (Control c in inputs.Controls)
                if (c is Button && c.Top == 240 && c != gameFolder) c.Left = 539;

            inputs.Controls.Add(Label("Launch arguments  /  one argument per line, no surrounding quotes", 0, 279, 730, 20, 9, Muted, false));
            arguments.SetBounds(0, 302, 728, 71);
            arguments.Multiline = true;
            arguments.ScrollBars = ScrollBars.Vertical;
            arguments.Font = new Font("Consolas", 10);
            arguments.TextChanged += delegate { UpdatePreview(); };
            inputs.Controls.Add(arguments);
            inputs.Controls.Add(Label("Placeholders: {game}   {gameDir}   {runtime}   {package}", 0, 379, 730, 20, 9, Muted, false));

            inputs.Controls.Add(Label("02   EXPORT", 0, 412, 730, 24, 10, Accent, true));
            Field("Export folder", output, 442, delegate { BrowseFolder(output); });
            output.Text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Exports");
            portable.SetBounds(0, 505, 730, 26);
            portable.Text = "Copy runtime folder + game files into the export";
            portable.Checked = true;
            portable.ForeColor = Color.WhiteSmoke;
            portable.CheckedChanged += delegate { UpdatePreview(); };
            inputs.Controls.Add(portable);

            content.Controls.Add(Label("Portable copies may still need runtime setup, firmware or installed dependencies.", 0, 626, 760, 22, 9, Muted, false));
            command.SetBounds(0, 655, 728, 45);
            command.ReadOnly = true;
            command.Multiline = true;
            command.BorderStyle = BorderStyle.None;
            command.Font = new Font("Consolas", 9);
            command.ForeColor = Muted;
            content.Controls.Add(command);
            build.SetBounds(0, 713, 215, 42);
            build.Click += async delegate { await RunOperation(true); };
            inspect.SetBounds(227, 713, 150, 42);
            inspect.Click += async delegate { await RunOperation(false); };
            cancel.SetBounds(389, 713, 90, 42);
            cancel.Visible = false;
            cancel.Click += delegate { if (cancellation != null) cancellation.Cancel(); };
            open.SetBounds(491, 713, 130, 42);
            open.Enabled = false;
            open.Click += delegate { if (lastExport != null) Process.Start("explorer.exe", GameManifest.Quote(lastExport)); };
            content.Controls.AddRange(new Control[] { build, inspect, cancel, open });
            progress.SetBounds(0, 769, 728, 4);
            progress.Style = ProgressBarStyle.Marquee;
            progress.Visible = false;
            content.Controls.Add(progress);
            status.SetBounds(0, 784, 728, 38);
            status.Font = new Font("Segoe UI", 9);
            status.ForeColor = Muted;
            status.Text = "Ready. Start with a game that already works in your selected runtime.";
            status.AutoEllipsis = true;
            content.Controls.Add(status);

            main.Controls.Add(content);
            Controls.Add(main);
            Controls.Add(sidebar);
            profile.SelectedIndex = 0;
            foreach (var box in new[] { runtime, game, title, output }) box.TextChanged += delegate { UpdatePreview(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (cancellation != null) { cancellation.Cancel(); e.Cancel = true; status.Text = "Cancelling. Close the app after cleanup finishes."; }
            };
        }

        private void Field(string name, TextBox box, int top, Action browse)
        {
            inputs.Controls.Add(Label(name, 0, top, 730, 20, 9, Muted, false));
            box.SetBounds(0, top + 23, browse == null ? 728 : 628, 31);
            box.AutoSize = false;
            inputs.Controls.Add(box);
            if (browse == null) return;
            var button = Button("Browse", false);
            button.SetBounds(640, top + 23, 88, 31);
            button.Click += delegate { browse(); };
            inputs.Controls.Add(button);
        }

        private void SetProfile()
        {
            arguments.Text = profile.SelectedIndex == 1 ? "-b\r\n-e\r\n{game}" : "{game}";
            profileNote.Text = profile.SelectedIndex == 1 ? "Batch launch preset. Use a configured Dolphin runtime." :
                profile.SelectedIndex == 2 ? "Use your existing Switch runtime and its documented launch arguments." :
                profile.SelectedIndex == 3 ? "Experimental: requires a runtime that supports this specific PS5 game." :
                "Enter the launch arguments supported by your runtime.";
            UpdatePreview();
        }

        private BuildRequest Request()
        {
            return new BuildRequest
            {
                Title = title.Text.Trim(), Profile = profile.Text,
                RuntimeExe = runtime.Text.Trim(), GamePath = game.Text.Trim(), OutputFolder = output.Text.Trim(),
                Arguments = arguments.Lines.Where(line => !String.IsNullOrWhiteSpace(line)).ToArray(), Portable = portable.Checked
            };
        }

        private void UpdatePreview()
        {
            try
            {
                var r = Request();
                string args = String.Join(" ", r.Arguments.Select(a => GameManifest.Quote(GameManifest.Expand(a, r.GamePath, r.RuntimeExe, r.OutputFolder))));
                command.Text = (r.Portable ? "COPY FILES  " : "LINK FILES  ") + GameManifest.Quote(r.RuntimeExe) + " " + args;
            }
            catch { command.Text = "Choose a runtime and game to preview the launch command."; }
        }

        private async Task RunOperation(bool export)
        {
            var request = Request();
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            build.Enabled = inspect.Enabled = inputs.Enabled = false;
            cancel.Visible = progress.Visible = true;
            status.ForeColor = Muted;
            status.Text = export ? "Preparing your Windows app..." : "Checking package files...";
            try
            {
                if (export)
                {
                    byte[] launcher;
                    using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("PortForge.Launcher.exe"))
                    using (var memory = new MemoryStream()) { source.CopyTo(memory); launcher = memory.ToArray(); }
                    var updates = new Progress<string>(message => status.Text = message);
                    lastExport = await Task.Run(() => PackageBuilder.Build(request, launcher, updates, token));
                    open.Enabled = true;
                    status.ForeColor = Accent;
                    status.Text = "Built " + request.Title + ".exe. Open the export and keep the whole folder together.";
                }
                else
                {
                    var plan = await Task.Run(() => PackageBuilder.Inspect(request, token));
                    status.ForeColor = Accent;
                    status.Text = (request.Portable ? plan.Files + " files / " + PackageBuilder.FormatBytes(plan.Bytes) + " to copy. " : "Linked export: original files must stay in place. ") + "File checks passed; gameplay compatibility is untested.";
                }
            }
            catch (OperationCanceledException) { status.Text = "Cancelled. No completed export was created."; }
            catch (Exception error)
            {
                status.ForeColor = Color.FromArgb(255, 155, 145);
                status.Text = error.Message;
                MessageBox.Show(this, error.Message, "PortForge", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
                build.Enabled = inspect.Enabled = inputs.Enabled = true;
                cancel.Visible = progress.Visible = false;
            }
        }

        private void LoadDemo()
        {
            if (cancellation != null) return;
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Demo");
            if (!File.Exists(Path.Combine(folder, "Runtime", "OrbitDemo.exe")))
            {
                MessageBox.Show(this, "Extract the entire PortForge ZIP so its Demo folder is beside PortForge.exe.", "Demo files");
                return;
            }
            profile.SelectedIndex = 0;
            title.Text = "Orbit Playground";
            runtime.Text = Path.Combine(folder, "Runtime", "OrbitDemo.exe");
            game.Text = Path.Combine(folder, "Game", "scene.txt");
            arguments.Text = "{game}";
            portable.Checked = true;
            status.Text = "Demo loaded. Build, then open Orbit Playground.exe in the export. This tests packaging, not console emulation.";
        }

        private void BrowseFile(TextBox box, string filter)
        {
            using (var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true })
                if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.FileName;
        }

        private void BrowseFolder(TextBox box)
        {
            using (var dialog = new FolderBrowserDialog { Description = "Choose a folder", ShowNewFolderButton = true })
                if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.SelectedPath;
        }

        private static TextBox Input()
        {
            return new TextBox { BackColor = Surface, ForeColor = Color.WhiteSmoke, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10) };
        }

        private static Button Button(string text, bool primary)
        {
            var button = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Surface, ForeColor = primary ? Background : Color.WhiteSmoke, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 10, primary ? FontStyle.Bold : FontStyle.Regular) };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private static Label Label(string text, int x, int y, int width, int height, float size, Color color, bool bold)
        {
            return new Label { Text = text, Left = x, Top = y, Width = width, Height = height, ForeColor = color, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular) };
        }

        private sealed class Sidebar : Panel
        {
            public Sidebar() { DoubleBuffered = true; }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Accent, 3))
                {
                    g.DrawPolygon(pen, new[] { new Point(24, 40), new Point(46, 27), new Point(68, 40), new Point(68, 66), new Point(46, 79), new Point(24, 66) });
                    g.DrawLines(pen, new[] { new Point(38, 65), new Point(38, 43), new Point(53, 43), new Point(53, 53), new Point(39, 53) });
                }
                using (var heading = new Font("Segoe UI", 19, FontStyle.Bold)) g.DrawString("PortForge", heading, Brushes.White, 20, 97);
                using (var small = new Font("Segoe UI", 9))
                using (var muted = new SolidBrush(Muted))
                using (var accent = new SolidBrush(Accent))
                {
                    g.DrawString("CONSOLE TO DESKTOP", small, accent, 23, 136);
                    g.DrawString("01    Choose your game\n\n02    Connect a runtime\n\n03    Build a Windows app", small, muted, new RectangleF(23, 201, 190, 125));
                    g.DrawString("PROTOTYPE  /  0.1", small, accent, 23, 437);
                    g.DrawString("One-click game packaging.\n\nThis version runs your chosen emulator or runtime.\n\nNative recompilation needs a dedicated backend for each console and game.", small, muted, new RectangleF(23, 465, 185, 210));
                }
            }
        }
    }
}
