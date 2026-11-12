using System.Text.Json;

namespace HookshotLauncher.Core;

/// What the build wizard remembers (&lt;home&gt;/settings.json): the game files last picked, the build's options and the channel new
/// builds start on. A license flag is true only once its license was accepted on its tick.
public sealed class LauncherSettings
{
    public int Format { get; set; } = 1;
    public string? Nup { get; set; }
    public string? Romfs { get; set; }
    public string? Exefs { get; set; }
    public string? Language { get; set; }
    public bool Dlss { get; set; }
    public bool DlssLicense { get; set; }
    public bool Fsr { get; set; }
    public bool FsrLicense { get; set; }
    public bool PrepareShaders { get; set; } = true;
    /// Build memory in GB (HOOKSHOT_MEM_LIMIT_GB); null: Automatic
    public int? MemoryGb { get; set; }
    /// Build workers (HOOKSHOT_JOBS / --jobs); null: Automatic
    public int? BuildWorkers { get; set; }
    /// make_game.py's --work; null: the launcher's own (LauncherHome.Work)
    public string? Work { get; set; }
    public string DefaultChannel { get; set; } = Versions.Release;
    /// A new build, or an update, takes its saves from the build chosen as the source when other builds exist (Windows: each
    /// game folder keeps its own saves; Linux builds share theirs). Saves.MoveOnBuild
    public bool MoveSaves { get; set; } = true;
    /// Where new builds go, each version in its own folder; null: DefaultBuildsFolder
    public string? BuildsFolder { get; set; }
    /// Ask GitHub for each channel's newest version when the launcher starts
    public bool CheckUpdates { get; set; } = true;

    /// The folder builds go in when none was chosen: make_game.py's own default game folder's parent, ~/Games/Hookshot
    /// (%USERPROFILE%\Games\Hookshot on Windows)
    public static string DefaultBuildsFolder(Func<string, string?>? getenv = null) =>
        Path.Combine(LauncherHome.Home(getenv ?? Environment.GetEnvironmentVariable), "Games", "Hookshot");

    public string BuildsFolderOrDefault(Func<string, string?>? getenv = null) =>
        string.IsNullOrWhiteSpace(BuildsFolder) ? DefaultBuildsFolder(getenv) : BuildsFolder;

    /// A new build's folder: the version's name in the builds folder, with " (2)" and so on when that is taken by something that
    /// is not the build being replaced
    public static string FolderFor(string buildsFolder, string version, Func<string, bool>? taken = null)
    {
        taken ??= p => Directory.Exists(p) || File.Exists(p);
        var name = Versions.IsTagName(version) ? version : "Hookshot";
        var path = Path.Combine(buildsFolder, name);
        for (var n = 2; taken(path); n++) path = Path.Combine(buildsFolder, $"{name} ({n})");
        return path;
    }

    /// The saved settings, or the defaults when there are none or they cannot be read
    public static LauncherSettings Load(string path)
    {
        try
        {
            var s = JsonSerializer.Deserialize(File.ReadAllText(path), CoreJson.Default.LauncherSettings) ?? new LauncherSettings();
            return s.Checked();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new LauncherSettings();
        }
    }

    LauncherSettings Checked()
    {
        if (!Versions.IsChannel(DefaultChannel)) DefaultChannel = Versions.Release;
        if (MemoryGb is <= 0) MemoryGb = null;
        if (BuildWorkers is <= 0) BuildWorkers = null;
        if (!DlssLicense) Dlss = false;   // (a tick saved before its license was accepted is not one)
        if (!FsrLicense) Fsr = false;
        return this;
    }

    public void Save(string path) => AtomicFile.Write(path, JsonSerializer.Serialize(Checked(), CoreJson.Default.LauncherSettings));

    /// The 2.x Builder's remembered choices (an import file's settings, docs/launcher-upgrade.md section 3)
    public void Apply(ImportedSettings s)
    {
        Nup = s.Nup;
        Romfs = s.Romfs;
        Exefs = s.Exefs;
        Language = s.Language ?? Language;
        Dlss = s.Dlss;
        DlssLicense = s.DlssLicense;
        Fsr = s.Fsr;
        FsrLicense = s.FsrLicense;
        PrepareShaders = s.PrepareShaders;
        MemoryGb = s.MemoryGb;
        BuildWorkers = s.Jobs;
        Work = s.Work ?? Work;
        Checked();
    }
}
