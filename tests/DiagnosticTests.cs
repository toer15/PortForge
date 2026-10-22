using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using PortForge;
internal static class DiagnosticTests
{
    private static int checks;
    private static void Check(bool v,string name){if(!v)throw new Exception("FAIL: "+name);checks++;Console.WriteLine("PASS: "+name);}
    private static void Reject(Action a,string name){bool rejected=false;try{a();}catch(IOException){rejected=true;}catch(InvalidDataException){rejected=true;}Check(rejected,name);}
    private static int Main(string[] args)
    {
        try
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"diagnostic-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            string exe=Path.Combine(root,"Diagnostic game.exe"),code=Path.Combine(root,"Diagnostic game_code.dll");File.Copy(args[0],exe);File.Copy(args[0],code);
            foreach(string lib in GameDiagnostics.Libraries)File.Copy(args[0],Path.Combine(root,lib));
            var cap=RuntimeCapabilities.Read(exe,CancellationToken.None);
            string ini="; preserve me\r\nfps_limit=120\r\nvsync=0\r\nresolution=1.5\r\nanisotropy=16\r\nshadows=0\r\ncompute=0\r\nlog=2\r\nvk_validation=1\r\nprofile=1\r\ngamepad_map=A:B\r\nuser=2\r\n";
            string log="vulkan: a 213 ms frame: 0 shaders translated (0 ms), 1 pipelines made (198 ms waited for)\nvulkan: 11 frames over 50 ms in the last 600, the slowest 220 ms\nvulkan: a samples-passed count spans more than 4096 passes, its reports are made up\nvulkan: 1000 draws, 0 skipped; 0 dispatches in 0 batches (0 ms waiting), 0 skipped\n";
            File.WriteAllText(Path.ChangeExtension(exe,".ini"),ini,new UTF8Encoding(false));File.WriteAllText(Path.ChangeExtension(exe,".log"),log);
            File.WriteAllText(Path.Combine(root,"save.dat"),"saved game");File.WriteAllText(Path.ChangeExtension(exe,".jitcache"),"keep cache");
            var health=GameDiagnostics.Scan(exe,cap,null);
            Check(health.Issues.Any(i=>i.Id=="debug")&&health.Issues.Any(i=>i.Id=="rendering_off")&&health.Issues.Any(i=>i.Id=="upscale_stalls"),"Checks detect costly debug flags, disabled rendering and slow frames with upscaling");
            Check(health.Issues.Any(i=>i.Id=="occlusion"&&i.Fix==null)&&health.Issues.Any(i=>i.Id=="pipeline_stalls"),"Engine occlusion warnings and shader stalls are reported without claiming a universal repair");
            Check(!health.Issues.Any(i=>i.Id=="renderer_failure"),"Zero skipped draws and dispatches do not create false rendering errors");
            string exeHash=GameDownloads.HashFile(exe,CancellationToken.None),codeHash=GameDownloads.HashFile(code,CancellationToken.None);
            GameDiagnostics.Repair(exe,cap,null);var values=GameOptimization.ReadValues(File.ReadAllText(Path.ChangeExtension(exe,".ini")));
            Check(values["compute"]=="1"&&values["shadows"]=="1"&&values["log"]=="1"&&values["vk_validation"]=="0"&&values["resolution"]=="1","Supported repairs restore rendering, disable expensive diagnostics and reduce excessive scale");
            Check(values["fps_limit"]=="120"&&values["vsync"]=="0"&&values["gamepad_map"]=="A:B"&&values["user"]=="2","Repairs preserve the requested FPS, VSync, controller and player settings");
            Check(GameDownloads.HashFile(exe,CancellationToken.None)==exeHash&&GameDownloads.HashFile(code,CancellationToken.None)==codeHash&&File.ReadAllText(Path.Combine(root,"save.dat"))=="saved game"&&File.ReadAllText(Path.ChangeExtension(exe,".jitcache"))=="keep cache","Repairs preserve executable code, game saves and caches");
            Check(GameDiagnostics.Scan(exe,cap,null).Issues.Any(i=>i.Id=="occlusion"&&i.Fix==null),"Changing settings does not falsely clear recorded engine-level problems");
            GameDiagnostics.RestoreLastRepair(exe);Check(File.ReadAllText(Path.ChangeExtension(exe,".ini"))==ini,"Undo restores the exact pre-repair settings");
            var profile=new GraphicsProfile {FpsLimit="144",Resolution=1,VSync=false,Anisotropy=8};GameOptimization.SaveAndVerify(exe,profile,cap);
            string saved=File.ReadAllText(Path.ChangeExtension(exe,".ini"));int backups=Directory.GetFiles(root,"*.before-portforge-*.bak").Length;
            GameOptimization.SaveAndVerify(exe,profile,cap);
            Check(GameOptimization.Load(exe).FpsLimit=="144"&&saved==File.ReadAllText(Path.ChangeExtension(exe,".ini"))&&Directory.GetFiles(root,"*.before-portforge-*.bak").Length==backups,"Saving verifies the active FPS and repeated unchanged saves do not grow the INI or create extra backups");
            string backendRoot=Path.Combine(root,"engine");Directory.CreateDirectory(backendRoot);string engine=Path.Combine(backendRoot,"scarletnx.exe");File.Copy(args[0],engine);
            foreach(string lib in GameDiagnostics.Libraries)File.Copy(args[0],Path.Combine(backendRoot,lib));
            string missing=Path.Combine(root,GameDiagnostics.Libraries[0]);File.Delete(missing);GameDiagnostics.Repair(exe,cap,engine,new[]{"restore_runtime"});
            Check(File.ReadAllBytes(missing).SequenceEqual(File.ReadAllBytes(Path.Combine(backendRoot,GameDiagnostics.Libraries[0]))),"Missing libraries are restored from an exactly matching runtime and verified");
            File.Delete(missing);using(var stream=new FileStream(engine,FileMode.Append,FileAccess.Write))stream.WriteByte(1);
            Reject(()=>GameDiagnostics.Repair(exe,cap,engine,new[]{"restore_runtime"}),"A mismatched engine is rejected before copying replacement libraries");Check(!File.Exists(missing),"An incompatible library is never published");
            // Stall causes are told apart: shader work versus loading data.
            string realLog="vulkan: a 234 ms frame: 0 shaders translated (0 ms), 0 pipelines made (0 ms waited for), 4 render targets made, 1 texture uploads (128 KB), 0 ms in finishes\nvulkan: a 345 ms frame: 0 shaders translated (0 ms), 0 pipelines made (0 ms waited for), 36 render targets made, 8 texture uploads (7478 KB), 0 ms in finishes\nvulkan: 2 frames over 50 ms in the last 600, the slowest 345 ms\n";
            var loadingOnly=GameDiagnostics.Inspect("fps_limit=game\n",realLog,cap);
            Check(loadingOnly.Issues.Any(i=>i.Id=="loading_stalls"&&i.Fix==null)&&!loadingOnly.Issues.Any(i=>i.Id=="pipeline_stalls"),"Freezes with no shader work are reported as loading stalls, not shader stalls");
            Check(GameDiagnostics.StallFrames(realLog).Count==2&&GameDiagnostics.StallFrames(log).Count==1&&GameDiagnostics.StallFrames(log)[0].WaitedMs==198,"Slow-frame log lines are parsed in both the full and short formats");
            var compileStall=GameDiagnostics.Inspect("fps_limit=game\n","vulkan: a 120 ms frame: 14 shaders translated (80 ms), 3 pipelines made (60 ms waited for), 2 render targets made, 1 texture uploads (12 KB), 0 ms in finishes\n",cap);
            Check(compileStall.Issues.Any(i=>i.Id=="pipeline_stalls"&&i.Fix=="speed_pipelines")&&!compileStall.Issues.Any(i=>i.Id=="loading_stalls"),"Shader compile stalls offer the supported pipeline repair");
            // The warm-up repair plays the game from a protected save and restores everything.
            GameOptimization.GameplaySeconds=3;
            string saveFolder=Path.Combine(root,"Diagnostic game_data","save");Directory.CreateDirectory(saveFolder);File.WriteAllText(Path.Combine(saveFolder,"slot.dat"),"original save");
            string warmIni="fps_limit=60\r\nlog=1\r\nuser=3\r\n";File.WriteAllText(Path.ChangeExtension(exe,".ini"),warmIni,new UTF8Encoding(false));
            string warmLog="vulkan: a 150 ms frame: 5 shaders translated (40 ms), 2 pipelines made (50 ms waited for)\nvulkan: 3 frames over 50 ms in the last 600, the slowest 150 ms\n";File.WriteAllText(Path.ChangeExtension(exe,".log"),warmLog);
            Check(GameDiagnostics.Scan(exe,cap,null).Issues.Any(i=>i.Fix=="speed_pipelines"),"The pipeline repair is offered for a recorded compile stall");
            Check(!GameDiagnostics.Repair(exe,cap,null,null,false).Contains("warmed")&&File.Exists(Path.ChangeExtension(exe,".ini")),"Plain repairs never start the slow warm-up");
            string message=GameDiagnostics.Repair(exe,cap,null,null,true);
            Check(message.Contains("Shader cache warmed")&&message.Contains("10 pipelines built"),"The warm-up repair runs the scripted play-through and reports what it built");
            Check(File.ReadAllText(Path.Combine(saveFolder,"slot.dat"))=="original save"&&!File.Exists(Path.Combine(saveFolder,"extra.dat")),"The scripted play-through cannot change or add to the game's save");
            var afterWarm=GameOptimization.ReadValues(File.ReadAllText(Path.ChangeExtension(exe,".ini")));
            Check(File.ReadAllText(Path.ChangeExtension(exe,".log"))==warmLog&&GameOptimization.ReadValues(warmIni).All(p=>afterWarm.ContainsKey(p.Key)&&afterWarm[p.Key]==p.Value),"The player's log and original settings are preserved while the supported worker repair is kept");
            Check(!Directory.GetDirectories(root,"PortForge-test-backup-*").Any()&&!Directory.GetDirectories(Path.Combine(root,"Diagnostic game_data"),"save.pf-test-*").Any(),"Temporary backups are removed after a successful warm-up");
            var again=GameDiagnostics.Scan(exe,cap,null).Issues.First(i=>i.Id=="pipeline_stalls");
            Check(again.Fix==null&&again.Evidence.Contains("already applied"),"The same recorded session is not warmed again and again");
            File.WriteAllText(Path.ChangeExtension(exe,".log"),warmLog+"vulkan: a 90 ms frame: 2 shaders translated (30 ms), 1 pipelines made (25 ms waited for)\n");
            Check(GameDiagnostics.Scan(exe,cap,null).Issues.First(i=>i.Id=="pipeline_stalls").Fix=="speed_pipelines","A new play session with new stalls can be repaired again");
            using(var cts=new CancellationTokenSource(1200))
            {
                GameOptimization.GameplaySeconds=20;bool stopped=false;
                try{GameOptimization.WarmCaches(exe,null,cts.Token);}catch(OperationCanceledException){stopped=true;}
                Check(stopped&&File.ReadAllText(Path.Combine(saveFolder,"slot.dat"))=="original save"&&!File.Exists(Path.Combine(saveFolder,"extra.dat"))&&!Directory.GetDirectories(root,"PortForge-test-backup-*").Any(),"Cancelling the warm-up stops the game and still restores the save");
            }
            GameOptimization.GameplaySeconds=45;
            File.WriteAllText(Path.ChangeExtension(exe,".ini"),ini,new UTF8Encoding(false));File.WriteAllText(Path.ChangeExtension(exe,".log"),log);            var source=GameDiagnostics.Inspect(ini,log,cap);source.Exe=exe;
            Reject(()=>CodexDiagnosis.Parse("{\"summary\":\"bad\",\"engineering_note\":\"\",\"recommended_rules\":[\"execute_code\"],\"needs_engine_fix\":false}",source),"AI output cannot introduce code execution or unknown repair actions");
            var noFix=GameDiagnostics.Inspect("fps_limit=game\nresolution=1\n", "", cap);
            var gated=CodexDiagnosis.Parse("{\"summary\":\"review\",\"engineering_note\":\"\",\"recommended_rules\":[\"reduce_upscale\"],\"needs_engine_fix\":false}",noFix);
            Check(gated.recommended_rules.Length==0,"AI recommendations are intersected with independently detected eligible repairs");
            source.Log+="\nFATAL: token=do-not-share C:\\Users\\private\\file\n";
            Check(!GameDiagnostics.SafeEvidence(source).Contains("do-not-share")&&!GameDiagnostics.SafeEvidence(source).Contains("C:\\Users"),"AI evidence excludes key/token lines and local path information");
            var ai=CodexDiagnosis.Review(source,Path.Combine(root,"AI reports & spaces"),CancellationToken.None,args[1]);
            Check(ai.needs_engine_fix&&ai.recommended_rules.SequenceEqual(new[]{"reduce_upscale"})&&File.Exists(ai.ReportPath),"The AI process receives restricted flags and returns validated structured diagnostics");
            source.Issues.Add(new GameIssue("wait","wait-fixture","test cancellation"));
            using(var cts=new CancellationTokenSource(500)){bool stopped=false;try{CodexDiagnosis.Review(source,Path.Combine(root,"cancelled-ai"),cts.Token,args[1]);}catch(OperationCanceledException){stopped=true;}Check(stopped,"Cancelling AI review stops its owned process without applying a repair");}
            Console.WriteLine(checks+" diagnostic checks passed. Evidence: "+root);return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
