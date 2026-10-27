using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PortForge
{
    public sealed class GameIssue
    {
        public string Id, Title, Evidence, Fix;
        public GameIssue(string id,string title,string evidence,string fix=null) {Id=id;Title=title;Evidence=evidence;Fix=fix;}
    }
    public sealed class GameHealth
    {
        public readonly List<GameIssue> Issues=new List<GameIssue>();
        public string Exe, Log;
        public bool Running;
        public string Summary {get{return Issues.Count==0?"No known problem found in the checked settings and log. Gameplay has not been certified.":Issues.Count+" finding(s); "+Issues.Count(x=>x.Fix!=null)+" supported repair(s).";}}
        public string Text()
        {
            var s=new StringBuilder(Summary+Environment.NewLine);
            foreach(var issue in Issues) s.AppendLine("\r\n"+(issue.Fix==null?"CHECK":"REPAIR AVAILABLE")+": "+issue.Title+"\r\n"+issue.Evidence);
            return s.ToString();
        }
    }
    public sealed class StallFrame
    {
        public int FrameMs, Shaders, TranslateMs, Pipelines, WaitedMs, RenderTargets, TextureUploads, TextureKb;
    }
    public static class GameDiagnostics
    {
        public static string UpscalingStatus(string log)
        {
            var records = Regex.Matches(log ?? "", @"vulkan: FSR 1 (?:active:|inactive:|upscaled )[^\r\n]*");
            return records.Count == 0 ? "FSR: no upscaled frame recorded. A selected option or GPU-ready message does not confirm active upscaling."
                : "Latest recorded FSR status: " + records[records.Count - 1].Value.Substring("vulkan: ".Length);
        }
        private static readonly Regex StallLine=new Regex(@"a (\d+) ms frame: (\d+) shaders translated \((\d+) ms\), (\d+) pipelines made \((\d+) ms waited for\)(?:, (\d+) render targets made, (\d+) texture uploads \((\d+) KB(?:, \d+ ms)?\))?",RegexOptions.Compiled);
        public static List<StallFrame> StallFrames(string log)
        {
            var list=new List<StallFrame>();
            foreach(Match m in StallLine.Matches(log??""))
            {
                Func<int,int> n=i=>m.Groups[i].Success?Int32.Parse(m.Groups[i].Value):0;
                list.Add(new StallFrame{FrameMs=n(1),Shaders=n(2),TranslateMs=n(3),Pipelines=n(4),WaitedMs=n(5),RenderTargets=n(6),TextureUploads=n(7),TextureKb=n(8)});
            }
            return list;
        }
        public static readonly string[] Libraries={"libc++.dll","libffi-8.dll","libiconv-2.dll","libLLVM-23.dll","libxml2-16.dll","libzstd.dll","zlib1.dll"};
        public static string WarmMarker(string folder){return Path.Combine(folder,"PortForge-warmed.txt");}
        public static string LogFingerprint(string log)
        {
            string lines=String.Join("\n",Regex.Matches(log??"",@"[^\n]*(?:ms frame:|frames over 50 ms)[^\n]*").Cast<Match>().Select(m=>m.Value.Trim()));
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(lines))).Replace("-","");
        }
        public static bool Running(string exe) {try {GameOptimization.EnsureStopped(exe);return false;} catch(IOException){return true;}}
        public static string Tail(string path,int bytes=196608)
        {
            if(!File.Exists(path))return "";
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            {stream.Seek(Math.Max(0,stream.Length-bytes),SeekOrigin.Begin);using(var reader=new StreamReader(stream,Encoding.UTF8)) return reader.ReadToEnd();}
        }
        public static GameHealth Scan(string exe,RuntimeCapabilities cap,string backend)
        {
            if(!File.Exists(exe))throw new ArgumentException("Choose the generated game executable first.");
            var health=Inspect(File.Exists(GameOptimization.ConfigPath(exe))?File.ReadAllText(GameOptimization.ConfigPath(exe)):"",Tail(Path.ChangeExtension(exe,".log")),cap);
            health.Exe=Path.GetFullPath(exe);health.Running=Running(exe);
            string folder=Path.GetDirectoryName(health.Exe);
            if(File.Exists(WarmMarker(folder))&&File.ReadAllText(WarmMarker(folder)).Trim()==LogFingerprint(health.Log))
                foreach(var issue in health.Issues.Where(i=>i.Fix=="speed_pipelines")){issue.Fix=null;issue.Evidence+=" This repair was already applied for the recorded session. Play again to check whether it helped; if stutter remains, shader compilation is not the cause.";}
            if(!File.Exists(Path.Combine(folder,Path.GetFileNameWithoutExtension(exe)+"_code.dll"))) health.Issues.Add(new GameIssue("missing_code","Game-code DLL is missing","Restore this game's original _code.dll from its compiled output or convert again. A different game's DLL cannot repair it."));
            string[] missing=Libraries.Where(n=>!File.Exists(Path.Combine(folder,n))).ToArray();
            if(missing.Length>0)
            {
                bool available=File.Exists(backend)&&missing.All(n=>File.Exists(Path.Combine(Path.GetDirectoryName(backend),n)));
                health.Issues.Add(new GameIssue("missing_runtime","Runtime libraries are missing",String.Join(", ",missing)+". Restoration requires an installed engine with an exactly matching runtime executable.",available?"restore_runtime":null));
            }
            return health;
        }
        public static GameHealth Inspect(string ini,string log,RuntimeCapabilities cap)
        {
            var h=new GameHealth {Log=log??""};var values=GameOptimization.ReadValues(ini??"");
            Func<string,string,string> get=(key,def)=>values.ContainsKey(key)?values[key]:def;
            bool debug=get("log","1")=="2"||new[]{"vk_validation","profile","nvn_trace","vk_gpu_profile","mme_verify","diag"}.Any(k=>get(k,"0")!="0");
            if(debug)h.Issues.Add(new GameIssue("debug","Expensive diagnostic settings are enabled","Verbose logging, validation or profiling can slow gameplay. The repair restores normal logging and switches expensive diagnostics off.",cap!=null&&cap.Keys.Contains("log")?"disable_debug":null));
            if(get("compute","1")=="0"||get("shadows","1")=="0")h.Issues.Add(new GameIssue("rendering_off","Part of the game's rendering is disabled","Skipped compute work or shadow maps can make the world look wrong. The repair restores both; it does not skip rendering to gain FPS.",cap!=null&&(get("compute","1")!="0"||cap.Keys.Contains("compute"))&&(get("shadows","1")!="0"||cap.Keys.Contains("shadows"))?"restore_rendering":null));
            var stalls=Regex.Matches(h.Log,@"(\d+) frames over 50 ms").Cast<Match>().Any(m=>Int32.Parse(m.Groups[1].Value)>0)||Regex.IsMatch(h.Log,@"a [1-9]\d{2,} ms frame:");
            decimal res;
            if(stalls&&Decimal.TryParse(get("resolution","1"),NumberStyles.Number,CultureInfo.InvariantCulture,out res)&&res>1)
                h.Issues.Add(new GameIssue("upscale_stalls","Upscaling is enabled while slow frames are recorded","The log records slow frames and the render scale is above 100%. The supported repair returns to 100%; it cannot remove engine or shader-compilation stalls.",cap!=null&&cap.Keys.Contains("resolution")?"reduce_upscale":null));
            var frames=StallFrames(h.Log);
            var compile=frames.Where(f=>f.WaitedMs>=8||f.Shaders>0&&f.TranslateMs>=8).ToList();
            var loading=frames.Where(f=>!compile.Contains(f)&&(f.TextureKb>=1024||f.RenderTargets>=20||f.TextureUploads>=20)).ToList();
            if(compile.Count>0||Regex.IsMatch(h.Log,@"[1-9]\d* ms waited for\)|[1-9]\d* ms waiting.*pipelines"))
            {
                string fix=cap!=null&&cap.Keys.Contains("log")?"speed_pipelines":null;
                h.Issues.Add(new GameIssue("pipeline_stalls","Shader/pipeline compilation stalls were recorded",
                    (compile.Count>0?compile.Count+" slow frame(s) were spent making shaders or pipelines (worst "+compile.Max(f=>f.FrameMs)+" ms). ":"The engine waited for new GPU pipelines. ")+
                    "The supported repair gives the engine more pipeline threads and pre-builds the shader cache by playing the opening of the game from a backed-up save. It helps scenes the test reaches; areas you have not visited can still compile once.",fix));
            }
            var unexplained=frames.Where(f=>!compile.Contains(f)&&!loading.Contains(f)&&f.FrameMs>=100).ToList();
            if(unexplained.Count>=8)
                h.Issues.Add(new GameIssue("system_slowdown","Slowdown with no new game work",
                    unexplained.Count+" slow frame(s) of 100 ms or more (worst "+unexplained.Max(f=>f.FrameMs)+" ms) had no shader, texture or loading work. That points outside the game's rendering: another program using the CPU or GPU, the game window losing focus, or Windows power saving. The live doctor in the main window records what else was running at that moment (PortForge-doctor.log in the game folder)."));
            if(loading.Count>0)
                h.Issues.Add(new GameIssue("loading_stalls","Short freezes while the game loads files and textures",
                    loading.Count+" slow frame(s) happened with no shader work, while "+loading.Max(f=>f.TextureUploads)+" textures ("+(loading.Max(f=>f.TextureKb)/1024)+" MB) or "+loading.Max(f=>f.RenderTargets)+" render targets were created at once (worst "+loading.Max(f=>f.FrameMs)+" ms). "+
                    "This is the game streaming data at scene changes, not a setting. Keep the game on a fast SSD and avoid antivirus scanning its folder. A real fix needs an engine change; the app reports it rather than hiding it."));
            if(Regex.IsMatch(h.Log,@"samples-passed count spans more than \d+ passes|occlusion queries aren't precise"))
                h.Issues.Add(new GameIssue("occlusion","The renderer reported unreliable occlusion counts","This is an engine-level graphics limitation and may affect visibility in the world. Configuration changes cannot prove it fixed. Preserve this report for a backend code fix."));
            if(Regex.IsMatch(h.Log,@"(?i)unsupported (?:shader|opcode)|shader[^\r\n]*(?:translation failed|failed to translate)|[1-9]\d* draws[^\r\n]*[1-9]\d* skipped|draws, [1-9]\d* skipped|batches[^\r\n]*[1-9]\d* skipped"))
                h.Issues.Add(new GameIssue("renderer_failure","Unsupported or skipped rendering was reported","Some rendering operations failed or were skipped. A supported engine/code fix is needed; the app will not mark this resolved by hiding the message."));
            /* GPU reset while pictures were shown between the game's frames (frame generation, or repeats up to an FPS limit
               above the game's pace). In tests the same game at its own pace ran without a reset, so the repair keeps
               this game's display to its own frames (paced_display=0, a per-game key the global settings don't change). */
            var pace=Regex.Matches(h.Log,@"Display FPS \d+, target (\d+) \(fps_limit [^,]+, game (\d+)").Cast<Match>().LastOrDefault();
            bool pacedRun=pace!=null&&Int32.Parse(pace.Groups[1].Value,CultureInfo.InvariantCulture)>Int32.Parse(pace.Groups[2].Value,CultureInfo.InvariantCulture);
            if(Regex.IsMatch(h.Log,@"GPU device was lost")&&pacedRun&&get("paced_display","1")!="0")
                h.Issues.Add(new GameIssue("gpu_reset_paced","The GPU reset while extra frames were shown","The session ended with a GPU device loss while frame generation or an FPS limit above the game's pace added pictures between its frames. The repair shows only this game's own frames (paced_display=0). Other games keep frame generation and their FPS limit.",cap!=null&&cap.Keys.Contains("paced_display")?"safe_pacing":null));
            /* Guest heap OOM: engine logs "null pointer plus an offset" / memory_gb=6 hint with FATAL invalid-memory-access.
               BOTW with FSR 3 can fill the default ~3.2 GB heap on Continue/load; raising memory_gb is the supported repair. */
            bool guestOom=Regex.IsMatch(h.Log,@"(?i)null pointer plus an offset|memory_gb=6 in its \.ini may help|a game short of memory");
            int memoryGb; if(!Int32.TryParse(get("memory_gb","0"),NumberStyles.Integer,CultureInfo.InvariantCulture,out memoryGb)) memoryGb=0;
            bool canRaiseMemory=guestOom&&memoryGb<6&&(cap==null||cap.Keys.Contains("memory_gb")||values.ContainsKey("memory_gb"));
            if(canRaiseMemory)
                h.Issues.Add(new GameIssue("guest_oom","The game ran out of console memory","The session crashed with a null-pointer access after the guest heap filled (default memory_gb is the console's ~3.2 GB). The repair sets memory_gb=6 (~4.8 GB), which the engine itself recommends for this failure.", "raise_memory"));
            else if(Regex.IsMatch(h.Log,@"(?im)^.*\bFATAL:|No new frame for \d+ seconds"))
                h.Issues.Add(new GameIssue("runtime_failure","The runtime reported a failure or stall","The latest recorded session contains a fatal error or stalled-frame warning. Review the game log and AI diagnosis; no general binary repair is known."));
            /* Engine 1.48: the FPS limit itself is the game's frame rate with the speed lock off (the app writes that for a
               number or Maximum) and the console pace with it on (Game speed), so speed_lock is the player's choice, not a fault */
            if((cap==null||!cap.Keys.Contains("speed_lock"))&&get("fps_limit","game")!="game") h.Issues.Add(new GameIssue("timing","Custom frame timing is selected","Higher FPS can change game speed, physics or world behavior in some games. If that happens, select Game speed. A limiter cannot create correct timing patches for every game."));
            return h;
        }
        private static readonly object RepairLock=new object();
        public static string Repair(string exe,RuntimeCapabilities cap,string backend,IEnumerable<string> requested=null,bool warm=false,IProgress<string> progress=null,CancellationToken token=default(CancellationToken))
        {
            using(PlayFirst.Begin(exe)) lock(RepairLock) return RepairLocked(exe,cap,backend,requested,warm,progress,token);
        }
        private static string RepairLocked(string exe,RuntimeCapabilities cap,string backend,IEnumerable<string> requested,bool warm,IProgress<string> progress,CancellationToken token)
        {
            GameOptimization.EnsureStopped(exe);var before=Scan(exe,cap,backend);
            var allowed=new HashSet<string>(before.Issues.Where(x=>x.Fix!=null).Select(x=>x.Fix));
            if(requested!=null)allowed.IntersectWith(requested);
            if(!warm)allowed.Remove("speed_pipelines");
            if(allowed.Count==0)return "No supported automatic repair is currently eligible. Existing files were kept.";
            string folder=Path.GetDirectoryName(Path.GetFullPath(exe));var changes=new Dictionary<string,string>();var done=new List<string>();
            if(allowed.Contains("restore_runtime"))
            {
                if(GameDownloads.HashFile(exe,CancellationToken.None)!=GameDownloads.HashFile(backend,CancellationToken.None)) throw new IOException("The installed engine does not exactly match this game's runtime. Its libraries were not copied. Restore the original matching package.");
                foreach(string lib in Libraries.Where(n=>!File.Exists(Path.Combine(folder,n))))
                {
                    string source=Path.Combine(Path.GetDirectoryName(backend),lib),target=Path.Combine(folder,lib);Paths.CheckLinks(source);Paths.CheckLinks(target);NativeConversion.RequireNativeX64(source);
                    string temporary=target+".repair-"+Guid.NewGuid().ToString("N")+".tmp";
                    try {File.Copy(source,temporary,false);if(GameDownloads.HashFile(source,CancellationToken.None)!=GameDownloads.HashFile(temporary,CancellationToken.None))throw new IOException("Runtime library verification failed.");File.Move(temporary,target);}
                    finally {try{if(File.Exists(temporary))File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
                    done.Add("Restored "+lib+" from the matching engine.");
                }
            }
            if(allowed.Contains("disable_debug"))foreach(string key in new[]{"log","vk_validation","profile","nvn_trace","vk_gpu_profile","mme_verify","diag"}) if(cap!=null&&cap.Keys.Contains(key))changes[key]=key=="log"?"1":"0";
            if(allowed.Contains("restore_rendering"))foreach(string key in new[]{"compute","shadows"})if(cap!=null&&cap.Keys.Contains(key))changes[key]="1";
            if(allowed.Contains("reduce_upscale")&&cap!=null&&cap.Keys.Contains("resolution"))changes["resolution"]="1";
            if(allowed.Contains("raise_memory"))changes["memory_gb"]="6";
            if(allowed.Contains("safe_pacing")&&cap!=null&&cap.Keys.Contains("paced_display"))changes["paced_display"]="0";
            if(allowed.Contains("speed_pipelines")&&cap!=null&&cap.Keys.Contains("pipeline_threads")&&Environment.ProcessorCount>=6)
            {
                int want=HardwareProfile.PipelineThreadsFor(Environment.ProcessorCount),now;string iniText=File.Exists(GameOptimization.ConfigPath(exe))?File.ReadAllText(GameOptimization.ConfigPath(exe)):"";
                var current=GameOptimization.ReadValues(iniText);
                if(cap.Keys.Contains("frame_guard"))changes["pipeline_threads"]="0";
                else if(!current.ContainsKey("pipeline_threads")||!Int32.TryParse(current["pipeline_threads"],out now)||now<want)changes["pipeline_threads"]=want.ToString(CultureInfo.InvariantCulture);
            }
            if(changes.Count>0)
            {
                string path=GameOptimization.ConfigPath(exe);string text=File.Exists(path)?File.ReadAllText(path):"";
                string backup=File.Exists(path)?path+".before-repair-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".bak":null;
                GameOptimization.AtomicWrite(path,Encoding.UTF8.GetBytes(GameOptimization.Merge(text,changes)),backup);
                var actual=GameOptimization.ReadValues(File.ReadAllText(path));foreach(var entry in changes)if(!actual.ContainsKey(entry.Key)||actual[entry.Key]!=entry.Value)throw new IOException("Repair verification failed for "+entry.Key);
                done.Add("Saved and verified: "+String.Join(", ",changes.Select(p=>p.Key+"="+p.Value))+". Previous settings backed up.");
            }
            if(allowed.Contains("speed_pipelines"))
            {
                try
                {
                    done.Add(GameOptimization.WarmCaches(exe,progress,token));
                    File.WriteAllText(WarmMarker(folder),LogFingerprint(before.Log));
                }
                catch(OperationCanceledException){throw;}
                catch(Exception e){done.Add("Pipeline threads were set, but the shader-cache warm-up could not run: "+e.Message);}
            }
            return String.Join(Environment.NewLine,done)+"\r\nRecorded engine warnings require a new gameplay check; they are not automatically cleared.";
        }
        public static string SafeEvidence(GameHealth health)
        {
            string text=health.Text();
            var lines=(health.Log??"").Split('\n').Where(l=>Regex.IsMatch(l,@"(?i)slowest|waited for|unsupported|FATAL:|samples-passed count|No new frame|draws, [1-9]\d* skipped")).TakeLastCompat(18);
            foreach(string line in lines) if(!Regex.IsMatch(line,@"(?i)keys?|password|secret|token|authorization"))text+="\n"+line.Trim();
            text=Regex.Replace(text,@"[A-Za-z]:[\\/][^\r\n]*","[local path removed]");
            return text.Length>10000?text.Substring(0,10000):text;
        }
        public static void RestoreLastRepair(string exe)
        {
            GameOptimization.EnsureStopped(exe);
            string config=GameOptimization.ConfigPath(exe),folder=Path.GetDirectoryName(config);
            string backup=Directory.EnumerateFiles(folder,Path.GetFileName(config)+".before-repair-*.bak").OrderByDescending(p=>p,StringComparer.Ordinal).FirstOrDefault();
            if(backup==null)throw new IOException("There is no automatic-repair settings backup for this game yet.");
            Paths.CheckLinks(backup);Paths.CheckLinks(config);
            GameOptimization.AtomicWrite(config,File.ReadAllBytes(backup),config+".before-undo-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".bak");
        }
        private static IEnumerable<T> TakeLastCompat<T>(this IEnumerable<T> items,int count) {var q=new Queue<T>();foreach(var item in items){q.Enqueue(item);if(q.Count>count)q.Dequeue();}return q;}
    }
}
