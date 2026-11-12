namespace HookshotLauncher.Core;

/// A dump of the game found on this PC: a .nup file, or a folder holding its romfs and exefs folders (Folders)
/// (Romfs, Exefs: the two folders when they were picked one by one, not side by side in Path)
public sealed record FoundGameFile(string Path, string Where, long Size, DateTime Modified, bool Folders, string? Romfs = null, string? Exefs = null)
{
    public string Name => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path));

    /// What make_game.py builds from
    public BuildSource Source => Folders
        ? new BuildSource.Folders(Romfs ?? System.IO.Path.Combine(Path, "romfs"), Exefs ?? System.IO.Path.Combine(Path, "exefs"))
        : new BuildSource.Nup(Path);
}

/// The player's game files where people keep them (builder/HookshotBuilder/Finder.cs, FindDumps): .nup files in Downloads,
/// Desktop, Documents, the home folder and the top of each drive, one folder level down too, newest first. Only names and sizes
/// are read here; the version is checked one file at a time afterwards (make_game.py --check). A folder holding romfs and exefs
/// folders counts too.
public static class GameFiles
{
    /// Where to look, with the name each place is shown with. drives: the tops of the drives (Windows: the fixed and removable
    /// ones; Linux: what is mounted under /mnt, /media and /run/media), null to ask the system
    public static IReadOnlyList<(string Dir, string Name)> Places(Platform platform, Func<string, string?>? getenv = null,
                                                                  IEnumerable<string>? drives = null)
    {
        getenv ??= Environment.GetEnvironmentVariable;
        var home = LauncherHome.Home(getenv);
        var places = new List<(string, string)>
        {
            (System.IO.Path.Combine(home, "Downloads"), "Downloads"),
            (System.IO.Path.Combine(home, "Desktop"), "Desktop"),
            (System.IO.Path.Combine(home, "Documents"), "Documents"),
            (home, "Home"),
        };
        foreach (var d in drives ?? Drives(platform))
            places.Add((d, platform == Platform.Windows ? d.TrimEnd('\\', '/') : d));
        return places;
    }

    static IEnumerable<string> Drives(Platform platform)
    {
        var found = new List<string>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    var root = d.RootDirectory.FullName;
                    if (platform == Platform.Windows)
                    {
                        if (d.DriveType is DriveType.Fixed or DriveType.Removable) found.Add(root);
                    }
                    else if (root.StartsWith("/mnt/", StringComparison.Ordinal) || root.StartsWith("/media/", StringComparison.Ordinal)
                             || root.StartsWith("/run/media/", StringComparison.Ordinal))
                        found.Add(root);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return found;
    }

    /// A folder Finder does not look in: hidden ones, $RECYCLE.BIN and the like, Windows, Program Files
    static bool Skipped(string name) =>
        name.StartsWith('.') || name.StartsWith('$') || string.Equals(name, "Windows", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Program", StringComparison.OrdinalIgnoreCase);

    /// The game files in places, each found once, newest first
    public static IReadOnlyList<FoundGameFile> Find(IEnumerable<(string Dir, string Name)> places)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);
        var found = new List<FoundGameFile>();
        foreach (var (dir, name) in places)
        {
            string full;
            try { full = System.IO.Path.GetFullPath(dir); }
            catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException) { continue; }
            if (!Directory.Exists(full) || !seen.Add(full)) continue;
            var dirs = new List<string> { full };
            try
            {
                dirs.AddRange(Directory.EnumerateDirectories(full).Where(s => !Skipped(System.IO.Path.GetFileName(s))).Take(200));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            foreach (var sub in dirs)
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(sub, "*.nup"))
                    {
                        var info = new FileInfo(f);
                        if (seen.Add(info.FullName)) found.Add(new FoundGameFile(info.FullName, name, info.Length, info.LastWriteTime, false));
                    }
                    if (sub != full && IsDumpFolder(sub) && seen.Add(sub))
                        found.Add(new FoundGameFile(sub, name, 0, Directory.GetLastWriteTime(sub), true));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        return found.OrderByDescending(d => d.Modified).ToList();
    }

    /// A folder with the game's romfs and exefs folders in it (exefs holding main)
    public static bool IsDumpFolder(string dir) =>
        Directory.Exists(System.IO.Path.Combine(dir, "romfs")) && File.Exists(System.IO.Path.Combine(dir, "exefs", "main"));

    /// What a path given to the window (Browse, a drop) is: a .nup, a folder holding romfs and exefs, or the romfs or exefs folder
    /// itself (its pair beside it); null for anything else
    public static FoundGameFile? FromPath(string path, string where)
    {
        string full;
        try { full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path)); }
        catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException) { return null; }
        if (File.Exists(full))
        {
            if (!full.EndsWith(".nup", StringComparison.OrdinalIgnoreCase)) return null;
            var info = new FileInfo(full);
            return new FoundGameFile(info.FullName, where, info.Length, info.LastWriteTime, false);
        }
        if (!Directory.Exists(full)) return null;
        if (IsDumpFolder(full)) return new FoundGameFile(full, where, 0, Directory.GetLastWriteTime(full), true);
        var name = System.IO.Path.GetFileName(full);
        var parent = System.IO.Path.GetDirectoryName(full);
        if (parent != null && (string.Equals(name, "romfs", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "exefs", StringComparison.OrdinalIgnoreCase))
            && IsDumpFolder(parent))
            return new FoundGameFile(parent, where, 0, Directory.GetLastWriteTime(parent), true);
        return null;
    }

    /// The romfs and exefs folders picked one by one (the "romfs and exefs folders" link); null when they are not the game's
    public static FoundGameFile? FromFolders(string romfs, string exefs, string where)
    {
        string r, e;
        try
        {
            r = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(romfs));
            e = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(exefs));
        }
        catch (Exception x) when (x is ArgumentException or IOException or NotSupportedException) { return null; }
        if (!Directory.Exists(r) || !File.Exists(System.IO.Path.Combine(e, "main"))) return null;
        var parent = System.IO.Path.GetDirectoryName(r) ?? r;
        return new FoundGameFile(parent, where, 0, Directory.GetLastWriteTime(r), true, r, e);
    }
}
