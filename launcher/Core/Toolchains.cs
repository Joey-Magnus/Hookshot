using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HookshotLauncher.Core;

/// How far an install is: Fraction 0..1 of the bytes of all its parts (a part kept from another version counts as done)
public sealed record ToolchainProgress(double Fraction, int Part, int Parts, string Text);

/// One installed version's build tools: nx2/make_game.py under ToolsRoot, and the Python that runs it
public sealed record Toolchain(string Tag, string Folder, ListKind Kind, string? Run)
{
    /// Windows: the setup's HookshotBuilder/tools beside its builder; Linux: the AppImage's usr/share/hookshot
    public string ToolsRoot => Kind == ListKind.Setup
        ? Path.Combine(Folder, Path.GetDirectoryName(Run ?? "HookshotBuilder/x")!.Replace('/', Path.DirectorySeparatorChar), "tools")
        : Path.Combine(Folder, "usr", "share", "hookshot");

    /// Linux: the AppImage's usr/lib/hookshot (emsdk, the sysroot, FFmpeg, SDL2 and the prebuilt Vulkan back end; builder/linux/AppRun)
    public string LibRoot => Path.Combine(Folder, "usr", "lib", "hookshot");

    public string Python => Kind == ListKind.Setup
        ? Path.Combine(ToolsRoot, "python", "python.exe")
        : Path.Combine(Folder, "usr", "python", "bin", "python3");

    public string MakeGame => Path.Combine(ToolsRoot, "nx2", "make_game.py");
}

/// The build tools of each version the launcher builds with, in &lt;home&gt;/toolchains/&lt;tag&gt;/, from the version's parts list.
/// Each part is checked for its size and SHA-256 before it is unpacked; a part another installed version already has (the same
/// SHA-256, unpacked the same way) is linked or copied from it instead of downloaded. A version is unpacked into &lt;tag&gt;.part and
/// renamed once complete, so a failed or cancelled install leaves nothing that looks installed.
public sealed class Toolchains
{
    public const string RecordName = "toolchain.json";
    public const string SysrootPart = "usr/lib/hookshot/sysroot";   // (builder/linux/part_links.py: its rooted links are rebased)
    const string Downloads = ".downloads";

    readonly string root;
    readonly IFetcher fetcher;

    public Toolchains(string root, IFetcher fetcher)
    {
        this.root = root;
        this.fetcher = fetcher;
    }

    public string Root => root;

    public string PathOf(string tag) =>
        Versions.IsTagName(tag) ? Path.Combine(root, tag) : throw new ArgumentException("Not a version name: " + tag, nameof(tag));

    public Toolchain? Find(string tag)
    {
        if (!Versions.IsTagName(tag)) return null;
        var record = Read(Path.Combine(root, tag));
        return record == null ? null : new Toolchain(tag, Path.Combine(root, tag), record.Kind, record.Run);
    }

    public bool IsInstalled(string tag) => Find(tag) != null;

    public IReadOnlyList<string> Installed()
    {
        if (!Directory.Exists(root)) return Array.Empty<string>();
        return Directory.EnumerateDirectories(root).Select(Path.GetFileName).OfType<string>()
            .Where(n => Versions.IsTagName(n) && !n.EndsWith(".part", StringComparison.Ordinal) && Read(Path.Combine(root, n)) != null)
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    /// Every version folder (and leftover staging or download) that keep does not name; the names removed
    public IReadOnlyList<string> Prune(IEnumerable<string> keep)
    {
        var kept = new HashSet<string>(keep, StringComparer.Ordinal);
        var removed = new List<string>();
        if (!Directory.Exists(root)) return removed;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(dir);
            var stale = name == Downloads || name.EndsWith(".part", StringComparison.Ordinal);
            if (!stale && (!Versions.IsTagName(name) || kept.Contains(name))) continue;
            DeleteTree(dir);
            removed.Add(name);
        }
        return removed;
    }

    public async Task<Toolchain> InstallAsync(PartsList list, Action<ToolchainProgress>? progress = null, CancellationToken cancel = default)
    {
        var dest = PathOf(list.Tag);
        var staging = dest + ".part";
        Directory.CreateDirectory(root);
        if (new DirectoryInfo(staging).LinkTarget != null) throw new IOException("The tools' staging folder is a symbolic link: " + staging);
        if (Directory.Exists(staging)) DeleteTree(staging);
        Directory.CreateDirectory(staging);
        var downloads = Path.Combine(root, Downloads);
        var total = Math.Max(1, list.Parts.Sum(p => p.Size));
        long done = 0;
        var sources = Others(list.Tag);
        var record = new Record { Tag = list.Tag, Kind = list.Kind, Run = list.Run };
        try
        {
            for (var i = 0; i < list.Parts.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var part = list.Parts[i];
                var entries = new List<Entry>();
                if (Reuse(part, sources, staging, entries))
                    progress?.Invoke(new ToolchainProgress((done += part.Size) / (double)total, i + 1, list.Parts.Count,
                                                           $"Part {i + 1} of {list.Parts.Count} unchanged: kept from an installed version"));
                else
                {
                    Directory.CreateDirectory(downloads);
                    var zip = Path.Combine(downloads, part.Sha + ".zip.part");
                    var before = done;
                    try
                    {
                        progress?.Invoke(new ToolchainProgress(done / (double)total, i + 1, list.Parts.Count,
                                                               $"Downloading part {i + 1} of {list.Parts.Count} ({part.Size >> 20} MB)"));
                        await Download(part, zip, have => progress?.Invoke(new ToolchainProgress(
                            (before + Math.Min(have, part.Size)) / (double)total, i + 1, list.Parts.Count,
                            $"Downloading part {i + 1} of {list.Parts.Count} ({have >> 20} of {part.Size >> 20} MB)")), cancel);
                        cancel.ThrowIfCancellationRequested();
                        Unpack(zip, part, staging, entries);
                    }
                    finally
                    {
                        File.Delete(zip);
                    }
                    done += part.Size;
                    progress?.Invoke(new ToolchainProgress(done / (double)total, i + 1, list.Parts.Count, $"Part {i + 1} of {list.Parts.Count} ready"));
                }
                record.Parts.Add(new RecordPart
                {
                    Sha = part.Sha, Size = part.Size, Dest = part.Dest, From = part.From, To = part.To, Url = part.Url,
                    Files = part.Files?.ToList(), Entries = entries,
                });
            }
            CheckLinksInside(staging);
            using (var f = new FileStream(Path.Combine(staging, RecordName), FileMode.CreateNew, FileAccess.Write))
                JsonSerializer.Serialize(f, record, CoreJson.Default.Record);
            cancel.ThrowIfCancellationRequested();   // (the last moment: from here the new folder takes the old one's place)
        }
        catch
        {
            DeleteTree(staging);
            throw;
        }
        if (Directory.Exists(dest)) DeleteTree(dest);
        Directory.Move(staging, dest);
        TryDeleteEmpty(downloads);
        return new Toolchain(list.Tag, dest, list.Kind, list.Run);
    }

    // ---- reuse ----

    List<(string Folder, Record Record)> Others(string tag)
    {
        var found = new List<(string, Record)>();
        foreach (var name in Installed())
            if (name != tag && Read(Path.Combine(root, name)) is { } r) found.Add((Path.Combine(root, name), r));
        return found;
    }

    static bool Reuse(Part part, List<(string Folder, Record Record)> sources, string staging, List<Entry> entries)
    {
        foreach (var (folder, record) in sources)
        {
            var old = record.Parts.FirstOrDefault(p => p.AsPart().Same(part));
            if (old == null || old.Entries.Count == 0) continue;
            if (!old.Entries.All(e => Exists(Path.Combine(folder, Native(e.Path)), e.Link != null))) continue;
            var made = new List<string>();
            try
            {
                foreach (var e in old.Entries)
                {
                    var target = Path.Combine(staging, Native(e.Path));
                    EnsureParent(staging, target);
                    if (e.Link != null) Symlink(staging, e.Link, target);
                    else FileLinks.LinkOrCopy(Path.Combine(folder, Native(e.Path)), target);
                    made.Add(target);
                    entries.Add(e);
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                foreach (var m in made) File.Delete(m);
                entries.Clear();
            }
        }
        return false;
    }

    static bool Exists(string path, bool link)
    {
        var info = new FileInfo(path);
        return link ? info.LinkTarget != null : info.Exists && info.LinkTarget == null;
    }

    // ---- download and unpack ----

    async Task Download(Part part, string zip, Action<long> progress, CancellationToken cancel)
    {
        await using (var file = new FileStream(zip, FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var checking = new CheckedStream(file, part.Size))
        {
            await fetcher.DownloadAsync(part.Url, checking, progress, cancel);
            if (checking.Length != part.Size)
                throw new InvalidDataException($"A part of the tools did not download completely ({checking.Length} of {part.Size} bytes).");
            if (!CryptographicOperations.FixedTimeEquals(checking.Hash(), Convert.FromHexString(part.Sha)))
                throw new InvalidDataException("A part of the tools did not download correctly (its checksum differs).");
        }
    }

    static string Native(string rel) => rel.Replace('/', Path.DirectorySeparatorChar);

    /// A zip entry's name: relative, '/' between its parts, none of them "..", no drive or backslash
    static string ZipName(string name)
    {
        if (!PartsManifest.SafeName(name) || name.Contains('\\') || name.Contains(':') || name.Split('/').Any(p => p == "."))
            throw new InvalidDataException("A part of the tools has a path outside its folder: " + name);
        return name;
    }

    static void Unpack(string zipPath, Part part, string staging, List<Entry> entries)
    {
        var rooted = new List<(string Target, string Rel, string LinkTo)>();
        var dest = part.Dest == "." ? staging : Path.Combine(staging, Native(part.Dest));
        using (var zip = ZipFile.OpenRead(zipPath))
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName;
                if (name.EndsWith('/')) continue;
                ZipName(name);
                if (part.Files != null && !part.Files.Contains(name))
                    throw new InvalidDataException($"A part of the tools has {name}, which its list does not name.");
                if (part.From != null)
                {
                    if (!name.StartsWith(part.From + "/", StringComparison.Ordinal))
                        throw new InvalidDataException($"A part of the tools has {name} outside its folder {part.From}.");
                    name = part.To + name[part.From.Length..];
                }
                var rel = part.Dest == "." ? name : part.Dest + "/" + name;
                var target = Path.Combine(staging, Native(rel));
                EnsureParent(staging, target);
                var mode = (entry.ExternalAttributes >> 16) & 0xFFFF;
                if ((mode & 0xF000) == 0xA000)
                {
                    string linkTo;
                    using (var reader = new StreamReader(entry.Open())) linkTo = reader.ReadToEnd();
                    if (linkTo.StartsWith('/'))
                    {
                        // (released sysroots hold /lib links rooted inside the sysroot; other parts must not have absolute links)
                        if (part.Dest != SysrootPart) throw new InvalidDataException($"A part of the tools has an absolute link: {name} -> {linkTo}");
                        rooted.Add((target, rel, linkTo));
                        continue;
                    }
                    Symlink(staging, linkTo, target);
                    entries.Add(new Entry { Path = rel, Link = linkTo });
                    continue;
                }
                using (var from = entry.Open())
                using (var to = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
                    from.CopyTo(to);
                if (!OperatingSystem.IsWindows() && (mode & 0x1FF) != 0)   // (the modes the parts are made with: none group- or world-writable)
                    File.SetUnixFileMode(target, (mode & 0x49) != 0
                        ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead
                          | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute
                        : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                entries.Add(new Entry { Path = rel });
            }
        // (a sysroot link may name a file a later link makes: until none is left, or none can be made)
        while (rooted.Count > 0)
        {
            var pending = new List<(string Target, string Rel, string LinkTo)>();
            foreach (var r in rooted)
            {
                if (r.LinkTo.StartsWith("//", StringComparison.Ordinal)) throw new InvalidDataException("Not a rooted sysroot link: " + r.LinkTo);
                var destination = Path.GetFullPath(Path.Combine(dest, Native(r.LinkTo.TrimStart('/'))));
                if (!Inside(dest, destination)) throw new InvalidDataException($"A sysroot link leaves its root: {r.Rel} -> {r.LinkTo}");
                if (!File.Exists(destination) && !Directory.Exists(destination)) { pending.Add(r); continue; }
                var relative = Path.GetRelativePath(Path.GetDirectoryName(r.Target)!, destination).Replace(Path.DirectorySeparatorChar, '/');
                Symlink(staging, relative, r.Target);
                entries.Add(new Entry { Path = r.Rel, Link = relative });
            }
            if (pending.Count == rooted.Count)
                throw new InvalidDataException("Sysroot links have missing or circular targets: " + string.Join(", ", pending.Select(p => p.Rel)));
            rooted = pending;
        }
    }

    static bool Inside(string root, string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.Equals(full, comparison) || path.StartsWith(full + Path.DirectorySeparatorChar, comparison);
    }

    /// The folders up to target's, made under root; never through a link a part made
    static void EnsureParent(string root, string target)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(target))!;
        if (!Inside(root, parent)) throw new InvalidDataException("A part of the tools would write outside its folder: " + target);
        var rel = Path.GetRelativePath(Path.GetFullPath(root), parent);
        var at = Path.GetFullPath(root);
        if (rel == ".") return;
        foreach (var piece in rel.Split(Path.DirectorySeparatorChar))
        {
            at = Path.Combine(at, piece);
            var info = new DirectoryInfo(at);
            if (info.LinkTarget != null || new FileInfo(at).LinkTarget != null)
                throw new InvalidDataException("A part of the tools would write through a symbolic link: " + at);
            if (File.Exists(at)) throw new InvalidDataException("A part of the tools has a file where a folder goes: " + at);
            if (!info.Exists) info.Create();
        }
    }

    /// linux_update._symlink: a relative link whose ".." come first, landing inside the staging folder
    static void Symlink(string root, string linkTo, string target)
    {
        var parts = linkTo.Split('/');
        var ups = 0;
        while (ups < parts.Length && parts[ups] == "..") ups++;
        if (parts.Skip(ups).Contains("..") || linkTo.StartsWith('/') || linkTo.Contains('\\'))
            throw new InvalidDataException($"A link in the tools climbs after a name: {target} -> {linkTo}");
        var destination = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(target)!, Native(linkTo)));
        if (!Inside(root, destination)) throw new InvalidDataException($"A link in the tools leaves its folder: {target} -> {linkTo}");
        File.CreateSymbolicLink(target, linkTo);
    }

    /// Every link in the finished folder resolves inside it, whatever order the parts made them in
    static void CheckLinksInside(string staging)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false };
        foreach (var path in Directory.EnumerateFileSystemEntries(staging, "*", options))
        {
            var info = new FileInfo(path);
            if (info.LinkTarget == null) continue;
            var final = info.ResolveLinkTarget(returnFinalTarget: true);
            if (final == null || !Inside(staging, Path.GetFullPath(final.FullName)))
                throw new InvalidDataException("A link in the tools leaves its folder: " + path);
        }
    }

    // ---- the record each version keeps of its parts: what reuse reads ----

    static Record? Read(string folder)
    {
        try
        {
            using var f = File.OpenRead(Path.Combine(folder, RecordName));
            return JsonSerializer.Deserialize(f, CoreJson.Default.Record);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    /// A folder and everything in it; a link in it is removed, never followed
    internal static void DeleteTree(string dir)
    {
        var info = new DirectoryInfo(dir);
        if (info.LinkTarget != null) { info.Delete(); return; }
        if (!info.Exists) return;
        foreach (var child in info.EnumerateFileSystemInfos())
        {
            if (child is DirectoryInfo d && d.LinkTarget == null) DeleteTree(d.FullName);
            else
            {
                if (!OperatingSystem.IsWindows() || child.LinkTarget != null) { child.Delete(); continue; }
                child.Attributes = FileAttributes.Normal;
                child.Delete();
            }
        }
        info.Delete();
    }

    static void TryDeleteEmpty(string dir)
    {
        try { if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
        catch (IOException) { }
    }

    internal sealed class Record
    {
        public int Format { get; set; } = 1;
        public string Tag { get; set; } = "";
        [JsonConverter(typeof(JsonStringEnumConverter<ListKind>))]
        public ListKind Kind { get; set; }
        public string? Run { get; set; }
        public List<RecordPart> Parts { get; set; } = new();
    }

    internal sealed class RecordPart
    {
        public string Sha { get; set; } = "";
        public long Size { get; set; }
        public string Dest { get; set; } = ".";
        public string? From { get; set; }
        public string? To { get; set; }
        public string Url { get; set; } = "";
        public List<string>? Files { get; set; }
        public List<Entry> Entries { get; set; } = new();

        public Part AsPart() => new(Sha, Size, Dest, From, To, Url, Files);
    }

    /// One file a part put in the version's folder, relative with '/'; Link: a symbolic link's target
    internal sealed class Entry
    {
        public string Path { get; set; } = "";
        public string? Link { get; set; }
    }

    /// The bytes written through it, counted and hashed, and stopped past the size the list says
    sealed class CheckedStream : Stream
    {
        readonly Stream inner;
        readonly long limit;
        readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long length;

        public CheckedStream(Stream inner, long limit)
        {
            this.inner = inner;
            this.limit = limit;
        }

        public byte[] Hash() => hash.GetHashAndReset();

        void Count(ReadOnlySpan<byte> data)
        {
            length += data.Length;
            if (length > limit) throw new InvalidDataException("A part of the tools is larger than its list says.");
            hash.AppendData(data);
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Count(buffer);
            inner.Write(buffer);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default)
        {
            Count(buffer.Span);
            await inner.WriteAsync(buffer, cancel);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancel) =>
            WriteAsync(buffer.AsMemory(offset, count), cancel).AsTask();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => length;
        public override long Position { get => length; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancel) => inner.FlushAsync(cancel);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) hash.Dispose();
            base.Dispose(disposing);
        }
    }
}
