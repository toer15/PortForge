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
            graph = Check("Show frame-time graph in game (F7 toggles it)", false), trace = Check("Record frame-time trace (to find hitches)", false),
            trueRes = Check("Match screen-shaped render targets to the selected resolution (experimental)", true),
            lowLatency = Check("Low latency: shorter VSync queue, game ahead of other programs", true),
            amdFg = Check("Generate frames with AMD FidelityFX SDK 1.1.4 (AMD's published FSR 3 optical flow and frame interpolation)", true);
        private readonly ComboBox resolution = Choice("Resolution", "Auto (game chooses)", "720p", "1080p", "1440p", "4K (2160p)"),
            upscale = Choice("Upscaling", "Linear", "AMD FSR 1", "AMD FSR 3 frame generation (FSR 1 + FidelityFX frame interpolation)"),
            mode = Choice("Rendering mode", "TV - game chooses its layout", "Handheld - lighter rendering where supported"),
            fps = Choice("Frame limit", "Game speed (console pace)", "30", "60", "120", "144", "240", "Maximum (this PC's speed)");
        private readonly Label status = new Label { AutoSize = true, ForeColor = Color.FromArgb(105,226,183), AccessibleName = "Global settings status", Text = "READY - Settings apply on the next game launch." };
        private readonly Label fpsNote = new Label { AutoSize = true, MaximumSize = new Size(820, 0), Font = new Font("Segoe UI", 9), Margin = new Padding(0, 0, 0, 8), AccessibleName = "Frame limit note" };
        private readonly Label upscaleNote = new Label { AutoSize = true, MaximumSize = new Size(820, 0), Font = new Font("Segoe UI", 9), Margin = new Padding(0, 0, 0, 12) };
        private readonly Label resolutionNote = new Label { AutoSize = true, MaximumSize = new Size(820, 0), Font = new Font("Segoe UI", 9), Margin = new Padding(0, 0, 0, 8), ForeColor = Color.FromArgb(105,226,183) };
        private GlobalSettings current;
        public GlobalSettingsPanel()
        {
            Dock = DockStyle.Fill; BackColor = Color.FromArgb(17,21,30); ForeColor = Color.WhiteSmoke;
            var layout = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24) };
            var scroll = new Panel { Dock=DockStyle.Fill, AutoScroll=true };
            scroll.Controls.Add(layout); Controls.Add(scroll);
            AddText(layout, "Settings for every game", 20);
            layout.Controls.Add(status);
            AddText(layout, "Save once. Every game using Engine 1.34 or newer reads these settings when it opens, even with PortForge closed. Restart a running game to apply changes. Since 0.67 every game is set to the fastest rate this PC can render, up to 240 FPS (240, AMD FSR 3 and the in-game frame-time graph), with no per-game setup - change the frame limit below, or turn these settings off, to choose something else. The FPS limit is the game's own frame rate: a number caps how fast the game runs (as a PC game's FPS cap does) and AMD FSR 3 fills the pictures the game cannot make, Maximum lets the game run as fast as this PC can render it with nothing capping it, and Game speed keeps the console's own 30 or 60 and lets AMD FSR 3 frame generation smooth it instead. In game, Esc shows the Engine version, the live Display FPS, its target and the Limit.", 10);
            layout.Controls.Add(enabled);
            AddChoice(layout, "Resolution", resolution);
            layout.Controls.Add(resolutionNote);
            layout.Controls.Add(trueRes);
            AddText(layout, "Engine 1.42 renders screen-shaped render targets at the selected 16:9 output. It records each game's own output height (many games draw 900 lines in TV mode) during its first run, including the warm-up after an engine install, and scales from that height from the next start. Data-shaped targets stay native. Dynamic-resolution games may differ, so this is not a per-game guarantee of exact internal pixels. Scaling up costs GPU; scaling down loses detail but can reduce GPU work. Turn this off if a game shows artifacts.", 10);
            AddChoice(layout, "Upscaling", upscale);
            upscaleNote.Text = "AMD FSR 3 frame generation = AMD FSR 1 upscaling plus the frame interpolation AMD publishes in the FidelityFX SDK 1.1.4 (its optical flow and frame interpolation shaders, MIT). Switch games give the PC no motion vectors or depth, so AMD's optical flow finds the motion and depth is flat. FSR 3's temporal upscaler needs those game motion vectors and cannot run on Switch games, so FSR 1 does the upscaling. One generated picture sits between each two real frames (a 30 FPS game shows 60), and up to three a frame where the limit leaves room for them. With Game speed it never speeds the game up; with a limit or Maximum the limit is the game's own frame rate, so its world runs faster than the console whenever that is above the console's pace. Turn it on or off in game from Esc.";
            layout.Controls.Add(upscaleNote);
            layout.Controls.Add(amdFg);
            AddChoice(layout, "Rendering mode", mode);
            fps.DropDownStyle = ComboBoxStyle.DropDown; /* type any limit from 10 to 1000, or pick a preset */
            AddChoice(layout, "FPS limit (type any number from 10 to 1000)", fps);
            layout.Controls.Add(fpsNote);
            foreach (var check in new[] {vsync,lowLatency,smoothing,fullscreen,cursor,indicator,graph,trace}) layout.Controls.Add(check);
            AddText(layout, "Low latency reduces the VSync image queue when supported and gives the game a higher Windows scheduling priority. It can reduce queue delay, but frame generation and GPU load can still add latency.", 10);
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
            fps.TextChanged += delegate { RefreshNotes(); };
            resolution.SelectedIndexChanged += delegate { RefreshNotes(); };
            mode.SelectedIndexChanged += delegate { RefreshNotes(); };
            trueRes.CheckedChanged += delegate { RefreshNotes(); };
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
            if (!String.IsNullOrEmpty(GlobalSettings.LastAutomaticNotice) && status.Text.StartsWith("READY"))
                status.Text = "READY - " + GlobalSettings.LastAutomaticNotice;
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
            s.Graphics.FpsLimit=FpsKey();
            s.TrueResolution=trueRes.Checked; s.LowLatency=lowLatency.Checked; s.AmdFrameGeneration=amdFg.Checked;
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
            if(fps.SelectedIndex<0) fps.Text=s.Graphics.FpsLimit;
            trueRes.Checked=s.TrueResolution; lowLatency.Checked=s.LowLatency; amdFg.Checked=s.AmdFrameGeneration;
            RefreshNotes();
        }
        /* What each choice does, in the engine's own rules (Engine 1.48). Game speed (SNX_FPS_GAME) waits at the
           console's pace and the screen gets its frames, doubled by frame generation. A number is the game's own
           frame rate (present_period waits 1/limit), with FSR 3 filling pictures up to it when the game cannot
           reach it. Maximum (0) is no wait at all: the game runs as fast as this PC renders it and the screen gets
           those frames, doubled by frame generation. The monitor's refresh caps the pictures under VSync. */
        private void RefreshNotes()
        {
            var preview = new GlobalSettings { DisplayResolution = ResolutionKey(resolution.SelectedIndex), TrueResolution = trueRes.Checked };
            preview.Graphics.Docked = mode.SelectedIndex == 0;
            resolutionNote.Text = preview.RenderResolutionNote();
            bool fsr3 = upscale.SelectedIndex == 2;
            string limit = FpsKey();
            int n;
            if (!Int32.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out n)) n = -1;
            if (limit != "game" && limit != "0" && (n < 10 || n > 1000)) { fpsNote.Text = "Type a whole number from 10 to 1000, or pick Game speed or Maximum."; return; }
            if (limit == "game")
                fpsNote.Text = "Game speed keeps the game's own console timing: a 30 FPS game (like Zelda BotW) stays at 30 and a 60 FPS game at 60, with its physics, timers and music untouched. " +
                    (fsr3 ? "AMD FSR 3 frame generation plays those frames twice, so the screen gets 60 pictures a second in a 30 FPS game and 120 in a 60 FPS game, never more than your monitor refreshes with VSync on. No frame is shown twice."
                          : "The screen shows the game's own frames.");
            else if (limit == "0")
                fpsNote.Text = "Maximum runs the game itself as fast as this PC can render it, over the console's own 30 or 60, so its physics, timers and music follow its frames and the world runs faster than real time. Esc shows the rate it reaches; pick a number to cap it. " +
                    (fsr3 ? "AMD FSR 3 frame generation doubles the pictures the game makes, up to your monitor's refresh."
                          : "The screen shows every frame the game makes.");
            else
                fpsNote.Text = "A limit of " + n + " is the game's own frame rate, as a PC game's FPS cap is: it runs at up to " + n + " FPS (or its console 30 or 60 if that is lower), and its physics, timers and music follow its frames, so a game made for 30 runs faster than the console whenever the limit is above 30. Esc shows the rate it reaches. " +
                    (n == 240 ? "This is what PortForge applies to every game automatically. " : "") +
                    (fsr3 ? "If the game cannot reach the limit, AMD FSR 3 fills the gap with pictures generated between real frames, up to the limit and never above your monitor's refresh. No frame is shown twice."
                          : "The screen shows the frames the game itself makes.");
        }
        /* the limit as the settings file keeps it: game, 0 for Maximum (no cap on the game), or the number typed or picked */
        private string FpsKey()
        {
            int at = fps.FindStringExact(fps.Text);
            if (at == 0) return "game";
            if (at == 6) return "0";
            string typed = fps.Text.Trim();
            if (typed.EndsWith("fps", StringComparison.OrdinalIgnoreCase)) typed = typed.Substring(0, typed.Length - 3).Trim();
            return typed;
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
