using HookshotLauncher.Core;

namespace HookshotLauncher.ViewModels;

public sealed record Choice(int? Value, string Label);

public sealed record LanguageChoice(string Code, string Name);

/// The settings the wizard remembers (LauncherSettings), saved when the page is left
public sealed class SettingsViewModel : ViewModelBase
{
    LauncherSettings s = new();

    public SettingsViewModel(MainViewModel main)
    {
        Main = main;
        BackCommand = new Command(Save);
        var totalGb = (int)Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (double)(1L << 30));
        // (BuildMemory.Choices and BuildWorkers.Choices of the 2.x Builder: Automatic, then every 4 GB, every CPU thread)
        MemoryChoices = new[] { new Choice(null, "Automatic") }
            .Concat(Enumerable.Range(1, Math.Max(1, (totalGb + 3) / 4)).Select(i => new Choice(i * 4, $"{i * 4} GB"))).ToList();
        WorkerChoices = new[] { new Choice(null, "Automatic") }
            .Concat(Enumerable.Range(1, Math.Max(1, Environment.ProcessorCount)).Select(i => new Choice(i, i.ToString()))).ToList();
        LanguageChoices = Languages.All.Select(l => new LanguageChoice(l.Code, l.Name)).ToList();
    }

    public MainViewModel Main { get; }
    public IReadOnlyList<Choice> MemoryChoices { get; }
    public IReadOnlyList<Choice> WorkerChoices { get; }
    public IReadOnlyList<LanguageChoice> LanguageChoices { get; }
    public Command BackCommand { get; }

    public void Load()
    {
        s = Main.Service.Settings;
        foreach (var n in new[] { nameof(Dlss), nameof(DlssLicense), nameof(Fsr), nameof(FsrLicense), nameof(PrepareShaders), nameof(MoveSaves),
                                  nameof(Memory), nameof(Workers), nameof(Language), nameof(FsrAvailable), nameof(MoveSavesNote) })
            Raise(n);
    }

    void Save()
    {
        Main.Service.SaveSettings();
        Main.ShowHome();
    }

    public bool Dlss
    {
        get => s.Dlss;
        set
        {
            s.Dlss = value;   // (kept only with NVIDIA's license accepted: LauncherSettings.Save)
            Raise();
        }
    }

    public bool DlssLicense
    {
        get => s.DlssLicense;
        set
        {
            s.DlssLicense = value;
            if (!value) s.Dlss = false;
            Raise();
            Raise(nameof(Dlss));
        }
    }

    public bool Fsr
    {
        get => s.Fsr;
        set
        {
            s.Fsr = value;
            Raise();
        }
    }

    public bool FsrLicense
    {
        get => s.FsrLicense;
        set
        {
            s.FsrLicense = value;
            if (!value) s.Fsr = false;
            Raise();
            Raise(nameof(Fsr));
        }
    }

    /// AMD FSR 4 is built on Windows only (the AppImage's Vulkan back end has none)
    public bool FsrAvailable => Main.Service.Platform == Platform.Windows;

    public bool PrepareShaders
    {
        get => s.PrepareShaders;
        set
        {
            s.PrepareShaders = value;
            Raise();
        }
    }

    public bool MoveSaves
    {
        get => s.MoveSaves;
        set
        {
            s.MoveSaves = value;
            Raise();
        }
    }

    public string MoveSavesNote => Main.Service.Platform == Platform.Windows
        ? "Each game folder keeps its own saves on Windows: a new build, or an update, starts from the saves of the build you choose."
        : "On Linux every build already uses the same saves.";

    public Choice Memory
    {
        get => MemoryChoices.FirstOrDefault(c => c.Value == s.MemoryGb) ?? MemoryChoices[0];
        set
        {
            s.MemoryGb = value?.Value;
            Raise();
        }
    }

    public Choice Workers
    {
        get => WorkerChoices.FirstOrDefault(c => c.Value == s.BuildWorkers) ?? WorkerChoices[0];
        set
        {
            s.BuildWorkers = value?.Value;
            Raise();
        }
    }

    public LanguageChoice Language
    {
        get => LanguageChoices.FirstOrDefault(l => l.Code == Languages.Normalize(s.Language)) ?? LanguageChoices[0];
        set
        {
            s.Language = value?.Code;
            Raise();
        }
    }
}
