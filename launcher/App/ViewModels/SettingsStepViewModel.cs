using HookshotLauncher.Core;

namespace HookshotLauncher.ViewModels;

public sealed record Choice(int? Value, string Label);

public sealed record LanguageChoice(string Code, string Name);

/// Step 2's saves dropdown: from a build, or none
public sealed record SaveSource(BuildEntry? Build, string Label);

/// Step 2, the build settings (LauncherSettings, remembered for the next build): the saves to move (Windows), DLSS and FSR 4 with
/// their licences, the game folder and the space it needs, the language, the shaders, and under Advanced the build's memory,
/// workers and build files
public sealed class SettingsStepViewModel : ViewModelBase, IWizardPage
{
    readonly LauncherSettings s;
    SaveSource source;
    long? free;

    public SettingsStepViewModel(BuildJob job)
    {
        Job = job;
        job.Settings = this;
        s = job.Service.Settings;
        var memory = SystemInfo.MemoryGb();
        var threads = SystemInfo.Threads();
        MemoryGb = memory;
        Threads = threads;
        // (the 2.x Builder's choices: Automatic, then every 4 GB of the PC's, every CPU thread)
        MemoryChoices = new[] { new Choice(null, "Automatic") }
            .Concat(Enumerable.Range(1, Math.Max(1, (memory + 3) / 4)).Select(i => new Choice(i * 4, $"{i * 4} GB"))).ToList();
        WorkerChoices = new[] { new Choice(null, "Automatic") }
            .Concat(Enumerable.Range(1, Math.Max(1, threads)).Select(i => new Choice(i, $"{i} at once"))).ToList();
        LanguageChoices = Languages.All.Select(l => new LanguageChoice(l.Code, l.Name)).ToList();
        var builds = job.Service.Registry.Builds.Where(b => b.Id != (job.Kind == JobKind.Rebuild ? job.Replaces?.Id : null)).ToList();
        SaveSources = builds.Select(b =>
        {
            var n = job.Service.SavesOf(b).Count;
            return new SaveSource(b, $"From {(b.Version is { } v ? Versions.Shown(v) : b.Name)}" + (n == 0 ? " (empty)" : n == 1 ? " (1 slot)" : $" ({n} slots)"));
        }).Append(new SaveSource(null, "Start with no saves")).ToList();
        source = job.MoveOn ? SaveSources.FirstOrDefault(x => x.Build?.Id == job.MoveFrom?.Id) ?? SaveSources[^1] : SaveSources[^1];
        BackCommand = new Command(() => job.Main.Navigate(job.Files ?? new FilesStepViewModel(job)));
        StartCommand = new Command(Start, () => !Short);
        ChangeInstallCommand = new Command(() => _ = ChangeInstallAsync());
        ChangeWorkCommand = new Command(() => _ = ChangeWorkAsync());
        AdvancedCommand = new Command(() => AdvancedOpen = !AdvancedOpen);
        Measure();
    }

    public BuildJob Job { get; }
    MainViewModel Main => Job.Main;
    public int Step => 2;
    public bool Finished => false;
    public bool Linux => Main.Linux;
    public bool Windows => !Main.Linux;
    public int MemoryGb { get; }
    public int Threads { get; }

    void Save()
    {
        try { Job.Service.SaveSettings(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ---- saves ----

    public bool ShowSaves => Job.Kind != JobKind.Rebuild && SaveSources.Count > 1;
    public bool ShowMoveSaves => ShowSaves && Windows;
    public bool ShowSharedSaves => ShowSaves && Linux;
    public IReadOnlyList<SaveSource> SaveSources { get; }
    public string MoveTitle => "Move my saves to " + Job.Shown;
    public bool MoveOn => source.Build != null;

    public SaveSource Source
    {
        get => source;
        set
        {
            if (value == null || !Set(ref source, value)) return;
            Job.MoveOn = value.Build != null;
            Job.MoveFrom = value.Build ?? Job.MoveFrom;
            s.MoveSaves = value.Build != null;
            Save();
            RaiseAll(nameof(MoveOn), nameof(MoveDesc));
        }
    }

    public string MoveDesc => source.Build is { } b
        ? SaveText.Long(Job.Service.SavesOf(b), DateTime.Now) + ". Backed up first."
        : Job.Shown + " starts empty. You can copy saves in later.";

    public string SharedDesc => "Every build on Linux uses the same saves: " +
        (Job.Service.Registry.Builds.FirstOrDefault() is { } any ? SaveText.Long(Job.Service.SavesOf(any), DateTime.Now) : "No saves yet") + ".";

    // ---- upscaling ----

    public bool DlssYourGpu => Job.Service.Gpus.Contains(SystemInfo.Nvidia);
    public bool FsrYourGpu => Job.Service.Gpus.Contains(SystemInfo.Amd);

    public bool Dlss
    {
        get => s.Dlss;
        set
        {
            if (value == s.Dlss) return;
            if (value && !s.DlssLicense)
            {
                Main.Modal = new LicenceViewModel(Main, true, Linux, () => { s.DlssLicense = true; s.Dlss = true; Save(); RaiseUpscalers(); },
                                                  RaiseUpscalers);
                Raise();
                return;
            }
            s.Dlss = value;
            Save();
            RaiseUpscalers();
        }
    }

    public bool Fsr
    {
        get => s.Fsr && Windows;
        set
        {
            if (Linux || value == s.Fsr) return;
            if (value && !s.FsrLicense)
            {
                Main.Modal = new LicenceViewModel(Main, false, Linux, () => { s.FsrLicense = true; s.Fsr = true; Save(); RaiseUpscalers(); },
                                                  RaiseUpscalers);
                Raise();
                return;
            }
            s.Fsr = value;
            Save();
            RaiseUpscalers();
        }
    }

    public bool DlssAccepted => s.Dlss;
    public string DlssLine => s.Dlss ? "" : s.DlssLicense ? "Licence accepted earlier." : "Shows NVIDIA's licence first.";
    public bool FsrAccepted => s.Fsr && Windows;
    public string FsrLine => Linux ? "" : s.Fsr ? "" : s.FsrLicense ? "Licence accepted earlier." : "Shows AMD's licence first.";

    void RaiseUpscalers() =>
        RaiseAll(nameof(Dlss), nameof(Fsr), nameof(DlssAccepted), nameof(DlssLine), nameof(FsrAccepted), nameof(FsrLine), nameof(Estimate));

    // ---- install ----

    public string InstallDir => ShownPath.Of(Job.InstallDir);
    public string InstallDirFull => Job.InstallDir;

    async Task ChangeInstallAsync()
    {
        if (Main.Host is not { } host) return;
        var picked = await host.PickFolderAsync("Where to install " + Job.Shown, Path.GetDirectoryName(Job.InstallDir));
        if (picked == null) return;
        // (a folder that holds other things gets the version's own folder in it; an empty one, or a game folder, is used as it is)
        var full = Path.GetFullPath(picked);
        var used = !Directory.Exists(full) || !Directory.EnumerateFileSystemEntries(full).Any() || GameFolder.IsGameFolder(full, Main.Platform)
            ? full : LauncherSettings.FolderFor(full, Job.Version, p => Job.Service.Registry.FindByFolder(p) is { } e && e.Id != Job.Replaces?.Id);
        Job.InstallDir = used;
        RaiseAll(nameof(InstallDir), nameof(InstallDirFull));
        Measure();
    }

    /// The free space where the game goes (null: unknown, taken as enough)
    public long? Free
    {
        get => free;
        set
        {
            free = value;
            RaiseAll(nameof(Free), nameof(Short), nameof(MeterText), nameof(MeterFraction), nameof(Estimate), nameof(SpaceWarning), nameof(ShowEstimate));
            StartCommand.Changed();
        }
    }

    public string Drive => Disk.DriveName(Job.InstallDir);
    void Measure() => Free = Disk.FreeBytes(Job.InstallDir);

    public bool Short => free is long f && f < Disk.BuildBytes;
    public double MeterFraction => free is long f && f > 0 ? Math.Min(1, Disk.BuildBytes / (double)f) : 0;
    public string MeterText => free is not long f ? $"Needs {Disk.BuildBytes >> 30} GB"
        : Short ? $"Needs {Disk.BuildBytes >> 30} GB, only {f >> 30} GB free on {Drive}"
        : $"Needs {Disk.BuildBytes >> 30} GB, {f >> 30} GB free on {Drive}";

    // ---- the game ----

    public IReadOnlyList<LanguageChoice> LanguageChoices { get; }

    public LanguageChoice Language
    {
        get => LanguageChoices.FirstOrDefault(l => l.Code == Languages.Normalize(s.Language)) ?? LanguageChoices[0];
        set
        {
            if (value == null) return;
            s.Language = value.Code;
            Save();
            Raise();
        }
    }

    public bool Prepare
    {
        get => s.PrepareShaders;
        set
        {
            s.PrepareShaders = value;
            Save();
            RaiseAll(nameof(Prepare), nameof(PrepareDesc), nameof(Estimate));
        }
    }

    public string PrepareDesc => s.PrepareShaders ? "Adds about 8 min here, so the game starts smoothly." : "The game prepares them at its first start.";

    // ---- advanced ----

    public bool AdvancedOpen
    {
        get => Job.AdvancedOpen;
        set
        {
            Job.AdvancedOpen = value;
            RaiseAll(nameof(AdvancedOpen), nameof(AdvancedDesc));
        }
    }

    public IReadOnlyList<Choice> MemoryChoices { get; }
    public IReadOnlyList<Choice> WorkerChoices { get; }

    public Choice Memory
    {
        get => MemoryChoices.FirstOrDefault(c => c.Value == s.MemoryGb) ?? MemoryChoices[0];
        set
        {
            if (value == null) return;
            s.MemoryGb = value.Value;
            Save();
            RaiseAll(nameof(Memory), nameof(MemoryDesc), nameof(AdvancedDesc));
        }
    }

    public Choice Workers
    {
        get => WorkerChoices.FirstOrDefault(c => c.Value == s.BuildWorkers) ?? WorkerChoices[0];
        set
        {
            if (value == null) return;
            s.BuildWorkers = value.Value;
            Save();
            RaiseAll(nameof(Workers), nameof(WorkersDesc), nameof(AdvancedDesc));
        }
    }

    // (make_game's automatic: the compilers take what memory is free (nx2/build.py MemoryGate); one per CPU thread, as it allows)
    public string MemoryDesc => s.MemoryGb == null ? $"Whatever is free of {MemoryGb} GB, leaving room for your other programs."
        : "The compilers may use this much, even if other programs need it.";
    public string WorkersDesc => s.BuildWorkers == null ? $"{Threads} of {Threads} at once, as the memory allows."
        : "Compilers running at once. Each needs about 2 GB.";
    public string Work => ShownPath.Of(Job.Service.Work);
    public string WorkFull => Job.Service.Work;

    public string AdvancedDesc => AdvancedOpen ? "For a PC that slows down or runs out of memory while building."
        : "Memory " + (s.MemoryGb is int m ? $"{m} GB" : $"automatic of {MemoryGb} GB") + ", " +
          (s.BuildWorkers is int w ? $"{w} workers" : $"{Threads} of {Threads} workers (automatic)") + ", build files on " + Disk.DriveName(WorkFull);

    async Task ChangeWorkAsync()
    {
        if (Main.Host is not { } host) return;
        var picked = await host.PickFolderAsync("Where the build files go", WorkFull);
        if (picked == null) return;
        s.Work = Path.GetFullPath(picked);
        Save();
        RaiseAll(nameof(Work), nameof(WorkFull), nameof(AdvancedDesc));
    }

    // ---- the footer ----

    public bool ShowEstimate => !Short;
    public string SpaceWarning => $"{Drive} is too full for the build. Change the game folder.";
    // (the 2.x Builders' "about 45 minutes", the shaders' share of it about 8)
    public string Estimate => s.PrepareShaders ? "About 45 min, shaders included" : "About 37 min";
    public string StartText => Job.Verb + Job.Shown;
    public bool IsUpdate => Job.Kind == JobKind.Update;

    public Command BackCommand { get; }
    public Command StartCommand { get; }
    public Command ChangeInstallCommand { get; }
    public Command ChangeWorkCommand { get; }
    public Command AdvancedCommand { get; }

    void Start()
    {
        if (Short || Job.Picked == null || Job.Tools == null) return;
        var file = Job.Picked;
        // (the game files are remembered for the next build)
        if (file.Folders && file.Source is BuildSource.Folders f)
        {
            s.Nup = null;
            s.Romfs = f.Romfs;
            s.Exefs = f.Exefs;
        }
        else
        {
            s.Nup = file.Path;
            s.Romfs = s.Exefs = null;
        }
        Save();
        Main.Navigate(new BuildStepViewModel(Job));
    }
}

/// NVIDIA's licence for DLSS, or AMD's for FSR 4, shown when the switch is turned on: Accept keeps it on (and is remembered)
public sealed class LicenceViewModel : ViewModelBase
{
    readonly Action accepted, declined;

    public LicenceViewModel(MainViewModel main, bool nvidia, bool linux, Action accepted, Action declined)
    {
        Main = main;
        Nvidia = nvidia;
        this.accepted = accepted;
        this.declined = declined;
        Text = nvidia ? DlssText(linux) : FsrText;
        AcceptCommand = new Command(() => { Main.Modal = null; this.accepted(); });
        DeclineCommand = new Command(() => { Main.Modal = null; this.declined(); });
    }

    public MainViewModel Main { get; }
    public bool Nvidia { get; }
    public string Title => Nvidia ? "NVIDIA's licence for DLSS" : "AMD's licence for FSR 4";
    public string Sub => (Nvidia ? "The build downloads NVIDIA's DLSS SDK" : "The build downloads AMD's FSR 4 runtime") + " under this licence. Nothing of " +
                         (Nvidia ? "NVIDIA's" : "AMD's") + " comes with Hookshot.";
    public string Text { get; }
    public Command AcceptCommand { get; }
    public Command DeclineCommand { get; }

    // (the 2.x Builders' texts: builder/HookshotBuilder/MainWindow.xaml.cs DlssLicense and FsrLicense, builder/linux DLSS_LICENSE)
    static string DlssText(bool linux) =>
        "NVIDIA DLSS is NVIDIA's, not Hookshot's. With it on, the build downloads NVIDIA's DLSS SDK (release v310.9.16) from " +
        "github.com/NVIDIA/DLSS, under NVIDIA's own license, the NVIDIA RTX SDKs license:\n\n" +
        "https://github.com/NVIDIA/DLSS/blob/9981c6350dc58c0ce21f024fd65e31e851c31689/LICENSE.txt\n\n" +
        "Its files stay on this PC (in the launcher's ngx-sdk folder, and " +
        (linux ? "libnvidia-ngx-dlss.so and libnvidia-ngx-dlssg.so" : "nvngx_dlss.dll and nvngx_dlssg.dll") + " in the game folder's ngx folder). " +
        "Hookshot never includes or shares them: never share a game folder built with them.";

    const string FsrText =
        "AMD FSR 4 is AMD's, not Hookshot's. With it on, the build downloads AMD's FSR runtime for Vulkan (FidelityFX SDK " +
        "v2.4.0: amd_fidelityfx_loader_vk.dll and amd_fidelityfx_upscaler_vk.dll, signed by AMD) from " +
        "github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK, each file checked against its SHA-256, under AMD's FSR SDK license:\n\n" +
        "https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/blob/7539ae8b6568ad29ed78a753404351daf3b5cca7/Kits/FidelityFX/docs/license.md\n\n" +
        "Its files stay on this PC (in the launcher's ffx-sdk folder, and the two DLLs with LICENSE-AMD-FSR-SDK.md in the game folder).";
}
