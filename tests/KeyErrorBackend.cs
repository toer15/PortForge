using System;

internal static class KeyErrorBackend
{
    private static int Main()
    {
        // Reproduce the diagnostic in the user's screenshot without reading a commercial game or any keys.
        Console.Error.WriteLine("scarletnx: Encrypted content needs prod.keys. Provide --keys, or put it in ~/.switch");
        return 1;
    }
}
