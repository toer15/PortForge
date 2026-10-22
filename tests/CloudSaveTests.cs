using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using PortForge;

internal sealed class SaveDavServer : IDisposable
{
    private readonly TcpListener listener=new TcpListener(IPAddress.Loopback,0);
    private readonly Dictionary<string,byte[]> files=new Dictionary<string,byte[]>();
    private readonly HashSet<string> folders=new HashSet<string>();
    private readonly object gate=new object();
    public readonly string Url;
    public int VerifiedGets,Uploads;
    public bool Corrupt;
    public SaveDavServer(){listener.Start();Url="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/dav/";new Thread(Accept){IsBackground=true}.Start();}
    private void Accept(){try{while(true){var client=listener.AcceptTcpClient();ThreadPool.QueueUserWorkItem(o=>Handle(client));}}catch(SocketException){}catch(ObjectDisposedException){}}
    private void Handle(TcpClient client)
    {
        using(client)try
        {
            client.ReceiveTimeout=client.SendTimeout=10000;var stream=client.GetStream();var bytes=new List<byte>();int value;
            while((value=stream.ReadByte())>=0){bytes.Add((byte)value);int n=bytes.Count;if(n>32768)throw new IOException();if(n>=4&&bytes[n-4]==13&&bytes[n-3]==10&&bytes[n-2]==13&&bytes[n-1]==10)break;}
            var lines=Encoding.ASCII.GetString(bytes.ToArray()).Split(new[]{"\r\n"},StringSplitOptions.None);var request=lines[0].Split(' ');if(request.Length<2)return;
            string method=request[0],path=request[1];var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(string line in lines.Skip(1)){int i=line.IndexOf(':');if(i>0)headers[line.Substring(0,i)]=line.Substring(i+1).Trim();}
            string auth;if(!headers.TryGetValue("Authorization",out auth)||auth!="Basic "+Convert.ToBase64String(Encoding.UTF8.GetBytes("player:app-password"))){Reply(stream,401,new byte[0]);return;}
            if(headers.ContainsKey("Expect")){var interim=Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");stream.Write(interim,0,interim.Length);}
            string content;int length=headers.TryGetValue("Content-Length",out content)?Int32.Parse(content):0;var body=new byte[length];int read=0;while(read<length){int count=stream.Read(body,read,length-read);if(count==0)throw new IOException();read+=count;}
            lock(gate)
            {
                if(method=="MKCOL"){Reply(stream,folders.Add(path)?201:405,new byte[0]);return;}
                if(method=="PROPFIND")
                {string xml="<?xml version=\"1.0\"?><d:multistatus xmlns:d=\"DAV:\">"+String.Join("",files.Keys.Where(k=>k.StartsWith(path,StringComparison.Ordinal)).Select(k=>"<d:response><d:href>"+k+"</d:href><d:propstat><d:prop/><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>"))+"</d:multistatus>";Reply(stream,207,Encoding.UTF8.GetBytes(xml));return;}
                if(method=="PUT"){if(!headers.ContainsKey("If-None-Match")||files.ContainsKey(path)){Reply(stream,412,new byte[0]);return;}files.Add(path,body);Uploads++;Reply(stream,201,new byte[0]);return;}
                byte[] saved;if(method=="GET"&&files.TryGetValue(path,out saved)){VerifiedGets++;if(Corrupt&&path.EndsWith(".zip"))saved=new byte[]{1,2,3};Reply(stream,200,saved);return;}
                Reply(stream,404,new byte[0]);
            }
        }catch(IOException){}catch(SocketException){}
    }
    private static void Reply(Stream stream,int code,byte[] data){byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 "+code+" Test\r\nContent-Type: application/xml\r\nContent-Length: "+data.Length+"\r\nConnection: close\r\n\r\n");stream.Write(header,0,header.Length);stream.Write(data,0,data.Length);}
    public void Dispose(){listener.Stop();}
}

internal static class CloudSaveTests
{
    private static int checks;
    private static readonly CancellationToken Token=CancellationToken.None;
    private static void Check(bool ok,string text){if(!ok)throw new Exception("FAIL: "+text);checks++;Console.WriteLine("PASS: "+text);}
    private static string Game(string root,string name,string fixture){string dir=Path.Combine(root,name);Directory.CreateDirectory(dir);string exe=Path.Combine(dir,"Game.exe");File.Copy(fixture,exe);File.Copy(fixture,Path.Combine(dir,"Game_code.dll"));return exe;}
    private static void Save(string exe,string progress){string save=CloudSaveService.SaveFolder(exe);Directory.CreateDirectory(Path.Combine(save,"profile"));File.WriteAllText(Path.Combine(save,"profile","progress.bin"),progress);}
    private static string Read(string exe){return File.ReadAllText(Path.Combine(CloudSaveService.SaveFolder(exe),"profile","progress.bin"));}
    private static CloudGame Config(string exe,string url){return new CloudGame{Exe=exe,Key="0100000000000001",Provider="WebDAV",Location=url,User="player",Secret=CloudSaves.ProtectPassword("app-password")};}
    private static IEnumerable<Control> All(Control root){foreach(Control child in root.Controls){yield return child;foreach(var nested in All(child))yield return nested;}}
    private static void Wait(Func<bool> condition){var watch=Stopwatch.StartNew();while(!condition()){if(watch.ElapsedMilliseconds>15000)throw new Exception("Cloud operation timed out");Application.DoEvents();Thread.Sleep(10);}Application.DoEvents();}
    private sealed class FrozenList : ICloudSaveStore
    {
        private readonly ICloudSaveStore inner;private readonly Barrier barrier;
        public FrozenList(ICloudSaveStore store,Barrier wait){inner=store;barrier=wait;}
        public string[] List(CancellationToken t){var result=inner.List(t);if(!barrier.SignalAndWait(10000))throw new IOException("Test barrier timed out");return result;}
        public byte[] ReadMetadata(string n,CancellationToken t){return inner.ReadMetadata(n,t);}public void Download(string n,string f,CancellationToken t){inner.Download(n,f,t);}public void Upload(string n,string f,CancellationToken t){inner.Upload(n,f,t);}public string SavedMessage{get{return inner.SavedMessage;}}
    }
    private sealed class StopAfterArchive : ICloudSaveStore
    {
        private readonly ICloudSaveStore inner;private readonly Action stop;
        public StopAfterArchive(ICloudSaveStore store,Action turnOff){inner=store;stop=turnOff;}
        public string[] List(CancellationToken t){return inner.List(t);}public byte[] ReadMetadata(string n,CancellationToken t){return inner.ReadMetadata(n,t);}public void Download(string n,string f,CancellationToken t){inner.Download(n,f,t);}
        public void Upload(string n,string f,CancellationToken t){inner.Upload(n,f,t);if(n.EndsWith(".zip"))stop();}public string SavedMessage{get{return inner.SavedMessage;}}
    }
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();int result=1;
        using(var host=new Form{ShowInTaskbar=false,Opacity=0,StartPosition=FormStartPosition.Manual,Location=new System.Drawing.Point(-10000,-10000)})
        {host.Shown+=delegate{host.BeginInvoke(new Action(delegate{try{result=RunChecks(args);}finally{host.Close();}}));};Application.Run(host);}return result;
    }
    private static int RunChecks(string[] args)
    {
        string root=Path.Combine(Path.GetDirectoryName(args[0]),"cloud-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string a=Game(root,"PC-A",args[0]),b=Game(root,"PC-B",args[0]),stateA=Path.Combine(root,"settings-a"),stateB=Path.Combine(root,"settings-b");
            Func<CloudGame,ICloudSaveStore> factory=config=>new WebDavSaveStore(config,true);
            var first=new CloudSaveService(stateA,factory);var second=new CloudSaveService(stateB,factory);
            Check(!first.Get(a).Enabled&&first.Sync(a,Token).State=="OFF","Cloud saves start off for every game");
            using(var server=new SaveDavServer())
            {
                first.Configure(Config(a,server.Url),true);second.Configure(Config(b,server.Url),true);Save(a,"Level 1");File.WriteAllText(Path.ChangeExtension(a,".ini"),"not a save");
                var initial=first.Sync(a,Token);Check(initial.State=="SAVED"&&server.Uploads==2&&server.VerifiedGets>=1,"Authenticated WebDAV publishes a verified ZIP and commit metadata over HTTP");
                Check(!File.ReadAllText(Path.Combine(stateA,"games.json")).Contains("app-password")&&CloudSaves.ReadPassword(first.Get(a).Secret)=="app-password","Cloud app password is encrypted for this Windows user");
                Check(second.Sync(b,Token).State=="RESTORED"&&Read(b)=="Level 1","A second PC downloads and restores real save files");
                Check(!File.Exists(Path.ChangeExtension(b,".ini")),"Cloud snapshots contain saves only, not game settings");
                Save(a,"Level 2");Check(first.Sync(a,Token).State=="SAVED","Changed progress uploads automatically after the game closes");
                Check(second.Sync(b,Token).State=="RESTORED"&&Read(b)=="Level 2"&&Directory.GetDirectories(Path.Combine(Path.GetDirectoryName(b),"PortForge-cloud-backups")).Length==1,"New progress restores with a local backup");
                Save(a,"Level 3-A");Save(b,"Level 3-B");first.Sync(a,Token);var conflict=second.Sync(b,Token);
                Check(conflict.Conflict&&Read(b)=="Level 3-B","Different progress on two PCs stops synchronization without overwriting either save");
                Check(second.Sync(b,Token,"local").State=="SAVED"&&Read(b)=="Level 3-B","Keep this PC publishes a new version while retaining history");
                Check(first.Sync(a,Token).State=="RESTORED"&&Read(a)=="Level 3-B","The other PC receives explicitly resolved progress");
                Save(a,"Concurrent A");Save(b,"Concurrent B");
                using(var barrier=new Barrier(2))
                {
                    var parallelA=new CloudSaveService(stateA,config=>new FrozenList(factory(config),barrier));var parallelB=new CloudSaveService(stateB,config=>new FrozenList(factory(config),barrier));
                    var results=Task.WhenAll(Task.Run(()=>parallelA.Sync(a,Token)),Task.Run(()=>parallelB.Sync(b,Token))).Result;
                    Check(results.All(r=>r.State=="SAVED"),"Concurrent PCs publish immutable revisions without overwriting one another");
                }
                conflict=first.Sync(a,Token);Check(conflict.Conflict&&conflict.Heads.Length==2&&Read(a)=="Concurrent A","Concurrent branches are detected and preserved");
                string chosen=conflict.Heads.Single(r=>r.SaveHash==CloudSaveService.TreeHash(CloudSaveService.SaveFolder(b),Token)).Id;
                Check(first.Sync(a,Token,"cloud",chosen).State=="SAVED"&&Read(a)=="Concurrent B","Use cloud copy restores the selected branch and resolves the conflict");second.Sync(b,Token);
                using(new GameStateGuard(a)){Save(a,"Temporary benchmark save");Check(first.Sync(a,Token).State=="WAITING","Scripted tests cannot upload their temporary progress");}
                Check(Read(a)=="Concurrent B","Protected gameplay restores the original progress");
                using(var running=Process.Start(new ProcessStartInfo(a,"-H --seconds=20"){UseShellExecute=false,CreateNoWindow=true}))
                {try{Check(first.Sync(a,Token).State=="WAITING","Automatic sync waits while the actual game process is running");}finally{if(!running.HasExited)running.Kill();running.WaitForExit();}}
                Save(b,"New cloud progress");second.Sync(b,Token);server.Corrupt=true;
                Check(first.Sync(a,Token).State=="ERROR"&&Read(a)=="Concurrent B","A corrupt cloud archive cannot replace local progress");server.Corrupt=false;first.Sync(a,Token);
                var settings=first.Get(a);first.Configure(settings,false);int before=server.Uploads;Save(a,"Offline progress");
                Check(first.Sync(a,Token).State=="OFF"&&server.Uploads==before&&Read(a)=="Offline progress","Turning cloud saves off stops uploads and preserves local progress");
                first.Configure(settings,true);
                var wrong=Config(b,server.Url);wrong.Secret=CloudSaves.ProtectPassword("wrong");var wrongService=new CloudSaveService(Path.Combine(root,"wrong-account"),factory);wrongService.Configure(wrong,true);
                Check(wrongService.Sync(b,Token).State=="ERROR"&&Read(b)=="New cloud progress","Cloud sign-in failure leaves local progress intact");
                bool rejected=false;try{new WebDavSaveStore(Config(a,server.Url),false);}catch(IOException){rejected=true;}Check(rejected,"Production WebDAV requires HTTPS");
                using(var ui=new CloudSaveWindow(a,first))
                {
                    var controls=All(ui).ToArray();var toggle=controls.OfType<CheckBox>().Single(x=>x.AccessibleName=="Cloud saves on for this game");Check(toggle.Checked&&controls.OfType<Button>().Any(x=>x.Text=="Sync now")&&controls.OfType<Button>().Count(x=>x.Text=="Clear")==5,"Cloud saves UI loads the game toggle, sync controls and Clear buttons");
                    ui.ShowInTaskbar=false;ui.StartPosition=FormStartPosition.Manual;ui.Location=new System.Drawing.Point(-10000,-10000);ui.Show();Application.DoEvents();
                    toggle.Checked=false;Check(!first.Get(a).Enabled,"The real Cloud saves switch disables synchronization");
                    toggle.Checked=true;var watch=Stopwatch.StartNew();while(!controls.OfType<Button>().Single(x=>x.Text=="Sync now").Enabled){if(watch.ElapsedMilliseconds>10000)throw new Exception("Cloud UI sync timed out");Application.DoEvents();Thread.Sleep(10);}
                    Check(first.Get(a).Enabled&&first.Status(a).State=="SAVED"&&controls.OfType<Label>().Any(x=>x.Text.StartsWith("SAVED")),"Enabling Cloud saves performs a real asynchronous upload and updates visible status");ui.Close();
                }
            }
            Check(first.Sync(a,Token).State=="ERROR"&&Read(a)=="Offline progress","An unavailable cloud server keeps offline progress for retry");
            bool cancelled=false;using(var stop=new CancellationTokenSource()){stop.Cancel();try{first.Sync(a,stop.Token);}catch(OperationCanceledException){cancelled=true;}}Check(cancelled&&Read(a)=="Offline progress","Cancellation leaves local progress intact");
            string folder=Path.Combine(root,"fake-cloud"),c=Game(root,"PC-C",args[0]);var local=new CloudSaveService(Path.Combine(root,"local-state"),cfg=>new FolderSaveStore(folder,cfg.Key,cfg.Exe));
            local.Configure(new CloudGame{Exe=c,Key="unsafe",Provider="OneDrive",Location=folder},true);Save(c,"Safe progress");
            string revision=Guid.NewGuid().ToString("N"),destination=Path.Combine(folder,"PortForgeCloudSaves","unsafe");Directory.CreateDirectory(destination);string badZip=Path.Combine(destination,revision+".zip");
            using(var zip=ZipFile.Open(badZip,ZipArchiveMode.Create))using(var writer=new StreamWriter(zip.CreateEntry("../escape.txt").Open()))writer.Write("bad");
            File.WriteAllText(Path.Combine(destination,revision+".json"),new JavaScriptSerializer().Serialize(new CloudRevision{Id=revision,SaveHash=new string('0',64),ZipHash=CloudSaveService.FileHash(badZip),CreatedUtc=DateTime.UtcNow.ToString("o")}));
            Check(local.Sync(c,Token,"cloud",revision).State=="ERROR"&&Read(c)=="Safe progress"&&!File.Exists(Path.Combine(Path.GetDirectoryName(CloudSaveService.SaveFolder(c)),"escape.txt")),"Unsafe ZIP paths are rejected before any local save is replaced");
            File.Delete(badZip);Check(local.Sync(c,Token).State=="ERROR"&&Read(c)=="Safe progress","An incompletely downloaded cloud revision waits without changing local saves");
            local.Configure(new CloudGame{Exe=c,Key="folder-game",Provider="OneDrive",Location=folder},true);
            Check(local.Sync(c,Token).State=="QUEUED"&&Read(c)=="Safe progress","OneDrive folder transport publishes a verified local snapshot and reports it as queued");
            string d=Game(root,"PC-D",args[0]);var folderSecond=new CloudSaveService(Path.Combine(root,"folder-second"),cfg=>new FolderSaveStore(folder,cfg.Key,cfg.Exe));folderSecond.Configure(new CloudGame{Exe=d,Key="folder-game",Provider="OneDrive",Location=folder},true);
            Check(folderSecond.Sync(d,Token).State=="RESTORED"&&Read(d)=="Safe progress","A second local sync folder consumer restores the versioned save");
            CloudSaveService interrupt=null;interrupt=new CloudSaveService(Path.Combine(root,"interrupted-state"),cfg=>new StopAfterArchive(new FolderSaveStore(folder,cfg.Key,cfg.Exe),()=>interrupt.Configure(interrupt.Get(c),false)));
            interrupt.Configure(new CloudGame{Exe=c,Key="interrupted",Provider="OneDrive",Location=folder},true);bool stopped=false;try{interrupt.Sync(c,Token);}catch(OperationCanceledException){stopped=true;}
            Check(stopped&&!Directory.GetFiles(Path.Combine(folder,"PortForgeCloudSaves","interrupted"),"*.json").Any()&&Read(c)=="Safe progress","Turning off during an upload prevents publishing a committed cloud version");
            string npdm=Path.Combine(Path.GetDirectoryName(c),"Game_data","exefs","main.npdm");Directory.CreateDirectory(Path.GetDirectoryName(npdm));var npdmBytes=new byte[180];Encoding.ASCII.GetBytes("META").CopyTo(npdmBytes,0);BitConverter.GetBytes((uint)128).CopyTo(npdmBytes,0x70);Encoding.ASCII.GetBytes("ACI0").CopyTo(npdmBytes,128);BitConverter.GetBytes((ulong)0x0100000000000001).CopyTo(npdmBytes,144);File.WriteAllBytes(npdm,npdmBytes);
            Check(CloudSaveService.GameKey(c)=="0100000000000001","A game title ID provides a stable cloud name across PCs");
            using(var automaticServer=new SaveDavServer())
            {
                string automaticRoot=Path.Combine(root,"automatic"),firstGame=Game(root,"Automatic-First",args[0]),nextGame=Game(root,"Automatic-Next",args[0]),excluded=Game(root,"Automatic-Excluded",args[0]);
                var auto=new CloudSaveService(automaticRoot,factory);
                Check(auto.AutomaticSettings().Enabled&&!auto.RegisterGame(firstGame)&&auto.EnabledGames().Length==0,"Automatic mode waits for a cloud account before enabling any game");
                auto.Configure(new CloudGame{Exe=excluded,Key="excluded"},false);auto.ConfigureAutomatic(Config(firstGame,automaticServer.Url),true);
                Check(auto.RegisterGames(new[]{firstGame,excluded})==1&&auto.Get(firstGame).Enabled&&auto.Get(firstGame).Automatic&&!auto.Get(excluded).Enabled,"Connect once enrolls existing games while respecting an explicit off switch");
                Save(firstGame,"Automatic progress");Check(auto.Sync(firstGame,Token).State=="SAVED","An automatically enrolled game uploads without a per-game setup step");
                var reopened=new CloudSaveService(automaticRoot,factory);Check(reopened.RegisterGame(nextGame)&&reopened.Get(nextGame).Enabled&&reopened.Get(nextGame).Location==automaticServer.Url,"The cloud connection survives restart and enrolls future games automatically");
                Check(auto.Sync(nextGame,Token).State=="RESTORED"&&Read(nextGame)=="Automatic progress","A future copy of the same game restores its existing cloud progress automatically");
                string baseline=auto.Get(firstGame).LastRevision;auto.ConfigureAutomatic(auto.AutomaticSettings(),false);
                Check(auto.Sync(firstGame,Token).State=="OFF"&&!auto.Get(nextGame).Enabled&&auto.Get(firstGame).LastRevision==baseline,"The global switch pauses automatic games without losing sync history");
                auto.ConfigureAutomatic(auto.AutomaticSettings(),true);Check(auto.Get(firstGame).Enabled&&auto.Get(nextGame).Enabled&&!auto.Get(excluded).Enabled,"Re-enabling automatic mode resumes games and retains individual exclusions");
                auto.Configure(auto.Get(nextGame),false);Check(!auto.RegisterGame(nextGame)&&!auto.Get(nextGame).Enabled,"Repeated discovery cannot re-enable a game the player turned off");
                string invalid=Path.Combine(root,"not-a-game.exe");File.WriteAllText(invalid,"not a game");Check(auto.RegisterGames(new[]{invalid,firstGame})==0,"Automatic enrollment rejects unsupported executables");
                using(var freshServer=new SaveDavServer())
                {
                    string backgroundGame=Game(root,"Background-Upload",args[0]);Save(backgroundGame,"Background progress");var background=new CloudSaveService(Path.Combine(root,"background-upload-state"),factory);background.ConfigureAutomatic(Config(backgroundGame,freshServer.Url),true);
                    using(var monitor=new CloudSaveMonitor(()=>new[]{backgroundGame},background,20,50)){Wait(()=>background.Status(backgroundGame).State=="SAVED");Check(background.Get(backgroundGame).Enabled&&freshServer.Uploads==2,"Background discovery uploads a game's save automatically without any per-game click");}
                }
                var migrated=new CloudSaveService(Path.Combine(root,"migrated"),factory);var legacy=Config(firstGame,automaticServer.Url);legacy.Key="my-existing-save";migrated.Configure(legacy,true);migrated.ConfigureAutomatic(Config(firstGame,automaticServer.Url),true);
                Check(migrated.Get(firstGame).Automatic&&migrated.Get(firstGame).Key=="my-existing-save","Existing per-game cloud settings are adopted without changing their cloud save name");
                var renamed=migrated.Get(firstGame);renamed.Key="custom-name";migrated.Configure(renamed,true);Check(migrated.Get(firstGame).Automatic,"A custom save name keeps the game attached to the automatic cloud connection");
                var reused=new CloudSaveService(Path.Combine(root,"reuse-019-account"),factory);reused.Configure(Config(firstGame,automaticServer.Url),true);
                Check(reused.AutomaticSettings().Location==automaticServer.Url&&reused.Get(firstGame).Automatic,"An existing single cloud connection is reused automatically after upgrading");
                Check(reused.RegisterGame(nextGame)&&reused.Get(nextGame).Enabled,"An already connected player gets automatic cloud saving on later games without reconnecting");
                var ambiguous=new CloudSaveService(Path.Combine(root,"ambiguous-accounts"),factory);ambiguous.Configure(Config(firstGame,automaticServer.Url),true);ambiguous.Configure(Config(nextGame,"http://127.0.0.1:1/other/"),true);
                Check(ambiguous.AutomaticSettings().Location.Length==0&&!ambiguous.RegisterGame(excluded),"Multiple individual accounts require choosing the shared account instead of guessing");
                string monitorGame=Game(root,"Monitor-Game",args[0]);Save(monitorGame,"Monitor progress");var monitorService=new CloudSaveService(Path.Combine(root,"monitor-state"),factory);monitorService.ConfigureAutomatic(Config(monitorGame,automaticServer.Url),true);
                using(var monitor=new CloudSaveMonitor(()=>new[]{monitorGame},monitorService,20,50))
                {Wait(()=>monitorService.Status(monitorGame).State=="CONFLICT");Check(monitorService.Get(monitorGame).Automatic&&Read(monitorGame)=="Monitor progress","The background monitor enrolls discovered games and preserves conflicts automatically");}
                string uiGame=Game(root,"Connect-UI",args[0]);var uiService=new CloudSaveService(Path.Combine(root,"connect-ui-state"),factory);
                using(var ui=new CloudSaveWindow(null,uiService,()=>new[]{uiGame}))
                {
                    ui.ShowInTaskbar=false;ui.StartPosition=FormStartPosition.Manual;ui.Location=new System.Drawing.Point(-10000,-10000);ui.Show();Application.DoEvents();var controls=All(ui).ToArray();
                    controls.OfType<ComboBox>().Single(x=>x.AccessibleName=="Cloud provider").SelectedItem="WebDAV";controls.OfType<TextBox>().Single(x=>x.AccessibleName=="Cloud folder or WebDAV URL").Text=automaticServer.Url;controls.OfType<TextBox>().Single(x=>x.AccessibleName=="WebDAV username").Text="player";controls.OfType<TextBox>().Single(x=>x.AccessibleName=="WebDAV app password").Text="app-password";
                    controls.OfType<Button>().Single(x=>x.Text=="Connect all games").PerformClick();Wait(()=>controls.OfType<Button>().Single(x=>x.Text=="Connect all games").Enabled);
                    Check(uiService.Get(uiGame).Enabled&&uiService.Get(uiGame).Automatic&&Read(uiGame)=="Automatic progress","Connect all games works without selecting a game and automatically restores a discovered game's save");
                    controls.OfType<CheckBox>().Single(x=>x.AccessibleName=="Automatic cloud saves for all games").Checked=false;
                    Check(!uiService.AutomaticSettings().Enabled&&!uiService.Get(uiGame).Enabled,"The visible global switch pauses automatic cloud saving");ui.Close();
                }
            }
            PortForge.NativeWindow.StartDoctorOnShow=false;using(var ui=new PortForge.NativeWindow())Check(All(ui).OfType<Button>().Any(x=>x.Text=="Cloud saves"&&x.Enabled),"The main app has an enabled Cloud saves button");
            Console.WriteLine(checks+" cloud-save checks passed.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{string build=Path.GetFullPath(Path.GetDirectoryName(args[0]));if(Directory.Exists(root)&&Paths.IsInside(root,build)){Paths.CheckLinks(root);Directory.Delete(root,true);}}
    }
}
