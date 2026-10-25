using System;
using System.Threading;
using PortForge;

internal static class ConversionRunner
{
    private sealed class Reporter : IProgress<string> { public void Report(string line) { Console.WriteLine(line); } }
    private static int Main(string[] args)
    {
        if (args.Length != 5 && args.Length != 6) return 2;
        try
        {
            var request = new NativeRequest { Input = args[0], Name = args[1], Output = args[2], Backend = args[3], ProductKeys = args[4], PatchFolder = args.Length == 6 ? args[5] : null };
            string result = NativeConversion.Convert(request, new Reporter(), CancellationToken.None);
            Console.WriteLine("VERIFIED CONVERSION OUTPUT: " + result);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
