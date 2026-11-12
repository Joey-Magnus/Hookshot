namespace HookshotLauncher.Core;

/// Whether a build has an update: the newest version of the build's channel, when it is newer than the build's version
/// (Versions.Newer: releases and release candidates in semver order, nightlies by their count). Never a downgrade: a build
/// newer than its channel's newest (a release candidate after its channel moved on, a test build) is left alone, and nothing
/// is offered across a nightly and a release, which are not ordered (docs/versioning.md, "Update tracks"): a release build
/// put on the nightly channel, or a nightly on a release's, is offered nothing until it is built again from that channel. A
/// version that names neither a release nor a nightly (a hash, "dev") is on no track, and is offered its channel's newest.
public static class UpdatePolicy
{
    /// The channel a build follows: the one chosen for it, else the track of its version
    public static string ChannelOf(string? channel, string? version) => Versions.IsChannel(channel) ? channel! : Versions.TrackOf(version);

    /// The version to update to, or null: none (no version known on either side, the same one, or an older one)
    public static string? UpdateTo(string? version, string channel, string? newest)
    {
        if (newest == null || version == null || newest == version) return null;
        if (!Versions.IsRelease(version) && Versions.TrackOf(version) != Versions.Nightly) return newest;
        return Versions.Newer(newest, version) ? newest : null;
    }

    public static string? UpdateTo(BuildEntry build, IReadOnlyDictionary<string, string?> newestByChannel) =>
        newestByChannel.TryGetValue(ChannelOf(build.Channel, build.Version), out var newest)
            ? UpdateTo(build.Version, ChannelOf(build.Channel, build.Version), newest)
            : null;
}
