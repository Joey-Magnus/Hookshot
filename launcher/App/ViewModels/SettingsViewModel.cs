using HookshotLauncher.Core;

namespace HookshotLauncher.ViewModels;

/// The launcher's settings: the channel new builds start on, where builds go, the build files, the update check. Each version
/// keeps its own build settings (step 2)
public sealed class SettingsViewModel : ViewModelBase
{
    readonly LauncherSettings s;
    long? workBytes;

    public sealed class ChannelOption(SettingsViewModel owner, string id, string name, string about, string? version) : ViewModelBase
    {
        public string Id => id;
        public string Name => name;
        public string About => about;
        public string Version => version == null ? "Not checked yet" : Versions.Shown(version);

        public bool IsPicked
        {
            get => owner.Channel == id;
            set
            {
                if (value) owner.Channel = id;
            }
        }

        public void Changed() => Raise(nameof(IsPicked));
    }

    public SettingsViewModel(MainViewModel main)
    {
        Main = main;
        s = main.Service.Settings;
        Channels = ChannelPickerViewModel.All.Select(c => new ChannelOption(this, c.Id, c.Name, c.About, main.Service.NewestOf(c.Id))).ToList();
        DoneCommand = new Command(Done);
        ChangeBuildsCommand = new Command(() => _ = ChangeBuildsAsync());
        ChangeWorkCommand = new Command(() => _ = ChangeWorkAsync());
        DefaultWorkCommand = new Command(() => { s.Work = null; Save(); RaiseAll(nameof(Work)); MeasureWork(); });
        FreeSpaceCommand = new Command(FreeSpace, () => workBytes > 0);
        ReleaseNotesCommand = new Command(() => Main.Host?.Open(Releases.Repo + "/releases"));
        MeasureWork();
    }

    public MainViewModel Main { get; }
    public IReadOnlyList<ChannelOption> Channels { get; }

    void Save()
    {
        try { Main.Service.SaveSettings(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Main.ShowToast("The settings could not be saved: " + e.Message); }
    }

    public string Channel
    {
        get => s.DefaultChannel;
        set
        {
            if (value == s.DefaultChannel || !Versions.IsChannel(value)) return;
            s.DefaultChannel = value;
            Save();
            foreach (var c in Channels) c.Changed();
            Raise(nameof(ChannelHint));
        }
    }

    public string ChannelHint => (ChannelPickerViewModel.All.FirstOrDefault(c => c.Id == s.DefaultChannel).About ?? "") + " Builds you have keep their own channel.";

    public string BuildsFolder
    {
        get => ShownPath.Of(Main.Service.BuildsFolder);
        set
        {
            s.BuildsFolder = string.IsNullOrWhiteSpace(value) ? null : ShownPath.Typed(value.Trim());
            Save();
            Raise();
        }
    }

    public string Work
    {
        get => ShownPath.Of(Main.Service.Work);
        set
        {
            s.Work = string.IsNullOrWhiteSpace(value) ? null : ShownPath.Typed(value.Trim());
            Save();
            Raise();
            MeasureWork();
        }
    }

    public string WorkNote => workBytes is long b && b > 0 ? $"{Durations.Size(b)} kept to make updates quicker." : "Nothing kept yet.";

    public bool CheckUpdates
    {
        get => s.CheckUpdates;
        set
        {
            s.CheckUpdates = value;
            Save();
            Raise();
        }
    }

    public string Version => "Hookshot Launcher " + (typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "3.0.0");

    public Command DoneCommand { get; }
    public Command ChangeBuildsCommand { get; }
    public Command ChangeWorkCommand { get; }
    public Command DefaultWorkCommand { get; }
    public Command FreeSpaceCommand { get; }
    public Command ReleaseNotesCommand { get; }

    void MeasureWork()
    {
        var folder = Main.Service.Work;
        Task.Run(() =>
        {
            var bytes = Disk.WorkBytes(folder);
            Ui.Post(() =>
            {
                workBytes = bytes;
                Raise(nameof(WorkNote));
                FreeSpaceCommand.Changed();
            });
        });
    }

    /// The build files deleted: the next build or update makes them again. Only what make_game made there (Disk.WorkParts): the
    /// folder may be one the player chose, holding other things
    void FreeSpace()
    {
        var folder = Main.Service.Work;
        var freed = workBytes ?? 0;
        try
        {
            Disk.FreeWork(folder);
            Main.ShowToast($"Freed {Durations.Size(freed)} of build files. The next update makes them again.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Main.ShowToast("The build files could not all be deleted: " + e.Message);
        }
        MeasureWork();
    }

    async Task ChangeBuildsAsync()
    {
        if (Main.Host is not { } host) return;
        if (await host.PickFolderAsync("Where builds go", Main.Service.BuildsFolder) is { } picked) BuildsFolder = Path.GetFullPath(picked);
    }

    async Task ChangeWorkAsync()
    {
        if (Main.Host is not { } host) return;
        if (await host.PickFolderAsync("Where the build files go", Main.Service.Work) is { } picked) Work = Path.GetFullPath(picked);
    }

    void Done()
    {
        Save();
        Main.ShowHome();
    }
}
