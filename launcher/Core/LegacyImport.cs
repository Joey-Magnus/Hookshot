using System.Text.Json;
using System.Text.Json.Nodes;

namespace HookshotLauncher.Core;

/// The 2.x Builder's remembered choices, as an import file carries them (docs/launcher-upgrade.md, section 3)
public sealed record ImportedSettings(
    string? Nup, string? Romfs, string? Exefs, string? Language, bool Dlss, bool DlssLicense, bool Fsr, bool FsrLicense,
    bool PrepareShaders, int? MemoryGb, int? Jobs, string? Work);

/// One game folder handed over. Folder is null only for a 2.x settings file that saved no install folder
public sealed record ImportFile(int Format, string? Folder, string? Version, string Track, bool Default, ImportedSettings Settings);

public sealed record ImportReport(IReadOnlyList<string> Folders, IReadOnlyList<string> Problems, bool ReadLegacy, bool SettingsImported);

/// The hand-over from the 2.x Builder (docs/launcher-upgrade.md, section 3): the &lt;home&gt;/import/*.json files the upgrader drops
/// (read, applied and deleted at start, before anything is shown), and on the first start with no builds.json the 2.x settings
/// files themselves (builder-settings.json on Windows, builder.ini on Linux), read as if an import file had been dropped.
public static class LegacyImport
{
    public const int Format = 1;
    static readonly string[] SettingsKeys =
        { "nup", "romfs", "exefs", "language", "dlss", "dlssLicense", "fsr", "fsrLicense", "prepareShaders", "memoryGb", "jobs", "work" };
    static readonly string[] Keys = { "format", "folder", "version", "track", "default", "settings" };

    // ---- the import file ----

    /// An import file, strictly: format 1, every key present with its type (null where nothing is known)
    public static ImportFile Parse(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("An import file is not a JSON object.");
        foreach (var k in Keys)
            if (!root.ContainsKey(k)) throw new FormatException($"An import file has no \"{k}\".");
        var format = Int(root, "format") ?? throw new FormatException("An import file has no format.");
        if (format != Format) throw new FormatException($"An import file has format {format}; this launcher reads format {Format}.");
        var folder = Text(root, "folder");
        if (string.IsNullOrEmpty(folder)) throw new FormatException("An import file names no folder.");
        var track = Text(root, "track");
        if (!Versions.IsChannel(track)) throw new FormatException("An import file's track is not release, rc or nightly.");
        var s = root["settings"] as JsonObject ?? throw new FormatException("An import file's settings are not an object.");
        foreach (var k in SettingsKeys)
            if (!s.ContainsKey(k)) throw new FormatException($"An import file's settings have no \"{k}\".");
        var settings = new ImportedSettings(
            Text(s, "nup"), Text(s, "romfs"), Text(s, "exefs"), Text(s, "language"),
            Bool(s, "dlss"), Bool(s, "dlssLicense"), Bool(s, "fsr"), Bool(s, "fsrLicense"), Bool(s, "prepareShaders"),
            Int(s, "memoryGb"), Int(s, "jobs"), Text(s, "work"));
        return new ImportFile(format, folder, Text(root, "version"), track!, Bool(root, "default"), settings);
    }

    static string? Text(JsonObject o, string key) => o[key] switch
    {
        null => null,
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        _ => throw new FormatException($"\"{key}\" in an import file is not text."),
    };

    static bool Bool(JsonObject o, string key) => o[key] switch
    {
        JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False => v.GetValue<bool>(),
        _ => throw new FormatException($"\"{key}\" in an import file is not true or false."),
    };

    static int? Int(JsonObject o, string key) => o[key] switch
    {
        null => null,
        JsonValue v when v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out var n) => n,
        _ => throw new FormatException($"\"{key}\" in an import file is not a whole number."),
    };

    /// The import file's JSON, keys in the contract's order (what the upgrader writes)
    public static JsonObject ToJson(ImportFile f) => new()
    {
        ["format"] = f.Format,
        ["folder"] = f.Folder,
        ["version"] = f.Version,
        ["track"] = f.Track,
        ["default"] = f.Default,
        ["settings"] = new JsonObject
        {
            ["nup"] = f.Settings.Nup,
            ["romfs"] = f.Settings.Romfs,
            ["exefs"] = f.Settings.Exefs,
            ["language"] = f.Settings.Language,
            ["dlss"] = f.Settings.Dlss,
            ["dlssLicense"] = f.Settings.DlssLicense,
            ["fsr"] = f.Settings.Fsr,
            ["fsrLicense"] = f.Settings.FsrLicense,
            ["prepareShaders"] = f.Settings.PrepareShaders,
            ["memoryGb"] = f.Settings.MemoryGb,
            ["jobs"] = f.Settings.Jobs,
            ["work"] = f.Settings.Work,
        },
    };

    // ---- the 2.x settings files, read the way the upgrader reads them (section 3's table) ----

    static string? NonEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    /// %LOCALAPPDATA%\HookshotBuilder\builder-settings.json (and work.txt beside it). locate: where a folder the file names is
    /// read on this PC (the tests map the fixture's Windows paths to a folder of theirs)
    public static ImportFile? FromWindowsSettings(string settingsPath, Func<string, string>? locate = null)
    {
        locate ??= f => f;
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
            root = doc.RootElement.Clone();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return null; }
        if (root.ValueKind != JsonValueKind.Object) return null;
        string? Str(string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? NonEmpty(v.GetString()) : null;
        bool Flag(string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
        int? Number(string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

        var folder = Str("Out");
        var version = folder == null ? null : GameFolder.Version(locate(folder));
        var language = folder == null ? null : GameFolder.Language(locate(folder));
        string? work = null;
        try
        {
            var workFile = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!, "work.txt");
            if (File.Exists(workFile)) work = NonEmpty(File.ReadAllText(workFile).Trim());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var track = Str("Track") is { } t && Versions.IsChannel(t) ? t : Versions.TrackOf(version);
        var settings = new ImportedSettings(Str("Nup"), Str("Romfs"), Str("Exefs"), language, Flag("Dlss"), Flag("DlssLicense"),
                                            Flag("Fsr"), Flag("FsrLicense"), !Flag("PrepareLater"), Number("MemoryGb"), null, work);
        return new ImportFile(Format, folder, version, track, true, settings);
    }

    /// ~/.config/hookshot/builder.ini: key=value lines, the last of a key wins (hookshot_builder.load_config)
    public static ImportFile? FromLinuxIni(string iniPath, Func<string, string>? locate = null)
    {
        locate ??= f => f;
        var cfg = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var line in File.ReadAllLines(iniPath))
            {
                var eq = line.IndexOf('=');
                if (eq >= 0) cfg[line[..eq]] = line[(eq + 1)..];
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        string? Get(string key) => cfg.TryGetValue(key, out var v) ? NonEmpty(v) : null;

        var folder = Get("out");
        var version = folder == null ? null : GameFolder.Version(locate(folder));
        var folders = Get("kind") == "folders";
        var language = Get("language") ?? (folder == null ? null : GameFolder.Language(locate(folder)));
        var track = Get("track") is { } t && Versions.IsChannel(t) ? t : Versions.TrackOf(version);
        var settings = new ImportedSettings(folders ? null : Get("nup"), folders ? Get("romfs") : null, folders ? Get("exefs") : null,
                                            language, Get("dlss") == "1", Get("nvidia_license") == "1", false, false,
                                            Get("prepare") != "0", null, null, null);
        return new ImportFile(Format, folder, version, track, true, settings);
    }

    /// Where the 2.x Builder kept its settings on this PC
    public static string LegacySettingsPath(Platform platform, Func<string, string?>? getenv = null)
    {
        getenv ??= Environment.GetEnvironmentVariable;
        if (platform == Platform.Windows)
        {
            var local = getenv("LOCALAPPDATA") is { Length: > 0 } l ? l : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "HookshotBuilder", "builder-settings.json");
        }
        var config = getenv("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(LauncherHome.Home(getenv), ".config");
        return Path.Combine(config, "hookshot", "builder.ini");
    }

    // ---- at start ----

    /// The import files consumed (each deleted once what it brought is saved; one that cannot be read is renamed &lt;name&gt;.bad so
    /// it is not read at every start), then on a first start the 2.x settings files. Saves builds.json (always on a first start,
    /// so the 2.x files are read once) and settings.json when they changed. The settings are imported only while the launcher has
    /// none of its own.
    public static ImportReport Run(LauncherHome home, BuildRegistry registry, Platform platform, Func<string, string?>? getenv = null)
    {
        var folders = new List<string>();
        var problems = new List<string>();
        var haveSettings = File.Exists(home.Settings);
        LauncherSettings? settings = null;
        var changed = false;

        void Apply(ImportFile f)
        {
            if (f.Folder != null)
            {
                registry.Add(f.Folder, f.Version ?? GameFolder.Version(f.Folder), f.Track, makeDefault: f.Default && registry.Default == null);
                folders.Add(f.Folder);
                changed = true;
            }
            if (!haveSettings)
            {
                settings ??= new LauncherSettings();
                settings.Apply(f.Settings);
            }
        }

        // (deleted only once builds.json and settings.json are saved: a disk that refuses the save keeps the hand-over for the next start)
        var consumed = new List<string>();
        if (Directory.Exists(home.Import))
            foreach (var file in Directory.EnumerateFiles(home.Import)
                         .Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal))
            {
                try
                {
                    Apply(Parse(File.ReadAllText(file)));
                    consumed.Add(file);
                }
                catch (Exception e) when (e is FormatException or JsonException or IOException or UnauthorizedAccessException)
                {
                    problems.Add($"{Path.GetFileName(file)}: {e.Message}");
                    try { File.Move(file, file + ".bad", overwrite: true); } catch (Exception) { }
                }
            }

        var legacy = false;
        if (!registry.Existed)
        {
            var path = LegacySettingsPath(platform, getenv);
            var found = !File.Exists(path) ? null : platform == Platform.Windows ? FromWindowsSettings(path) : FromLinuxIni(path);
            if (found != null)
            {
                legacy = true;
                // (a saved install folder that was never made is not a build; the settings still are the player's)
                Apply(found.Folder != null && Directory.Exists(found.Folder) ? found : found with { Folder = null });
            }
        }
        if (changed || !registry.Existed) registry.Save();
        if (settings != null) settings.Save(home.Settings);
        foreach (var file in consumed)
        {
            try { File.Delete(file); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { problems.Add($"{Path.GetFileName(file)}: {e.Message}"); }
        }
        return new ImportReport(folders, problems, legacy, settings != null);
    }
}
