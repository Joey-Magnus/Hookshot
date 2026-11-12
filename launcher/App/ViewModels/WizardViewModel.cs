using System.Collections.ObjectModel;
using System.Diagnostics;
using HookshotLauncher.Core;

namespace HookshotLauncher.ViewModels;

public enum JobKind { Build, Rebuild, Update }

/// One run of the wizard: what is built (a new version, one you have again, an update of a build), from which game files, into
/// which folder; the version's tools, downloaded in step 1; and the pages of its steps, kept for Back
public sealed class BuildJob
{
    public BuildJob(MainViewModel main, JobKind kind, string channel, string version, BuildEntry? replaces)
    {
        Main = main;
        Kind = kind;
        Channel = channel;
        Version = version;
        Replaces = replaces;
        var s = main.Service;
        InstallDir = kind == JobKind.Rebuild && replaces != null ? replaces.Folder
            : LauncherSettings.FolderFor(s.BuildsFolder, version, p => s.Registry.FindByFolder(p) is { } e && e.Id != replaces?.Id);
        // (the saves come from the build an update replaces, else the default)
        var others = s.Registry.Builds.Where(b => kind != JobKind.Rebuild || b.Id != replaces?.Id).ToList();
        MoveFrom = others.FirstOrDefault(b => b.Id == replaces?.Id) ?? others.FirstOrDefault(b => b.Default) ?? others.FirstOrDefault();
        MoveOn = s.Settings.MoveSaves && MoveFrom != null;
    }

    public MainViewModel Main { get; }
    public LauncherService Service => Main.Service;
    public JobKind Kind { get; }
    public string Channel { get; }
    public string Version { get; }
    /// The build an update or a rebuild takes the place of
    public BuildEntry? Replaces { get; }
    public string? From => Kind == JobKind.Update ? Replaces?.Version : null;
    /// The version and the one it replaces as players see them (a nightly: "Nightly 2029 · c544828f", Versions.Shown)
    public string Shown => Versions.Shown(Version);
    public string? ShownFrom => From == null ? null : Versions.Shown(From);
    public string InstallDir { get; set; }
    public FoundGameFile? Picked { get; set; }
    public Toolchain? Tools { get; set; }
    /// The tools were downloaded in step 1 (not already on this PC), and how long that took
    public bool ToolsDownloaded { get; set; }
    public TimeSpan ToolsTime { get; set; }
    /// Step 2's saves: from which build (Windows, where each build has its own), or none
    public BuildEntry? MoveFrom { get; set; }
    public bool MoveOn { get; set; }
    public bool AdvancedOpen { get; set; }
    public readonly CancellationTokenSource Cancel = new();

    public FilesStepViewModel? Files { get; set; }
    public SettingsStepViewModel? Settings { get; set; }

    public string Verb => Kind switch { JobKind.Update => "Update to ", JobKind.Rebuild => "Rebuild ", _ => "Build " };

    /// Everything of step 1 stops (the download, the checks): the wizard was left
    public void Stop()
    {
        try { Cancel.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}

/// One game file found on this PC (or browsed, or dropped), and what make_game.py --check said of it
public sealed class GameFileViewModel : ViewModelBase
{
    readonly FilesStepViewModel step;
    GameCheck? check;

    public GameFileViewModel(FilesStepViewModel step, FoundGameFile file)
    {
        this.step = step;
        File = file;
    }

    public FoundGameFile File { get; }
    public string Name => File.Folders ? File.Name + " (romfs and exefs)" : File.Name;
    public string Where => File.Where == "Dropped on the window" ? File.Where : ShownPath.Of(Path.GetDirectoryName(File.Path) ?? File.Path);
    public string? WhereFull => File.Where == "Dropped on the window" ? null : File.Path;

    public GameCheck? Check
    {
        get => check;
        set
        {
            if (!Set(ref check, value)) return;
            RaiseAll(nameof(Checking), nameof(Ok), nameof(Old), nameof(Bad), nameof(ChipText), nameof(Dim), nameof(CanPick), nameof(Tip));
        }
    }

    public bool Checking => check == null;
    public bool Ok => check?.Status == GameCheckStatus.Supported;
    public bool Old => check?.Status == GameCheckStatus.OtherVersion;
    public bool Bad => check?.Status == GameCheckStatus.Unreadable;
    public string ChipText => check?.Status switch
    {
        GameCheckStatus.Supported => "Supported version",
        GameCheckStatus.OtherVersion => "Not the supported version",
        GameCheckStatus.Unreadable => "Can't be read",
        _ => "Checking…",
    };
    public string? Tip => check?.Text;
    public bool Dim => check != null && !Ok;
    public bool CanPick => !Dim;

    public bool IsPicked
    {
        get => step.Picked == this;
        set
        {
            if (value && CanPick) step.Picked = this;
            else Raise();
        }
    }

    public void PickedChanged() => Raise(nameof(IsPicked));
}

/// Step 1, the game files: the version's tools download (Toolchains) while the game files found on this PC are listed, then each
/// is checked with make_game.py --check from those tools. Next once both are done and a supported file is picked
public sealed class FilesStepViewModel : ViewModelBase, IWizardPage
{
    GameFileViewModel? picked;
    double toolsFraction;
    long toolsTotal;
    bool toolsDone, toolsFailed, searching = true;
    string? toolsProblem;
    readonly Stopwatch toolsClock = new();
    bool checking;

    /// tools: start the tools' download (the screenshots show it at a set point instead)
    public FilesStepViewModel(BuildJob job, bool tools = true)
    {
        Job = job;
        job.Files = this;
        BackCommand = new Command(Back);
        NextCommand = new Command(Next, () => CanNext);
        BrowseCommand = new Command(() => _ = BrowseAsync());
        FoldersCommand = new Command(() => _ = FoldersAsync());
        RetryCommand = new Command(StartTools);
        if (tools) StartTools();
        StartSearch();
    }

    public BuildJob Job { get; }
    MainViewModel Main => Job.Main;
    LauncherService Service => Job.Service;
    public int Step => 1;
    public bool Finished => false;

    public ObservableCollection<GameFileViewModel> Files { get; } = new();

    public string Sub => Job.Kind == JobKind.Update
        ? $"Updating {Job.ShownFrom} to {Job.Shown}. The file you built it from is picked; pick another if it moved."
        : "Pick your dump of the game. Hookshot reads it on this PC; nothing is uploaded.";

    // ---- the tools ----

    public string ToolsTitle => "Builder tools for " + Job.Shown;
    public double ToolsFraction { get => toolsFraction; private set => Set(ref toolsFraction, value); }
    public bool ToolsDone => toolsDone;
    public bool ToolsFailed => toolsFailed;
    public bool ToolsBusy => !toolsDone && !toolsFailed;

    public string ToolsNote
    {
        get
        {
            if (toolsFailed) return "The download stopped: " + toolsProblem;
            if (toolsDone)
                return Job.ToolsDownloaded ? $"Downloaded, {Durations.Size(toolsTotal)}. Kept for this version's updates."
                    : "Already on this PC. Kept for this version's updates.";
            if (toolsTotal == 0) return "Looking for this version's tools…";
            var have = (long)(toolsFraction * toolsTotal);
            var text = $"{have >> 20} of {Durations.Size(toolsTotal)}";
            var secs = toolsClock.Elapsed.TotalSeconds;
            if (toolsFraction > 0.02 && secs > 2)
            {
                var left = secs / toolsFraction * (1 - toolsFraction);
                text += left < 90 ? $", about {Math.Max(1, (int)Math.Round(left))} s left" : $", about {(int)Math.Round(left / 60)} min left";
            }
            return text;
        }
    }

    void StartTools()
    {
        toolsFailed = false;
        toolsProblem = null;
        RaiseTools();
        toolsClock.Restart();
        var token = Job.Cancel.Token;
        Task.Run(async () =>
        {
            try
            {
                var t = await ToolsSource.EnsureAsync(Service.Tools, Service.Fetcher, Service.Platform, Job.Version, Service.LocalList,
                                                      p => Ui.Post(() => Progress(p)), token);
                Ui.Post(() => ToolsReady(t));
            }
            catch (OperationCanceledException) { }
            catch (Exception e) when (e is FetchException or IOException or InvalidDataException or FormatException or UnauthorizedAccessException
                                          or ArgumentException)
            {
                Ui.Post(() =>
                {
                    toolsFailed = true;
                    toolsProblem = e.Message;
                    RaiseTools();
                });
            }
        }, token);
    }

    void Progress(ToolsDownloadProgress p)
    {
        if (p.Total > 0)
        {
            toolsTotal = p.Total;
            Job.ToolsDownloaded = true;
        }
        ToolsFraction = p.Fraction;
        Raise(nameof(ToolsNote));
    }

    void ToolsReady(Toolchain t)
    {
        Job.Tools = t;
        Job.ToolsTime = toolsClock.Elapsed;
        toolsDone = true;
        ToolsFraction = 1;
        RaiseTools();
        _ = CheckAllAsync();
    }

    void RaiseTools()
    {
        RaiseAll(nameof(ToolsDone), nameof(ToolsFailed), nameof(ToolsBusy), nameof(ToolsNote));
        RaiseNext();
    }

    /// For the screenshots: the tools as if this far into their download
    public void ShowToolsAt(double fraction, long total)
    {
        toolsTotal = total;
        Job.ToolsDownloaded = true;
        toolsDone = fraction >= 1;
        ToolsFraction = fraction;
        RaiseTools();
    }

    // ---- the game files ----

    public bool Searching => searching && Files.Count == 0;
    public bool NothingFound => !searching && Files.Count == 0;

    void StartSearch()
    {
        var remembered = Remembered();
        var token = Job.Cancel.Token;
        Task.Run(() =>
        {
            var found = GameFiles.Find(GameFiles.Places(Service.Platform));
            Ui.Post(() =>
            {
                if (token.IsCancellationRequested) return;
                searching = false;
                if (remembered != null) Add(remembered, pick: true, first: true);
                foreach (var f in found) Add(f, pick: false, first: false);
                RaiseAll(nameof(Searching), nameof(NothingFound));
                if (Job.Tools != null) _ = CheckAllAsync();
            });
        }, token);
    }

    /// The game files this build (an update) or the last build was made from
    FoundGameFile? Remembered()
    {
        var e = Job.Replaces;
        var s = Service.Settings;
        var nup = e?.Nup ?? (e?.Romfs == null ? s.Nup : null);
        if (nup != null && File.Exists(nup)) return GameFiles.FromPath(nup, "Last used");
        var (romfs, exefs) = e?.Romfs != null ? (e.Romfs, e.Exefs) : (s.Romfs, s.Exefs);
        return romfs != null && exefs != null ? GameFiles.FromFolders(romfs, exefs, "Last used") : null;
    }

    GameFileViewModel Add(FoundGameFile f, bool pick, bool first)
    {
        var have = Files.FirstOrDefault(x => BuildRegistry.SameFolder(x.File.Path, f.Path) && x.File.Exefs == f.Exefs);
        if (have == null)
        {
            have = new GameFileViewModel(this, f);
            if (first) Files.Insert(0, have);
            else Files.Add(have);
        }
        if (pick) Picked = have;
        RaiseAll(nameof(Searching), nameof(NothingFound));
        return have;
    }

    /// Each file checked in turn (the check unpacks into one probe folder); the first supported one is picked when none is
    async Task CheckAllAsync()
    {
        if (checking || Job.Tools is not { } tools) return;
        checking = true;
        var token = Job.Cancel.Token;
        try
        {
            while (!token.IsCancellationRequested && Files.FirstOrDefault(f => f.Check == null) is { } next)
            {
                var command = MakeGameCommand.ForCheck(tools, next.File.Source, Service.Platform, Service.Home, LauncherService.LauncherPath, Service.Work);
                var result = await Task.Run(() => MakeGameRun.CheckAsync(command, token), token);
                next.Check = result;
                if (picked == next && !next.Ok) Picked = null;
                if (picked == null && next.Ok) Picked = next;
                RaiseNext();
            }
        }
        catch (OperationCanceledException) { }
        finally { checking = false; }
    }

    public GameFileViewModel? Picked
    {
        get => picked;
        set
        {
            var was = picked;
            if (!Set(ref picked, value)) return;
            was?.PickedChanged();
            value?.PickedChanged();
            Job.Picked = value?.File;
            RaiseNext();
        }
    }

    // ---- Browse, the folders, a drop ----

    async Task BrowseAsync()
    {
        if (Main.Host is not { } host) return;
        var path = await host.PickFileAsync("Your game file (.nup)", "*.nup");
        if (path == null) return;
        if (GameFiles.FromPath(path, "Browsed") is { } f) Picked = Add(f, pick: true, first: true);
        else Main.ShowToast("That is not a .nup file.");
        _ = CheckAllAsync();
    }

    async Task FoldersAsync()
    {
        if (Main.Host is not { } host) return;
        var first = await host.PickFolderAsync("The game's romfs folder, or the folder holding romfs and exefs", null);
        if (first == null) return;
        var f = GameFiles.FromPath(first, "Picked");
        if (f == null)
        {
            var exefs = await host.PickFolderAsync("The game's exefs folder", Path.GetDirectoryName(first));
            if (exefs == null) return;
            f = GameFiles.FromFolders(first, exefs, "Picked");
        }
        if (f != null) Add(f, pick: true, first: true);
        else Main.ShowToast("Those are not the game's romfs and exefs folders.");
        _ = CheckAllAsync();
    }

    public void Dropped(IReadOnlyList<string> paths)
    {
        var any = false;
        foreach (var p in paths)
            if (GameFiles.FromPath(p, "Dropped on the window") is { } f)
            {
                Add(f, pick: true, first: true);
                any = true;
            }
        if (!any) Main.ShowToast("Drop a .nup file, or the folder holding romfs and exefs.");
        _ = CheckAllAsync();
    }

    // ---- Back and Next ----

    public bool CanNext => toolsDone && picked?.Ok == true;

    public string Hint => CanNext ? "Ready for the settings."
        : !toolsDone && picked?.Ok != true ? "Next waits for the tools download and a supported game file."
        : !toolsDone ? "Next waits for the tools download."
        : "Pick a supported game file to go on.";

    void RaiseNext()
    {
        RaiseAll(nameof(CanNext), nameof(Hint));
        NextCommand.Changed();
    }

    public Command BackCommand { get; }
    public Command NextCommand { get; }
    public Command BrowseCommand { get; }
    public Command FoldersCommand { get; }
    public Command RetryCommand { get; }

    void Back()
    {
        Job.Stop();
        Main.ShowHome();
    }

    void Next()
    {
        if (!CanNext) return;
        Main.Navigate(Job.Settings ?? new SettingsStepViewModel(Job));
    }
}
