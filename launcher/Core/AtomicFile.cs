using System.Text;

namespace HookshotLauncher.Core;

/// A file written whole or not at all: into &lt;name&gt;.tmp, flushed to the disk, then renamed over the old one
static class AtomicFile
{
    public static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var bytes = new UTF8Encoding(false).GetBytes(text);
            f.Write(bytes);
            f.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }
}
