namespace HookshotLauncher.Core;

/// Space on the disk: what a folder holds, what is free where a build goes, and what a build needs
public static class Disk
{
    /// What a build needs while it builds: the game folder and the build files (the 2.x Builders' "Needs 45 GB")
    public const long BuildBytes = 45L << 30;

    /// What a build makes in its build files folder (make_game.py --work) that Free up space lets go, as the 2.x Builder's
    /// Storage.Freeable: the compiled objects and their system headers' stamp, the translation, the unpacked exefs and the check's
    /// probe. Never the folder itself: the player may have chosen one that holds other things
    public static readonly IReadOnlyList<string> WorkParts = new[] { "objcache", "sa", "exefs", "probe", "system_headers.txt" };

    /// The bytes Free up space would let go in work
    public static long WorkBytes(string work) => WorkParts.Sum(p =>
    {
        var path = Path.Combine(work, p);
        var file = new FileInfo(path);
        return file.Exists ? (file.LinkTarget == null ? file.Length : 0) : FolderBytes(path);
    });

    /// WorkParts let go (a link among them is removed, never followed); the rest of work stays
    public static void FreeWork(string work)
    {
        foreach (var p in WorkParts)
        {
            var path = Path.Combine(work, p);
            if (Directory.Exists(path) || new DirectoryInfo(path).LinkTarget != null) Toolchains.DeleteTree(path);
            else if (File.Exists(path)) File.Delete(path);
        }
    }

    /// The bytes of the files under folder; links are counted as links, never followed; what cannot be read counts nothing
    public static long FolderBytes(string folder)
    {
        long sum = 0;
        var pending = new Stack<string>();
        pending.Push(folder);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            try
            {
                foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    if (entry.LinkTarget != null) continue;
                    if (entry is DirectoryInfo d) pending.Push(d.FullName);
                    else if (entry is FileInfo f) sum += f.Length;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return sum;
    }

    /// The free bytes on the drive that holds path (or would: the nearest folder above it that exists); null when unknown
    public static long? FreeBytes(string path)
    {
        try
        {
            var at = Path.GetFullPath(path);
            while (!Directory.Exists(at))
            {
                var up = Path.GetDirectoryName(at);
                if (up == null || up == at) return null;
                at = up;
            }
            // (the mount that holds it: the longest drive root it starts with)
            var drive = DriveInfo.GetDrives()
                .Where(d => { try { return d.IsReady && Within(at, d.RootDirectory.FullName); } catch (IOException) { return false; } })
                .OrderByDescending(d => d.RootDirectory.FullName.Length).FirstOrDefault();
            return drive?.AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    static bool Within(string path, string root)
    {
        var c = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var r = Path.TrimEndingDirectorySeparator(root);
        return r.Length == 0 || path.Equals(r, c) || path.StartsWith(r + Path.DirectorySeparatorChar, c) || root == "/";
    }

    /// The drive a path is on, as the window names it: "D:" on Windows, the mount point on Linux ("/" , "/home", "/mnt/games")
    public static string DriveName(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (OperatingSystem.IsWindows()) return (Path.GetPathRoot(full) ?? full).TrimEnd('\\', '/');
            var mount = DriveInfo.GetDrives()
                .Select(d => d.RootDirectory.FullName)
                .Where(r => Within(full, r))
                .OrderByDescending(r => r.Length).FirstOrDefault();
            return mount ?? "/";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return path; }
    }
}
