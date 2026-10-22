using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace PortForge
{
    public sealed class CloudGame
    {
        public string Exe, Key, Provider = "OneDrive", Location = "", User = "", Secret = "";
        public bool Enabled;
        public bool Automatic, IsDefault;
        public string LastRevision = "", LastHash = "";
    }
    public sealed class CloudRevision
    {
        public int Version = 1;
        public string Id, SaveHash, ZipHash, CreatedUtc;
        public string[] Parents = new string[0];
    }
    public sealed class CloudResult
    {
        public string State, Message;
        public CloudRevision[] Heads = new CloudRevision[0];
        public bool Conflict { get { return State == "CONFLICT"; } }
    }
    public interface ICloudSaveStore
    {
        string[] List(CancellationToken token);
        byte[] ReadMetadata(string name, CancellationToken token);
        void Download(string name, string local, CancellationToken token);
        void Upload(string name, string local, CancellationToken token);
        string SavedMessage { get; }
    }

    /// <summary>Immutable revisions keep concurrent and offline PC uploads from overwriting each other.</summary>
    public sealed class CloudSaveService
    {
        public const long MaxBytes = 256L * 1024 * 1024;
        public const int MaxFiles = 10000;
        private readonly string root, settings;
        private readonly Func<CloudGame,ICloudSaveStore> factory;
        private readonly object gate = new object();
        private readonly Dictionary<string,CloudResult> statuses = new Dictionary<string,CloudResult>(StringComparer.OrdinalIgnoreCase);
        private static JavaScriptSerializer Json { get { return new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024, RecursionLimit = 20 }; } }
        private static readonly Regex Id = new Regex("^[a-f0-9]{32}$"), Hash = new Regex("^[a-f0-9]{64}$");
        public CloudSaveService(string stateFolder, Func<CloudGame,ICloudSaveStore> createStore)
        { root = Path.GetFullPath(stateFolder); settings = Path.Combine(root,"games.json"); factory = createStore; }
        public static string SaveFolder(string exe) { return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)),Path.GetFileNameWithoutExtension(exe)+"_data","save"); }
        public static string GameKey(string exe)
        {
            string npdm = Path.Combine(Path.GetDirectoryName(exe),Path.GetFileNameWithoutExtension(exe)+"_data","exefs","main.npdm");
            try
            {
                byte[] b=File.ReadAllBytes(npdm);
                if(b.Length>=128&&Encoding.ASCII.GetString(b,0,4)=="META")
                {uint off=BitConverter.ToUInt32(b,0x70);if(off<=b.Length-24&&Encoding.ASCII.GetString(b,(int)off,4)=="ACI0"){ulong title=BitConverter.ToUInt64(b,(int)off+16);if(title!=0)return title.ToString("x16",CultureInfo.InvariantCulture);}}
            }catch(IOException){}catch(UnauthorizedAccessException){}
            string name=Regex.Replace(Path.GetFileNameWithoutExtension(exe).ToLowerInvariant(),"[^a-z0-9]+","-").Trim('-');
            return (name.Length==0?"game":name).Substring(0,Math.Min(60,name.Length==0?4:name.Length));
        }
        private List<CloudGame> Load()
        {
            if(!File.Exists(settings))return new List<CloudGame>();
            if(new FileInfo(settings).Length>2*1024*1024)throw new IOException("Cloud settings are too large.");
            return Json.Deserialize<List<CloudGame>>(File.ReadAllText(settings))??new List<CloudGame>();
        }
        private void Change(Action<List<CloudGame>> action)
        {
            Directory.CreateDirectory(root);
            using(var mutex=new Mutex(false,"Local\\PortForge.CloudSettings."+Digest(Encoding.UTF8.GetBytes(settings.ToLowerInvariant())).Substring(0,24)))
            {
                bool held=false;
                try {try{held=mutex.WaitOne(10000);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("Another PortForge window is saving cloud settings.");var games=Load();action(games);GameOptimization.AtomicWrite(settings,new UTF8Encoding(false).GetBytes(Json.Serialize(games)),null);}
                finally{if(held)mutex.ReleaseMutex();}
            }
        }
        public CloudGame Get(string exe)
        {
            exe=Path.GetFullPath(exe);
            lock(gate)return Load().FirstOrDefault(g=>!g.IsDefault&&String.Equals(g.Exe,exe,StringComparison.OrdinalIgnoreCase))??new CloudGame{Exe=exe,Key=GameKey(exe)};
        }
        public CloudGame[] EnabledGames(){lock(gate)return Load().Where(g=>!g.IsDefault&&g.Enabled&&File.Exists(g.Exe)).ToArray();}
        public CloudGame AutomaticSettings()
        {
            lock(gate)
            {
                var games=Load();var defaults=games.FirstOrDefault(g=>g.IsDefault);if(defaults!=null)return defaults;
                var existing=games.Where(g=>g.Enabled&&!String.IsNullOrWhiteSpace(g.Location)).ToArray();
                // Reuse a player's already enabled 0.19 connection when there is only one account to choose from.
                if(existing.Length>0&&existing.All(g=>SameConnection(g,existing[0])))
                {try{ConfigureAutomatic(existing[0],true);return Load().First(g=>g.IsDefault);}catch(ArgumentException){}catch(IOException){}catch(System.Security.Cryptography.CryptographicException){}}
                return new CloudGame{IsDefault=true,Enabled=true,Key="connection",Exe=""};
            }
        }
        private static bool SameConnection(CloudGame a,CloudGame b)
        {return a.Provider==b.Provider&&a.Location==b.Location&&a.User==b.User;}
        private static void CopyConnection(CloudGame target,CloudGame source)
        {target.Provider=source.Provider;target.Location=source.Location;target.User=source.User;target.Secret=source.Secret;}
        public void ConfigureAutomatic(CloudGame connection,bool enabled)
        {
            var value=new CloudGame{IsDefault=true,Enabled=enabled,Key="connection",Exe=Path.Combine(root,"connection.exe")};CopyConnection(value,connection);
            if(enabled&&!String.IsNullOrWhiteSpace(value.Location))factory(value);
            lock(gate)
            {
                Change(games=>
                {
                    games.RemoveAll(g=>g.IsDefault);
                    foreach(var game in games)
                    {
                        // An explicit per-game off switch remains an exception to the global default.
                        if(!game.Automatic&&(!game.Enabled||!SameConnection(game,value)))continue;
                        if(!SameConnection(game,value)){game.LastHash=game.LastRevision="";}
                        game.Automatic=true;game.Enabled=enabled&&!String.IsNullOrWhiteSpace(value.Location);CopyConnection(game,value);
                    }
                    games.Add(value);
                });
                statuses.Clear();
            }
        }
        public bool RegisterGame(string exe)
        {
            exe=Path.GetFullPath(exe);
            lock(gate)
            {
                AutomaticSettings();
                var known=Load();var defaults=known.FirstOrDefault(g=>g.IsDefault);
                if(known.Any(g=>!g.IsDefault&&String.Equals(g.Exe,exe,StringComparison.OrdinalIgnoreCase))||defaults==null||!defaults.Enabled||String.IsNullOrWhiteSpace(defaults.Location))return false;
                GameOptimization.ValidateGame(exe);string key=GameKey(exe);bool added=false;
                Change(games=>
                {
                    var current=games.FirstOrDefault(g=>g.IsDefault);
                    if(current==null||!current.Enabled||String.IsNullOrWhiteSpace(current.Location)||games.Any(g=>!g.IsDefault&&String.Equals(g.Exe,exe,StringComparison.OrdinalIgnoreCase)))return;
                    var game=new CloudGame{Exe=exe,Key=key,Enabled=true,Automatic=true};CopyConnection(game,current);games.Add(game);added=true;
                });return added;
            }
        }
        public int RegisterGames(IEnumerable<string> games,CancellationToken token=default(CancellationToken))
        {
            var defaults=AutomaticSettings();if(!defaults.Enabled||String.IsNullOrWhiteSpace(defaults.Location))return 0;
            int added=0;foreach(string exe in games.Where(p=>!String.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase)){token.ThrowIfCancellationRequested();try{if(RegisterGame(exe))added++;}catch(ArgumentException){}catch(IOException){}catch(UnauthorizedAccessException){}}
            return added;
        }
        public void Configure(CloudGame game, bool enabled)
        {
            game.Exe=Path.GetFullPath(game.Exe); ValidateKey(game.Key);
            if(enabled)factory(game); // validate the destination without creating or uploading anything
            lock(gate)Change(games=>
            {
                game.IsDefault=false;game.Automatic=false;
                var defaults=games.FirstOrDefault(g=>g.IsDefault);
                var old=games.FirstOrDefault(g=>!g.IsDefault&&String.Equals(g.Exe,game.Exe,StringComparison.OrdinalIgnoreCase));
                if(enabled&&defaults!=null&&defaults.Enabled&&SameConnection(game,defaults))game.Automatic=true;
                if(old!=null){games.Remove(old);if(old.Key==game.Key&&old.Provider==game.Provider&&old.Location==game.Location&&old.User==game.User){game.LastHash=old.LastHash;game.LastRevision=old.LastRevision;}else{game.LastHash=game.LastRevision="";}}
                game.Enabled=enabled;games.Add(game);
            });
            SetStatus(game.Exe,new CloudResult{State=enabled?"READY":"OFF",Message=enabled?"Cloud saves enabled. Checking for changes happens while the game is closed.":"Cloud saves are off. Local saves and existing cloud copies are kept."});
        }
        public CloudResult Status(string exe){lock(gate){CloudResult value;return statuses.TryGetValue(Path.GetFullPath(exe),out value)?value:new CloudResult{State=Get(exe).Enabled?"READY":"OFF",Message="Cloud saves have not synced in this app session."};}}
        private CloudResult SetStatus(string exe,CloudResult result){lock(gate)statuses[Path.GetFullPath(exe)]=result;return result;}
        private static void ValidateKey(string key){if(key==null||!Regex.IsMatch(key,"^[a-zA-Z0-9][a-zA-Z0-9_-]{0,79}$"))throw new ArgumentException("Use 1-80 letters, numbers, dashes or underscores for the cloud save name.");}
        private void EnsureEnabled(CloudGame config)
        {
            var current=Get(config.Exe);
            if(!current.Enabled||current.Key!=config.Key||current.Provider!=config.Provider||current.Location!=config.Location||current.User!=config.User||current.Secret!=config.Secret)throw new OperationCanceledException("Cloud saves were turned off or their connection changed.");
            GameOptimization.EnsureStopped(config.Exe);if(GameStateGuard.IsProtected(config.Exe))throw new IOException("A protected game test is using this save. Sync will wait.");
        }
        public CloudResult Sync(string exe,CancellationToken token,string choice=null,string selectedRevision=null)
        {
            exe=Path.GetFullPath(exe);
            using(var mutex=new Mutex(false,"Local\\PortForge.CloudSync."+Digest(Encoding.UTF8.GetBytes(exe.ToLowerInvariant())).Substring(0,24)))
            {
                bool held=false;
                try
                {
                    try{held=mutex.WaitOne(0);}catch(AbandonedMutexException){held=true;}
                    if(!held)return new CloudResult{State="BUSY",Message="Cloud saves are already being checked for this game."};
                    var config=Get(exe);if(!config.Enabled)return SetStatus(exe,new CloudResult{State="OFF",Message="Cloud saves are off."});
                    if(GameDiagnostics.Running(exe)||GameStateGuard.IsProtected(exe)||Directory.GetDirectories(Path.GetDirectoryName(exe),"PortForge-test-backup-*").Length>0)
                        return SetStatus(exe,new CloudResult{State="WAITING",Message="Waiting for the game or protected test to close. No save files were changed."});
                    token.ThrowIfCancellationRequested();EnsureEnabled(config);ValidateKey(config.Key);
                    var store=factory(config);var revisions=ReadRevisions(store,token);
                    var parentIds=new HashSet<string>(revisions.SelectMany(r=>r.Parents));
                    var heads=revisions.Where(r=>!parentIds.Contains(r.Id)).OrderBy(r=>r.Id,StringComparer.Ordinal).ToArray();
                    if(revisions.Count>0&&heads.Length==0)throw new IOException("Cloud history is invalid. No saves were changed.");
                    string local=TreeHash(SaveFolder(exe),token);
                    bool empty=!Directory.Exists(SaveFolder(exe))||!Directory.EnumerateFiles(SaveFolder(exe),"*",SearchOption.AllDirectories).Any();
                    var head=heads.FirstOrDefault();
                    if(choice=="cloud")
                    {
                        var selected=heads.FirstOrDefault(r=>r.Id==selectedRevision)??(heads.Length==1?head:null);
                        if(selected==null)throw new IOException("Select the cloud version you want to restore.");
                        Restore(config,store,selected,token);local=selected.SaveHash;
                        if(heads.Length>1)return Publish(config,store,local,heads,token);
                        Acknowledge(config,selected);return SetStatus(exe,new CloudResult{State="RESTORED",Message="Cloud save restored. Your previous local save is backed up beside the game.",Heads=heads});
                    }
                    if(choice=="local")return Publish(config,store,local,heads,token);
                    if(head==null)
                    {if(empty)return SetStatus(exe,new CloudResult{State="READY",Message="No save exists yet. Play the game, close it, and keep PortForge open to sync."});return Publish(config,store,local,heads,token);}
                    if(heads.Any(r=>r.SaveHash!=head.SaveHash))return SetStatus(exe,Conflict(heads));
                    if(local==head.SaveHash){Acknowledge(config,head);return SetStatus(exe,new CloudResult{State=config.Provider=="OneDrive"?"QUEUED":"CURRENT",Message=config.Provider=="OneDrive"?"Local save matches the OneDrive folder. Check OneDrive for upload status.":"Local save matches the cloud revision.",Heads=heads});}
                    if(config.LastRevision.Length==0)
                    {if(empty){Restore(config,store,head,token);Acknowledge(config,head);return SetStatus(exe,new CloudResult{State="RESTORED",Message="Downloaded the cloud save for this PC.",Heads=heads});}return SetStatus(exe,Conflict(heads));}
                    if(empty)return SetStatus(exe,Conflict(heads)); // do not propagate accidental deletion
                    if(heads.Any(r=>r.Id==config.LastRevision)&&heads.All(r=>r.SaveHash==config.LastHash))return Publish(config,store,local,heads,token);
                    if(local==config.LastHash){Restore(config,store,head,token);Acknowledge(config,head);return SetStatus(exe,new CloudResult{State="RESTORED",Message="Newer cloud progress restored. The previous local save is backed up.",Heads=heads});}
                    return SetStatus(exe,Conflict(heads));
                }
                catch(OperationCanceledException){throw;}
                catch(Exception e){return SetStatus(exe,new CloudResult{State="ERROR",Message=e.Message});}
                finally{if(held)mutex.ReleaseMutex();}
            }
        }
        private static CloudResult Conflict(CloudRevision[] heads){return new CloudResult{State="CONFLICT",Message="This PC and the cloud have different progress. Choose Keep this PC or Use cloud copy. Both histories are kept until you choose.",Heads=heads};}
        private List<CloudRevision> ReadRevisions(ICloudSaveStore store,CancellationToken token)
        {
            var all=new HashSet<string>(store.List(token));
            var names=all.Where(n=>Regex.IsMatch(n,"^[a-f0-9]{32}\\.json$")).ToArray();
            if(names.Length>512)throw new IOException("Cloud history has over 512 revisions. Archive older versions in your cloud account before syncing.");
            var result=new List<CloudRevision>();
            foreach(string name in names)
            {
                token.ThrowIfCancellationRequested();var r=Json.Deserialize<CloudRevision>(Encoding.UTF8.GetString(store.ReadMetadata(name,token)));
                if(r==null||r.Version!=1||!Id.IsMatch(r.Id??"")||name!=r.Id+".json"||!Hash.IsMatch(r.SaveHash??"")||!Hash.IsMatch(r.ZipHash??"")||r.Parents==null||r.Parents.Length>512||r.Parents.Any(p=>!Id.IsMatch(p??"")||p==r.Id))throw new IOException("A cloud revision is invalid. No saves were changed.");
                result.Add(r);
                if(!all.Contains(r.Id+".zip"))throw new IOException("A cloud version has not finished arriving. Sync will retry; your local save was kept.");
            }
            return result;
        }
        private CloudResult Publish(CloudGame config,ICloudSaveStore store,string expectedHash,CloudRevision[] heads,CancellationToken token)
        {
            Directory.CreateDirectory(root);string zip=Path.Combine(root,"snapshot-"+Guid.NewGuid().ToString("N")+".zip"),metadata=zip+".json",check=zip+".verify",captured=zip+".contents";
            try
            {
                EnsureEnabled(config);Capture(SaveFolder(config.Exe),zip,token);
                Extract(zip,captured,token);
                if(TreeHash(captured,token)!=expectedHash)throw new IOException("The save changed during capture. Sync will retry while the game is closed.");
                if(TreeHash(SaveFolder(config.Exe),token)!=expectedHash)throw new IOException("The save changed during capture. Sync will retry while the game is closed.");
                var revision=new CloudRevision{Id=Guid.NewGuid().ToString("N"),SaveHash=expectedHash,ZipHash=FileHash(zip),CreatedUtc=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture),Parents=heads.Select(r=>r.Id).ToArray()};
                token.ThrowIfCancellationRequested();EnsureEnabled(config);store.Upload(revision.Id+".zip",zip,token);
                store.Download(revision.Id+".zip",check,token);if(FileHash(check)!=revision.ZipHash)throw new IOException("Cloud upload verification failed. The previous cloud version was kept.");
                File.WriteAllText(metadata,Json.Serialize(revision),new UTF8Encoding(false));token.ThrowIfCancellationRequested();EnsureEnabled(config);store.Upload(revision.Id+".json",metadata,token);
                Acknowledge(config,revision);return SetStatus(config.Exe,new CloudResult{State=config.Provider=="OneDrive"?"QUEUED":"SAVED",Message=store.SavedMessage,Heads=new[]{revision}});
            }
            finally{foreach(string file in new[]{zip,metadata,check})try{File.Delete(file);}catch(IOException){}if(Directory.Exists(captured)){if(!Paths.IsInside(captured,root))throw new IOException("Invalid snapshot folder.");Paths.CheckLinks(captured);Directory.Delete(captured,true);}}
        }
        private void Acknowledge(CloudGame config,CloudRevision revision)
        {
            lock(gate)Change(games=>{var current=games.FirstOrDefault(g=>String.Equals(g.Exe,config.Exe,StringComparison.OrdinalIgnoreCase));if(current!=null&&current.Enabled&&current.Key==config.Key&&current.Location==config.Location&&current.Provider==config.Provider&&current.User==config.User){current.LastHash=revision.SaveHash;current.LastRevision=revision.Id;}});
        }
        private void Restore(CloudGame config,ICloudSaveStore store,CloudRevision revision,CancellationToken token)
        {
            string save=SaveFolder(config.Exe),parent=Path.GetDirectoryName(save),incoming=Path.Combine(parent,"PortForge-cloud-incoming-"+Guid.NewGuid().ToString("N")),zip=Path.Combine(root,"restore-"+Guid.NewGuid().ToString("N")+".zip");
            Paths.CheckLinks(parent);Directory.CreateDirectory(root);Directory.CreateDirectory(parent);
            try
            {
                store.Download(revision.Id+".zip",zip,token);if(FileHash(zip)!=revision.ZipHash)throw new IOException("Cloud save checksum failed. Your local save was kept.");
                Extract(zip,incoming,token);if(TreeHash(incoming,token)!=revision.SaveHash)throw new IOException("Cloud save content verification failed. Your local save was kept.");
                token.ThrowIfCancellationRequested();EnsureEnabled(config);Paths.CheckLinks(save);
                string backup=Path.Combine(Path.GetDirectoryName(config.Exe),"PortForge-cloud-backups",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N"));
                if(!Paths.IsInside(backup,Path.GetDirectoryName(config.Exe)))throw new IOException("Invalid backup folder.");Paths.CheckLinks(backup);
                bool had=Directory.Exists(save);if(had){Directory.CreateDirectory(Path.GetDirectoryName(backup));Directory.Move(save,backup);}
                try{Directory.Move(incoming,save);}catch{if(had&&!Directory.Exists(save))Directory.Move(backup,save);throw;}
            }
            finally{try{File.Delete(zip);}catch(IOException){}if(Directory.Exists(incoming)){if(!Paths.IsInside(incoming,parent))throw new IOException("Invalid restore folder.");Paths.CheckLinks(incoming);Directory.Delete(incoming,true);}}
        }
        public static string Digest(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        public static string FileHash(string file){using(var sha=SHA256.Create())using(var s=File.OpenRead(file))return BitConverter.ToString(sha.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
        private static string SafeRelative(string name)
        {
            string value=name.Replace('\\','/').TrimEnd('/');
            if(value.Length==0||value.StartsWith("/")||value.Split('/').Any(p=>p.Length==0||p=="."||p==".."||p.IndexOfAny(Path.GetInvalidFileNameChars())>=0||p.EndsWith(".")||p.EndsWith(" ")||Regex.IsMatch(p.Split('.')[0],"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$",RegexOptions.IgnoreCase)))throw new IOException("Unsafe path in save data.");
            return value;
        }
        private static string[] Entries(string folder)
        {
            if(!Directory.Exists(folder))return new string[0];Paths.CheckLinks(folder);
            var result=new List<string>();var todo=new Stack<string>();todo.Push(folder);
            while(todo.Count>0){string current=todo.Pop();foreach(string item in Directory.GetFileSystemEntries(current)){if((File.GetAttributes(item)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked save files are not supported for cloud sync.");result.Add(item);if(result.Count>MaxFiles)throw new IOException("This save contains too many files for cloud sync.");if(Directory.Exists(item))todo.Push(item);}}
            return result.OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        }
        public static string TreeHash(string folder,CancellationToken token)
        {
            var listing=new StringBuilder();long bytes=0;
            foreach(string entry in Entries(folder)){token.ThrowIfCancellationRequested();string rel=SafeRelative(entry.Substring(folder.TrimEnd('\\','/').Length+1));bool dir=Directory.Exists(entry);if(!dir){bytes+=new FileInfo(entry).Length;if(bytes>MaxBytes)throw new IOException("This save is larger than the 256 MB cloud-save limit.");}listing.Append(dir?"D ":"F ").Append(rel).Append(dir?"":" "+FileHash(entry)).Append('\n');}
            return Digest(Encoding.UTF8.GetBytes(listing.ToString()));
        }
        private static void Capture(string folder,string zip,CancellationToken token)
        {
            long size=0;using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create))foreach(string entry in Entries(folder))
            {token.ThrowIfCancellationRequested();string rel=SafeRelative(entry.Substring(folder.TrimEnd('\\','/').Length+1));if(Directory.Exists(entry)){archive.CreateEntry(rel+"/");continue;}size+=new FileInfo(entry).Length;if(size>MaxBytes)throw new IOException("This save is larger than the 256 MB cloud-save limit.");var part=archive.CreateEntry(rel,CompressionLevel.Optimal);using(var src=new FileStream(entry,FileMode.Open,FileAccess.Read,FileShare.Read))using(var dst=part.Open())src.CopyTo(dst);}
        }
        private static void Extract(string zip,string target,CancellationToken token)
        {
            Directory.CreateDirectory(target);long total=0;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using(var archive=ZipFile.OpenRead(zip))
            {
                if(archive.Entries.Count>MaxFiles)throw new IOException("Cloud save contains too many files.");
                foreach(var entry in archive.Entries)
                {
                    token.ThrowIfCancellationRequested();string rel=SafeRelative(entry.FullName);
                    if(!seen.Add(rel)||((entry.ExternalAttributes>>16)&0xf000)==0xa000)throw new IOException("Duplicate or linked path in cloud save.");
                    total+=entry.Length;if(total>MaxBytes)throw new IOException("Cloud save exceeds the 256 MB limit.");
                    string path=Path.GetFullPath(Path.Combine(target,rel.Replace('/',Path.DirectorySeparatorChar)));if(!Paths.IsInside(path,target))throw new IOException("Unsafe cloud save path.");
                    if(entry.FullName.EndsWith("/")){Directory.CreateDirectory(path);continue;}
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using(var src=entry.Open())using(var dst=new FileStream(path,FileMode.CreateNew,FileAccess.Write))
                    {byte[] buffer=new byte[65536];long copied=0;int count;while((count=src.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();copied+=count;if(copied>entry.Length||copied>MaxBytes)throw new IOException("Cloud archive contains oversized data.");dst.Write(buffer,0,count);}if(copied!=entry.Length)throw new IOException("Cloud save entry is incomplete.");}
                }
            }
        }
    }
}
