using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using PortForge;

internal sealed class DownloadServer : IDisposable
{
    private readonly TcpListener listener = new TcpListener(IPAddress.Loopback,0);
    private readonly Thread thread;
    private readonly object sync = new object();
    private readonly Dictionary<string,int> gets = new Dictionary<string,int>();
    public readonly byte[] Data;
    private readonly byte[] zipData;
    public string BaseUrl;
    public int Version = 1, Active, MaxActive, ResumeRequests, BusyRequests;
    public DownloadServer(byte[] contents = null)
    {
        Data=contents ?? new byte[2*1024*1024];
        if(contents==null) { for(int i=0;i<Data.Length;i++) Data[i]=(byte)(i*7); Encoding.ASCII.GetBytes("NRO0").CopyTo(Data,0x10); }
        using(var memory=new MemoryStream()) { using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true)) using(var entry=zip.CreateEntry("game.nro").Open()) entry.Write(Data,0,Data.Length); zipData=memory.ToArray(); }
        listener.Start(); BaseUrl="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port;
        thread = new Thread(Accept) { IsBackground=true }; thread.Start();
    }
    private void Accept() { try { while(true) { var client=listener.AcceptTcpClient(); ThreadPool.QueueUserWorkItem(o=>Handle(client)); } } catch(SocketException) { } catch(ObjectDisposedException) { } }
    public int Gets(string path) { lock(sync) return gets.ContainsKey(path)?gets[path]:0; }
    private void Handle(TcpClient client)
    {
        using(client)
        try
        {
            client.ReceiveTimeout=5000; client.SendTimeout=5000;
            var stream=client.GetStream(); var reader=new StreamReader(stream,Encoding.ASCII,false,4096,true);
            string request=reader.ReadLine(); if(request==null) return; string route=request.Split(' ')[1].Split('?')[0];
            var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); string line;
            while(!String.IsNullOrEmpty(line=reader.ReadLine())) { int split=line.IndexOf(':'); if(split>0) headers[line.Substring(0,split)]=line.Substring(split+1).Trim(); }
            string range; bool ranged=headers.TryGetValue("Range",out range); bool probe=ranged && range=="bytes=0-0";
            int count; lock(sync) { gets[route]=gets.ContainsKey(route)?gets[route]+1:1; count=gets[route]; }
            if(route=="/redirect.nro") { Write(stream,"HTTP/1.1 302 Found\r\nLocation: "+BaseUrl+"/game.nro\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"); return; }
            if(route=="/missing.nro") { Reply(stream,"404 Not Found",new byte[0],0,-1,false,"",false); return; }
            if(route=="/html.nro") { Write(stream,"HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: 13\r\nConnection: close\r\n\r\n<html></html>"); return; }
            if(route=="/busy.nro" && Interlocked.Increment(ref BusyRequests)==1) { Write(stream,"HTTP/1.1 503 Unavailable\r\nRetry-After: 0\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"); return; }
            if(route=="/reject-probe.nro" && ranged) { Reply(stream,"416 Range Not Satisfiable",new byte[0],0,-1,false,"",false); return; }
            if(route=="/chunked.nro") { Write(stream,"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n\r\n"+Data.Length.ToString("x")+"\r\n"); stream.Write(Data,0,Data.Length); Write(stream,"\r\n0\r\n\r\n"); return; }
            byte[] data=(byte[])(route=="/repair.zip"?zipData:Data).Clone(); int version=Version;
            if(version==2) data[200]^=0xff;
            if(route=="/repair.nro" && count==2) data[100]^=0xff;
            if(route=="/repair.zip" && count==2) data[data.Length-22]=0;
            bool ignore=route=="/no-range.nro" || (route=="/fallback.nro" && !probe);
            long start=0,end=data.Length-1;
            if(ranged && !ignore)
            {
                string[] pair=range.Substring(6).Split('-'); start=Int64.Parse(pair[0]); if(pair[1].Length>0) end=Int64.Parse(pair[1]);
                if(start>0) { Interlocked.Increment(ref ResumeRequests); if(!headers.ContainsKey("If-Range") && route!="/no-etag.nro") throw new Exception("Missing If-Range"); }
            }
            bool body=!probe; if(body) { int active=Interlocked.Increment(ref Active); lock(sync) MaxActive=Math.Max(MaxActive,active); Thread.Sleep(35); }
            try
            {
                string etag=route=="/no-etag.nro"?"":route=="/lastmod.nro"?"lm:Wed, 21 Oct 2015 07:28:00 GMT":"\"version-"+version+"\"";
                bool drop=route=="/drop.nro" && !ranged;
                Reply(stream,ranged&&!ignore?"206 Partial Content":"200 OK",data,start,end,ranged&&!ignore,etag,drop,route=="/slow.nro");
            }
            finally { if(body) Interlocked.Decrement(ref Active); }
        }
        catch(IOException) { } catch(SocketException) { } catch(ObjectDisposedException) { }
    }
    private static void Write(NetworkStream stream,string text) { byte[] bytes=Encoding.ASCII.GetBytes(text); stream.Write(bytes,0,bytes.Length); }
    private static void Reply(NetworkStream stream,string status,byte[] data,long start,long end,bool range,string etag,bool drop,bool slow=false)
    {
        string head="HTTP/1.1 "+status+"\r\nContent-Type: application/octet-stream\r\nContent-Length: "+Math.Max(0,end-start+1)+"\r\nConnection: close\r\n";
        if(range) head+="Content-Range: bytes "+start+"-"+end+"/"+data.Length+"\r\n";
        if(etag.StartsWith("lm:")) head+="Last-Modified: "+etag.Substring(3)+"\r\n"; else if(etag.Length>0) head+="ETag: "+etag+"\r\n"; Write(stream,head+"\r\n");
        long actualEnd=drop?Math.Min(end,start+(end-start)/2):end;
        for(long at=start;at<=actualEnd;at+=16384) { int n=(int)Math.Min(16384,actualEnd-at+1); stream.Write(data,(int)at,n); if(slow) Thread.Sleep(20); }
    }
    public void Dispose() { listener.Stop(); thread.Join(1000); }
}

internal static class DownloadTests
{
    private static int checks;
    private sealed class Updates : IProgress<DownloadProgress> { public Action<DownloadProgress> Callback; public readonly List<string> Messages=new List<string>(); public void Report(DownloadProgress p) { lock(Messages) Messages.Add(p.Message); if(Callback!=null) Callback(p); } }
    private static void Check(bool value,string text) { if(!value) throw new Exception("FAIL: "+text); checks++; Console.WriteLine("PASS: "+text); }
    private static void Reject(Action action,string text) { bool failed=false; try { action(); } catch(ArgumentException) {failed=true;} catch(IOException) {failed=true;} catch(InvalidDataException) {failed=true;} Check(failed,text); }
    private static int Main()
    {
        try
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"download-test-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string fixture=Path.Combine(root,"fixture.nro");
            using(var server=new DownloadServer())
            {
                File.WriteAllBytes(fixture,server.Data); string expected=GameDownloads.HashFile(fixture,CancellationToken.None);
                Func<string,string,int,DownloadRequest> request=(route,name,connections)=>new DownloadRequest {Url=server.BaseUrl+route,Destination=Path.Combine(root,name),Connections=connections};
                var invalid=request("/game.nro","invalid.nro",1); invalid.Url="file:///C:/bad"; Reject(()=>GameDownloads.Validate(invalid),"Non-HTTP links are rejected");
                invalid=request("/game.nro","invalid.nro",1); invalid.Sha256="bad"; Reject(()=>GameDownloads.Validate(invalid),"Invalid source checksums are rejected before downloading");
                invalid=request("/game.nro","invalid.exe",1); Reject(()=>GameDownloads.Validate(invalid),"The downloader accepts game files and ZIPs rather than executing installers");
                var p=request("/game.nro","parallel.nro",8); p.Sha256=expected;
                var result=GameDownloads.Download(p,new Updates(),CancellationToken.None);
                Check(File.ReadAllBytes(result.File).SequenceEqual(server.Data) && result.Sha256==expected,"Parallel ranges assemble into the exact source file and verify SHA-256");
                Check(server.MaxActive>=2,"A range-capable server receives concurrent download connections");
                Check(GameDownloads.Download(request("/redirect.nro","redirected.nro",4),new Updates(),CancellationToken.None).Sha256==expected,"Direct links following HTTP redirects retain correct download content");
                int before=server.Gets("/game.nro"); var checksumReused=GameDownloads.Download(p,new Updates(),CancellationToken.None);
                Check(checksumReused.Reused && server.Gets("/game.nro")==before,"A file matching the source checksum is reused without a network transfer");
                p.Sha256=null; var reused=GameDownloads.Download(p,new Updates(),CancellationToken.None);
                Check(reused.Reused && server.Gets("/game.nro")==before+1,"A completed verified download is reused after a metadata probe");
                Check(!Directory.GetFiles(result.File+".portforge-download","*.bin").Any(),"Completed part files are cleaned without deleting the finished game");
                var drop=request("/drop.nro","resume.nro",1); var retried=GameDownloads.Download(drop,new Updates(),CancellationToken.None);
                Check(retried.Sha256==expected && server.ResumeRequests>0,"An interrupted response retries from the saved byte offset with If-Range");
                var fallback=request("/fallback.nro","fallback.nro",4); var messages=new Updates();
                Check(GameDownloads.Download(fallback,messages,CancellationToken.None).Sha256==expected && messages.Messages.Any(s=>s.Contains("Falling back")),"A server refusing parallel requests falls back to one complete transfer");
                Check(GameDownloads.Download(request("/no-range.nro","plain.nro",4),new Updates(),CancellationToken.None).Sha256==expected,"Servers ignoring byte ranges still download correctly");
                Check(GameDownloads.Download(request("/reject-probe.nro","rejected-range.nro",4),new Updates(),CancellationToken.None).Sha256==expected,"Servers rejecting the range probe fall back to a normal request");
                Check(GameDownloads.Download(request("/no-etag.nro","no-etag.nro",4),new Updates(),CancellationToken.None).Sha256==expected,"A server with no validator at all still downloads the exact file");
                int lastModifiedBefore=server.Gets("/lastmod.nro");
                Check(GameDownloads.Download(request("/lastmod.nro","lastmod.nro",4),new Updates(),CancellationToken.None).Sha256==expected && server.Gets("/lastmod.nro")-lastModifiedBefore>=4,"A server that gives Last-Modified instead of an ETag is still split into parallel connections");
                var automatic=request("/game.nro","automatic.nro",0); var autoUpdates=new Updates();
                Check(GameDownloads.Download(automatic,autoUpdates,CancellationToken.None).Sha256==expected,"Automatic connection choice downloads the exact file");
                Check(GameDownloads.Download(request("/chunked.nro","chunked.nro",4),new Updates(),CancellationToken.None).Sha256==expected,"Chunked downloads without a known total length finish correctly");
                Check(GameDownloads.Download(request("/busy.nro","busy.nro",1),new Updates(),CancellationToken.None).Sha256==expected && server.BusyRequests>1,"Temporary server failures are retried");
                var repair=request("/repair.nro","repair.nro",1); repair.Sha256=expected; var repairUpdates=new Updates();
                Check(GameDownloads.Download(repair,repairUpdates,CancellationToken.None).Sha256==expected && repairUpdates.Messages.Any(s=>s.Contains("Checksum mismatch")),"A checksum mismatch triggers one fresh download and recovers from a bad first copy");
                var zipRepair=request("/repair.zip","repaired.zip",1); var zipUpdates=new Updates();
                Check(GameDownloads.Download(zipRepair,zipUpdates,CancellationToken.None).Kind=="ZIP" && zipUpdates.Messages.Any(s=>s.Contains("ZIP structure")),"A damaged ZIP structure triggers one fresh download before publication");
                var wrong=request("/game.nro","wrong-hash.nro",1); wrong.Sha256=new string('0',64); Reject(()=>GameDownloads.Download(wrong,new Updates(),CancellationToken.None),"A repeated checksum mismatch stops instead of publishing damaged content");
                Check(!File.Exists(wrong.Destination),"A failed integrity check never creates the final game file");
                var html=request("/html.nro","webpage.nro",1); Reject(()=>GameDownloads.Download(html,new Updates(),CancellationToken.None),"HTML download or login pages are explained instead of saved as games");
                Reject(()=>GameDownloads.Download(request("/missing.nro","missing.nro",1),new Updates(),CancellationToken.None),"An unavailable source fails with an actionable source error");
                var collision=request("/game.nro","keep.nro",1); File.WriteAllText(collision.Destination,"keep"); Reject(()=>GameDownloads.Download(collision,new Updates(),CancellationToken.None),"An existing unverified destination is never overwritten"); Check(File.ReadAllText(collision.Destination)=="keep","The pre-existing file remains unchanged");
                var slow=request("/slow.nro?token=do-not-store","paused.nro",1);
                using(var cts=new CancellationTokenSource())
                {
                    var updates=new Updates { Callback=pulse=> {if(pulse.Received>65536) cts.Cancel();} }; bool cancelled=false;
                    try { GameDownloads.Download(slow,updates,cts.Token); } catch(OperationCanceledException) {cancelled=true;}
                    Check(cancelled && !File.Exists(slow.Destination) && new FileInfo(Path.Combine(slow.Destination+".portforge-download","part-0.bin")).Length>0,"Pausing keeps partial bytes and does not publish an incomplete game");
                    Check(!File.ReadAllText(Path.Combine(slow.Destination+".portforge-download","download.xml")).Contains("do-not-store"),"Recovery metadata stores a URL hash without persisting signed-link tokens");
                }
                server.Version=2; byte[] second=(byte[])server.Data.Clone(); second[200]^=0xff;
                var resumed=GameDownloads.Download(slow,new Updates(),CancellationToken.None);
                Check(File.ReadAllBytes(resumed.File).SequenceEqual(second),"A changed source ETag restarts safely instead of mixing old and new file versions");
                var same=request("/slow.nro","resume-after-pause.nro",1);
                using(var cts=new CancellationTokenSource())
                {
                    try { GameDownloads.Download(same,new Updates {Callback=pulse=>{if(pulse.Received>65536) cts.Cancel();}},cts.Token); } catch(OperationCanceledException) { }
                }
                int priorResumes=server.ResumeRequests; same.Connections=4;
                Check(File.ReadAllBytes(GameDownloads.Download(same,new Updates(),CancellationToken.None).File).SequenceEqual(second) && server.ResumeRequests>priorResumes,"A later session resumes saved parts with their original partition layout");
            }
            string goodZip=Path.Combine(root,"homebrew.zip");
            using(var zip=ZipFile.Open(goodZip,ZipArchiveMode.Create)) { var e=zip.CreateEntry("Game/demo.nro"); using(var s=e.Open()) {byte[] data=File.ReadAllBytes(fixture);s.Write(data,0,data.Length);} var readme=zip.CreateEntry("Game/readme.txt"); using(var w=new StreamWriter(readme.Open())) w.Write("test"); }
            var unpacked=GameDownloads.Unpack(goodZip,new Updates(),CancellationToken.None);
            Check(unpacked.Count==1 && File.ReadAllBytes(unpacked[0]).SequenceEqual(File.ReadAllBytes(fixture)),"ZIP preparation finds the game and preserves its content");
            foreach(string entry in new[]{"../escape.nro","C:/escape.nro","Game/file:stream.nro"})
            {
                string bad=Path.Combine(root,"bad-"+Guid.NewGuid().ToString("N")+".zip"); using(var zip=ZipFile.Open(bad,ZipArchiveMode.Create)) {zip.CreateEntry(entry);}
                Reject(()=>GameDownloads.Unpack(bad,new Updates(),CancellationToken.None),"Unsafe archive entry rejected: "+entry);
            }
            string duplicate=Path.Combine(root,"duplicate.zip"); using(var zip=ZipFile.Open(duplicate,ZipArchiveMode.Create)) {zip.CreateEntry("a.nro");zip.CreateEntry("A.nro");}
            Reject(()=>GameDownloads.Unpack(duplicate,new Updates(),CancellationToken.None),"Duplicate archive paths cannot overwrite one another");
            Console.WriteLine(checks+" download checks passed. Evidence: "+root); return 0;
        }
        catch(Exception e) {Console.Error.WriteLine(e);return 1;}
    }
}
