using System.Diagnostics;
using HookshotLauncher.Core;

namespace HookshotLauncher;

/// The launcher's state for the window and the command line: its home, its list of builds, its settings, and what GitHub said
/// is newest on each channel
public sealed class LauncherService
{
    public Platform Platform { get; }
    public LauncherHome Home { get; }
    public BuildRegistry Registry { get; private set; }
    public LauncherSettings Settings { get; private set; }
    public ImportReport? Import { get; private set; }
    public IReadOnlyDictionary<string, string?> Newest => newest;

    readonly Dictionary<string, string?> newest = new();

    LauncherService(Platform platform, LauncherHome home)
    {
        Platform = platform;
        Home = home;
        Registry = BuildRegistry.Load(home.Builds);
        Settings = LauncherSettings.Load(home.Settings);
    }

    /// The home opened, and the 2.x Builder's hand-over read before anything is shown (docs/launcher-upgrade.md, section 3)
    public static LauncherService Open()
    {
        var platform = Platforms.Current;
        var home = LauncherHome.Resolve(platform);
        Directory.CreateDirectory(home.Root);
        var service = new LauncherService(platform, home);
        try { service.Import = LegacyImport.Run(home, service.Registry, platform); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            service.Import = new ImportReport(Array.Empty<string>(), new[] { e.Message }, false, false);
        }
        service.Settings = LauncherSettings.Load(home.Settings);
        return service;
    }

    /// This launcher's path, for the game folders' stamp (builder=): the AppImage on Linux, else this program
    public static string LauncherPath =>
        Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage ? appImage : Environment.ProcessPath ?? "HookshotLauncher";

    public void SaveSettings() => Settings.Save(Home.Settings);

    /// A folder the command line named (--launch, --update), listed if it is not yet: the 2.x Builder passes its folder on
    public BuildEntry Ensure(string folder)
    {
        var full = Path.GetFullPath(folder);
        if (Registry.FindByFolder(full) is { } known) return known;
        var version = GameFolder.Version(full);
        var entry = Registry.Add(full, version, Versions.TrackOf(version));
        Registry.Save();
        return entry;
    }

    public bool IsBuilt(BuildEntry b) => GameFolder.IsBuilt(b.Folder, Platform);

    /// The newest version of each channel a build follows, and the one new builds start on. Null: GitHub answered nothing
    /// (the window says so quietly); the old answers are kept
    public async Task<bool> CheckReleasesAsync(CancellationToken cancel = default)
    {
        var channels = Registry.Builds.Select(b => UpdatePolicy.ChannelOf(b.Channel, b.Version)).Append(Settings.DefaultChannel).Distinct().ToList();
        using var fetcher = new HttpFetcher(TimeSpan.FromSeconds(8));
        var releases = new Releases(fetcher, Platform);
        var any = false;
        foreach (var channel in channels)
        {
            var tag = await releases.NewestAsync(channel, cancel);
            if (tag != null)
            {
                newest[channel] = tag;
                any = true;
            }
        }
        return any;
    }

    public string? UpdateFor(BuildEntry b) => UpdatePolicy.UpdateTo(b, newest);

    /// What a channel's newest is, as the update check found it (also the screenshots' and a later test's stand-in for GitHub)
    public void SetNewest(string channel, string? tag) => newest[channel] = tag;

    /// The build's game, from its folder (no window of the launcher needed)
    public void Play(BuildEntry b)
    {
        using var _ = Process.Start(GamePlay.StartInfo(b.Folder, Platform));
    }

    public void SetDefault(BuildEntry b)
    {
        Registry.SetDefault(b.Id);
        Registry.Save();
    }

    /// The build out of the list; with deleteFiles, its game folder from the disk too (only a game folder: BuildRegistry.DeleteFolder)
    public void Remove(BuildEntry b, bool deleteFiles)
    {
        if (deleteFiles) BuildRegistry.DeleteFolder(b.Folder, Platform);
        Registry.Remove(b.Id);
        Registry.Save();
        // (the tools of versions no build uses any more go too)
        try { new Toolchains(Home.Toolchains, new NoFetcher()).Prune(Registry.Builds.Select(x => x.Version).OfType<string>()); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    sealed class NoFetcher : IFetcher
    {
        public Task<string> GetTextAsync(string url, string? accept, CancellationToken cancel) => throw new FetchException("offline", null);
        public Task DownloadAsync(string url, Stream to, Action<long>? progress, CancellationToken cancel) => throw new FetchException("offline", null);
    }
}
