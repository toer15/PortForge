using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace PortForge
{
    public sealed class GlobalSettingsPanel : UserControl
    {
        private readonly CheckBox enabled = Check("Apply these settings to every game", true), fullscreen = Check("Start fullscreen", false),
            cursor = Check("Hide cursor during fullscreen play", true), indicator = Check("Show save activity", true),
            vsync = Check("VSync", true), smoothing = Check("Smooth jagged edges", false),
            graph = Check("Show frame-time graph in game (F7 toggles it)", false), trace = Check("Record frame-time trace (to find hitches)", false);
        private readonly ComboBox resolution = Choice("Resolution", "Auto (game chooses)", "720p", "1080p", "1440p", "4K (2160p)"),
            upscale = Choice("Upscaling", "Linear", "AMD FSR 1", "PortForge FSR 3 (2x, from scratch)"),
            mode = Choice("Rendering mode", "TV - game chooses its layout", "Handheld - lighter rendering where supported"),
            fps = Choice("Frame limit", "Game speed (recommended)", "30", "60", "120", "144", "240", "Unlimited");
        private readonly Label status = new Label { AutoSize = true, ForeColor = Color.FromArgb(105,226,183), AccessibleName = "Global settings status", Text = "READY - Settings apply on the next game launch." };
        private readonly Label fpsNote = new Label { AutoSize = true, MaximumSize = new Size(820, 0), Font = new Font("Segoe UI", 9), Margin = new Padding(0, 0, 0, 8) };
        private readonly Label upscaleNote = new Label { AutoSize = true, MaximumSize = new Size(820, 0), Font = new Font("Segoe UI", 9), Margin = new Padding(0, 0, 0, 12) };
        private GlobalSettings current;
        public GlobalSettingsPanel()
        {
            Dock = DockStyle.Fill; BackColor = Color.FromArgb(17,21,30); ForeColor = Color.WhiteSmoke;
            var layout = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24) };
            var scroll = new Panel { Dock=DockStyle.Fill, AutoScroll=true };
            scroll.Controls.Add(layout); Controls.Add(scroll);
            AddText(layout, "Settings for every game", 20);
            layout.Controls.Add(status);
            AddText(layout, "Save once. Every game using Engine 1.29 or newer reads these settings when opened, even with PortForge closed. Restart running games to apply changes. The game always runs at its own speed. The FPS limit and PortForge FSR 3 only change how many pictures are shown. Esc shows Engine version, Display FPS and Limit on one line — if Engine is older than 1.29, update the app Engine folder and use Doctor → Engine (or close the game so the Doctor can install it).", 10);
            layout.Controls.Add(enabled);
            AddChoice(layout, "Resolution", resolution);
            AddText(layout, "720p through 4K size the window/swapchain; FSR / PortForge FSR 3 enlarge the finished image. Internal game buffers stay native (scaling them broke titles like Breath of the Wild). Auto keeps the game's layout.", 10);
            AddChoice(layout, "Upscaling", upscale);
            upscaleNote.Text = "PortForge FSR 3 is built from scratch from FSR 3 / AFMF ideas (optical flow, disocclusion, HUD lock) plus AMD FSR 1 spatial upscale — not the AMD FidelityFX SDK. Esc/Home → PortForge FSR 3 On/Off. It adds a picture between the game's frames (a 30 FPS game can show about 60, a 60 FPS game about 120) without speeding the game up. Esc Display FPS shows that live rate.";
            layout.Controls.Add(upscaleNote);
            AddChoice(layout, "Rendering mode", mode);
            AddChoice(layout, "FPS limit", fps);
            layout.Controls.Add(fpsNote);
            foreach (var check in new[] {vsync,smoothing,fullscreen,cursor,indicator,graph,trace}) layout.Controls.Add(check);
            AddText(layout, "The cursor returns in the Escape menu and when another window or the Windows menu has focus. Save activity follows the game's writes; it does not create a save point.", 10);
            AddText(layout, "Save-anywhere is not supported: games only save through their own save menus and autosaves.", 10);
            var actions = new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Bottom,Padding=new Padding(24,8,24,12)};
            var save = new ReadableButton {Text="Save for all games",AutoSize=true,Height=36,AccessibleName="Save global settings"};
            var reset = new ReadableButton {Text="Reset",AutoSize=true,Height=36};
            actions.Controls.Add(save); actions.Controls.Add(reset); Controls.Add(actions);
            var tools = new ReadableButton { Text="Game tools and diagnostics",AutoSize=true };
            tools.Click += delegate { using(var window=new GameSettingsWindow(GameSettingsWindow.RememberedGame())) window.ShowDialog(this); };
            layout.Controls.Add(tools);
            var traces = new ReadableButton { Text="View frame-time trace",AutoSize=true };
            traces.Click += delegate { using(var window=new FrameTraceWindow(GameSettingsWindow.RememberedGame())) window.ShowDialog(this); };
            layout.Controls.Add(traces);
            AddText(layout, "Global settings take priority over the per-game settings in Game tools. Turn global settings off and save to use each game's own choices again.", 10);
            upscale.SelectedIndexChanged += delegate { RefreshNotes(); };
            fps.SelectedIndexChanged += delegate { RefreshNotes(); };
            save.Click += delegate {
                try {
                    ReadInto(current);
                    current.Save(GlobalSettings.FilePath);
                    status.Text="DONE - " + (current.Enabled ? "Saved for every updated game. Applies at next launch." : "Global settings off. Games use their own settings next launch.");
                    RefreshNotes();
                } catch (Exception e) { status.Text="ERROR - " + e.Message; }
            };
            reset.Click += delegate { ShowSettings(new GlobalSettings()); status.Text="READY - Defaults selected. Save to apply."; };
            try { ShowSettings(GlobalSettings.Load(GlobalSettings.FilePath)); }
            catch(Exception e) { ShowSettings(new GlobalSettings()); status.Text="ERROR - Could not load settings: " + e.Message; }
        }
        private void ReadInto(GlobalSettings s)
        {
            s.Enabled=enabled.Checked; s.HideCursor=cursor.Checked; s.SaveIndicator=indicator.Checked;
            s.FrameTimeGraph=graph.Checked; s.FrameTrace=trace.Checked;
            s.DisplayResolution = ResolutionKey(resolution.SelectedIndex);
            s.UpscaleMode = upscale.SelectedIndex;
            s.ApplyUpscaleMode();
            s.Graphics.Docked=mode.SelectedIndex==0;
            s.Graphics.Fullscreen=fullscreen.Checked; s.Graphics.VSync=vsync.Checked; s.Graphics.PixelSmoothing=smoothing.Checked;
            s.Graphics.FpsLimit=fps.SelectedIndex==0 ? "game" : fps.SelectedIndex==6 ? "0" : fps.Text;
        }
        private void ShowSettings(GlobalSettings s)
        {
            current=s; enabled.Checked=s.Enabled; cursor.Checked=s.HideCursor; indicator.Checked=s.SaveIndicator;
            graph.Checked=s.FrameTimeGraph; trace.Checked=s.FrameTrace;
            resolution.SelectedIndex = ResolutionIndex(s.DisplayResolution);
            upscale.SelectedIndex = Math.Max(0, Math.Min(2, s.UpscaleMode));
            fullscreen.Checked=s.Graphics.Fullscreen; vsync.Checked=s.Graphics.VSync; smoothing.Checked=s.Graphics.PixelSmoothing;
            mode.SelectedIndex=s.Graphics.Docked?0:1;
            fps.SelectedIndex=s.Graphics.FpsLimit=="game"?0:s.Graphics.FpsLimit=="0"?6:fps.Items.IndexOf(s.Graphics.FpsLimit);
            if(fps.SelectedIndex<0) { fps.Items.Add(s.Graphics.FpsLimit); fps.SelectedIndex=fps.Items.Count-1; }
            RefreshNotes();
        }
        private void RefreshNotes()
        {
            bool fsr3 = upscale.SelectedIndex == 2;
            string limit = fps.SelectedIndex == 0 ? "game" : fps.SelectedIndex == 6 ? "0" : fps.Text;
            int n;
            if (fsr3 && Int32.TryParse(limit, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n > 0)
                fpsNote.Text = n + " does not change game speed. It only drops pictures when it is below the game's pace. With PortForge FSR 3 the screen can show about twice that pace (a 30 FPS game about 60, a 60 FPS game about 120), not twice " + n + ".";
            else if (fsr3)
                fpsNote.Text = "PortForge FSR 3 is on. The game stays at its own pace and the display can show about twice that (30 → ~60, 60 → ~120). A number below the game's pace shows fewer frames. A higher number does not speed the game up.";
            else
                fpsNote.Text = "Speed lock keeps the game at its own pace when the cap is higher, and Display FPS shows that real rate. A lower cap drops frames (30 does not stay 60) without slowing the game.";
        }
        private static string ResolutionKey(int index)
        {
            switch (index) { case 1: return "720"; case 2: return "1080"; case 3: return "1440"; case 4: return "2160"; default: return "auto"; }
        }
        private static int ResolutionIndex(string key)
        {
            if (key == "720") return 1; if (key == "1080") return 2; if (key == "1440") return 3; if (key == "2160") return 4; return 0;
        }
        private static CheckBox Check(string text,bool value) {return new CheckBox{Text=text,AccessibleName=text,Checked=value,AutoSize=true,Margin=new Padding(0,5,0,5)};}
        private static ComboBox Choice(string name,params string[] values) {var c=new ComboBox{AccessibleName=name,DropDownStyle=ComboBoxStyle.DropDownList,Width=440};c.Items.AddRange(values);c.SelectedIndex=0;return c;}
        private static void AddText(Control root,string text,int size) {root.Controls.Add(new Label{Text=text,AutoSize=true,MaximumSize=new Size(820,0),Font=new Font("Segoe UI",size),Margin=new Padding(0,4,0,12)});}
        private static void AddChoice(Control root,string name,ComboBox choice) {AddText(root,name,10);var row=new FlowLayoutPanel{AutoSize=true};row.Controls.Add(choice);var clear=new ReadableButton{Text="Clear",AutoSize=true,AccessibleName="Clear "+name};clear.Click+=delegate{choice.SelectedIndex=0;};row.Controls.Add(clear);root.Controls.Add(row);}
    }
}
