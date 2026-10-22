using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PortForge
{
    public sealed class DownloadWindow : Form
    {
        private static readonly Color Bg = Color.FromArgb(17,21,30), Surface = Color.FromArgb(28,34,46), Mint = Color.FromArgb(105,226,183), Muted = Color.FromArgb(173,185,203);
        private readonly TextBox url = Box("Direct download link"), destination = Box("Download save path"), checksum = Box("Expected SHA-256"), log = Box("Download details");
        private readonly CheckBox parallel = new CheckBox { Text = "Use up to 8 connections when supported", Checked = true, AutoSize = true }, unpack = new CheckBox { Text = "Unpack ZIP files automatically", Checked = true, AutoSize = true }, convertAfter = new CheckBox { Text = "Start conversion when the game is ready", Checked = true, AutoSize = true };
        private readonly Label badge = Caption("READY", 9, Mint), title = Caption("Download your game file", 18, Color.White), message = Caption("Paste a direct file link and choose where to save it.", 10, Muted), speed = Caption("", 10, Muted);
        private readonly Button start = Button("Download / Resume", true), pause = Button("Pause", false), use = Button("Use selected game", true), folder = Button("Open folder", false);
        private readonly ProgressBar bar = new ProgressBar { Dock = DockStyle.Fill };
        private readonly ListBox games = new ListBox { AccessibleName = "Downloaded game files", Dock = DockStyle.Fill, HorizontalScrollbar = true, BackColor = Surface, ForeColor = Color.WhiteSmoke };
        private readonly TableLayoutPanel fields = Stack();
        private CancellationTokenSource cancellation;
        private string completedPath;
        public string SelectedInput { get; private set; }
        public bool ConvertWhenReady { get { return convertAfter.Checked; } }

        public DownloadWindow()
        {
            Text = "PortForge | Downloads"; ClientSize = new Size(900, 820); MinimumSize = new Size(820,740); StartPosition = FormStartPosition.CenterParent;
            BackColor = Bg; ForeColor = Color.WhiteSmoke; Font = new Font("Segoe UI",10); AutoScaleMode = AutoScaleMode.Dpi;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(22) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,45)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,290)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,120));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,32)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,50));
            root.Controls.Add(Caption("Downloads", 24, Color.White),0,0);
            fields.Dock = DockStyle.Fill;
            AddField("Direct download link",url,null);
            AddField("Save as",destination,delegate
            {
                using (var picker = new SaveFileDialog { Filter = "Game or ZIP|*.nro;*.nsp;*.xci;*.zip|All files|*.*", FileName = "game.nro", OverwritePrompt = true })
                {
                    try { string directory = Path.GetDirectoryName(destination.Text); if (Directory.Exists(directory)) picker.InitialDirectory = directory; if (!String.IsNullOrWhiteSpace(Path.GetFileName(destination.Text))) picker.FileName = Path.GetFileName(destination.Text); } catch (ArgumentException) { }
                    if (picker.ShowDialog(this)==DialogResult.OK) destination.Text = picker.FileName;
                }
            });
            AddField("Expected SHA-256 (optional)",checksum,null);
            var toggles = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            foreach (var check in new[] {parallel,unpack,convertAfter}) { check.Margin = new Padding(0,3,15,3); toggles.Controls.Add(check); }
            fields.Controls.Add(toggles);
            var hint = Caption("Use a direct link to a game file or ZIP. Resume and parallel downloads depend on the server. Speed is limited by your connection and the source.",9,Muted);
            hint.MaximumSize = new Size(800,0); fields.Controls.Add(hint); root.Controls.Add(fields,0,1);
            var card = Stack(); card.Dock = DockStyle.Fill; card.BackColor = Surface; card.Padding = new Padding(12); card.Controls.Add(badge); card.Controls.Add(title); card.Controls.Add(message);
            card.SizeChanged += delegate { message.MaximumSize = new Size(Math.Max(200,card.ClientSize.Width-28),0); }; root.Controls.Add(card,0,2);
            var meter = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0,8,0,0) };
            meter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); meter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,285)); meter.Controls.Add(bar,0,0); meter.Controls.Add(speed,1,0); root.Controls.Add(meter,0,3);
            var results = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0,10,0,0) };
            results.RowStyles.Add(new RowStyle(SizeType.Absolute,24)); results.RowStyles.Add(new RowStyle(SizeType.Absolute,75)); results.RowStyles.Add(new RowStyle(SizeType.Absolute,24)); results.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            results.Controls.Add(Caption("Ready game files (select one if the ZIP has several)",10,Muted),0,0); results.Controls.Add(games,0,1);
            results.Controls.Add(Caption("Download details",10,Muted),0,2); log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.Dock = DockStyle.Fill; results.Controls.Add(log,0,3); root.Controls.Add(results,0,4);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0,10,0,0) };
            foreach (var b in new[] {start,pause,use,folder}) { b.Size = new Size(175,34); actions.Controls.Add(b); }
            pause.Enabled = use.Enabled = folder.Enabled = false;
            start.Click += async delegate { await Download(); };
            pause.Click += delegate { if (cancellation != null) { cancellation.Cancel(); pause.Enabled = false; SetStatus("STOPPING","Pausing download","Keeping completed parts so a supported server can resume.",Muted); } };
            use.Click += delegate { ChooseGame(); }; games.SelectedIndexChanged += delegate { use.Enabled = cancellation == null && games.SelectedItem != null; };
            folder.Click += delegate { if (File.Exists(completedPath)) Process.Start("explorer.exe",GameManifest.Quote(Path.GetDirectoryName(completedPath))); };
            root.Controls.Add(actions,0,5); Controls.Add(root);
            destination.Text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Downloads","game.nro");
            url.Leave += delegate
            {
                Uri uri;
                if (!Uri.TryCreate(url.Text.Trim(),UriKind.Absolute,out uri)) return;
                try
                {
                    string file = Path.GetFileName(uri.LocalPath);
                    if (new[] {".nro",".nsp",".xci",".dnsp",".dxci",".zip"}.Contains(Path.GetExtension(file).ToLowerInvariant()) && file.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && Path.GetFileName(destination.Text) == "game.nro") destination.Text = Path.Combine(Path.GetDirectoryName(destination.Text),file);
                }
                catch (ArgumentException) { }
            };
            foreach (var box in new[] {url,destination,checksum}) box.TextChanged += delegate
            {
                games.Items.Clear(); SelectedInput = null; use.Enabled = false;
                if (cancellation == null) { completedPath=null; folder.Enabled=false; speed.Text=""; bar.Value=0; SetStatus("READY","Download choices updated","Select Download / Resume to use these choices. Existing files and partial downloads are kept.",Muted); }
            };
            FormClosing += delegate(object sender,FormClosingEventArgs e) { if (cancellation != null) { e.Cancel = true; cancellation.Cancel(); } };
        }
        private async Task Download()
        {
            if (cancellation != null) return;
            var request = new DownloadRequest { Url = url.Text.Trim(), Destination = destination.Text.Trim(), Sha256 = checksum.Text.Trim(), Connections = parallel.Checked ? 8 : 1 };
            try { GameDownloads.Validate(request); } catch (Exception e) { Error(e); return; }
            cancellation = new CancellationTokenSource(); var token = cancellation.Token;
            fields.Enabled = start.Enabled = use.Enabled = folder.Enabled = false; pause.Enabled = true;
            games.Items.Clear(); log.Clear(); completedPath = null; bar.Style = ProgressBarStyle.Marquee; bar.Value = 0;
            SetStatus("WORKING","Preparing download","Checking the source and any previous download parts.",Muted);
            string lastMessage = null;
            var updates = new Progress<DownloadProgress>(p =>
            {
                if (cancellation == null || token.IsCancellationRequested) return;
                message.Text = p.Message;
                if (lastMessage != p.Message) { if (log.TextLength > 80000) log.Clear(); log.AppendText(p.Message + Environment.NewLine); lastMessage = p.Message; }
                if (p.Total > 0) { bar.Style = ProgressBarStyle.Continuous; bar.Value = (int)Math.Max(0,Math.Min(100,p.Received * 100.0 / p.Total)); }
                if (p.BytesPerSecond > 0) speed.Text = (p.BytesPerSecond/1048576).ToString("F1") + " MB/s    " + (p.Received/1048576.0).ToString("F0") + " / " + (p.Total>0 ? (p.Total/1048576.0).ToString("F0") : "?") + " MB";
            });
            bool automaticallyUse = false;
            try
            {
                var result = await Task.Run(() => GameDownloads.Download(request,updates,token)); completedPath = result.File;
                log.AppendText("Local SHA-256: " + result.Sha256 + Environment.NewLine);
                if (result.Kind == "ZIP" && unpack.Checked)
                {
                    SetStatus("WORKING","Download saved; unpacking ZIP","The completed ZIP is kept while the game files are unpacked.",Muted);
                    var files = await Task.Run(() => GameDownloads.Unpack(result.File,updates,token)); foreach (string file in files) games.Items.Add(file);
                }
                else if (result.Kind != "ZIP") games.Items.Add(result.File);
                if (games.Items.Count == 1) games.SelectedIndex = 0;
                automaticallyUse = games.Items.Count == 1 && convertAfter.Checked;
                SetStatus("DONE",result.Reused ? "Verified download reused" : "Download complete",games.Items.Count > 0 ? (games.Items.Count == 1 ? "Your game is ready. Use selected game to continue to conversion." : "Select the game you want, then choose Use selected game.") : "ZIP saved. Enable automatic unpacking and resume, or unpack it yourself and choose a game on the main screen.",Mint);
                bar.Style = ProgressBarStyle.Continuous; bar.Value = 100;
            }
            catch (OperationCanceledException) { SetStatus("PAUSED","Download paused",completedPath == null ? "Completed parts were kept. Select Download / Resume to continue; servers without safe resume restart the file." : "The downloaded ZIP is saved. Unpacking stopped; resume to try again.",Muted); }
            catch (Exception e) { Error(e); log.AppendText(e.Message + Environment.NewLine); }
            finally
            {
                cancellation.Dispose(); cancellation = null; fields.Enabled = start.Enabled = true; pause.Enabled = false;
                use.Enabled = games.SelectedItem != null; folder.Enabled = File.Exists(completedPath); bar.Style = ProgressBarStyle.Continuous;
            }
            if (automaticallyUse) ChooseGame();
        }
        private void ChooseGame() { if (cancellation != null || games.SelectedItem == null) return; SelectedInput = games.SelectedItem.ToString(); DialogResult = DialogResult.OK; Close(); }
        private void Error(Exception e) { SetStatus("ERROR",File.Exists(completedPath) ? "File saved; preparation stopped" : "Download needs attention",e.Message,Color.FromArgb(255,157,148)); }
        private void SetStatus(string state,string heading,string text,Color color) { badge.Text = state; title.Text = heading; message.Text = text; badge.ForeColor = title.ForeColor = color; }
        private void AddField(string label,TextBox box,Action browse)
        {
            var block = Stack(); block.Margin = new Padding(0,0,0,8); block.Controls.Add(Caption(label,10,Muted));
            var row = new TableLayoutPanel { Dock = DockStyle.Top, Height = 30, ColumnCount = browse == null ? 2 : 3, Margin = new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); row.Controls.Add(box,0,0); int column = 1;
            if (browse != null) { row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,80)); var pick = Button("Browse",false); pick.Dock = DockStyle.Fill; pick.AccessibleName = "Browse " + box.AccessibleName; pick.Click += delegate { browse(); }; row.Controls.Add(pick,column++,0); }
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,70)); var clear = Button("Clear",false); clear.AccessibleName = "Clear " + box.AccessibleName; clear.Dock = DockStyle.Fill; clear.Click += delegate { box.Clear(); box.Focus(); }; row.Controls.Add(clear,column,0); block.Controls.Add(row); fields.Controls.Add(block);
        }
        private static TextBox Box(string name) { return new TextBox { AccessibleName = name, Dock = DockStyle.Fill, BackColor = Surface, ForeColor = Color.WhiteSmoke, BorderStyle = BorderStyle.FixedSingle }; }
        private static TableLayoutPanel Stack() { return new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = new Padding(0) }; }
        private static Label Caption(string text,float size,Color color) { return new Label { Text = text, AutoSize = true, ForeColor = color, Font = new Font("Segoe UI",size), Margin = new Padding(0,0,0,4) }; }
        private static Button Button(string text,bool primary) { var b = new Button { Text = text, AccessibleName = text, FlatStyle = FlatStyle.Flat, BackColor = primary ? Mint : Surface, ForeColor = primary ? Bg : Color.WhiteSmoke, Cursor = Cursors.Hand }; b.FlatAppearance.BorderSize = 0; return b; }
    }
}
