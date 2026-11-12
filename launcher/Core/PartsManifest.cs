using System.Globalization;
using System.Text.RegularExpressions;

namespace HookshotLauncher.Core;

/// One part of a version's tools: a zip checked by its SHA-256 and size, unpacked under Dest of the version's folder.
/// From/To (the Windows list): the zip's top folder From is renamed To (llvm-mingw-<version>-ucrt-x86_64 to llvm-mingw).
/// Files (the Linux list): the names the zip may hold, null for a whole tree.
public sealed record Part(string Sha, long Size, string Dest, string? From, string? To, string Url, IReadOnlyList<string>? Files)
{
    /// The same part for reuse: the same bytes, unpacked to the same place in the same way
    public bool Same(Part other) =>
        other.Sha == Sha && other.Dest == Dest && other.From == From && other.To == To
        && (Files == null ? other.Files == null : other.Files != null && Files.SequenceEqual(other.Files));
}

public enum ListKind { Setup, Linux }

/// A version's parts list. Run: the Windows list's builder, relative to the install
public sealed record PartsList(ListKind Kind, string Tag, IReadOnlyList<Part> Parts, string? Run);

/// The launcher release's hookshot-launcher.txt line for one platform (docs/launcher-upgrade.md, section 1)
public sealed record LauncherAsset(string Platform, string Sha, long Size, string Url);

public sealed record LauncherList(string Tag, int Format, IReadOnlyList<LauncherAsset> Assets);

/// The parts lists, each with its reader's rules:
///   hookshot-setup.txt (scripts/make_setup_parts.py, read by builder/setup/setup.c)
///       tag <tag>
///       part <sha256> <bytes> <folder under the install> <top folder in the zip to rename, or -> <new name, or -> <url>
///       run <the builder, relative to the install>
///   hookshot-linux.txt (builder/linux/linux_update.py parse_list)
///       tag <tag>
///       part <sha256> <bytes> <dir under the install> <url>
///       tree | file <name>...
/// Names stay inside the version's folder, checksums are 64 lowercase hex, links https.
public static class PartsManifest
{
    static readonly Regex Sha256 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    public static PartsList Parse(Platform platform, string text) =>
        platform == Platform.Windows ? ParseSetup(text) : ParseLinux(text);

    static IEnumerable<string> Lines(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'));

    static long Size(string s) =>
        long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n
        : throw new FormatException("a part line's size is not a number: " + s);

    /// setup.c's list_name_ok: letters, digits and . _ + - (and with path, / or \ between the parts), no "." or ".." part, no root
    public static bool NameOk(string s, bool path)
    {
        if (s.Length == 0 || s[0] is '/' or '\\') return false;
        foreach (var c in s)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '+' or '-' || (path && c is '/' or '\\'))) return false;
        foreach (var piece in path ? s.Split('/', '\\') : new[] { s })
            if (piece is "" or "." or "..") return false;
        return true;
    }

    /// linux_update._safe_name: a relative zip or list name with no ".." part
    public static bool SafeName(string s) =>
        s.Length > 0 && !s.StartsWith('/') && !s.Split('/').Contains("..");

    static void CheckPart(string sha, string url)
    {
        if (!Sha256.IsMatch(sha)) throw new FormatException("a part line has a bad checksum");
        if (!url.StartsWith("https://", StringComparison.Ordinal)) throw new FormatException("a part line's link is not https");
    }

    public static PartsList ParseSetup(string text)
    {
        string? tag = null, run = null;
        var parts = new List<Part>();
        foreach (var line in Lines(text))
        {
            var w = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (w[0] == "tag" && w.Length == 2) tag = w[1];
            else if (w[0] == "run" && w.Length == 2) run = w[1];
            else if (w[0] == "part")
            {
                // (setup.c skips a part line it cannot read: here it is an error, as a tool missing from a version is)
                if (w.Length != 7) throw new FormatException("a part line needs sha, size, folder, from, to and url");
                CheckPart(w[1], w[6]);
                if ((w[3] != "." && !NameOk(w[3], true)) || (w[4] != "-" && !NameOk(w[4], false)) || (w[5] != "-" && !NameOk(w[5], false))
                    || ((w[4] == "-") != (w[5] == "-")))
                    throw new FormatException("a part line has a name the setup does not accept");
                parts.Add(new Part(w[1], Size(w[2]), w[3], w[4] == "-" ? null : w[4], w[5] == "-" ? null : w[5], w[6], null));
            }
            // (other lines: setup.c reads past them, so a later list may add some)
        }
        if (tag == null || run == null || parts.Count == 0) throw new FormatException("the setup list has no release");
        if (!NameOk(tag, false) || !Versions.IsTagName(tag)) throw new FormatException("the setup list's tag is not a version name");
        if (!NameOk(run, true)) throw new FormatException("the setup list's run line is not a name under the install");
        return new PartsList(ListKind.Setup, tag, parts, run);
    }

    /// local: a list from this PC, whose parts may be file:// links (linux_update.parse_list's local)
    public static PartsList ParseLinux(string text, bool local = false)
    {
        string tag = "";
        var parts = new List<(string Sha, long Size, string Dest, string Url, List<string>? Files)>();
        var current = -1;
        foreach (var line in Lines(text))
        {
            if (line.StartsWith("tag ", StringComparison.Ordinal)) tag = line[4..].Trim();
            else if (line.StartsWith("part ", StringComparison.Ordinal))
            {
                var bits = line.Split(' ');
                if (bits.Length != 5) throw new FormatException("a part line needs sha, size, directory and url");
                parts.Add((bits[1], Size(bits[2]), bits[3], bits[4], new List<string>()));
                current = parts.Count - 1;
            }
            else if (line == "tree")
            {
                if (current < 0) throw new FormatException("tree with no part");
                parts[current] = parts[current] with { Files = null };
            }
            else if (line.StartsWith("file ", StringComparison.Ordinal))
            {
                if (current < 0 || parts[current].Files == null) throw new FormatException("file with no part");
                parts[current].Files!.Add(line[5..].Trim());
            }
            else throw new FormatException("unknown list line");
        }
        if (tag.Length == 0 || parts.Count == 0) throw new FormatException("the setup list has no release");
        if (!Versions.IsTagName(tag)) throw new FormatException("the setup list's tag is not a version name");
        foreach (var p in parts)
        {
            if (!Sha256.IsMatch(p.Sha) || !(p.Url.StartsWith("https://", StringComparison.Ordinal)
                                            || (local && p.Url.StartsWith("file://", StringComparison.Ordinal))))
                throw new FormatException("a part line has a bad checksum or link");
            if ((p.Dest != "." && !SafeName(p.Dest)) || (p.Files != null && !p.Files.All(SafeName)))
                throw new FormatException("the zip has a path outside its folder");
        }
        return new PartsList(ListKind.Linux, tag, parts.Select(p => new Part(p.Sha, p.Size, p.Dest, null, null, p.Url, p.Files)).ToList(), null);
    }

    /// hookshot-launcher.txt (docs/launcher-upgrade.md, section 1): every launcher line, unknown platforms included (the reader
    /// picks its own); a format above 1 is read, and the caller refuses it
    public static LauncherList ParseLauncher(string text)
    {
        string? tag = null;
        int? format = null;
        var assets = new List<LauncherAsset>();
        foreach (var line in Lines(text))
        {
            var w = line.Split(' ');
            if (w[0] == "tag" && w.Length == 2) tag = w[1];
            else if (w[0] == "format" && w.Length == 2 && int.TryParse(w[1], NumberStyles.None, CultureInfo.InvariantCulture, out var f)) format = f;
            else if (w[0] == "launcher" && w.Length == 5)
            {
                CheckPart(w[2], w[4]);
                assets.Add(new LauncherAsset(w[1], w[2], Size(w[3]), w[4]));
            }
            else throw new FormatException("unknown launcher list line");
        }
        if (tag == null || format == null) throw new FormatException("the launcher list has no tag or format");
        if (!Versions.IsTagName(tag)) throw new FormatException("the launcher list's tag is not a version name");
        return new LauncherList(tag, format.Value, assets);
    }
}
