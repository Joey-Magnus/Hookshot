using System.Text.Json;

namespace HookshotLauncher.Core;

/// The newest version of each channel on GitHub, quietly: null when GitHub did not answer. The same questions and fallbacks as
/// the 2.x Builders (Updater.LatestTag, linux_update.latest_tag):
///   release   the latest release (the API, a few tries; else the tag line of the latest release's parts list, a file download
///             that is not the API's 60-an-hour limit)
///   rc        the newest of the release candidates and releases in the releases list (an RC is offered its version's release
///             once that ships); with no API answer, the newer of the rolling rc release's list tag and the latest release
///   nightly   the tag line of the rolling nightly release's parts list
public sealed class Releases
{
    public const string Repo = "https://github.com/NotRexed/Hookshot";
    public const string LatestApi = "https://api.github.com/repos/NotRexed/Hookshot/releases/latest";
    public const string ReleasesApi = "https://api.github.com/repos/NotRexed/Hookshot/releases?per_page=20";
    const string Json = "application/vnd.github+json";

    readonly IFetcher fetcher;
    readonly string listName;
    readonly Func<TimeSpan, CancellationToken, Task> delay;

    /// delay: the wait between tries (the tests pass none)
    public Releases(IFetcher fetcher, Platform platform, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.fetcher = fetcher;
        listName = Platforms.PartsListName(platform);
        this.delay = delay ?? Task.Delay;
    }

    /// The parts list of a version, on the release that holds it (a nightly's: the rolling nightly); null: the latest release's
    public string ListUrl(string? tag) =>
        tag == null ? $"{Repo}/releases/latest/download/{listName}" : $"{Repo}/releases/download/{Versions.ReleaseOf(tag)}/{listName}";

    public async Task<string?> NewestAsync(string channel, CancellationToken cancel = default)
    {
        switch (channel)
        {
            case Versions.Nightly:
                return await ListTagAsync($"{Repo}/releases/download/nightly/{listName}", 2, cancel);
            case Versions.Candidate:
                try
                {
                    var text = await fetcher.GetTextAsync(ReleasesApi, Json, cancel);
                    using var doc = JsonDocument.Parse(text);
                    var tags = new List<string?>();
                    foreach (var r in doc.RootElement.EnumerateArray())
                        if (!(r.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                            && r.TryGetProperty("tag_name", out var name) && name.ValueKind == JsonValueKind.String)
                            tags.Add(name.GetString());
                    if (Versions.NewestRelease(tags) is { } newest) return newest;
                }
                catch (Exception e) when (e is FetchException or JsonException or InvalidOperationException) { }
                var rolling = await ListTagAsync($"{Repo}/releases/download/rc/{listName}", 1, cancel);
                var latest = await LatestAsync(cancel);
                if (!Versions.IsRelease(rolling)) return latest;
                return latest == null || Versions.Newer(rolling, latest) ? rolling : latest;
            default:
                return await LatestAsync(cancel);
        }
    }

    /// The latest release's tag: the API up to three times (not again after a 4xx: the API's limit), then the latest release's list
    public async Task<string?> LatestAsync(CancellationToken cancel = default)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var doc = JsonDocument.Parse(await fetcher.GetTextAsync(LatestApi, Json, cancel));
                if (doc.RootElement.TryGetProperty("tag_name", out var tag) && tag.GetString() is { Length: > 0 } t) return t;
                break;
            }
            catch (FetchException e) when (!e.Transient) { break; }
            catch (Exception e) when (e is FetchException or JsonException or InvalidOperationException) { }
            if (attempt < 2) await delay(TimeSpan.FromSeconds(2 << attempt), cancel);
        }
        return await ListTagAsync(ListUrl(null), 2, cancel);
    }

    /// The "tag" line of a parts list (either grammar), tries times
    async Task<string?> ListTagAsync(string url, int tries, CancellationToken cancel)
    {
        for (var attempt = 0; attempt < tries; attempt++)
        {
            try
            {
                var text = await fetcher.GetTextAsync(url, null, cancel);
                foreach (var line in text.Split('\n'))
                    if (line.StartsWith("tag ", StringComparison.Ordinal))
                    {
                        var tag = line[4..].Trim();
                        return Versions.IsTagName(tag) ? tag : null;
                    }
                return null;
            }
            catch (FetchException e) when (!e.Transient) { return null; }
            catch (FetchException) { }
            if (attempt + 1 < tries) await delay(TimeSpan.FromSeconds(2), cancel);
        }
        return null;
    }
}
