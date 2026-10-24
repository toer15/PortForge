using System;
using System.Drawing;
using System.Windows.Forms;

namespace PortForge
{
    public sealed class GlobalSettingsPanel : UserControl
    {
        private readonly CheckBox enabled = Check("Apply these settings to every game", true), fullscreen = Check("Start fullscreen", false),
            cursor = Check("Hide cursor during fullscreen play", true), indicator = Check("Show save activity", true),
            vsync = Check("VSync", true), smoothing = Check("Smooth jagged edges", false),
            graph = Check("Show frame-time graph in game (F7 toggles it)", false), trace = Check("Record frame-time trace (to find hitches)", false);
        private readonly ComboBox mode = Choice("Rendering mode", "TV - game chooses its resolution", "Handheld - lighter rendering where supported"),
            upscaler = Choice("Upscaler", "Linear", "AMD FSR 1"), framegen = Choice("Frame generation", "Off", "PortForge 2x - experimental"), fps = Choice("Frame limit", "Game speed (recommended)", "60", "120", "144", "240", "Unlimited");
        private readonly Label status = new Label { AutoSize = true, ForeColor = Color.FromArgb(105,226,183), AccessibleName = "Global settings status", Text = "READY - Settings apply on the next game launch." };
        private readonly Label framegenNote = new Label { AutoSize = true, MaximumSize = new Size(820, 0), Font = new Font("Segoe UI", 9), Margin = new Padding(0, 0, 0, 12) };
        private GlobalSettings current;
        public GlobalSettingsPanel()
        {
            Dock = DockStyle.Fill; BackColor = Color.FromArgb(17,21,30); ForeColor = Color.WhiteSmoke;
            var layout = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24) };
            var scroll = new Panel { Dock=DockStyle.Fill, AutoScroll=true };
            scroll.Controls.Add(layout); Controls.Add(scroll);
            AddText(layout, "Settings for every game", 20);
            layout.Controls.Add(status);
            AddText(layout, "Save once. Every game using Engine 1.17 or newer reads these settings when opened, even with PortForge closed. Restart running games to apply changes.", 10);
            layout.Controls.Add(enabled);
            AddChoice(layout, "Rendering mode", mode); AddChoice(layout, "Upscaling", upscaler); AddChoice(layout, "FPS limit", fps);
            AddText(layout, "Internal resolution is chosen by the game in TV or handheld mode. Universal native 720p / 1080p / 1440p / 4K overrides are not supported. FSR 1 enlarges the rendered image to fit the window.", 10);
            AddChoice(layout, "Frame generation (Engine 1.18 or newer)", framegen);
            framegenNote.Text = "Adds an estimated frame between game frames to smooth motion. Experimental: artifacts and extra latency are possible. It needs GPU headroom and a display faster than the game. This is PortForge interpolation, not AMD FSR 3. Simulation and input stay at the game's rate. No Radeon driver settings are changed.";
            layout.Controls.Add(framegenNote);
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
            save.Click += delegate {
                try {
                    current.Enabled=enabled.Checked; current.HideCursor=cursor.Checked; current.SaveIndicator=indicator.Checked;
                    current.FrameTimeGraph=graph.Checked; current.FrameTrace=trace.Checked;
                    current.FrameGeneration=framegen.SelectedIndex==1;
                    current.Graphics.Docked=mode.SelectedIndex==0; current.Graphics.Upscaler=upscaler.SelectedIndex;
                    current.Graphics.Fullscreen=fullscreen.Checked; current.Graphics.VSync=vsync.Checked; current.Graphics.PixelSmoothing=smoothing.Checked;
                    current.Graphics.FpsLimit=fps.SelectedIndex==0 ? "game" : fps.SelectedIndex==5 ? "0" : fps.Text;
                    current.Save(GlobalSettings.FilePath);
                    status.Text="DONE - " + (current.Enabled ? "Saved for every updated game. Applies at next launch." : "Global settings off. Games use their own settings next launch.");
                } catch (Exception e) { status.Text="ERROR - " + e.Message; }
            };
            reset.Click += delegate { ShowSettings(new GlobalSettings()); status.Text="READY - Defaults selected. Save to apply."; };
            try { ShowSettings(GlobalSettings.Load(GlobalSettings.FilePath)); }
            catch(Exception e) { ShowSettings(new GlobalSettings()); status.Text="ERROR - Could not load settings: " + e.Message; }
        }
        private void ShowSettings(GlobalSettings s)
        {
            current=s; enabled.Checked=s.Enabled; cursor.Checked=s.HideCursor; indicator.Checked=s.SaveIndicator;
            graph.Checked=s.FrameTimeGraph; trace.Checked=s.FrameTrace;
            framegen.SelectedIndex=s.FrameGeneration?1:0;
            fullscreen.Checked=s.Graphics.Fullscreen; vsync.Checked=s.Graphics.VSync; smoothing.Checked=s.Graphics.PixelSmoothing;
            mode.SelectedIndex=s.Graphics.Docked?0:1; upscaler.SelectedIndex=s.Graphics.Upscaler;
            fps.SelectedIndex=s.Graphics.FpsLimit=="game"?0:s.Graphics.FpsLimit=="0"?5:fps.Items.IndexOf(s.Graphics.FpsLimit);
            if(fps.SelectedIndex<0) { fps.Items.Add(s.Graphics.FpsLimit); fps.SelectedIndex=fps.Items.Count-1; }
        }
        private static CheckBox Check(string text,bool value) {return new CheckBox{Text=text,AccessibleName=text,Checked=value,AutoSize=true,Margin=new Padding(0,5,0,5)};}
        private static ComboBox Choice(string name,params string[] values) {var c=new ComboBox{AccessibleName=name,DropDownStyle=ComboBoxStyle.DropDownList,Width=440};c.Items.AddRange(values);c.SelectedIndex=0;return c;}
        private static void AddText(Control root,string text,int size) {root.Controls.Add(new Label{Text=text,AutoSize=true,MaximumSize=new Size(820,0),Font=new Font("Segoe UI",size),Margin=new Padding(0,4,0,12)});}
        private static void AddChoice(Control root,string name,ComboBox choice) {AddText(root,name,10);var row=new FlowLayoutPanel{AutoSize=true};row.Controls.Add(choice);var clear=new ReadableButton{Text="Clear",AutoSize=true,AccessibleName="Clear "+name};clear.Click+=delegate{choice.SelectedIndex=0;};row.Controls.Add(clear);root.Controls.Add(row);}
    }
}
