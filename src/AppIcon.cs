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

    /// <summary>
    /// Smooth painting for a window: Windows paints it and everything in it off screen and shows the result at once, so a text change or a
    /// panel resize no longer flickers.
    /// </summary>
    public static class SmoothPaint
    {
        public static CreateParams Composited(CreateParams cp) { cp.ExStyle |= 0x02000000; return cp; } // WS_EX_COMPOSITED
    }

    /// <summary>Windows ignores a disabled flat button's ForeColor; paint its label with readable contrast.</summary>
    public sealed class ReadableButton : Button
    {
        public ReadableButton() { FlatStyle = FlatStyle.Flat; }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Enabled) { base.OnPaint(e); return; }
            e.Graphics.Clear(BackColor);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Color.FromArgb(159, 170, 188),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
    }

    /// <summary>
    /// Locks a window's inputs while a task runs without disabling whole panels: a disabled panel draws all its text in near-black on this
    /// dark theme. Boxes become read-only and buttons stop answering; labels, check boxes and lists stay exactly as they look. Unlock puts back
    /// what each one was before (a button that was off stays off).
    /// </summary>
    public sealed class UiLock : IMessageFilter, IDisposable
    {
        private readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Control, bool>> saved = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Control, bool>>();
        private readonly System.Collections.Generic.HashSet<Control> inputs = new System.Collections.Generic.HashSet<Control>();
        private bool released;
        public static UiLock Lock(Control root) { var l = new UiLock(); l.Walk(root); Application.AddMessageFilter(l); return l; }
        private void Walk(Control root)
        {
            foreach (Control c in root.Controls)
            {
                var box = c as TextBox; var button = c as Button;
                if (box != null) { saved.Add(new System.Collections.Generic.KeyValuePair<Control, bool>(box, box.ReadOnly)); box.ReadOnly = true; }
                else if (button != null) { saved.Add(new System.Collections.Generic.KeyValuePair<Control, bool>(button, button.Enabled)); button.Enabled = false; }
                else if (c is ComboBox || c is NumericUpDown || c is CheckBox) inputs.Add(c);
                if (c.HasChildren) Walk(c);
            }
        }
        public bool PreFilterMessage(ref Message m)
        {
            // Keep the normal theme while blocking edits, including a spinner's child edit/buttons.
            if (released || !((m.Msg >= 0x100 && m.Msg <= 0x109) || (m.Msg >= 0x201 && m.Msg <= 0x20e))) return false;
            for (Control c = Control.FromChildHandle(m.HWnd); c != null; c = c.Parent)
                if (inputs.Contains(c)) return true;
            return false;
        }
        public void Unlock()
        {
            if (released) return;
            released = true; Application.RemoveMessageFilter(this);
            foreach (var kv in saved)
            {
                if (kv.Key.IsDisposed) continue;
                var box = kv.Key as TextBox;
                if (box != null) box.ReadOnly = kv.Value; else ((Button)kv.Key).Enabled = kv.Value;
            }
            saved.Clear();
            inputs.Clear();
        }
        public void Dispose() { Unlock(); }
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
