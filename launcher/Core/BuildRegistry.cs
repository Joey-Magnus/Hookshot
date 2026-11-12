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
    /// folder a list entry might name. The entry itself is removed with Remove
    public static void DeleteFolder(string folder, Platform platform)
    {
        var full = Path.GetFullPath(folder);
        if (Path.GetPathRoot(full) == full) throw new IOException("Will not delete a drive's root: " + folder);
        if (!GameFolder.IsGameFolder(full, platform))
            throw new IOException("This folder is not a Hookshot game folder, so the launcher does not delete it: " + folder);
        Toolchains.DeleteTree(full);
    }
}
