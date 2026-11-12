using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using Avalonia.Threading;
using HookshotLauncher.Core;

namespace HookshotLauncher.ViewModels;

/// A stage's row in step 3: its state, what it is doing, and its time
public sealed class StageRowViewModel : ViewModelBase
{
    string detail = "", time = "", timeFaint = "", state = "wait";

    public StageRowViewModel(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }
    public string Name { get; }
    public string Detail { get => detail; set => Set(ref detail, value); }
    public string Time { get => time; set => Set(ref time, value); }
    public string TimeFaint { get => timeFaint; set => Set(ref timeFaint, value); }

    /// wait, run, done, skip or fail
    public string State
    {
        get => state;
        set
        {
            if (Set(ref state, value)) RaiseAll(nameof(IsWait), nameof(IsRun), nameof(IsDone), nameof(IsSkip), nameof(IsFail));
        }
    }

    public bool IsWait => state == "wait";
    public bool IsRun => state == "run";
    public bool IsDone => state == "done";
    public bool IsSkip => state == "skip";
    public bool IsFail => state == "fail";
}

/// Step 3: make_game.py run with the step's settings (MakeGameCommand), its @@ lines driving one overall bar and the stages'
/// rows (BuildProgress, BuildClock); the log under Show details; Cancel stops it and everything it started
public sealed class BuildStepViewModel : ViewModelBase, IWizardPage
{
    static readonly Dictionary<string, string> Names = new()
    {
        ["download"] = "Download tools", ["read"] = "Read the game files", ["lift"] = "Translate", ["compile"] = "Compile",
        ["finish"] = "Finish", [MakeGameProtocol.ShadersStage] = "Prepare shaders",
    };

    readonly BuildProgress progress;
    readonly BuildClock clock = new();
    readonly Stopwatch watch = new();
    readonly List<string> log = new();
    readonly CancellationTokenSource cancel = new();
    readonly DispatcherTimer? ticker;
    string? lastError;
    TimeSpan? fixedTime;
    bool showLog, running;

    public BuildStepViewModel(BuildJob job, bool start = true)
    {
        Job = job;
        var s = job.Service.Settings;
        progress = new BuildProgress(download: true, shaders: s.PrepareShaders);
        progress.Download(1);
        Rows = progress.Stages.Select(st => new StageRowViewModel(st.Id, Names.GetValueOrDefault(st.Id, st.Title))).ToList();
        Notches = progress.Stages.Skip(1).Select(st => st.Start).ToList();
        ToggleLogCommand = new Command(() => ShowLog = !ShowLog);
        CancelCommand = new Command(() => Job.Main.Modal = new StopBuildViewModel(this));
        log.Add($"Hookshot Launcher {typeof(BuildStepViewModel).Assembly.GetName().Version?.ToString(3)}: {job.Verb.Trim().ToLowerInvariant()} {job.Version} ({job.Channel})");
        log.Add($"settings: memory {(s.MemoryGb is int m ? m + " GB" : "automatic")}, workers {(s.BuildWorkers is int w ? w : "automatic")}, " +
                $"{Languages.NameOf(s.Language)}{(s.Dlss ? ", DLSS" : "")}{(s.Fsr && !job.Main.Linux ? ", FSR 4" : "")}");
        if (start)
        {
            ticker = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Update());
            Run();
        }
        Update();
    }

    public BuildJob Job { get; }
    public int Step => 3;
    public bool Finished => false;
    public IReadOnlyList<StageRowViewModel> Rows { get; }
    public IReadOnlyList<double> Notches { get; }
    public BuildProgress Progress => progress;
    public IReadOnlyList<string> Log => log;

    public string Title => Job.Kind switch
    {
        JobKind.Update => "Updating to " + Job.Shown,
        JobKind.Rebuild => "Rebuilding " + Job.Shown,
        _ => "Building " + Job.Shown,
    };

    public double Overall => progress.Overall;
    public string Percent => (int)Math.Floor(progress.Overall * 100) + "%";
    public string Left => clock.SecondsLeft(progress) is double secs ? Durations.About(secs) + " left" : "Working out the time left";

    public bool ShowLog
    {
        get => showLog;
        set
        {
            if (Set(ref showLog, value)) RaiseAll(nameof(ShowStages), nameof(LogButton), nameof(LogText));
        }
    }

    public bool ShowStages => !showLog;
    public string LogButton => showLog ? "Hide details" : "Show details";
    public string LogText
    {
        get
        {
            lock (log) return string.Join("\n", log.Count > 2000 ? log.Skip(log.Count - 2000) : log);
        }
    }

    public Command ToggleLogCommand { get; }
    public Command CancelCommand { get; }

    void Run()
    {
        var job = Job;
        var s = job.Service.Settings;
        var file = job.Picked!;
        var request = new BuildRequest(file.Source, job.InstallDir, s.Language, job.Service.Work, s.BuildWorkers, s.MemoryGb, s.PrepareShaders,
                                       s.Dlss, s.DlssLicense, s.Fsr, s.FsrLicense);
        MakeGameCommand command;
        try
        {
            command = MakeGameCommand.For(job.Tools!, request, job.Service.Platform, job.Service.Home, LauncherService.LauncherPath);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(job.InstallDir))!);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Fail(e.Message);
            return;
        }
        running = true;
        watch.Start();
        ticker?.Start();
        Task.Run(async () =>
        {
            int code;
            try
            {
                code = await MakeGameRun.RunAsync(command.StartInfo(), line => Ui.Post(() => Line(line)),
                                                  line => Ui.Post(() => ErrorLine(line)), cancel.Token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
            {
                Ui.Post(() => Fail("The build tools could not start: " + e.Message));
                return;
            }
            Ui.Post(() => Exited(code));
        });
    }

    public void Line(string line)
    {
        var parsed = progress.Feed(line);
        lock (log)
        {
            switch (parsed)
            {
                case MakeGameLine.Plain p:
                    log.Add(p.Text);
                    break;
                case MakeGameLine.Stage st:
                    log.Add($"[{Names.GetValueOrDefault(st.Id, st.Id)}] {st.Text}");
                    break;
                case MakeGameLine.Error e:
                    log.Add("error: " + e.Text);
                    break;
                case MakeGameLine.NotPrepared n:
                    log.Add(line);
                    break;
            }
        }
        Update();
    }

    void ErrorLine(string line)
    {
        if (!string.IsNullOrWhiteSpace(line)) lastError = line.Trim();
        lock (log) log.Add(line);
        if (showLog) Raise(nameof(LogText));
    }

    void Exited(int code)
    {
        running = false;
        ticker?.Stop();
        Update();
        if (progress.Finished && code == 0) Complete();
        else Fail(progress.Error ?? lastError ?? $"make_game.py stopped with exit code {code} without saying why.");
    }

    /// The rows, the bar and the time left from the progress and the clock
    public void Update()
    {
        clock.Observe(progress, fixedTime ?? watch.Elapsed);
        foreach (var (st, row) in progress.Stages.Zip(Rows))
        {
            if (st.Id == "download")
            {
                row.State = "done";
                row.Detail = Job.ToolsDownloaded ? "Downloaded in step 1" : "Already on this PC";
                row.Time = Job.ToolsDownloaded ? Durations.Clock(Job.ToolsTime) : "";
                row.TimeFaint = "";
                continue;
            }
            var took = clock.Of(st.Id);
            switch (st.State)
            {
                case StageState.Done:
                    row.State = "done";
                    row.Detail = st.Note ?? "Done";
                    row.Time = took is { } t ? Durations.Clock(t) : "";
                    row.TimeFaint = "";
                    break;
                case StageState.Running:
                    row.State = "run";
                    var frac = st.Weight > 0 ? Math.Clamp((progress.Overall - st.Start) / st.Weight, 0, 1) : 0;
                    row.Detail = st.Note ?? (st.Id == "finish" ? "Writing the game folder" : $"{(int)(frac * 100)}%");
                    row.Time = took is { } r ? Durations.Clock(r) : "";
                    row.TimeFaint = clock.Estimate(progress, st) is double est ? " / " + Durations.Roughly(est) : "";
                    break;
                case StageState.Skipped:
                    row.State = "skip";
                    row.Detail = st.Note ?? "At the game's first start";
                    row.Time = "";
                    row.TimeFaint = "Skipped";
                    break;
                default:
                    row.State = progress.Error != null && progress.Current == st.Id ? "fail" : "wait";
                    row.Detail = "";
                    row.Time = "";
                    row.TimeFaint = clock.Estimate(progress, st) is double e ? Durations.Roughly(e) : "";
                    break;
            }
        }
        RaiseAll(nameof(Overall), nameof(Percent), nameof(Left));
        if (showLog) Raise(nameof(LogText));
    }

    /// Cancel: the build and everything it started stopped, back home
    public void Stop()
    {
        running = false;
        ticker?.Stop();
        cancel.Cancel();
        Job.Main.ShowHome();
        Job.Main.ShowToast("Build stopped.");
    }

    void Fail(string why)
    {
        running = false;
        ticker?.Stop();
        var at = progress.Stages.FirstOrDefault(s => s.Id == progress.Current) ?? progress.Stages.LastOrDefault(s => s.State == StageState.Done);
        var name = at != null ? Names.GetValueOrDefault(at.Id, at.Title) : "the build";
        var pct = (int)Math.Floor(progress.Overall * 100);
        Job.Main.Navigate(new ProblemViewModel(this, why, name, pct));
    }

    void Complete()
    {
        var job = Job;
        var service = job.Service;
        var registry = service.Registry;
        var file = job.Picked!;
        var s = service.Settings;
        var wasEmpty = registry.Builds.Count == 0;
        BuildEntry entry = job.Kind == JobKind.Update && job.Replaces != null && registry.Find(job.Replaces.Id) != null
            ? registry.Replace(job.Replaces.Id, job.InstallDir, job.Version, job.Channel)
            : registry.Add(job.InstallDir, job.Version, job.Channel, name: job.Version, makeDefault: wasEmpty);
        if (file.Source is BuildSource.Folders f)
        {
            entry.Nup = null;
            entry.Romfs = f.Romfs;
            entry.Exefs = f.Exefs;
        }
        else
        {
            entry.Nup = file.Path;
            entry.Romfs = entry.Exefs = null;
        }
        entry.Dlss = s.Dlss && s.DlssLicense;
        entry.Fsr = !job.Main.Linux && s.Fsr && s.FsrLicense;
        entry.Bytes = null;
        string? problem = null;
        try { registry.Save(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { problem = "The list of builds could not be saved: " + e.Message; }
        service.PruneTools();

        // (the saves of the build chosen in step 2, copied into the new one after a backup: Windows, where each build has its own)
        string? moved = null, backup = null;
        if (!job.Main.Linux && job.MoveOn && job.MoveFrom is { } from && !BuildRegistry.SameFolder(from.Folder, job.InstallDir))
        {
            try
            {
                var copy = Saves.BetweenBuilds(from.Folder, job.InstallDir, service.SaveEnv);
                moved = from.Version ?? from.Name;
                backup = copy.Backup;
            }
            catch (Exception e) when (e is SaveFailure or IOException or UnauthorizedAccessException)
            {
                problem = $"Your saves from {from.Version ?? from.Name} were not moved: {e.Message} You can copy them from Saves.";
            }
        }
        job.Main.Navigate(new ReadyViewModel(job.Main, entry, job.Kind, job.From, clock.Elapsed + job.ToolsTime, moved, backup, problem));
    }

    /// For the screenshots: the lines a build would have said so far, with the clock at elapsed
    public void Replay(IEnumerable<(string Line, TimeSpan At)> lines)
    {
        foreach (var (l, at) in lines)
        {
            fixedTime = at;
            Line(l);
        }
    }

    public void ReplayExit(int code) => Exited(code);

    public bool Running => running;

    /// A bug report: the version, this PC, the settings and the log's last 200 lines
    public string Report(string why)
    {
        var s = Job.Service.Settings;
        var sb = new StringBuilder();
        sb.AppendLine($"Hookshot Launcher {typeof(BuildStepViewModel).Assembly.GetName().Version?.ToString(3)}: {Job.Verb.Trim()} {Job.Version} ({Job.Channel})");
        sb.AppendLine($"{System.Runtime.InteropServices.RuntimeInformation.OSDescription}, {SystemInfo.MemoryGb()} GB, {SystemInfo.Threads()} threads" +
                      (Job.Service.Gpus.Count > 0 ? ", " + string.Join(" and ", Job.Service.Gpus) + " GPU" : ""));
        sb.AppendLine($"settings: memory {(s.MemoryGb is int m ? m + " GB" : "automatic")}, workers {(s.BuildWorkers is int w ? w : "automatic")}, " +
                      $"shaders {(s.PrepareShaders ? "now" : "at first start")}, DLSS {(s.Dlss ? "on" : "off")}, FSR 4 {(s.Fsr ? "on" : "off")}");
        sb.AppendLine("stopped: " + why);
        sb.AppendLine();
        lock (log) foreach (var l in log.Skip(Math.Max(0, log.Count - 200))) sb.AppendLine(l);
        return sb.ToString();
    }
}

/// "Stop the build?"
public sealed class StopBuildViewModel(BuildStepViewModel build) : ViewModelBase
{
    public string Sub => $"The tools and the files read so far are kept, so building {build.Job.Shown} again starts faster.";
    public Command KeepCommand { get; } = new(() => build.Job.Main.Modal = null);
    public Command StopCommand { get; } = new(() => { build.Job.Main.Modal = null; build.Stop(); });
}

/// The build finished: Play, Open folder, the shortcuts, whether Play starts this version
public sealed class ReadyViewModel : ViewModelBase, IWizardPage
{
    bool desktop = true, menu = true;

    public ReadyViewModel(MainViewModel main, BuildEntry entry, JobKind kind, string? from, TimeSpan took, string? moved, string? backup, string? problem)
    {
        Main = main;
        Entry = entry;
        Kind = kind;
        From = from;
        Took = took;
        Moved = moved;
        Backup = backup;
        Problem = problem;
        PlayCommand = new Command(() =>
        {
            try
            {
                Main.Service.Play(Entry);
                Main.ShowHome();
                Main.ShowToast($"Starting Ocarina of Time, {Version}…");
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
            {
                Main.ShowToast($"{Version} could not start: {e.Message}");
            }
        });
        FolderCommand = new Command(() => Main.Host?.Open(Entry.Folder));
        BackupCommand = new Command(() => { if (Backup != null) Main.Host?.Open(Backup); });
        HomeCommand = new Command(Main.ShowHome);
        WriteShortcuts();
    }

    public MainViewModel Main { get; }
    public BuildEntry Entry { get; }
    public JobKind Kind { get; }
    public string? From { get; }
    public TimeSpan Took { get; }
    public string? Moved { get; }
    public string? Backup { get; }
    public string? Problem { get; }
    public int Step => 3;
    public bool Finished => true;

    public string Version => Entry.Version ?? Entry.Name;
    public string Heading => Version + " is ready";
    public string Sub => (Kind == JobKind.Update && From != null ? "Updated from " + From : "Built") +
                         $" in {Math.Max(1, (int)Math.Round(Took.TotalMinutes))} min, in {ShownPath.Of(Entry.Folder)}.";
    public bool HasMoved => Moved != null;
    public string MovedText => $"Your saves from {Moved} are here.";
    public bool HasBackup => Backup != null;
    public bool HasProblem => Problem != null;
    public string MenuLabel => Main.Linux ? "Applications menu entry" : "Start menu entry";
    public bool ShowDefaultTick => Main.Service.Registry.Builds.Count > 1;

    public bool Desktop
    {
        get => desktop;
        set
        {
            if (Set(ref desktop, value)) WriteShortcuts();
        }
    }

    public bool Menu
    {
        get => menu;
        set
        {
            if (Set(ref menu, value)) WriteShortcuts();
        }
    }

    public bool IsDefault
    {
        get => Entry.Default;
        set
        {
            if (value)
            {
                Main.Service.SetDefault(Entry);
                Main.ShowToast($"Play now starts {Version}.");
            }
            else if (Main.Service.Registry.Builds.FirstOrDefault(b => b.Id != Entry.Id) is { } other)
            {
                Main.Service.SetDefault(other);
                Main.ShowToast($"Play now starts {other.Version ?? other.Name}.");
            }
            Raise();
        }
    }

    /// The shortcuts start the launcher's Play of the default build, so they follow "Play starts this version" and updates
    void WriteShortcuts()
    {
        if (Main.Host == null) return;
        try
        {
            if (Main.Linux) Shortcuts.WriteLinux(LauncherService.LauncherPath, null, desktop, menu);
            else WindowsShortcuts.Write(LauncherService.LauncherPath, desktop, menu);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Main.ShowToast("The shortcuts could not be made: " + e.Message);
        }
    }

    public Command PlayCommand { get; }
    public Command FolderCommand { get; }
    public Command BackupCommand { get; }
    public Command HomeCommand { get; }
}

/// The build stopped: what happened, what to do, Copy for a bug report, Build again
public sealed class ProblemViewModel : ViewModelBase, IWizardPage
{
    readonly BuildStepViewModel build;
    bool showLog;

    public ProblemViewModel(BuildStepViewModel build, string why, string stage, int percent)
    {
        this.build = build;
        Why = why;
        Stage = stage;
        Percent = percent;
        ToggleLogCommand = new Command(() => ShowLog = !ShowLog);
        CopyCommand = new Command(() => _ = Copy());
        BackCommand = new Command(() => build.Job.Main.Navigate(build.Job.Settings ?? new SettingsStepViewModel(build.Job)));
        RetryCommand = new Command(() => build.Job.Main.Navigate(new BuildStepViewModel(build.Job)));
    }

    public int Step => 3;
    public bool Finished => false;
    public string Why { get; }
    public string Stage { get; }
    public int Percent { get; }
    public bool Memory => Why.Contains("memory", StringComparison.OrdinalIgnoreCase);
    public string Head => Memory ? "The PC ran out of memory while building" : "make_game.py stopped";
    public string Body => $"Building {build.Job.Shown} stopped in {Stage}, {Percent}% of the way through. It said: {Why}";
    public bool ShowLog { get => showLog; set { if (Set(ref showLog, value)) RaiseAll(nameof(LogButton), nameof(LogTail)); } }
    public string LogButton => showLog ? "Hide details" : "Show details";
    public string LogTail => string.Join("\n", build.Log.Skip(Math.Max(0, build.Log.Count - 6)));

    public Command ToggleLogCommand { get; }
    public Command CopyCommand { get; }
    public Command BackCommand { get; }
    public Command RetryCommand { get; }

    async Task Copy()
    {
        if (build.Job.Main.Host is { } host) await host.CopyAsync(build.Report(Why));
        build.Job.Main.ShowToast("Copied: the version, this PC's memory and GPU, the settings and the log's last 200 lines.");
    }
}
