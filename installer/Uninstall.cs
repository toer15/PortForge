using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

// Removes PortForge: its shortcuts, its entry under "Installed apps" and its program files. The player's converted games, downloads and
// exports are kept unless they ask for them to go too.
namespace PortForgeUninstall
{
    internal static class Program
    {
        private static readonly string[] UserFolders = { "NativeGames", "Downloads", "Exports" };

        [STAThread]
        private static int Main(string[] args)
        {
            bool silent = args.Any(a => a.Equals("/S", StringComparison.OrdinalIgnoreCase)), everything = args.Any(a => a.Equals("/ALL", StringComparison.OrdinalIgnoreCase));
            string dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string info = Path.Combine(dir, "installed.txt");
            if (!File.Exists(info)) { if (!silent) MessageBox.Show("This does not look like an installed copy of PortForge, so nothing was removed.", "PortForge", MessageBoxButtons.OK, MessageBoxIcon.Information); return 2; }
            string[] lines = File.ReadAllLines(info);
            string regKey = lines.Length > 1 && lines[1].Length > 0 ? lines[1] : @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PortForge";
            string startMenu = lines.Length > 2 ? lines[2] : "", desktop = lines.Length > 3 ? lines[3] : "";
            if (!silent)
            {
                Application.EnableVisualStyles();
                var answer = MessageBox.Show("Remove PortForge from this PC?\n\nThe games you converted and your downloads are kept in:\n" + dir + "\n\nChoose No to also delete those, or Cancel to stop.", "Uninstall PortForge", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
                if (answer == DialogResult.Cancel) return 1;
                everything = answer == DialogResult.No;
                if (everything && MessageBox.Show("This deletes every converted game, download and export in the PortForge folder. This cannot be undone. Delete them?", "Uninstall PortForge", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return 1;
            }
            foreach (var p in Process.GetProcessesByName("PortForge"))
            {
                try { if (p.MainModule.FileName.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) { p.CloseMainWindow(); if (!p.WaitForExit(5000)) p.Kill(); } } catch (Exception) { } finally { p.Dispose(); }
            }
            foreach (string link in new[] { startMenu.Length > 0 ? Path.Combine(startMenu, "PortForge.lnk") : "", desktop.Length > 0 ? Path.Combine(desktop, "PortForge.lnk") : "" })
                try { if (link.Length > 0 && File.Exists(link)) File.Delete(link); } catch (IOException) { }
            try { Registry.CurrentUser.DeleteSubKeyTree(regKey, false); } catch (Exception) { }
            // Program files go; the player's folders stay unless they asked for them too. This program is the one file Windows will not let go of yet.
            string self = Process.GetCurrentProcess().MainModule.FileName;
            foreach (string file in Directory.GetFiles(dir)) { if (!String.Equals(file, self, StringComparison.OrdinalIgnoreCase)) try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            foreach (string sub in Directory.GetDirectories(dir))
            {
                bool keep = !everything && UserFolders.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase);
                if (!keep) try { Directory.Delete(sub, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            // After this program exits: delete it, then the folder if nothing of the player's is left in it.
            string cmd = "/c ping 127.0.0.1 -n 3 > nul & del /f /q \"" + self + "\" & rmdir \"" + dir + "\"";
            Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            if (!silent) MessageBox.Show("PortForge was removed." + (everything ? "" : "\nYour converted games and downloads are still in " + dir + "."), "PortForge", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
    }
}
