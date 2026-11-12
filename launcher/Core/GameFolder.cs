namespace HookshotLauncher.Core;

/// What a game folder says about itself: the stamp make_game.py writes (hookshot_version.txt: version=, builder=, builder_local=),
/// the game's language (language.txt) and whether it is built (bin/hookshot[.exe])
public static class GameFolder
{
    public const string StampName = "hookshot_version.txt";
    public const string LanguageName = "language.txt";

    /// The stamp's key=value lines, in order (the first of a key wins, as linux_update.game_version reads version=); null: none
    public static IReadOnlyDictionary<string, string>? ReadStamp(string folder)
    {
        string[] lines;
        try { lines = File.ReadAllLines(Path.Combine(folder, StampName)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
        var stamp = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var eq = line.IndexOf('=');
            if (eq > 0) stamp.TryAdd(line[..eq], line[(eq + 1)..].Trim());
        }
        return stamp;
    }

    /// The release the folder was built with (version=), or null
    public static string? Version(string folder) =>
        ReadStamp(folder) is { } s && s.TryGetValue("version", out var v) && v.Length > 0 ? v : null;

    /// A portable install's folder (builder_local=1): the hand-over leaves its stamp alone
    public static bool Portable(string folder) => ReadStamp(folder) is { } s && s.TryGetValue("builder_local", out var v) && v == "1";

    public static string ExePath(string folder, Platform platform) => Path.Combine(folder, "bin", Platforms.GameExe(platform));

    public static bool IsBuilt(string folder, Platform platform) => File.Exists(ExePath(folder, platform));

    /// The game's language: the first line of language.txt that is not blank or a # comment, as written (make_game.folder_language
    /// also wants it to be one of its codes: Languages.Normalize); null when it has none
    public static string? Language(string folder)
    {
        try
        {
            return File.ReadLines(Path.Combine(folder, LanguageName)).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    /// Built with NVIDIA DLSS: its runtime in ngx/ (hookshot_builder.folder_has_dlss: libnvidia-ngx-dlss.so*; on Windows nvngx_dlss.dll)
    public static bool HasDlss(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(Path.Combine(folder, "ngx")).Select(Path.GetFileName)
                .Any(n => n != null && (n.StartsWith("libnvidia-ngx-dlss.so", StringComparison.Ordinal)
                                        || n.Equals("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    /// A folder make_game.py made: its stamp, or the game in bin/. Only such a folder may be deleted from the launcher
    public static bool IsGameFolder(string folder, Platform platform) =>
        Directory.Exists(folder) && (File.Exists(Path.Combine(folder, StampName)) || IsBuilt(folder, platform));
}
