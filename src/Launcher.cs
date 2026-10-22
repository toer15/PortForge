using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace PortForge
{
    internal static class GameLauncher
    {
        [STAThread]
        private static int Main()
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            string title = "PortForge game";
            try
            {
                var manifest = GameManifest.Load(Path.Combine(root, "portforge.xml"));
                title = manifest.Title;
                var start = manifest.Resolve(root);
                Log(root, "Starting " + title);
                using (var process = Process.Start(start))
                {
                    if (process == null) throw new IOException("Windows could not start the selected runtime.");
                    process.WaitForExit();
                    int code = process.ExitCode;
                    Log(root, "Runtime exited with code " + code);
                    return code;
                }
            }
            catch (Exception error)
            {
                Log(root, error.ToString());
                MessageBox.Show(error.Message + "\n\nLogs are in %LOCALAPPDATA%\\PortForge\\Logs.", title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static void Log(string root, string message)
        {
            try
            {
                string id;
                using (var hash = SHA256.Create()) id = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(root.ToUpperInvariant()))).Replace("-", "").Substring(0, 16);
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PortForge", "Logs");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, id + ".log"), DateTime.UtcNow.ToString("u") + "  " + message + Environment.NewLine);
            }
            catch { /* Logging must never prevent game launch. */ }
        }
    }
}
