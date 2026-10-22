using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PortForge
{
    /// <summary>The PortForge logo as the icon of every window and of the tray.</summary>
    public static class AppIcon
    {
        private static Icon cached;
        public static Icon Get()
        {
            if (cached == null) { try { cached = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { } }
            return cached;
        }
        public static void Apply(Form form) { var icon = Get(); if (icon != null) form.Icon = icon; }
    }

    /// <summary>Where the app keeps what is the player's. A copy installed by the setup program keeps its program folder clean: games and downloads go to the player's own folders.</summary>
    public static class AppPaths
    {
        public static string Base { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        public static bool Installed { get { return File.Exists(Path.Combine(Base, "installed.txt")); } }

        public static string DefaultGamesFolder
        {
            get
            {
                string own = Path.Combine(Base, "NativeGames");
                if (!Installed || Directory.Exists(own)) return own;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PortForge Games");
            }
        }

        public static string DefaultDownloadsFolder
        {
            get
            {
                string own = Path.Combine(Base, "Downloads");
                if (!Installed || Directory.Exists(own)) return own;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "PortForge");
            }
        }

        /// <summary>After an update, the version Windows lists under Installed apps follows the program that is really there.</summary>
        public static void RefreshRegistration()
        {
            try
            {
                if (!Installed) return;
                string[] lines = File.ReadAllLines(Path.Combine(Base, "installed.txt"));
                if (lines.Length < 2 || lines[1].Length == 0) return;
                string version = System.Diagnostics.FileVersionInfo.GetVersionInfo(Application.ExecutablePath).FileVersion;
                using (var key = Registry.CurrentUser.OpenSubKey(lines[1], true)) if (key != null && version != null) key.SetValue("DisplayVersion", version);
                lines[0] = version; File.WriteAllLines(Path.Combine(Base, "installed.txt"), lines);
            }
            catch (Exception) { }
        }
    }
}
