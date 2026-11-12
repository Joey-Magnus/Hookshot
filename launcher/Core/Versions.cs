using System.Globalization;
using System.Text.RegularExpressions;

namespace HookshotLauncher.Core;

/// The update tracks (channels) and the version order: the rules of builder/HookshotBuilder/Updater.cs (TrackOf, Newer, Target,
/// ReleaseOf), builder/linux/linux_update.py and host/hos_update.c. docs/versioning.md; tests/test_update_tracks.py holds the
/// vectors all of them are checked against.
public static class Versions
{
    public const string Release = "release", Candidate = "rc", Nightly = "nightly";
    public static readonly IReadOnlyList<string> Channels = new[] { Release, Candidate, Nightly };

    static readonly Regex NightlyName = new(@"^nightly-([0-9]{1,9})-([0-9a-f]{8})(-dirty)?$", RegexOptions.CultureInvariant);
    static readonly Regex OldNightlyName = new(@"-[0-9]+-g[0-9a-f]+(-dirty)?$", RegexOptions.CultureInvariant);
    static readonly Regex ReleaseName = new(@"^v(\d+)\.(\d+)\.(\d+)(?:-rc\.(\d+))?$", RegexOptions.CultureInvariant);
    static readonly Regex TagName = new(@"^[0-9A-Za-z._+-]{1,128}$", RegexOptions.CultureInvariant);

    public static bool IsChannel(string? channel) => channel is Release or Candidate or Nightly;

    /// A build's track from its version: nightly-2029-c544828f (nightly-&lt;commit count&gt;-&lt;commit&gt;) is the nightly, as is
    /// git describe --long's v2.1.0-rc.1-3-g1a2b3c4 that named it before; v2.1.0-rc.1 a release candidate; anything else (and
    /// no version) a release
    public static string TrackOf(string? version) =>
        version == null ? Release
        : NightlyKey(version) != null ? Nightly
        : ReleaseName.IsMatch(version) && version.Contains('-') ? Candidate
        : Release;

    /// The GitHub release that holds a version's files: the nightly's are on the rolling nightly release
    public static string ReleaseOf(string tag) => TrackOf(tag) == Nightly ? Nightly : tag;

    /// A release tag, vX.Y.Z or vX.Y.Z-rc.N (not a nightly's name, not the rolling releases)
    public static bool IsRelease(string? tag) => tag != null && ReleaseName.IsMatch(tag);

    /// A name a version folder may have (the setup lists' rule): [0-9A-Za-z._+-]{1,128}, not "." or ".."
    public static bool IsTagName(string? tag) => tag != null && TagName.IsMatch(tag) && tag != "." && tag != "..";

    /// A version as players see it: nightly-2029-c544828f as "Nightly 2029 · c544828f" (-dirty kept), any other as it is.
    /// Files and logs keep the version itself
    public static string Shown(string? version) =>
        version != null && NightlyName.Match(version) is { Success: true } m
            ? $"Nightly {m.Groups[1].Value} \u00b7 {m.Groups[2].Value}{m.Groups[3].Value}"
            : version ?? "";

    /// A nightly's place in the nightly order (null: no nightly): nightly-&lt;count&gt;-&lt;commit&gt; by its count, -dirty after the
    /// build of its own commit; the old git describe names before every one of those, and not ordered among themselves
    static long[]? NightlyKey(string? v)
    {
        if (v == null) return null;
        var m = NightlyName.Match(v);
        if (m.Success)
            return new[] { 1, long.Parse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture), m.Groups[3].Success ? 1 : 0 };
        return OldNightlyName.IsMatch(v) ? new long[] { 0, 0, 0 } : null;
    }

    static long[]? ReleaseKey(string? v)
    {
        if (v == null) return null;
        var m = ReleaseName.Match(v);
        if (!m.Success) return null;
        var key = new long[4];
        for (var i = 0; i < 3; i++)
            if (!long.TryParse(m.Groups[i + 1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out key[i])) return null;
        key[3] = long.MaxValue;
        if (m.Groups[4].Success && !long.TryParse(m.Groups[4].Value, NumberStyles.None, CultureInfo.InvariantCulture, out key[3])) return null;
        return key;
    }

    /// a newer than b: both vX.Y.Z[-rc.N] in semver order (v2.1.0-rc.1 &lt; v2.1.0-rc.2 &lt; v2.1.0), or both nightlies in the
    /// nightly order (NightlyKey). A nightly and a release are not ordered (a nightly's name has no version in it): false both
    /// ways, as when either is not a version
    public static bool Newer(string? a, string? b)
    {
        var na = NightlyKey(a);
        var nb = NightlyKey(b);
        var x = na ?? (nb == null ? ReleaseKey(a) : null);
        var y = nb ?? (na == null ? ReleaseKey(b) : null);
        if (x == null || y == null) return false;
        for (var i = 0; i < x.Length; i++)
            if (x[i] != y[i]) return x[i] > y[i];
        return false;
    }

    /// The order Newer defines, for sorting: versions before names that are none, which keep their order among themselves
    public static int Compare(string? a, string? b) => Newer(a, b) ? 1 : Newer(b, a) ? -1 : 0;

    /// What to update to: the track's newest (latest) for a release, or after a switch of track (even an older version); for
    /// a release candidate and a nightly only when it is newer, so a release candidate is never taken back to an older release
    /// and a nightly only goes forward (Updater.cs Target)
    public static string? Target(string? latest, string? tools, string track)
    {
        if (latest == null) return tools;
        if (tools == null || track == Release || TrackOf(tools) != track) return latest;
        return Newer(latest, tools) ? latest : tools;
    }

    /// The newest of a list of release tags (releases and release candidates) in semver order; null when none is one
    public static string? NewestRelease(IEnumerable<string?> tags)
    {
        string? newest = null;
        foreach (var t in tags)
            if (IsRelease(t) && (newest == null || Newer(t, newest))) newest = t;
        return newest;
    }
}
