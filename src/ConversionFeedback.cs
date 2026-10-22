using System;
using System.IO;

namespace PortForge
{
    public sealed class ConversionStatus
    {
        public string Title, Message;
        public ConversionStatus(string title, string message) { Title = title; Message = message; }
    }

    public sealed class ConversionFailure : IOException
    {
        public string UserTitle { get; private set; }
        public string NextStep { get; private set; }
        public string Field { get; private set; }
        public string LogPath { get; set; }
        public string RecoveryFolder { get; set; }

        public ConversionFailure(string title, string nextStep, string field, string details) : base(details)
        { UserTitle = title; NextStep = nextStep; Field = field; }

        public static ConversionFailure FromBackend(int exitCode, string details)
        {
            string text = (details ?? "").ToLowerInvariant();
            if (text.Contains("needs prod.keys") || text.Contains("provide --keys") || text.Contains("prod.keys is missing"))
                return new ConversionFailure("Key file needed", "This game is encrypted. Choose your prod.keys file in Extra settings, then start the conversion again.", "keys", details);
            if (text.Contains("title.keys") && (text.Contains("missing") || text.Contains("needs") || text.Contains("not found") || text.Contains("provide")))
                return new ConversionFailure("Title key file needed", "Choose your title.keys file in Extra settings, then start the conversion again.", "titleKeys", details);
            if (text.Contains("key") && (text.Contains("decrypt") || text.Contains("missing") || text.Contains("invalid")))
                return new ConversionFailure("Could not read the encrypted game", "The selected key files may be missing or may not match this game. Check them in Extra settings, then try again.", "keys", details);
            if (text.Contains("not enough space") || text.Contains("disk full") || text.Contains("no space left"))
                return new ConversionFailure("Not enough free space", "Free some space or choose a different save folder, then try again.", "output", details);
            return new ConversionFailure("Conversion failed", "The conversion engine could not finish this game (error " + exitCode + "). Check the technical details below for the reason. Some games are not supported yet.", null, details);
        }
    }
}
