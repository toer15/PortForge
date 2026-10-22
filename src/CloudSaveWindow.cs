using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PortForge
{
    public sealed class CloudSaveWindow : Form
    {
        protected override CreateParams CreateParams { get { return SmoothPaint.Composited(base.CreateParams); } }
        private static readonly Color Bg=Color.FromArgb(17,21,30),Surface=Color.FromArgb(28,34,46),Mint=Color.FromArgb(105,226,183);
        private readonly TextBox game=Box("Windows game"),key=Box("Cloud save name"),location=Box("Cloud folder or WebDAV URL"),user=Box("WebDAV username"),password=Box("WebDAV app password");
        private readonly ComboBox provider=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=180,AccessibleName="Cloud provider"},versions=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top,AccessibleName="Cloud version to restore"};
        private readonly CheckBox enabled=new CheckBox{Text="Cloud saves on for this game",AutoSize=true,AccessibleName="Cloud saves on for this game"};
        private readonly CheckBox automatic=new CheckBox{Text="Automatic cloud saves for all games",AutoSize=true,AccessibleName="Automatic cloud saves for all games"};
        private readonly Label status=new Label{Dock=DockStyle.Fill,ForeColor=Mint,Text="OFF — Choose a game to set up cloud saves.",AutoEllipsis=true};
        private readonly Label locationLabel=new Label{Text="OneDrive account folder",AutoSize=true};
        private readonly TableLayoutPanel settings=new TableLayoutPanel{Dock=DockStyle.Top,ColumnCount=1,AutoSize=true};
        private readonly FlowLayoutPanel actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};
        private readonly Button sync=Button("Sync now"),keep=Button("Keep this PC"),restore=Button("Use cloud copy"),cancel=Button("Cancel sync");
        private readonly System.Windows.Forms.Timer refresh=new System.Windows.Forms.Timer{Interval=1500};
        private readonly CloudSaveService service;
        private readonly Func<IEnumerable<string>> knownGames;
        private string exe,lastMessage;
        private bool loading;
        private bool closeWhenDone;
        private CancellationTokenSource operation;
        private UiLock settingsLock,actionsLock;
        public CloudSaveWindow(string initialGame) : this(initialGame,CloudSaves.Service) { }
        public CloudSaveWindow(string initialGame,CloudSaveService cloudService,Func<IEnumerable<string>> games=null)
        {
            service=cloudService;knownGames=games??FindGames;Text="PortForge | Cloud saves";AppIcon.Apply(this);ClientSize=new Size(740,850);MinimumSize=new Size(680,830);StartPosition=FormStartPosition.CenterParent;
            Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Dpi;BackColor=Bg;ForeColor=Color.WhiteSmoke;password.UseSystemPasswordChar=true;
            var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=8,Padding=new Padding(22)};
            foreach(int height in new[]{45,75,68,310,90,40,55})root.RowStyles.Add(new RowStyle(SizeType.Absolute,height));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            root.Controls.Add(new Label{Text="Cloud saves",Font=new Font(Font.FontFamily,22),AutoSize=true},0,0);
            var gameRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2};gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,225));
            gameRow.Controls.Add(new Label{Text="Converted Windows game (.exe)",AutoSize=true},0,0);gameRow.Controls.Add(game,0,1);
            var fileActions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};Button browse=Button("Browse"),clear=Button("Clear"),load=Button("Load");foreach(var b in new[]{browse,clear,load}){b.Width=68;fileActions.Controls.Add(b);}gameRow.Controls.Add(fileActions,1,1);root.Controls.Add(gameRow,0,1);
            browse.Click+=delegate{if(operation!=null)return;using(var dialog=new OpenFileDialog{Filter="Converted Windows game|*.exe",CheckFileExists=true})if(dialog.ShowDialog(this)==DialogResult.OK){game.Text=dialog.FileName;LoadGame();}};
            clear.Click+=delegate{if(operation!=null)return;game.Clear();exe=null;loading=true;enabled.Checked=false;loading=false;enabled.Enabled=false;status.Text="OFF — Choose a game to set up cloud saves.";};
            load.Click+=delegate{LoadGame();};var switches=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false};switches.Controls.Add(automatic);switches.Controls.Add(enabled);root.Controls.Add(switches,0,2);
            provider.Items.AddRange(new object[]{"OneDrive","WebDAV"});provider.SelectedIndex=0;
            var providerRow=new FlowLayoutPanel{Dock=DockStyle.Top,Height=42,WrapContents=false};providerRow.Controls.Add(new Label{Text="Cloud service",Width=140,Padding=new Padding(0,6,0,0)});providerRow.Controls.Add(provider);settings.Controls.Add(providerRow);
            var connect=Button("Connect all games");connect.Width=190;providerRow.Controls.Add(connect);
            AddField("Cloud save name (same on each PC)",key);
            AddField(locationLabel,location);
            var choose=Button("Choose OneDrive folder");choose.Width=210;choose.Click+=delegate{using(var dialog=new FolderBrowserDialog{Description="Choose the root folder of your Windows OneDrive account"})if(dialog.ShowDialog(this)==DialogResult.OK)location.Text=dialog.SelectedPath;};settings.Controls.Add(choose);
            AddField("WebDAV username",user);AddField("WebDAV app password",password);
            root.Controls.Add(settings,0,3);root.Controls.Add(status,0,4);root.Controls.Add(versions,0,5);
            foreach(var b in new[]{sync,keep,restore,cancel})actions.Controls.Add(b);root.Controls.Add(actions,0,6);
            root.Controls.Add(new Label{Dock=DockStyle.Fill,Text="Connect an account once per PC. Existing and new converted games join automatically; you can turn any game off. Sync runs while PortForge is open and the game is closed.\r\nOneDrive handles its uploads; check its sync icon before switching PCs. WebDAV uploads are verified directly. Restore backups and old versions are kept. ROMs and keys are never uploaded.",ForeColor=Color.FromArgb(173,185,203)},0,7);
            Controls.Add(root);provider.SelectedIndexChanged+=delegate{locationLabel.Text=provider.Text=="OneDrive"?"OneDrive account folder":"HTTPS WebDAV folder URL";};
            connect.Click+=async delegate{await ConnectAll();};
            automatic.CheckedChanged+=delegate
            {
                if(loading)return;
                try{service.ConfigureAutomatic(service.AutomaticSettings(),automatic.Checked);if(operation!=null)operation.Cancel();RefreshSwitch();status.Text=automatic.Checked?(service.AutomaticSettings().Location.Length==0?"SETUP — Choose your cloud service below, then select Connect all games.":"ON — Existing and new converted games use your connected cloud account automatically."):"PAUSED — Automatic cloud saving is off. Existing local and cloud saves are kept.";lastMessage=null;}
                catch(Exception e){status.Text="ERROR — "+e.Message;loading=true;automatic.Checked=service.AutomaticSettings().Enabled;loading=false;}
            };
            enabled.CheckedChanged+=async delegate
            {
                if(loading||exe==null)return;
                try
                {
                    if(!enabled.Checked){var existing=service.Get(exe);service.Configure(existing,false);if(operation!=null)operation.Cancel();ShowResult(service.Status(exe));return;}
                    service.Configure(ReadConfig(),true);await RunSync(null);
                }
                catch(Exception e){loading=true;enabled.Checked=service.Get(exe).Enabled;loading=false;status.Text="ERROR — "+e.Message;}
            };
            sync.Click+=async delegate{try{if(exe==null)throw new IOException("Load a converted game first.");if(!enabled.Checked)throw new IOException("Turn Cloud saves on first.");service.Configure(ReadConfig(),true);await RunSync(null);}catch(Exception e){status.Text="ERROR — "+e.Message;}};
            keep.Click+=async delegate{await RunSync("local");};restore.Click+=async delegate{await RunSync("cloud");};cancel.Click+=delegate{if(operation!=null)operation.Cancel();};
            refresh.Tick+=delegate{if(exe!=null&&operation==null)ShowResult(service.Status(exe));};refresh.Start();
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(operation!=null){closeWhenDone=true;operation.Cancel();e.Cancel=true;status.Text="STOPPING — Finishing the current save operation...";}};FormClosed+=delegate{refresh.Stop();refresh.Dispose();};
            game.Text=initialGame??"";enabled.Enabled=false;keep.Enabled=restore.Enabled=cancel.Enabled=false;
            loading=true;automatic.Checked=service.AutomaticSettings().Enabled;loading=false;
            if(File.Exists(initialGame))LoadGame();else{LoadConnection(service.AutomaticSettings());status.Text=service.AutomaticSettings().Location.Length==0?"SETUP — Connect a cloud account once to save all converted games automatically.":"READY — Your cloud connection is saved. Known and new games join automatically.";}
        }
        private static IEnumerable<string> FindGames()
        {
            var games=LiveDoctor.FindConvertedGames(LiveDoctor.DefaultDiscoveryRoots(AppDomain.CurrentDomain.BaseDirectory));
            if(LiveDoctor.Current!=null)games.AddRange(LiveDoctor.Current.Games);string last=GameSettingsWindow.RememberedGame();if(last!=null)games.Add(last);return games;
        }
        private void LoadConnection(CloudGame config)
        {
            provider.SelectedItem=config.Provider;location.Text=config.Location;user.Text=config.User;try{password.Text=CloudSaves.ReadPassword(config.Secret);}catch(Exception){password.Clear();}
            if(config.Location.Length==0&&config.Provider=="OneDrive")location.Text=CloudSaves.OneDriveFolders().FirstOrDefault()??"";
        }
        private void RefreshSwitch(){if(exe==null)return;loading=true;enabled.Checked=service.Get(exe).Enabled;loading=false;}
        private async Task ConnectAll()
        {
            if(operation!=null)return;var source=operation=new CancellationTokenSource();settingsLock=UiLock.Lock(settings);actionsLock=UiLock.Lock(actions);cancel.Enabled=true;status.Text="CONNECTING — Applying this account to existing and future games...";
            try
            {
                if(String.IsNullOrWhiteSpace(location.Text))throw new IOException("Choose your OneDrive account folder or enter an HTTPS WebDAV URL first.");
                service.ConfigureAutomatic(ReadConfig(),true);loading=true;automatic.Checked=true;loading=false;
                string selected=exe;int added=await Task.Run(()=>service.RegisterGames(knownGames().Concat(selected==null?new string[0]:new[]{selected}),source.Token));
                var first=service.EnabledGames().FirstOrDefault(g=>g.Automatic);
                CloudResult result=first==null?null:await Task.Run(()=>service.Sync(first.Exe,source.Token));
                if(exe!=null)RefreshSwitch();status.Text=result!=null&&result.State=="ERROR"?"CONNECTION SAVED — "+result.Message:"ON — Cloud saving is automatic for existing and new converted games. "+added+" game(s) added. "+(result==null?"Sync starts once a game has a save.":result.Message);lastMessage=null;
            }
            catch(OperationCanceledException){status.Text="STOPPED — Setup stopped. Existing saves are kept.";}
            catch(Exception e){status.Text="ERROR — "+e.Message;}
            finally{settingsLock.Unlock();actionsLock.Unlock();source.Dispose();operation=null;cancel.Enabled=false;if(exe!=null)ShowButtons(service.Status(exe));if(closeWhenDone)Close();}
        }
        private void AddField(string label,TextBox box){AddField(new Label{Text=label,AutoSize=true},box);}
        private void AddField(Label label,TextBox box)
        {
            var row=new TableLayoutPanel{Dock=DockStyle.Top,Height=51,ColumnCount=2,RowCount=2,Margin=new Padding(0)};row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,70));row.Controls.Add(label,0,0);row.Controls.Add(box,0,1);
            var clear=Button("Clear");clear.Width=64;clear.AccessibleName="Clear "+box.AccessibleName;clear.Click+=delegate{box.Clear();};row.Controls.Add(clear,1,1);settings.Controls.Add(row);
        }
        private CloudGame ReadConfig(){return new CloudGame{Exe=exe,Key=key.Text.Trim(),Provider=provider.Text,Location=location.Text.Trim(),User=user.Text.Trim(),Secret=CloudSaves.ProtectPassword(password.Text)};}
        private void LoadGame()
        {
            if(operation!=null)return;
            exe=null;loading=true;enabled.Checked=false;enabled.Enabled=false;loading=false;
            try
            {
                string path=Path.GetFullPath(game.Text.Trim());GameOptimization.ValidateGame(path);service.RegisterGame(path);var config=service.Get(path);exe=path;game.Text=path;loading=true;
                lastMessage=null;key.Text=config.Key;LoadConnection(config.Location.Length==0&&service.AutomaticSettings().Location.Length>0?service.AutomaticSettings():config);
                enabled.Enabled=true;enabled.Checked=config.Enabled;loading=false;ShowResult(service.Status(exe));
                if(!config.Enabled&&service.AutomaticSettings().Location.Length==0)status.Text="SETUP — Connect a cloud account once with Connect all games. Future games will join automatically.";
            }catch(Exception e){loading=false;status.Text="ERROR — "+e.Message;}
        }
        private async Task RunSync(string choice)
        {
            if(operation!=null||exe==null)return;
            string target=exe,selected=versions.SelectedValue as string;var source=operation=new CancellationTokenSource();
            lastMessage=null;settingsLock=UiLock.Lock(settings);actionsLock=UiLock.Lock(actions);cancel.Enabled=true;status.Text="SYNCING — Checking saved progress in the background...";
            try{var result=await Task.Run(()=>service.Sync(target,source.Token,choice,selected));if(!IsDisposed)ShowResult(result);}
            catch(OperationCanceledException){if(!IsDisposed)status.Text="STOPPED — Sync cancelled. Completed revisions and local saves are kept.";}
            catch(Exception e){if(!IsDisposed)status.Text="ERROR — "+e.Message;}
            finally{settingsLock.Unlock();actionsLock.Unlock();source.Dispose();operation=null;if(!IsDisposed){cancel.Enabled=false;ShowButtons(service.Status(target));if(closeWhenDone)Close();}}
        }
        private void ShowResult(CloudResult result)
        {
            string message=result.State+" — "+result.Message;if(message!=lastMessage){status.Text=message;lastMessage=message;}
            string previous=versions.SelectedValue as string;var rows=result.Heads.Select(r=>new VersionChoice{Id=r.Id,Label=r.CreatedUtc+"  ("+r.Id.Substring(0,8)+")"}).ToArray();
            if(String.Join(",",rows.Select(r=>r.Id))!=String.Join(",",versions.Items.Cast<VersionChoice>().Select(r=>r.Id)))
            {versions.DisplayMember="Label";versions.ValueMember="Id";versions.DataSource=rows;if(previous!=null&&rows.Any(r=>r.Id==previous))versions.SelectedValue=previous;}
            ShowButtons(result);
        }
        private void ShowButtons(CloudResult result){sync.Enabled=exe!=null&&operation==null;keep.Enabled=restore.Enabled=exe!=null&&enabled.Checked&&result.Conflict&&operation==null;}
        private sealed class VersionChoice{public string Id{get;set;}public string Label{get;set;}}
        private static TextBox Box(string label){return new TextBox{AccessibleName=label,Dock=DockStyle.Fill,BackColor=Surface,ForeColor=Color.WhiteSmoke,BorderStyle=BorderStyle.FixedSingle};}
        private static Button Button(string text){return new ReadableButton{Text=text,AccessibleName=text,Width=145,Height=32,ForeColor=Color.WhiteSmoke,BackColor=Surface,Margin=new Padding(0,0,8,0)};}
    }
}
