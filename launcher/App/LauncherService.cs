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
    /// A parts list on this PC that stands in for the releases (HOOKSHOT_LAUNCHER_PARTS: trying the launcher out); null normally
    public string? LocalList { get; }
    public IFetcher Fetcher { get; }
    public Toolchains Tools { get; }
    /// The save folders are worked out from this (the screenshots point it into a temporary folder)
    public SaveEnvironment SaveEnv { get; set; }
    /// The makers of this PC's graphics cards (Linux only for now: SystemInfo.GpuVendors)
    public IReadOnlyList<string> Gpus { get; set; }
    /// Whether the update check has answered (or was not asked: offline, or a local list)
    public bool Checked { get; set; }

    readonly Dictionary<string, string?> newest = new();

    LauncherService(Platform platform, LauncherHome home)
    {
        Platform = platform;
        Home = home;
        Registry = BuildRegistry.Load(home.Builds);
        Settings = LauncherSettings.Load(home.Settings);
        LocalList = ToolsSource.LocalList();
        Fetcher = new LocalFileFetcher(new HttpFetcher(TimeSpan.FromSeconds(60)));
        Tools = new Toolchains(home.Toolchains, Fetcher);
        SaveEnv = SaveEnvironment.Current() with { Platform = platform };
        Gpus = SystemInfo.GpuVendors(platform);
        if (LocalList != null)
        {
            try
            {
                var tag = ToolsSource.ReadLocal(LocalList, platform).Tag;
                foreach (var c in Versions.Channels) newest[c] = tag;
                Checked = true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException) { }
        }
    }

    /// The home opened, and the 2.x Builder's hand-over read before anything is shown (docs/launcher-upgrade.md, section 3)
    public static LauncherService Open() => Open(Platforms.Current, null);

    /// platform: the screenshots draw the Windows screens on Linux too; home: null for LauncherHome.Resolve
    public static LauncherService Open(Platform platform, LauncherHome? at)
    {
        var home = at ?? LauncherHome.Resolve(platform);
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

    public string BuildsFolder => Settings.BuildsFolderOrDefault();
    public string Work => string.IsNullOrWhiteSpace(Settings.Work) ? Home.Work : Settings.Work;

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

    /// The newest version of each channel a build follows, and the one new builds start on. False: GitHub answered nothing
    /// (the window says so quietly); the old answers are kept
    public async Task<bool> CheckReleasesAsync(CancellationToken cancel = default)
    {
        if (LocalList != null) return true;
        var channels = Versions.Channels.ToList();
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
        Checked = true;
        return any;
    }

    public string? UpdateFor(BuildEntry b) => UpdatePolicy.UpdateTo(b, newest);

    public string? NewestOf(string channel) => newest.TryGetValue(channel, out var v) ? v : null;

    /// What a channel's newest is, as the update check found it (also the screenshots' stand-in for GitHub)
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

    /// What deleting a build's folder must leave: the other builds' folders and the launcher's home (BuildRegistry.DeleteFolder)
    public IReadOnlyList<string> KeepOf(BuildEntry b) => Registry.Builds.Where(x => x.Id != b.Id).Select(x => x.Folder).Append(Home.Root).ToList();

    /// The build out of the list; with deleteFiles, its game folder from the disk too (only a game folder: BuildRegistry.DeleteFolder)
    public void Remove(BuildEntry b, bool deleteFiles)
    {
        if (deleteFiles) BuildRegistry.DeleteFolder(b.Folder, Platform, KeepOf(b));
        Registry.Remove(b.Id);
        Registry.Save();
        PruneTools();
    }

    /// The tools of versions no build uses any more go
    public void PruneTools()
    {
        try { Tools.Prune(Registry.Builds.Select(x => x.Version).OfType<string>()); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// Each build's folder measured in the background, once (its size is kept in builds.json); then done runs on the UI thread
    public void Measure(Action done)
    {
        var todo = Registry.Builds.Where(b => b.Bytes == null && Directory.Exists(b.Folder)).Select(b => (b, b.Folder)).ToList();
        if (todo.Count == 0) return;
        Task.Run(() =>
        {
            var sizes = todo.Select(t => (t.b, Bytes: Disk.FolderBytes(t.Folder))).ToList();
            ViewModels.Ui.Post(() =>
            {
                foreach (var (b, bytes) in sizes) b.Bytes = bytes;
                try { Registry.Save(); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                done();
            });
        });
    }

    /// The saves of a build: its own folder's on Windows, the shared ones on Linux
    public IReadOnlyList<SaveSlot> SavesOf(BuildEntry b)
    {
        var set = Saves.ForBuild(b.Folder, SaveEnv);
        return set.Found ? Saves.Describe(set.Profiles[0].Path) : Array.Empty<SaveSlot>();
    }
}
