using System.Collections.ObjectModel;
using HookshotLauncher.Core;

namespace HookshotLauncher.ViewModels;

/// One place saves were found: a build's (or, on Linux, every build's), or an emulator's
public sealed class SaveSetViewModel
{
    public SaveSetViewModel(SavesViewModel page, SaveSet set, BuildEntry? build)
    {
        Set = set;
        Build = build;
        Title = build != null ? build.Name : set.Display;
        Where = set.Folder ?? "";
        Summary = set.Found
            ? string.Join("\n", set.Profiles.Select(p =>
              {
                  var slots = Saves.Describe(p.Path);
                  var text = slots.Count == 0 ? "no slots"
                      : string.Join(", ", slots.Select(s => s.Saved is { } t ? $"{s.Name} ({t:yyyy-MM-dd HH:mm})" : s.Name));
                  return $"{p.Id}: {text}{(Saves.PendingCommit(p.Path) != null ? " · a save is waiting in save-tx" : "")}";
              }))
            : set.Problem ?? "Nothing found";
        IsEmulator = build == null && set.Owner != "hookshot";
        IsOtherBuild = build != null && page.Main.Service.Platform == Platform.Windows;
        ImportCommand = new Command(() => page.Import(this), () => set.Found);
        ExportCommand = new Command(() => page.Export(this));
        CopyFromCommand = new Command(() => page.CopyFrom(this), () => set.Found);
    }

    public SaveSet Set { get; }
    public BuildEntry? Build { get; }
    public string Title { get; }
    public string Where { get; }
    public string Summary { get; }
    public bool IsEmulator { get; }
    public bool IsOtherBuild { get; }
    public Command ImportCommand { get; }
    public Command ExportCommand { get; }
    public Command CopyFromCommand { get; }
}

/// Saves (a placeholder page): what was found where, and the copies Core makes (Saves: a backup first, refused while a game
/// runs or NX2navi has a save waiting). Import and export are for the build chosen at the top
public sealed class SavesViewModel : ViewModelBase
{
    BuildEntry? target;
    string message = "";
    bool keepSettings;

    public SavesViewModel(MainViewModel main)
    {
        Main = main;
        BackCommand = new Command(Main.ShowHome);
    }

    public MainViewModel Main { get; }
    public ObservableCollection<BuildEntry> Builds { get; } = new();
    public ObservableCollection<SaveSetViewModel> Sets { get; } = new();
    public Command BackCommand { get; }

    public BuildEntry? Target
    {
        get => target;
        set
        {
            if (Set(ref target, value)) Reload();
        }
    }

    public bool KeepSettings
    {
        get => keepSettings;
        set => Set(ref keepSettings, value);
    }

    public string Message
    {
        get => message;
        set => Set(ref message, value);
    }

    public string Intro => Main.Service.Platform == Platform.Windows
        ? "Each game folder keeps its own saves. Import an emulator's save into the build chosen here, export it, or copy another build's."
        : "Every build shares one save folder. Import an emulator's save into it, or export it to an emulator.";

    SaveEnvironment Env => SaveEnvironment.Current() with { Platform = Main.Service.Platform };

    public void Load(BuildEntry? build)
    {
        Builds.Clear();
        foreach (var b in Main.Service.Registry.Builds) Builds.Add(b);
        target = build ?? Main.Service.Registry.Default;
        Raise(nameof(Target));
        Message = "";
        Reload();
    }

    void Reload()
    {
        Sets.Clear();
        var env = Env;
        if (env.Platform == Platform.Windows)
        {
            foreach (var b in Main.Service.Registry.Builds)
                if (b.Id != target?.Id) Sets.Add(new SaveSetViewModel(this, Saves.ForBuild(b.Folder, env), b));
        }
        foreach (var set in Saves.Emulator(env)) Sets.Add(new SaveSetViewModel(this, set, null));
        Raise(nameof(TargetSummary));
    }

    public string TargetSummary
    {
        get
        {
            if (target == null) return "No build yet.";
            var set = Saves.ForBuild(target.Folder, Env);
            return set.Found ? $"{set.Profiles.Count} profile(s) in {set.Profiles[0].Path}" : set.Problem ?? "";
        }
    }

    void Run(Func<SaveCopy> copy)
    {
        try
        {
            var done = copy();
            Message = $"Copied {done.Slots.Count} slot(s) into {done.Target}." +
                      (done.Backup != null ? $" The save it replaced is backed up in {done.Backup}." : "");
        }
        catch (Exception e) when (e is SaveFailure or IOException or UnauthorizedAccessException)
        {
            Message = e.Message;
        }
        Reload();
    }

    SaveProgram ProgramOf(SaveSetViewModel row) => Saves.Named(row.Set.Owner);

    public void Import(SaveSetViewModel row)
    {
        if (target is not { } b) return;
        Run(() => Saves.ImportExport(true, b.Folder, ProgramOf(row), row.Set.Folder, Env, keepSettings: KeepSettings));
    }

    public void Export(SaveSetViewModel row)
    {
        if (target is not { } b) return;
        Run(() => Saves.ImportExport(false, b.Folder, ProgramOf(row), row.Set.Folder, Env, keepSettings: KeepSettings));
    }

    public void CopyFrom(SaveSetViewModel row)
    {
        if (target is not { } b || row.Build is not { } from) return;
        Run(() => Saves.BetweenBuilds(from.Folder, b.Folder, Env, KeepSettings));
    }
}
