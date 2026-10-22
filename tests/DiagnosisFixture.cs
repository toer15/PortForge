using System;
using System.IO;
using System.Linq;
using System.Threading;
internal static class DiagnosisFixture
{
    private static int Main(string[] args)
    {
        string prompt=Console.In.ReadToEnd();
        if(!args.Contains("read-only")||!args.Contains("shell_tool")||!args.Contains("unified_exec")||!args.Contains("--ignore-user-config"))return 4;
        if(prompt.Contains("wait-fixture"))Thread.Sleep(15000);
        int index=Array.IndexOf(args,"--output-last-message");if(index<0)return 3;
        File.WriteAllText(args[index+1],"{\"summary\":\"Fixture diagnosis; verify in gameplay.\",\"engineering_note\":\"An engine code fix may still be required.\",\"recommended_rules\":[\"reduce_upscale\"],\"needs_engine_fix\":true}");
        return 0;
    }
}
