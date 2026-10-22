using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using Microsoft.Win32;

namespace PortForge
{
    public static class CloudSaves
    {
        private static readonly Lazy<CloudSaveService> service=new Lazy<CloudSaveService>(()=>new CloudSaveService(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PortForge","CloudSaves"),CreateStore));
        public static CloudSaveService Service { get { return service.Value; } }
        public static string ProtectPassword(string value){return String.IsNullOrEmpty(value)?"":Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value),Encoding.UTF8.GetBytes("PortForge.Cloud.v1"),DataProtectionScope.CurrentUser));}
        public static string ReadPassword(string value){return String.IsNullOrEmpty(value)?"":Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value),Encoding.UTF8.GetBytes("PortForge.Cloud.v1"),DataProtectionScope.CurrentUser));}
        public static string[] OneDriveFolders()
        {
            var paths=new List<string>();foreach(string key in new[]{"OneDrive","OneDriveConsumer","OneDriveCommercial"}){string path=Environment.GetEnvironmentVariable(key);if(!String.IsNullOrWhiteSpace(path)&&Directory.Exists(path))paths.Add(Path.GetFullPath(path));}
            try{using(var accounts=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts"))if(accounts!=null)foreach(string name in accounts.GetSubKeyNames())using(var account=accounts.OpenSubKey(name)){string path=account==null?null:account.GetValue("UserFolder") as string;if(!String.IsNullOrWhiteSpace(path)&&Directory.Exists(path))paths.Add(Path.GetFullPath(path));}}catch(Exception){}
            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        public static ICloudSaveStore CreateStore(CloudGame config)
        {
            if(config.Provider=="WebDAV")return new WebDavSaveStore(config,false);
            if(config.Provider!="OneDrive")throw new IOException("Choose OneDrive or WebDAV as the cloud provider.");
            if(String.IsNullOrWhiteSpace(config.Location))throw new IOException("Set up OneDrive in Windows and choose its account folder.");
            if(!OneDriveFolders().Any(p=>String.Equals(p.TrimEnd('\\','/'),Path.GetFullPath(config.Location).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase)))throw new IOException("Choose the OneDrive root folder for a Windows OneDrive account. Set up OneDrive first if no account is listed.");
            return new FolderSaveStore(config.Location,config.Key,config.Exe);
        }
    }

    public sealed class FolderSaveStore : ICloudSaveStore
    {
        private readonly string folder;
        public FolderSaveStore(string oneDriveFolder,string key,string gameExe)
        {
            if(!Regex.IsMatch(key??"","^[a-zA-Z0-9][a-zA-Z0-9_-]{0,79}$"))throw new IOException("Invalid cloud save name.");
            folder=Path.GetFullPath(Path.Combine(oneDriveFolder,"PortForgeCloudSaves",key));
            if(Paths.Overlap(folder,CloudSaveService.SaveFolder(gameExe)))throw new IOException("The cloud destination must be outside the game's save folder.");
        }
        private string PathFor(string name){if(!Regex.IsMatch(name??"","^[a-f0-9]{32}\\.(json|zip)$"))throw new IOException("Invalid cloud revision filename.");return Path.Combine(folder,name);}
        public string[] List(CancellationToken token){token.ThrowIfCancellationRequested();return Directory.Exists(folder)?Directory.GetFiles(folder).Select(Path.GetFileName).ToArray():new string[0];}
        public byte[] ReadMetadata(string name,CancellationToken token){token.ThrowIfCancellationRequested();string path=PathFor(name);if(new FileInfo(path).Length>65536)throw new IOException("Cloud revision metadata is too large.");return File.ReadAllBytes(path);}
        public void Download(string name,string local,CancellationToken token){Copy(PathFor(name),local,token);}
        public void Upload(string name,string local,CancellationToken token)
        {
            Directory.CreateDirectory(folder);string destination=PathFor(name),temp=Path.Combine(folder,"pending-"+Guid.NewGuid().ToString("N")+".tmp");
            try{Copy(local,temp,token);token.ThrowIfCancellationRequested();File.Move(temp,destination);}finally{try{File.Delete(temp);}catch(IOException){}}
        }
        private static void Copy(string source,string destination,CancellationToken token)
        {
            using(var src=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))using(var dst=new FileStream(destination,FileMode.CreateNew,FileAccess.Write))
            {byte[] buffer=new byte[65536];long total=0;int count;while((count=src.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();total+=count;if(total>CloudSaveService.MaxBytes+8*1024*1024)throw new IOException("Cloud save archive exceeds its size limit.");dst.Write(buffer,0,count);}}
        }
        public string SavedMessage
        {
            get
            {
                var processes=Process.GetProcessesByName("OneDrive");bool running=processes.Length>0;foreach(var process in processes)process.Dispose();
                return running?"Save placed in the OneDrive folder. OneDrive handles the cloud upload; check its sync status for confirmation.":"Save queued in the OneDrive folder. OneDrive is not running; start it and sign in to upload this save.";
            }
        }
    }

    /// <summary>Direct HTTPS WebDAV transport; immutable filenames never overwrite another device's save.</summary>
    public sealed class WebDavSaveStore : ICloudSaveStore
    {
        private readonly Uri endpoint,folder;
        private readonly string authorization;
        public WebDavSaveStore(CloudGame config,bool testLoopback)
        {
            Uri url;if(!Uri.TryCreate(config.Location,UriKind.Absolute,out url)||(url.Scheme!="https"&&!(testLoopback&&url.Scheme=="http"&&url.IsLoopback))||url.UserInfo.Length>0||url.Query.Length>0||url.Fragment.Length>0)throw new IOException("Enter an HTTPS WebDAV folder URL without credentials or a query string.");
            if(!Regex.IsMatch(config.Key??"","^[a-zA-Z0-9][a-zA-Z0-9_-]{0,79}$"))throw new IOException("Invalid cloud save name.");
            endpoint=new Uri(url.AbsoluteUri.TrimEnd('/')+"/");folder=new Uri(endpoint,"PortForgeCloudSaves/"+Uri.EscapeDataString(config.Key)+"/");
            string password=CloudSaves.ReadPassword(config.Secret);authorization=String.IsNullOrEmpty(config.User)&&password.Length==0?null:"Basic "+Convert.ToBase64String(Encoding.UTF8.GetBytes(config.User+":"+password));
        }
        private Uri FileUri(string name){if(!Regex.IsMatch(name??"","^[a-f0-9]{32}\\.(json|zip)$"))throw new IOException("Invalid cloud revision filename.");return new Uri(folder,name);}
        private HttpWebRequest Request(Uri uri,string method)
        {
            var request=(HttpWebRequest)WebRequest.Create(uri);request.Method=method;request.AllowAutoRedirect=false;request.Timeout=request.ReadWriteTimeout=20000;request.UserAgent="PortForge-CloudSaves/1";
            request.AutomaticDecompression=DecompressionMethods.None;if(authorization!=null)request.Headers[HttpRequestHeader.Authorization]=authorization;return request;
        }
        private static HttpWebResponse Response(HttpWebRequest request,CancellationToken token)
        {
            try{return (HttpWebResponse)request.GetResponse();}
            catch(WebException e)
            {
                token.ThrowIfCancellationRequested();var reply=e.Response as HttpWebResponse;
                if(reply!=null){int code=(int)reply.StatusCode;reply.Dispose();throw new IOException(code==401||code==403?"Cloud sign-in failed. Check the WebDAV username and app password.":"The cloud server returned HTTP "+code+". Your local save was kept.");}
                throw new IOException("Cloud connection unavailable. Your local save was kept for a later retry.",e);
            }
        }
        private void EnsureFolder(CancellationToken token)
        {
            foreach(var path in new[]{new Uri(endpoint,"PortForgeCloudSaves/"),folder})
            {
                var request=Request(path,"MKCOL");request.ContentLength=0;
                using(token.Register(request.Abort))try{using(var response=(HttpWebResponse)request.GetResponse())if((int)response.StatusCode!=201)throw new IOException("The cloud folder could not be created.");}
                catch(WebException e){token.ThrowIfCancellationRequested();using(var response=e.Response as HttpWebResponse){if(response==null||(int)response.StatusCode!=405)throw new IOException("The cloud folder could not be opened. Check the WebDAV address and app password.");}}
            }
        }
        public string[] List(CancellationToken token)
        {
            EnsureFolder(token);var request=Request(folder,"PROPFIND");request.Headers["Depth"]="1";request.ContentType="application/xml";
            byte[] body=Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:resourcetype/></d:prop></d:propfind>");request.ContentLength=body.Length;
            using(token.Register(request.Abort))
            {
                using(var output=request.GetRequestStream())output.Write(body,0,body.Length);
                using(var response=Response(request,token))using(var input=response.GetResponseStream())
                {
                    if((int)response.StatusCode!=207)throw new IOException("The WebDAV server did not return a folder listing.");
                    var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2*1024*1024};
                    var doc=new XmlDocument{XmlResolver=null};using(var reader=XmlReader.Create(input,settings))doc.Load(reader);
                    var names=new HashSet<string>(StringComparer.Ordinal);
                    foreach(XmlNode node in doc.GetElementsByTagName("href","DAV:"))
                    {
                        Uri uri;if(!Uri.TryCreate(folder,node.InnerText,out uri)||uri.Scheme!=folder.Scheme||uri.Authority!=folder.Authority)continue;
                        string parent=uri.AbsolutePath.Substring(0,uri.AbsolutePath.LastIndexOf('/')+1);if(parent!=folder.AbsolutePath)continue;
                        string name=Uri.UnescapeDataString(uri.Segments.Last());if(Regex.IsMatch(name,"^[a-f0-9]{32}\\.(json|zip)$"))names.Add(name);
                    }
                    return names.ToArray();
                }
            }
        }
        public byte[] ReadMetadata(string name,CancellationToken token)
        {using(var memory=new MemoryStream()){Read(name,memory,65536,token);return memory.ToArray();}}
        public void Download(string name,string local,CancellationToken token)
        {using(var file=new FileStream(local,FileMode.CreateNew,FileAccess.Write))Read(name,file,CloudSaveService.MaxBytes+8*1024*1024,token);}
        private void Read(string name,Stream output,long limit,CancellationToken token)
        {
            var request=Request(FileUri(name),"GET");using(token.Register(request.Abort))using(var response=Response(request,token))using(var input=response.GetResponseStream())
            {if((int)response.StatusCode!=200)throw new IOException("The cloud save could not be downloaded.");byte[] buffer=new byte[65536];long total=0;int count;while((count=input.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();total+=count;if(total>limit)throw new IOException("Cloud response exceeds its size limit.");output.Write(buffer,0,count);}}
        }
        public void Upload(string name,string local,CancellationToken token)
        {
            EnsureFolder(token);var request=Request(FileUri(name),"PUT");request.Headers["If-None-Match"]="*";request.ContentType=name.EndsWith(".json")?"application/json":"application/zip";request.ContentLength=new FileInfo(local).Length;
            using(token.Register(request.Abort))
            {
                using(var input=File.OpenRead(local))using(var output=request.GetRequestStream()){byte[] buffer=new byte[65536];int count;while((count=input.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();output.Write(buffer,0,count);}}
                using(var response=Response(request,token))if((int)response.StatusCode!=201&&(int)response.StatusCode!=204&&(int)response.StatusCode!=200)throw new IOException("The cloud server did not accept the save.");
            }
        }
        public string SavedMessage { get { return "Cloud save uploaded and its archive checksum verified over WebDAV."; } }
    }

    public sealed class CloudSaveMonitor : IDisposable
    {
        private readonly Timer timer;private readonly CancellationTokenSource stop=new CancellationTokenSource();private int busy;
        private readonly Dictionary<string,DateTime> retry=new Dictionary<string,DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<IEnumerable<string>> knownGames;
        private readonly CloudSaveService service;
        public CloudSaveMonitor(Func<IEnumerable<string>> games=null,CloudSaveService cloudService=null,int firstDelayMs=5000,int intervalMs=15000){service=cloudService??CloudSaves.Service;knownGames=games??(()=>new string[0]);timer=new Timer(delegate{Tick();},null,firstDelayMs,intervalMs);}
        private void Tick(){if(Interlocked.Exchange(ref busy,1)!=0)return;try{service.RegisterGames(knownGames(),stop.Token);foreach(var game in service.EnabledGames()){if(stop.IsCancellationRequested)return;DateTime next;if(retry.TryGetValue(game.Exe,out next)&&DateTime.UtcNow<next)continue;var result=service.Sync(game.Exe,stop.Token);retry[game.Exe]=DateTime.UtcNow.AddSeconds(result.State=="ERROR"?120:result.Conflict?30:15);}}catch(OperationCanceledException){}catch(Exception){}finally{Interlocked.Exchange(ref busy,0);}}
        public void Dispose(){stop.Cancel();timer.Dispose();}
    }
}
