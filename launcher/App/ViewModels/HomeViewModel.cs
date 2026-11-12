using System.Collections.ObjectModel;
using HookshotLauncher.Core;

namespace HookshotLauncher.ViewModels;

/// One build in the home list
public sealed class BuildItemViewModel : ViewModelBase
{
    readonly HomeViewModel home;

    public BuildItemViewModel(HomeViewModel home, BuildEntry entry, bool built, string? update)
    {
        this.home = home;
        Entry = entry;
        IsBuilt = built;
        Update = update;
        PlayCommand = new Command(() => home.Play(this), () => IsBuilt);
        SetDefaultCommand = new Command(() => home.SetDefault(this), () => !IsDefault);
        DeleteCommand = new Command(() => home.AskDelete(this));
        UpdateCommand = new Command(() => home.UpdateBuild(this), () => HasUpdate);
        SavesCommand = new Command(() => home.Main.ShowSaves(Entry));
    }

    public BuildEntry Entry { get; }
    public string Name => Entry.Name;
    public string Folder => Entry.Folder;
    public string Version => Entry.Version ?? "Unknown version";
    public string Channel => ChannelLabel(Entry.Channel);
    public bool IsDefault => Entry.Default;
    public bool IsBuilt { get; }
    public string BuiltText => IsBuilt ? "Built" : "Not built";
    public string? Update { get; }
    public bool HasUpdate => Update != null;
    public string UpdateText => Update == null ? "" : "Update to " + Update;

    public Command PlayCommand { get; }
    public Command SetDefaultCommand { get; }
    public Command DeleteCommand { get; }
    public Command UpdateCommand { get; }
    public Command SavesCommand { get; }

    public static string ChannelLabel(string channel) => channel switch
    {
        Versions.Candidate => "Release candidate",
        Versions.Nightly => "Nightly",
        _ => "Release",
    };
}

public sealed record ChannelChoice(string Id, string Label, string Note);

/// Home: Build when there is no build, else Play (the default build); the list of builds; the channel new builds follow
public sealed class HomeViewModel : ViewModelBase
{
    BuildItemViewModel? pendingDelete;
    bool deleteFiles;
    string status = "";
    string? notice;
    ChannelChoice channel;

    public HomeViewModel(MainViewModel main)
    {
        Main = main;
        Channels = new[]
        {
            new ChannelChoice(Versions.Release, "Release", "Tested releases"),
            new ChannelChoice(Versions.Candidate, "Release candidate", "The next release, to try first"),
            new ChannelChoice(Versions.Nightly, "Nightly", "Every change of the day"),
        };
        channel = Channels.FirstOrDefault(c => c.Id == main.Service.Settings.DefaultChannel) ?? Channels[0];
        PrimaryCommand = new Command(Primary);
        NewBuildCommand = new Command(() => Main.ShowWizard());
        SettingsCommand = new Command(Main.ShowSettings);
        SavesCommand = new Command(() => Main.ShowSaves());
        ConfirmDeleteCommand = new Command(ConfirmDelete);
        CancelDeleteCommand = new Command(() => PendingDelete = null);
        DismissNoticeCommand = new Command(() => Notice = null);
        Refresh();
    }

    public MainViewModel Main { get; }
    LauncherService Service => Main.Service;

    public ObservableCollection<BuildItemViewModel> Builds { get; } = new();
    public IReadOnlyList<ChannelChoice> Channels { get; }

    public bool HasBuilds => Builds.Count > 0;
    public bool NoBuilds => Builds.Count == 0;
    public BuildItemViewModel? DefaultBuild => Builds.FirstOrDefault(b => b.IsDefault);
    public string PrimaryText => DefaultBuild is { } d && d.IsBuilt ? "Play" : "Build";
    public string Headline => DefaultBuild is { } d ? d.Name : "No game built yet";
    public string Subline => DefaultBuild is { } d
        ? $"{d.Version} · {d.Channel}{(d.IsBuilt ? "" : " · not built")}"
        : "Build Hookshot from your own copy of the game. It takes a while the first time.";
    public bool DefaultHasUpdate => DefaultBuild?.HasUpdate == true;
    public string DefaultUpdateText => DefaultBuild?.UpdateText ?? "";
    public Command DefaultUpdateCommand => DefaultBuild?.UpdateCommand ?? new Command(() => { }, () => false);

    public ChannelChoice Channel
    {
        get => channel;
        set
        {
            if (value == null || !Set(ref channel, value)) return;
            Service.Settings.DefaultChannel = value.Id;
            Service.SaveSettings();
        }
    }

    /// The quiet line at the bottom: the update check's answer
    public string Status
    {
        get => status;
        set => Set(ref status, value);
    }

    /// A message that waits to be read (an import that failed, a command-line update): null for none
    public string? Notice
    {
        get => notice;
        set
        {
            if (Set(ref notice, value)) Raise(nameof(HasNotice));
        }
    }

    public bool HasNotice => notice != null;

    public BuildItemViewModel? PendingDelete
    {
        get => pendingDelete;
        set
        {
            if (!Set(ref pendingDelete, value)) return;
            DeleteFiles = false;
            Raise(nameof(Deleting));
            Raise(nameof(DeleteTitle));
            Raise(nameof(DeleteFolderText));
            Raise(nameof(CanDeleteFiles));
        }
    }

    public bool Deleting => pendingDelete != null;
    public string DeleteTitle => pendingDelete == null ? "" : $"Remove {pendingDelete.Name} from the list?";
    public string DeleteFolderText => pendingDelete == null ? "" : "Also delete its game folder from the disk: " + pendingDelete.Folder;
    public bool CanDeleteFiles => pendingDelete != null && GameFolder.IsGameFolder(pendingDelete.Folder, Service.Platform);

    public bool DeleteFiles
    {
        get => deleteFiles;
        set => Set(ref deleteFiles, value);
    }

    public Command PrimaryCommand { get; }
    public Command NewBuildCommand { get; }
    public Command SettingsCommand { get; }
    public Command SavesCommand { get; }
    public Command ConfirmDeleteCommand { get; }
    public Command CancelDeleteCommand { get; }
    public Command DismissNoticeCommand { get; }

    /// The start's own request: --launch and --update name a build (listed if it was not); the hand-over's problems are shown
    public void Open(StartRequest start)
    {
        var notes = new List<string>();
        if (Service.Import is { Problems.Count: > 0 } import)
            notes.Add("Some of the 2.x Builder's hand-over could not be read: " + string.Join("; ", import.Problems));
        if (Service.Registry.Problem is { } broken) notes.Add("The list of builds could not be read (kept as builds.json.broken): " + broken);
        if (start.Problem != null) notes.Add(start.Problem);
        if (start.Folder != null && start.Kind is StartKind.Launch or StartKind.Update)
        {
            var entry = Service.Ensure(start.Folder);
            if (start.Kind == StartKind.Update)
                notes.Add($"Updating {entry.Name}{(start.Tag != null ? " to " + start.Tag : "")} comes with the build wizard.");
        }
        Notice = notes.Count > 0 ? string.Join("\n", notes) : null;
        Refresh();
    }

    public void Refresh()
    {
        Builds.Clear();
        // (the default first, then the newest version first)
        foreach (var b in Service.Registry.Builds.OrderByDescending(b => b.Default).ThenByDescending(b => b.Version, Comparer<string?>.Create(Versions.Compare)))
            Builds.Add(new BuildItemViewModel(this, b, Service.IsBuilt(b), Service.UpdateFor(b)));
        foreach (var name in new[] { nameof(HasBuilds), nameof(NoBuilds), nameof(DefaultBuild), nameof(PrimaryText), nameof(Headline), nameof(Subline),
                                     nameof(DefaultHasUpdate), nameof(DefaultUpdateText), nameof(DefaultUpdateCommand) })
            Raise(name);
    }

    /// The update check, in the background: a failure is one quiet line, never a stop
    public async Task CheckReleasesAsync()
    {
        Status = "Checking for updates…";
        try
        {
            var any = await Service.CheckReleasesAsync();
            Refresh();
            var updates = Builds.Count(b => b.HasUpdate);
            Status = !any ? "Could not reach GitHub: updates were not checked."
                : updates > 0 ? $"{updates} update{(updates > 1 ? "s" : "")} available."
                : "Up to date.";
        }
        catch (Exception e)
        {
            Status = "Could not check for updates: " + e.Message;
        }
    }

    void Primary()
    {
        if (DefaultBuild is { IsBuilt: true } d) Play(d);
        else Main.ShowWizard();
    }

    public void Play(BuildItemViewModel b)
    {
        try { Service.Play(b.Entry); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            Notice = $"{b.Name} could not start: {e.Message}";
        }
    }

    public void SetDefault(BuildItemViewModel b)
    {
        Service.SetDefault(b.Entry);
        Refresh();
    }

    public void AskDelete(BuildItemViewModel b) => PendingDelete = b;

    void ConfirmDelete()
    {
        if (pendingDelete is not { } b) return;
        try { Service.Remove(b.Entry, DeleteFiles && CanDeleteFiles); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Notice = $"{b.Folder} could not be deleted: {e.Message}";
        }
        PendingDelete = null;
        Refresh();
    }

    public void UpdateBuild(BuildItemViewModel b) => Main.ShowWizard(b.Entry, b.Update);
}
