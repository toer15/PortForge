using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

// The PortForge installer: puts the app in its own folder, adds Start menu (and optionally desktop) shortcuts with the logo, and
// registers it under "Installed apps" so Windows can uninstall it. The app's files are the embedded payload.zip.
namespace PortForgeSetup
{
    internal sealed class Options
    {
        public bool Silent, NoLaunch, NoDesktop;
        public string Dir, StartMenuDir, DesktopDir, RegKey;

        public static Options Parse(string[] args)
        {
            var o = new Options(); string open = null; // the option whose path Windows split at a space
            foreach (string raw in args)
            {
                string a = raw.Trim();
                if (!a.StartsWith("/", StringComparison.Ordinal) && open != null)
                {
                    if (open == "D") o.Dir += " " + a.Trim('"'); else if (open == "S") o.StartMenuDir += " " + a.Trim('"'); else if (open == "K") o.DesktopDir += " " + a.Trim('"');
                    continue;
                }
                open = a.StartsWith("/D=", StringComparison.OrdinalIgnoreCase) ? "D" : a.StartsWith("/STARTMENU=", StringComparison.OrdinalIgnoreCase) ? "S" : a.StartsWith("/DESKTOP=", StringComparison.OrdinalIgnoreCase) ? "K" : null;
                if (a.Equals("/S", StringComparison.OrdinalIgnoreCase) || a.Equals("/silent", StringComparison.OrdinalIgnoreCase)) o.Silent = true;
                else if (a.Equals("/NOLAUNCH", StringComparison.OrdinalIgnoreCase)) o.NoLaunch = true;
                else if (a.Equals("/NODESKTOP", StringComparison.OrdinalIgnoreCase)) o.NoDesktop = true;
                else if (a.StartsWith("/D=", StringComparison.OrdinalIgnoreCase)) o.Dir = a.Substring(3).Trim('"');
                else if (a.StartsWith("/STARTMENU=", StringComparison.OrdinalIgnoreCase)) o.StartMenuDir = a.Substring(11).Trim('"');
                else if (a.StartsWith("/DESKTOP=", StringComparison.OrdinalIgnoreCase)) o.DesktopDir = a.Substring(9).Trim('"');
                else if (a.StartsWith("/REGKEY=", StringComparison.OrdinalIgnoreCase)) o.RegKey = a.Substring(8).Trim('"');
            }
            return o;
        }
    }

    internal static class Installer
    {
        public const string DefaultRegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PortForge";
        public static string DefaultDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PortForge"); } }

        private static Stream Resource(string name) { return Assembly.GetExecutingAssembly().GetManifestResourceStream(name); }

        private static void MakeShortcut(string lnk, string target, string workDir, string description)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lnk));
            Type shell = Type.GetTypeFromProgID("WScript.Shell");
            object sh = Activator.CreateInstance(shell);
            object link = shell.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
            Type t = link.GetType();
            t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
            t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { workDir });
            t.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { description });
            t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { target + ",0" });
            t.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        }

        /// <summary>Installs everything. Throws with a plain message when something cannot be done. report is told the progress, 0 to 100.</summary>
        public static string Install(Options o, bool desktopShortcut, Action<int, string> report)
        {
            string dir = Path.GetFullPath(String.IsNullOrWhiteSpace(o.Dir) ? DefaultDir : o.Dir);
            string exe = Path.Combine(dir, "PortForge.exe");
            foreach (var p in Process.GetProcessesByName("PortForge"))
            {
                try { if (String.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase)) throw new IOException("PortForge is open. Close it, then run the installer again."); }
                catch (System.ComponentModel.Win32Exception) { }
                finally { p.Dispose(); }
            }
            Directory.CreateDirectory(dir);
            report(2, "Copying the app files...");
            using (var payload = Resource("payload.zip"))
            using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
            {
                int done = 0, total = zip.Entries.Count;
                foreach (var entry in zip.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(dir, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("The installer package contains an unsafe path.");
                    if (entry.FullName.EndsWith("/")) { Directory.CreateDirectory(target); }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (var input = entry.Open()) using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20)) input.CopyTo(output, 1 << 20);
                    }
                    done++; report(2 + (int)(80.0 * done / total), "Copying the app files...");
                }
            }
            if (!File.Exists(exe)) throw new IOException("The installer package does not contain PortForge.exe.");
            report(84, "Adding the uninstaller...");
            using (var u = Resource("uninstall.exe")) using (var output = new FileStream(Path.Combine(dir, "Uninstall.exe"), FileMode.Create, FileAccess.Write)) u.CopyTo(output);
            string version = FileVersionInfo.GetVersionInfo(exe).FileVersion ?? "";
            string startMenu = o.StartMenuDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
            string desktop = o.DesktopDir ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string regKey = o.RegKey ?? DefaultRegKey;
            report(88, "Creating shortcuts...");
            MakeShortcut(Path.Combine(startMenu, "PortForge.lnk"), exe, dir, "PortForge: turn Switch games into Windows games");
            if (desktopShortcut) MakeShortcut(Path.Combine(desktop, "PortForge.lnk"), exe, dir, "PortForge: turn Switch games into Windows games");
            report(94, "Registering the app with Windows...");
            using (var key = Registry.CurrentUser.CreateSubKey(regKey))
            {
                key.SetValue("DisplayName", "PortForge"); key.SetValue("DisplayVersion", version); key.SetValue("Publisher", "PortForge");
                key.SetValue("InstallLocation", dir); key.SetValue("DisplayIcon", exe + ",0");
                key.SetValue("UninstallString", "\"" + Path.Combine(dir, "Uninstall.exe") + "\"");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                long bytes = 0; foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) bytes += new FileInfo(f).Length;
                key.SetValue("EstimatedSize", (int)Math.Min(Int32.MaxValue, bytes / 1024), RegistryValueKind.DWord);
            }
            // What the uninstaller needs to know, and what tells the app it is installed (it then keeps its files out of the program folder).
            File.WriteAllLines(Path.Combine(dir, "installed.txt"), new[] { version, regKey, startMenu, desktopShortcut ? desktop : "" }, Encoding.UTF8);
            report(100, "Done.");
            return dir;
        }
    }

    internal sealed class SetupForm : Form
    {
        public static int ExitCode;
        private readonly Options options;
        private readonly TextBox folder = new TextBox();
        private readonly CheckBox desktop = new CheckBox { Text = "Add a shortcut to the desktop", Checked = true, AutoSize = true };
        private readonly CheckBox launch = new CheckBox { Text = "Open PortForge when the install finishes", Checked = true, AutoSize = true };
        private readonly Button install = new Button(), browse = new Button();
        private readonly ProgressBar bar = new ProgressBar { Dock = DockStyle.Top, Height = 8, Visible = false };
        private readonly Label status = new Label { AutoSize = true };
        private string installedTo;

        public SetupForm(Options o)
        {
            options = o;
            Color bg = Color.FromArgb(17, 21, 30), surface = Color.FromArgb(28, 34, 46), mint = Color.FromArgb(105, 226, 183), muted = Color.FromArgb(173, 185, 203);
            Text = "PortForge Setup"; ClientSize = new Size(600, 400); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            BackColor = bg; ForeColor = Color.WhiteSmoke; Font = new Font("Segoe UI", 10);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            var logo = new PictureBox { Left = 28, Top = 28, Width = 96, Height = 96, SizeMode = PictureBoxSizeMode.Zoom };
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png")) if (s != null) logo.Image = Image.FromStream(s);
            var title = new Label { Text = "Install PortForge", Left = 144, Top = 34, AutoSize = true, Font = new Font("Segoe UI", 20, FontStyle.Bold), ForeColor = Color.White };
            var about = new Label { Text = "Turn Switch game files into Windows games. This puts PortForge on your PC, with a Start menu shortcut and an uninstaller. Your games and saves are never touched.", Left = 146, Top = 78, Width = 420, Height = 60, ForeColor = muted };
            var where = new Label { Text = "Install to", Left = 30, Top = 160, AutoSize = true, ForeColor = muted };
            folder.SetBounds(30, 186, 440, 28); folder.Text = String.IsNullOrWhiteSpace(o.Dir) ? Installer.DefaultDir : o.Dir; folder.BackColor = surface; folder.ForeColor = Color.WhiteSmoke; folder.BorderStyle = BorderStyle.FixedSingle;
            browse.Text = "Browse"; browse.SetBounds(480, 184, 90, 32); Style(browse, surface, false, mint, bg);
            browse.Click += delegate { using (var d = new FolderBrowserDialog { Description = "Choose the folder to install PortForge in" }) if (d.ShowDialog(this) == DialogResult.OK) folder.Text = Path.Combine(d.SelectedPath, "PortForge"); };
            desktop.Left = 30; desktop.Top = 232; launch.Left = 30; launch.Top = 262;
            status.Left = 30; status.Top = 300; status.MaximumSize = new Size(540, 0); status.ForeColor = muted;
            bar.Left = 30; bar.Top = 330; bar.Width = 540; bar.Dock = DockStyle.None;
            install.Text = "Install"; install.SetBounds(440, 346, 130, 38); Style(install, mint, true, mint, bg);
            install.Click += delegate { Run(); };
            Controls.AddRange(new Control[] { logo, title, about, where, folder, browse, desktop, launch, status, bar, install });
            AcceptButton = install;
        }

        private static void Style(Button b, Color back, bool primary, Color mint, Color bg)
        {
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.BackColor = back; b.ForeColor = primary ? bg : Color.WhiteSmoke; b.Cursor = Cursors.Hand;
        }

        private void Run()
        {
            if (installedTo != null) { Finish(); return; }
            options.Dir = folder.Text.Trim();
            install.Enabled = browse.Enabled = folder.Enabled = desktop.Enabled = launch.Enabled = false;
            bar.Visible = true; bar.Style = ProgressBarStyle.Continuous; status.Text = "Starting...";
            bool wantDesktop = desktop.Checked;
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, e) => { e.Result = Installer.Install(options, wantDesktop, (pct, text) => { BeginInvoke(new Action(() => { bar.Value = Math.Max(0, Math.Min(100, pct)); status.Text = text; })); }); };
            worker.RunWorkerCompleted += (s, e) =>
            {
                if (e.Error != null) { status.ForeColor = Color.FromArgb(255, 157, 148); status.Text = e.Error.Message; install.Enabled = browse.Enabled = folder.Enabled = desktop.Enabled = launch.Enabled = true; return; }
                installedTo = (string)e.Result; status.ForeColor = Color.FromArgb(105, 226, 183); status.Text = "PortForge is installed in " + installedTo + ".";
                install.Text = launch.Checked ? "Open PortForge" : "Close"; install.Enabled = true;
            };
            worker.RunWorkerAsync();
        }

        private void Finish()
        {
            if (launch.Checked) { try { Process.Start(new ProcessStartInfo(Path.Combine(installedTo, "PortForge.exe")) { WorkingDirectory = installedTo, UseShellExecute = true }); } catch (Exception) { } }
            Close();
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var o = Options.Parse(args);
            if (o.Silent)
            {
                try
                {
                    string dir = Installer.Install(o, !o.NoDesktop, (p, t) => { });
                    if (!o.NoLaunch) Process.Start(new ProcessStartInfo(Path.Combine(dir, "PortForge.exe")) { WorkingDirectory = dir, UseShellExecute = true });
                    return 0;
                }
                catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
            }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm(o));
            return SetupForm.ExitCode;
        }
    }
}
