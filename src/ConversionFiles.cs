using System;
using System.IO;
using System.Threading;

namespace PortForge
{
    public static class ConversionFiles
    {
        // Move the complete game instead of copying gigabytes of assets a third time.
        // Only the top-level DLLs need independent copies: ScarletNX may hard-link them.
        public static void Publish(string compiledGame, string destination, CancellationToken token)
        {
            if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("That output already exists. Choose a new name or save folder.");
            Paths.CheckLinks(compiledGame);
            Paths.CheckLinks(Path.GetDirectoryName(destination));
            if (Paths.Overlap(compiledGame, destination)) throw new IOException("The completed game and destination must be separate folders.");
            foreach (string library in Directory.EnumerateFiles(compiledGame, "*.dll", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                Paths.CheckLinks(library);
                string temporary = library + ".pf-" + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var input = File.OpenRead(library))
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[1024 * 1024];
                        int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                        { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); }
                    }
                    token.ThrowIfCancellationRequested();
                    ReplaceWithRetry(temporary, library, token);
                }
                finally
                {
                    // An inability to remove a temporary copy must not hide the real error.
                    try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                }
            }
            token.ThrowIfCancellationRequested();
            Directory.Move(compiledGame, destination);
        }

        public static bool TryCleanup(string work, string outputRoot, Action<string> warning)
        {
            try
            {
                if (!Paths.IsInside(work, outputRoot) || !Path.GetFileName(work).StartsWith(".portforge-native-", StringComparison.Ordinal))
                    throw new IOException("The temporary folder is outside this conversion's output folder.");
                Paths.CheckLinks(work);
                if (Directory.Exists(work)) Directory.Delete(work, true);
                return true;
            }
            catch (Exception error)
            {
                warning("WARNING: Temporary files could not be removed from " + work + ". " + error.Message);
                return false;
            }
        }

        public static void ReplaceWithRetry(string temporary, string destination, CancellationToken token, string backup = null)
        {
            for (int attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try { File.Replace(temporary, destination, backup); return; }
                catch (IOException e)
                {
                    int code = e.HResult & 0xffff;
                    if (attempt >= 6 || (code != 32 && code != 33 && code != 1175 && code != 1176 && code != 1177)) throw;
                    if (token.WaitHandle.WaitOne(100 << attempt)) token.ThrowIfCancellationRequested();
                }
            }
        }
    }
}
