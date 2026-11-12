using System.Text.Json;

namespace HookshotLauncher.Core;

/// One build in the launcher's list: a game folder, the version it was built with, the channel its updates follow
public sealed class BuildEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public string? Version { get; set; }
    public string Channel { get; set; } = Versions.Release;
    public bool Default { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Updated { get; set; }
    /// What the launcher built it with (null for a build it did not make: a 2.x folder handed over), so that its update can be
    /// built from the same game files and the home can say what it has
    public string? Nup { get; set; }
    public string? Romfs { get; set; }
    public string? Exefs { get; set; }
    public bool? Dlss { get; set; }
    public bool? Fsr { get; set; }
    /// Its folder's size in bytes when it was last measured; null: not yet
    public long? Bytes { get; set; }
}

public sealed class RegistryFile
{
    public int Format { get; set; } = 1;
    public List<BuildEntry> Builds { get; set; } = new();
}

/// &lt;home&gt;/builds.json, the launcher's list of builds (docs/launcher-upgrade.md: the launcher alone writes it). Written whole
/// (AtomicFile); exactly one build is the default whenever the list is not empty. One entry per folder.
public sealed class BuildRegistry
{
    readonly string path;
    readonly List<BuildEntry> builds;
    readonly Func<DateTimeOffset> now;

    BuildRegistry(string path, List<BuildEntry> builds, Func<DateTimeOffset>? now, bool existed, string? problem)
    {
        this.path = path;
        this.builds = builds;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        Existed = existed;
        Problem = problem;
        Normalize();
    }

    /// builds.json was there when it was loaded (no file: the launcher's first start, when the 2.x settings are read)
    public bool Existed { get; }

    /// Why the file could not be read (it was set aside as builds.json.broken); null when it was fine or absent
    public string? Problem { get; }

    public IReadOnlyList<BuildEntry> Builds => builds;

    public BuildEntry? Default => builds.FirstOrDefault(b => b.Default);

    public static BuildRegistry Load(string path, Func<DateTimeOffset>? now = null)
    {
        if (!File.Exists(path)) return new BuildRegistry(path, new List<BuildEntry>(), now, false, null);
        try
        {
            var file = JsonSerializer.Deserialize(File.ReadAllText(path), CoreJson.Default.RegistryFile)
                       ?? throw new JsonException("builds.json is empty");
            if (file.Format != 1) throw new JsonException($"builds.json has format {file.Format}, which this launcher does not know");
            var list = file.Builds.Where(b => b != null && b.Folder.Length > 0).ToList();
            foreach (var b in list)
            {
                if (b.Id.Length == 0) b.Id = NewId();
                if (!Versions.IsChannel(b.Channel)) b.Channel = Versions.TrackOf(b.Version);
            }
            return new BuildRegistry(path, list, now, true, null);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // (kept beside it rather than lost: a newer launcher's list, or a broken disk, is not overwritten by an empty one)
            try { File.Copy(path, path + ".broken", overwrite: true); } catch (Exception) { }
            return new BuildRegistry(path, new List<BuildEntry>(), now, true, e.Message);
        }
    }

    static string NewId() => Guid.NewGuid().ToString("N")[..12];

    public static bool SameFolder(string a, string b)
    {
        static string Norm(string p)
        {
            try { p = Path.GetFullPath(p); } catch (Exception) { }
            return Path.TrimEndingDirectorySeparator(p);
        }
        // (a Windows path read on Linux, as in a test of the Windows hand-over, compares as Windows compares it)
        var windows = OperatingSystem.IsWindows() || a.Contains('\\') || b.Contains('\\');
        return string.Equals(Norm(a), Norm(b), windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    public BuildEntry? Find(string id) => builds.FirstOrDefault(b => b.Id == id);

    public BuildEntry? FindByFolder(string folder) => builds.FirstOrDefault(b => SameFolder(b.Folder, folder));

    /// A folder added, or the one already listed brought up to date (its id, name and default kept). makeDefault: it becomes the
    /// default; the first build is the default anyway
    public BuildEntry Add(string folder, string? version, string? channel, string? name = null, bool makeDefault = false)
    {
        var at = now();
        var entry = FindByFolder(folder);
        if (entry == null)
        {
            entry = new BuildEntry
            {
                Id = NewId(),
                Folder = folder,
                Name = name ?? DefaultName(folder),
                Created = at,
            };
            builds.Add(entry);
        }
        else if (name != null) entry.Name = name;
        entry.Version = version ?? entry.Version;
        entry.Channel = Versions.IsChannel(channel) ? channel! : Versions.TrackOf(entry.Version);
        entry.Updated = at;
        if (makeDefault) SetDefault(entry.Id);
        Normalize();
        return entry;
    }

    static string DefaultName(string folder)
    {
        var trimmed = folder.TrimEnd('/', '\\');
        var cut = trimmed.LastIndexOfAny(new[] { '/', '\\' });
        var name = cut >= 0 ? trimmed[(cut + 1)..] : trimmed;
        return name.Length > 0 ? name : "Hookshot";
    }

    /// The build out of the list (its folder stays on the disk: DeleteFolder is the other, explicit, step). When it was the
    /// default, the first one left becomes it
    public bool Remove(string id)
    {
        var entry = Find(id);
        if (entry == null) return false;
        builds.Remove(entry);
        Normalize();
        return true;
    }

    /// An update built into folder takes the row of the build it updates (old): its id, its place in the list and whether it is
    /// the default stay; its folder, version and channel are the new build's. When another row already lists folder, that row
    /// goes (one entry per folder). The old folder stays on the disk
    public BuildEntry Replace(string oldId, string folder, string? version, string? channel)
    {
        var entry = Find(oldId) ?? throw new KeyNotFoundException("No build " + oldId);
        if (FindByFolder(folder) is { } other && other != entry)
        {
            builds.Remove(other);
            if (other.Default) entry.Default = true;
        }
        if (!SameFolder(entry.Folder, folder)) entry.Name = DefaultName(folder);
        entry.Folder = folder;
        entry.Version = version ?? entry.Version;
        entry.Channel = Versions.IsChannel(channel) ? channel! : Versions.TrackOf(entry.Version);
        entry.Updated = now();
        entry.Bytes = null;
        Normalize();
        return entry;
    }

    public void SetDefault(string id)
    {
        if (Find(id) == null) throw new KeyNotFoundException("No build " + id);
        foreach (var b in builds) b.Default = b.Id == id;
    }

    public void SetChannel(string id, string channel)
    {
        if (!Versions.IsChannel(channel)) throw new ArgumentException("Not a channel: " + channel, nameof(channel));
        (Find(id) ?? throw new KeyNotFoundException("No build " + id)).Channel = channel;
    }

    /// Exactly one default when the list is not empty: the first marked one, else the first
    void Normalize()
    {
        var chosen = builds.FirstOrDefault(b => b.Default) ?? builds.FirstOrDefault();
        foreach (var b in builds) b.Default = ReferenceEquals(b, chosen);
    }

    public void Save()
    {
        Normalize();
        AtomicFile.Write(path, JsonSerializer.Serialize(new RegistryFile { Builds = builds }, CoreJson.Default.RegistryFile));
    }

    /// A build's game folder deleted from the disk: only a folder make_game.py made (its stamp or bin/hookshot), never another
    /// folder a list entry might name. Not a link either (deleting it would free nothing), nor the home folder or one that holds
    /// it, nor one that holds a folder of keep: the other builds and the launcher's home. (A 2.x install in ~/Games/Hookshot holds
    /// the launcher's own builds, ~/Games/Hookshot/&lt;version&gt;, while that is the builds folder.) The entry itself is removed with
    /// Remove
    public static void DeleteFolder(string folder, Platform platform, IEnumerable<string>? keep = null, Func<string, string?>? getenv = null)
    {
        var full = Path.GetFullPath(folder);
        if (Path.GetPathRoot(full) == full) throw new IOException("Will not delete a drive's root: " + folder);
        if (!GameFolder.IsGameFolder(full, platform))
            throw new IOException("This folder is not a Hookshot game folder, so the launcher does not delete it: " + folder);
        if (new DirectoryInfo(full).LinkTarget != null)
            throw new IOException("This folder is a link to another one, so the launcher does not delete it: " + folder);
        if (Holds(full, LauncherHome.Home(getenv ?? Environment.GetEnvironmentVariable)))
            throw new IOException("This folder holds your home folder, so the launcher does not delete it: " + folder);
        if ((keep ?? Array.Empty<string>()).FirstOrDefault(k => Holds(full, k)) is { } other)
            throw new IOException($"This folder holds {other}, which the launcher keeps, so it does not delete it: {folder}");
        Toolchains.DeleteTree(full);
    }

    /// path is folder or somewhere under it
    static bool Holds(string folder, string path)
    {
        if (SameFolder(folder, path)) return true;
        var windows = OperatingSystem.IsWindows() || folder.Contains('\\') || path.Contains('\\');
        string Norm(string p)
        {
            try { p = Path.GetFullPath(p); } catch (Exception) { }
            return Path.TrimEndingDirectorySeparator(p);
        }
        var sep = windows ? "\\" : "/";
        return Norm(path).StartsWith(Norm(folder) + sep, windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
