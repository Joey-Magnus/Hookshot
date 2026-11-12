using System.Collections.ObjectModel;
using HookshotLauncher.Core;

namespace HookshotLauncher.ViewModels;

/// One version in the home's list
public sealed class BuildItemViewModel : ViewModelBase
{
    readonly HomeViewModel home;
    bool selected;

    public BuildItemViewModel(HomeViewModel home, BuildEntry entry, bool built, string? update, IReadOnlyList<SaveSlot> saves)
    {
        this.home = home;
        Entry = entry;
        IsBuilt = built;
        Update = update;
        Saves = saves;
        SelectCommand = new Command(() => home.Select(this));
        PlayCommand = new Command(() => home.Play(this), () => IsBuilt);
        SetDefaultCommand = new Command(() => home.SetDefault(this));
        DeleteCommand = new Command(() => home.AskDelete(this));
        UpdateCommand = new Command(() => home.UpdateBuild(this));
        SavesCommand = new Command(() => home.Main.ShowSaves(Entry));
        FolderCommand = new Command(() => home.Main.Host?.Open(Entry.Folder));
    }

    public BuildEntry Entry { get; }
    public IReadOnlyList<SaveSlot> Saves { get; }
    /// The version as players see it (a nightly: "Nightly 2029 · c544828f", Versions.Shown), else the build's name
    public string Version => Entry.Version is { } v ? Versions.Shown(v) : Entry.Name;
    public string Channel => UpdatePolicy.ChannelOf(Entry.Channel, Entry.Version);
    public bool IsRelease => Channel == Versions.Release;
    public bool IsCandidate => Channel == Versions.Candidate;
    public bool IsNightly => Channel == Versions.Nightly;
    public bool IsDefault => Entry.Default;
    public bool IsBuilt { get; }
    public bool Imported => Entry.Nup == null && Entry.Romfs == null && home.ImportedFolders.Any(f => BuildRegistry.SameFolder(f, Entry.Folder));
    public string? Update { get; }
    public bool HasUpdate => Update != null;
    public string UpdateLine => Update == null ? "" : Versions.Shown(Update) + " is out";

    /// "Built 3 Nov, 38.4 GB, 3 save slots" (no saves count on Linux, where every build shares them)
    public string Meta
    {
        get
        {
            var bits = new List<string> { Imported ? "From Builder 2.x" : "Built " + Durations.Day(Entry.Updated.LocalDateTime, DateTime.Now) };
            if (!IsBuilt) bits.Add("not built");
            if (Entry.Bytes is long b) bits.Add(Durations.Size(b));
            if (!home.Main.Linux) bits.Add(SaveText.Count(Saves.Count));
            return string.Join(", ", bits);
        }
    }

    public bool IsSelected
    {
        get => selected;
        set
        {
            if (Set(ref selected, value)) RaiseAll(nameof(ShowActions), nameof(ShowOtherActions), nameof(ShowDefaultNote));
        }
    }

    public bool ShowActions => selected;
    public bool ShowOtherActions => selected && !IsDefault;
    public bool ShowDefaultNote => selected && IsDefault;
    public bool ShowRing => !IsDefault;
    public bool ShowMeta => !HasUpdate;
    public bool IsFirst { get; set; }
    public Avalonia.Thickness Separator => IsFirst ? default : new Avalonia.Thickness(0, 1, 0, 0);

    public void RaiseMeta() => Raise(nameof(Meta));

    public Command SelectCommand { get; }
    public Command PlayCommand { get; }
    public Command SetDefaultCommand { get; }
    public Command DeleteCommand { get; }
    public Command UpdateCommand { get; }
    public Command SavesCommand { get; }
    public Command FolderCommand { get; }
}

/// Home: Build when there is no build; else the default version on the left (Play, its update, its saves) and every version on
/// the right; the notice after the 2.x Builder handed its game over
public sealed class HomeViewModel : ViewModelBase
{
    string? notice;
    string? importNotice;
    string status = "";
    BuildEntry? pendingUpdate;

    public HomeViewModel(MainViewModel main)
    {
        Main = main;
        BuildCommand = new Command(() => StartBuild(Service.Settings.DefaultChannel), () => NewestDefault != null);
        ChannelCommand = new Command(() => Main.Modal = new ChannelPickerViewModel(Main, Service.Settings.DefaultChannel));
        DismissNoticeCommand = new Command(() => ImportNotice = null);
        DismissProblemCommand = new Command(() => Notice = null);
        RetryCheckCommand = new Command(() => _ = CheckReleasesAsync());
        PlayDefaultCommand = new Command(() => { if (Default is { } d) Play(d); }, () => Default?.IsBuilt == true);
        UpdateDefaultCommand = new Command(() => { if (Default is { } d) UpdateBuild(d); });
        SavesDefaultCommand = new Command(() => Main.ShowSaves(Default?.Entry));
    }

    public MainViewModel Main { get; }
    LauncherService Service => Main.Service;

    public ObservableCollection<BuildItemViewModel> Builds { get; } = new();
    public IReadOnlyList<string> ImportedFolders { get; private set; } = Array.Empty<string>();

    public bool HasBuilds => Builds.Count > 0;
    public bool NoBuilds => Builds.Count == 0;
    public BuildItemViewModel? Default => Builds.FirstOrDefault(b => b.IsDefault);

    // ---- the empty home ----

    public string? NewestDefault => Service.NewestOf(Service.Settings.DefaultChannel);
    public string NewestDefaultText => NewestDefault is { } v ? Versions.Shown(v) : "";
    public string DefaultChannel => Service.Settings.DefaultChannel;
    public bool DefaultIsRelease => DefaultChannel == Versions.Release;
    public bool DefaultIsCandidate => DefaultChannel == Versions.Candidate;
    public bool DefaultIsNightly => DefaultChannel == Versions.Nightly;
    public bool KnowsNewest => NewestDefault != null;
    public bool CheckFailed => NewestDefault == null && Service.Checked;
    public bool Checking => NewestDefault == null && !Service.Checked;

    // ---- the default version, on the left ----

    public string HeroVersion => Default?.Version ?? "";
    public string HeroChannel => Default?.Channel ?? Versions.Release;
    public bool HeroIsRelease => HeroChannel == Versions.Release;
    public bool HeroIsCandidate => HeroChannel == Versions.Candidate;
    public bool HeroIsNightly => HeroChannel == Versions.Nightly;

    /// "Your default version. Built 3 Nov, English (US), NVIDIA DLSS."
    public string HeroNote
    {
        get
        {
            if (Default is not { } d) return "";
            var e = d.Entry;
            var bits = new List<string>
            {
                d.Imported ? "Built with Builder 2.x" : "Built " + Durations.Day(e.Updated.LocalDateTime, DateTime.Now),
            };
            if (GameFolder.Language(e.Folder) is { } lang) bits.Add(Languages.NameOf(lang));
            if (e.Dlss ?? GameFolder.HasDlss(e.Folder)) bits.Add("NVIDIA DLSS");
            if (e.Fsr == true) bits.Add("AMD FSR 4");
            if (!d.IsBuilt) bits.Add("not built: its game is missing");
            return "Your default version. " + string.Join(", ", bits) + ".";
        }
    }

    public string HeroSaves => Default is { } d ? SaveText.Short(d.Saves, DateTime.Now) : "";
    public bool HeroHasUpdate => Default?.HasUpdate == true;
    public string HeroUpdate => Default is { Update: { } u } d ? $"{u} is out on the {d.Channel} channel." : "";

    /// "3 versions use 116.7 GB in D:\Games\Hookshot."
    public string Footer
    {
        get
        {
            var n = Builds.Count;
            var known = Builds.Where(b => b.Entry.Bytes != null).Sum(b => b.Entry.Bytes!.Value);
            var what = n == 1 ? "1 version" : n + " versions";
            return known > 0 ? $"{what} {(n == 1 ? "uses" : "use")} {Durations.Size(known)} in {ShownPath.Of(Service.BuildsFolder)}." : $"{what} in {ShownPath.Of(Service.BuildsFolder)}.";
        }
    }

    // ---- notices ----

    /// After the 2.x Builder handed its game over: "v2.3.0 in D:\Games\Hookshot is in your versions, ..."
    public string? ImportNotice
    {
        get => importNotice;
        set
        {
            if (Set(ref importNotice, value)) RaiseAll(nameof(HasImportNotice), nameof(ShowLeftPanel));
        }
    }

    public bool HasImportNotice => importNotice != null;

    /// A problem to read (a hand-over that failed, a build that would not start): null for none
    public string? Notice
    {
        get => notice;
        set
        {
            if (Set(ref notice, value)) Raise(nameof(HasNotice));
        }
    }

    public bool HasNotice => notice != null;
    public bool ShowLeftPanel => HasBuilds;

    /// The quiet answer of the update check
    public string Status
    {
        get => status;
        set => Set(ref status, value);
    }

    public Command BuildCommand { get; }
    public Command ChannelCommand { get; }
    public Command DismissNoticeCommand { get; }
    public Command DismissProblemCommand { get; }
    public Command RetryCheckCommand { get; }
    public Command PlayDefaultCommand { get; }
    public Command UpdateDefaultCommand { get; }
    public Command SavesDefaultCommand { get; }

    /// The start's own request: --launch and --update name a build (listed if it was not); the hand-over's problems are shown
    public void Open(StartRequest start)
    {
        var notes = new List<string>();
        if (Service.Import is { } import)
        {
            if (import.Problems.Count > 0) notes.Add("Some of the 2.x Builder's hand-over could not be read: " + string.Join("; ", import.Problems));
            ImportedFolders = import.Folders;
            if (import.Folders.Count > 0 && Service.Registry.FindByFolder(import.Folders[0]) is { } first)
                ImportNotice = $"{(first.Version is { } v ? Versions.Shown(v) : first.Name)} in {ShownPath.Of(first.Folder)} is in your versions, with the settings you built it with. " +
                               "Your saves came along as they are.";
        }
        if (Service.Registry.Problem is { } broken) notes.Add("The list of builds could not be read (kept as builds.json.broken): " + broken);
        if (start.Problem != null) notes.Add(start.Problem);
        if (start.Folder != null && start.Kind is StartKind.Launch or StartKind.Update)
        {
            var entry = Service.Ensure(start.Folder);
            if (start.Kind == StartKind.Update && start.Tag != null && Versions.IsTagName(start.Tag))
                Ui.Post(() => Main.StartWizard(JobKind.Update, UpdatePolicy.ChannelOf(entry.Channel, entry.Version), start.Tag, entry));
            // (no --tag: its channel's newest, once the update check has answered)
            else if (start.Kind == StartKind.Update) pendingUpdate = entry;
        }
        Notice = notes.Count > 0 ? string.Join("\n", notes) : null;
        Refresh();
    }

    public void Refresh()
    {
        var selected = Builds.FirstOrDefault(b => b.IsSelected)?.Entry.Id;
        Builds.Clear();
        foreach (var b in Service.Registry.Builds)
        {
            var item = new BuildItemViewModel(this, b, Service.IsBuilt(b), Service.UpdateFor(b), Service.SavesOf(b));
            item.IsSelected = b.Id == selected;
            item.IsFirst = Builds.Count == 0;
            Builds.Add(item);
        }
        RaiseAll(nameof(HasBuilds), nameof(NoBuilds), nameof(Default), nameof(ShowLeftPanel), nameof(HeroVersion), nameof(HeroChannel),
                 nameof(HeroIsRelease), nameof(HeroIsCandidate), nameof(HeroIsNightly), nameof(HeroNote), nameof(HeroSaves), nameof(HeroHasUpdate),
                 nameof(HeroUpdate), nameof(Footer), nameof(NewestDefault), nameof(NewestDefaultText), nameof(DefaultChannel), nameof(DefaultIsRelease), nameof(DefaultIsCandidate),
                 nameof(DefaultIsNightly), nameof(KnowsNewest), nameof(CheckFailed), nameof(Checking));
        BuildCommand.Changed();
        PlayDefaultCommand.Changed();
        Service.Measure(() => { foreach (var b in Builds) b.RaiseMeta(); Raise(nameof(Footer)); });
    }

    /// The update check, in the background: a failure is one quiet line, never a stop
    public async Task CheckReleasesAsync()
    {
        Service.Checked = false;
        Refresh();
        try
        {
            var any = await Service.CheckReleasesAsync();
            Status = any ? "" : "Could not reach GitHub: updates were not checked.";
        }
        catch (Exception e)
        {
            Service.Checked = true;
            Status = "Could not check for updates: " + e.Message;
        }
        Refresh();
        if (pendingUpdate is { } p && Main.Page == this)
        {
            pendingUpdate = null;
            var version = p.Version is { } v ? Versions.Shown(v) : p.Name;
            if (Service.UpdateFor(p) is { } to) Main.StartWizard(JobKind.Update, UpdatePolicy.ChannelOf(p.Channel, p.Version), to, p);
            else if (Service.NewestOf(UpdatePolicy.ChannelOf(p.Channel, p.Version)) != null) Main.ShowToast($"{version} is up to date.");
        }
    }

    public void Select(BuildItemViewModel b)
    {
        var on = !b.IsSelected;
        foreach (var x in Builds) x.IsSelected = false;
        b.IsSelected = on;
    }

    public void Play(BuildItemViewModel b)
    {
        try
        {
            Service.Play(b.Entry);
            Main.ShowToast($"Starting Ocarina of Time, {b.Version}…");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            Notice = $"{b.Version} could not start: {e.Message}";
        }
    }

    public void SetDefault(BuildItemViewModel b)
    {
        Service.SetDefault(b.Entry);
        Refresh();
        Main.ShowToast($"Play now starts {b.Version}.");
    }

    public void AskDelete(BuildItemViewModel b) => Main.Modal = new DeleteViewModel(Main, b);

    public void UpdateBuild(BuildItemViewModel b)
    {
        if (b.Update is not { } to) return;
        ImportNotice = null;
        Main.StartWizard(JobKind.Update, b.Channel, to, b.Entry);
    }

    public void StartBuild(string channel)
    {
        if (Service.NewestOf(channel) is not { } version) return;
        var same = Service.Registry.Builds.FirstOrDefault(b => b.Version == version);
        Main.StartWizard(same != null ? JobKind.Rebuild : JobKind.Build, channel, version, same);
    }
}

/// Choosing what to build: release, rc or nightly, each with its newest version and whether you have it
public sealed class ChannelPickerViewModel : ViewModelBase
{
    string pick;

    public sealed class Row(ChannelPickerViewModel owner, string id, string name, string about, string? version, bool have) : ViewModelBase
    {
        public string Id => id;
        public string Name => name;
        public string About => about;
        public string Version => version == null ? "Not found" : Versions.Shown(version);
        public bool Known => version != null;
        public bool Have => have;
        public bool IsRelease => id == Versions.Release;
        public bool IsCandidate => id == Versions.Candidate;
        public bool IsNightly => id == Versions.Nightly;
        public string Tag => id;

        public bool IsPicked
        {
            get => owner.Pick == id;
            set
            {
                if (value) owner.Pick = id;
            }
        }

        public void Changed() => Raise(nameof(IsPicked));
    }

    public static readonly (string Id, string Name, string About)[] All =
    {
        (Versions.Release, "Release", "The tested version. Best for playing."),
        (Versions.Candidate, "Release candidate", "The next version, to try before it is released."),
        (Versions.Nightly, "Nightly", "The newest development build, with every change of the day."),
    };

    public ChannelPickerViewModel(MainViewModel main, string start)
    {
        Main = main;
        Rows = All.Select(c => new Row(this, c.Id, c.Name, c.About, main.Service.NewestOf(c.Id), Has(main.Service.NewestOf(c.Id)))).ToList();
        pick = start;
        GoCommand = new Command(Go, () => Version != null);
        CancelCommand = new Command(() => Main.Modal = null);
    }

    bool Has(string? v) => v != null && Main.Service.Registry.Builds.Any(b => b.Version == v);

    public MainViewModel Main { get; }
    public IReadOnlyList<Row> Rows { get; }
    public Command GoCommand { get; }
    public Command CancelCommand { get; }

    public string Pick
    {
        get => pick;
        set
        {
            if (!Set(ref pick, value)) return;
            foreach (var r in Rows) r.Changed();
            RaiseAll(nameof(Again), nameof(GoText));
            GoCommand.Changed();
        }
    }

    string? Version => Main.Service.NewestOf(pick);
    public bool Again => Has(Version);
    public string GoText => Version == null ? "Continue" : (Again ? "Rebuild " : "Continue with ") + Versions.Shown(Version);

    void Go()
    {
        Main.Modal = null;
        Main.Home.StartBuild(pick);
    }
}

/// Deleting a build: what goes with it (on Windows its saves), and who becomes the default
public sealed class DeleteViewModel : ViewModelBase
{
    readonly BuildItemViewModel build;

    public DeleteViewModel(MainViewModel main, BuildItemViewModel build)
    {
        Main = main;
        this.build = build;
        KeepCommand = new Command(() => Main.Modal = null);
        DeleteCommand = new Command(() => _ = DeleteAsync());
    }

    public MainViewModel Main { get; }
    public string Title => $"Delete {build.Version}?";
    bool GameFolderHere => GameFolder.IsGameFolder(build.Entry.Folder, Main.Platform);

    public string Sub => GameFolderHere
        ? "This deletes its folder" + (build.Entry.Bytes is long b ? $" and frees {Durations.Size(b)}:" : ":")
        : "This takes it off your versions. Its folder is not a Hookshot game folder, so it stays on the disk:";
    public string Folder => ShownPath.Of(build.Entry.Folder);
    public string FolderFull => build.Entry.Folder;

    public bool Linux => Main.Linux;
    public bool Windows => !Main.Linux;
    public string SavesWarning => "Its saves go with it" + (build.Saves.Count > 0 ? $" ({SaveText.Long(build.Saves, DateTime.Now)})" : "") +
                                  ". To keep them, copy them to another build in Saves first.";
    BuildEntry? Next => Main.Service.Registry.Builds.FirstOrDefault(b => b.Id != build.Entry.Id);
    public bool DefaultNote => build.IsDefault && Next != null;
    public string DefaultText => Next is { } n ? $"It is your default: Play will start {(n.Version is { } v ? Versions.Shown(v) : n.Name)} instead." : "";

    public Command KeepCommand { get; }
    public Command DeleteCommand { get; }

    async Task DeleteAsync()
    {
        Main.Modal = null;
        var service = Main.Service;
        var entry = build.Entry;
        var files = GameFolderHere;
        // (a running game holds its files: on Windows the delete would stop half-way through the folder)
        if (files && Saves.Running(service.Platform).Contains(Saves.Hookshot.Display))
        {
            Main.Home.Notice = $"Close Hookshot first: {build.Version} is not deleted while a game runs.";
            return;
        }
        try
        {
            if (files)
            {
                // (tens of GB: off the window's thread, the folder first and the row once it is gone)
                var keep = service.KeepOf(entry);
                Main.ShowToast($"Deleting {build.Version}…");
                await Task.Run(() => BuildRegistry.DeleteFolder(entry.Folder, service.Platform, keep));
            }
            // (the default goes to a release when there is one left, as the mockup: Registry.Remove takes the first otherwise)
            var wasDefault = build.IsDefault;
            service.Remove(entry, deleteFiles: false);
            if (wasDefault && service.Registry.Builds.FirstOrDefault(b => UpdatePolicy.ChannelOf(b.Channel, b.Version) == Versions.Release) is { } rel)
                service.SetDefault(rel);
            Main.ShowToast($"Deleted {build.Version}" + (entry.Bytes is long b && files ? $", {Durations.Size(b)} freed." : "."));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Main.Home.Notice = $"{entry.Folder} could not be deleted: {e.Message}";
        }
        Main.Home.Refresh();
    }
}
