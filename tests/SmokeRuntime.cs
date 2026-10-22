using System;
using System.IO;
using System.Xml;

internal static class SmokeRuntime
{
    private static int Main(string[] args)
    {
        // Receives real Windows argv from a generated launcher, not a reconstructed command.
        using (var writer = XmlWriter.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "proof.xml")))
        {
            writer.WriteStartElement("Proof");
            writer.WriteElementString("WorkingDirectory", Environment.CurrentDirectory);
            foreach (string argument in args) writer.WriteElementString("Argument", argument);
            writer.WriteEndElement();
        }
        return 23;
    }
}
