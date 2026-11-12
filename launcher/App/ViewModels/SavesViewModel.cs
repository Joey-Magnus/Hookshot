using HookshotLauncher.Core;

namespace HookshotLauncher.ViewModels;

public enum SaveMode { Copy, Import, Export }

/// A row of the Saves page's list: a group's title, a build's saves, or an emulator's
public sealed class SaveRowViewModel
{
    public bool IsGroup { get; init; }
    public bool IsRow => !IsGroup;
    public string Name { get; init; } = "";
    public string Where { get; init; } = "";
    public string Summary { get; init; } = "";
    public string? Channel { get; init; }
    public bool IsRelease => Channel == Versions.Release;
    public bool IsCandidate => Channel == Versions.Candidate;
    public bool IsNightly => Channel == Versions.Nightly;
    public bool HasChannel => Channel != null;
    public bool IsThis { get; init; }
    public bool Shared { get; init; }
    public bool Dim { get; init; }
    public string? Profiles { get; init; }
    public bool HasProfiles => Profiles != null;
    public bool Waiting { get; init; }
    public bool First { get; init; }
    public bool AfterGroup { get; set; }
    public Avalonia.Thickness Line => AfterGroup ? default : new Avalonia.Thickness(0, 1, 0, 0);
}

/// A save slot of the build on the right
public sealed record SlotViewModel(int Number, string Name, string Saved);

static class SaveViews
{
    /// A long path shortened as the mockup does: its first part, …, its last two
    public static string Short(string p)
    {
        p = ShownPath.Of(p);
        if (p.Length <= 34) return p;
        var sep = p.Contains('/') ? '/' : '\\';
        var parts = p.Split(sep);
        return parts.Length <= 3 ? p : parts[0] + sep + "…" + sep + string.Join(sep, parts[^2..]);
    }

    public static IReadOnlyList<SlotViewModel> Slots(IReadOnlyList<SaveSlot> slots) =>
        slots.Select(s => new SlotViewModel(SaveText.Number(s.Name), "Slot " + SaveText.Number(s.Name),
                                            s.Saved is { } t ? Durations.When(t, DateTime.Now) : "")).ToList();

    public static IReadOnlyList<SaveSlot> Describe(SaveProfile? p) => p == null ? Array.Empty<SaveSlot>() : Saves.Describe(p.Path);
}

/// Saves: one build's slots, every save found on this PC (the builds' and the emulators'), and the copies: from another build
/// (Windows), import from and export to an emulator or another folder. Each copy is confirmed first and backs up what it replaces
public sealed class SavesViewModel : ViewModelBase
{
    BuildEntry target;

    public SavesViewModel(MainViewModel main, BuildEntry? build)
    {
        Main = main;
        target = build ?? main.Service.Registry.Default ?? main.Service.Registry.Builds[0];
        Builds = main.Service.Registry.Builds.ToList();
        CopyCommand = new Command(() => Main.Modal = new SavePickViewModel(this, SaveMode.Copy), () => OthersHaveSaves);
        ImportCommand = new Command(() => Main.Modal = new SavePickViewModel(this, SaveMode.Import));
        ExportCommand = new Command(() => Main.Modal = new SavePickViewModel(this, SaveMode.Export), () => Slots.Count > 0);
        DoneCommand = new Command(Main.ShowHome);
        Reload();
    }

    public MainViewModel Main { get; }
    LauncherService Service => Main.Service;
    public SaveEnvironment Env => Service.SaveEnv;
    public bool Linux => Main.Linux;
    public bool Windows => !Main.Linux;
    public IReadOnlyList<BuildEntry> Builds { get; }
    public IReadOnlyList<SaveRowViewModel> Rows { get; private set; } = Array.Empty<SaveRowViewModel>();
    public IReadOnlyList<SlotViewModel> Slots { get; private set; } = Array.Empty<SlotViewModel>();
    public IReadOnlyList<SaveSet> Emulators { get; private set; } = Array.Empty<SaveSet>();
    public bool HasSlots => Slots.Count > 0;
    public bool NoSlots => Slots.Count == 0;

    public BuildEntry Target
    {
        get => target;
        set
        {
            if (value == null || !Set(ref target, value)) return;
            Reload();
        }
    }

    public string Sub => Linux ? "On Linux every build plays from the same saves." : "Each build keeps its own saves, in its game folder.";
    /// (the build is named by "Saves of" above it: a version upper-cased would read badly, a nightly's commit most of all)
    public string ThisLabel => Linux ? "EVERY BUILD'S SLOTS" : "SLOTS";
    public bool OthersHaveSaves => Windows && Builds.Any(b => b.Id != target.Id && Service.SavesOf(b).Count > 0);

    public Command CopyCommand { get; }
    public Command ImportCommand { get; }
    public Command ExportCommand { get; }
    public Command DoneCommand { get; }

    public void Reload()
    {
        var now = DateTime.Now;
        var rows = new List<SaveRowViewModel> { new() { IsGroup = true, Name = "Hookshot", First = true } };
        if (Linux)
        {
            var set = Saves.ForBuild(target.Folder, Env);
            rows.Add(new SaveRowViewModel
            {
                Name = "Every build", Shared = true, IsThis = true,
                Where = SaveViews.Short(set.Found ? set.Profiles[0].Path : Path.Combine(Env.Data, "hookshot", "save")),
                Summary = SaveText.Long(Service.SavesOf(target), now),
            });
        }
        else
        {
            foreach (var b in Builds)
            {
                var set = Saves.ForBuild(b.Folder, Env);
                rows.Add(new SaveRowViewModel
                {
                    Name = b.Version is { } v ? Versions.Shown(v) : b.Name, Channel = UpdatePolicy.ChannelOf(b.Channel, b.Version), IsThis = b.Id == target.Id,
                    Where = SaveViews.Short(set.Found ? set.Profiles[0].Path : Path.Combine(b.Folder, "user", "save")),
                    Summary = SaveText.Long(Service.SavesOf(b), now),
                });
            }
        }
        rows.Add(new SaveRowViewModel { IsGroup = true, Name = "Emulators and ports" });
        Emulators = Saves.Emulator(Env);
        foreach (var e in Emulators)
        {
            if (!e.Found)
            {
                rows.Add(new SaveRowViewModel { Name = e.Display.Replace(" / ", ", "), Where = "Not found on this PC", Dim = true });
                continue;
            }
            rows.Add(new SaveRowViewModel
            {
                Name = e.Display.Replace(" / ", ", "),
                Where = SaveViews.Short(e.Folder ?? ""),
                Profiles = e.Profiles.Count > 1 ? $"{e.Profiles.Count} profiles" : null,
                Waiting = e.Profiles.Any(p => Saves.PendingCommit(p.Path) != null),
                Summary = SaveText.Long(SaveViews.Describe(e.Profiles[0]), now),
            });
        }
        for (var i = 1; i < rows.Count; i++) rows[i].AfterGroup = rows[i - 1].IsGroup;
        Rows = rows;
        Slots = SaveViews.Slots(Service.SavesOf(target));
        RaiseAll(nameof(Rows), nameof(Slots), nameof(HasSlots), nameof(NoSlots), nameof(ThisLabel), nameof(OthersHaveSaves));
        CopyCommand.Changed();
        ExportCommand.Changed();
    }
}

/// One side of a copy: a build, an emulator's profile, or a folder
public sealed record SaveSide(string Name, string Where, IReadOnlyList<SaveSlot> Slots, string? Path, bool Emulator);

/// The copy being set up: which way, which build, from or to what
public sealed class SaveOp
{
    public SaveMode Mode { get; init; }
    public BuildEntry Target { get; init; } = null!;
    public BuildEntry? SourceBuild { get; set; }
    public SaveSet? Set { get; set; }
    public string? Folder { get; set; }
    public string? Profile { get; set; }
    public bool KeepSettings { get; set; }
    public string Verb => Mode switch { SaveMode.Copy => "Copy", SaveMode.Import => "Import", _ => "Export" };
    public SaveProgram Program => Set != null ? Saves.Named(Set.Owner) : Saves.Folder;
    public string OtherName => Set != null ? Set.Display.Replace(" / ", ", ") : "the folder";
}

/// Import or export: which emulator (or another folder), and which profile when it has several; or which build to copy from
public sealed class SavePickViewModel : ViewModelBase
{
    public sealed class Option(SavePickViewModel owner, string key, string name, string about, string right, bool enabled, string? channel = null,
                               bool waiting = false) : ViewModelBase
    {
        public string Key => key;
        public string Name => name;
        public string About => about;
        public bool HasAbout => about.Length > 0;
        public string Right => right;
        public bool Enabled => enabled;
        public bool Dim => !enabled;
        public string? Channel => channel;
        public bool HasChannel => channel != null;
        public bool IsRelease => channel == Versions.Release;
        public bool IsCandidate => channel == Versions.Candidate;
        public bool IsNightly => channel == Versions.Nightly;
        public bool Waiting => waiting;

        public bool IsPicked
        {
            get => owner.Pick == key;
            set
            {
                if (value) owner.Pick = key;
            }
        }

        public void Changed() => Raise(nameof(IsPicked));
    }

    public sealed class ProfileOption(SavePickViewModel owner, SaveProfile p, string summary) : ViewModelBase
    {
        public string Id => p.Id;
        public string Name => "Profile " + (p.Id.Length > 16 ? p.Id[..8] + "…" + p.Id[^4..] : p.Id);
        public string Summary => summary;

        public bool IsPicked
        {
            get => owner.Profile == p.Id;
            set
            {
                if (value) owner.Profile = p.Id;
            }
        }

        public void Changed() => Raise(nameof(IsPicked));
    }

    string? pick, profile;
    IReadOnlyList<ProfileOption> profiles = Array.Empty<ProfileOption>();

    public SavePickViewModel(SavesViewModel page, SaveMode mode)
    {
        Page = page;
        Mode = mode;
        var now = DateTime.Now;
        var main = page.Main;
        if (mode == SaveMode.Copy)
        {
            Options = page.Builds.Where(b => b.Id != page.Target.Id).Select(b =>
            {
                var slots = main.Service.SavesOf(b);
                var set = Saves.ForBuild(b.Folder, page.Env);
                return new Option(this, b.Id, b.Version is { } v ? Versions.Shown(v) : b.Name, SaveViews.Short(set.Found ? set.Profiles[0].Path : b.Folder),
                                  SaveText.Long(slots, now), slots.Count > 0, UpdatePolicy.ChannelOf(b.Channel, b.Version));
            }).ToList();
        }
        else
        {
            Options = page.Emulators.Select(e => new Option(this, e.Owner, e.Display.Replace(" / ", ", "), e.Found ? "" : "Not found on this PC",
                    !e.Found ? "" : e.Profiles.Count > 1 ? $"{e.Profiles.Count} profiles" : SaveText.Long(SaveViews.Describe(e.Profiles[0]), now), e.Found,
                    waiting: e.Profiles.Any(p => Saves.PendingCommit(p.Path) != null)))
                .Append(new Option(this, "folder", "Another folder…",
                                   mode == SaveMode.Import ? "A folder holding account.sav and slot folders, or one above it"
                                       : "A portable emulator's user folder, or any folder", "", true))
                .ToList();
        }
        GoCommand = new Command(() => _ = GoAsync(), () => CanGo);
        Pick = Options.FirstOrDefault(o => o.Enabled)?.Key;
        CancelCommand = new Command(() => main.Modal = null);
    }

    public SavesViewModel Page { get; }
    public SaveMode Mode { get; }
    public IReadOnlyList<Option> Options { get; }
    public Command GoCommand { get; }
    public Command CancelCommand { get; }

    public string Title => Mode switch
    {
        SaveMode.Copy => "Copy saves from which build?",
        SaveMode.Import => "Import from which emulator?",
        _ => "Export to which emulator?",
    };

    public string Sub => Mode == SaveMode.Export
        ? $"The saves of {(Page.Linux ? "every build" : Versions.Shown(Page.Target.Version))} replace the ones there, after a backup."
        : $"Its saves replace {(Page.Linux ? "the ones every build uses" : Versions.Shown(Page.Target.Version) + "'s")}, after a backup.";

    SaveSet? PickedSet => Page.Emulators.FirstOrDefault(e => e.Owner == pick);

    public string? Pick
    {
        get => pick;
        set
        {
            if (!Set(ref pick, value)) return;
            foreach (var o in Options) o.Changed();
            var set = PickedSet;
            Profiles = set is { Profiles.Count: > 1 }
                ? set.Profiles.Select(p => new ProfileOption(this, p, SaveText.Long(Saves.Describe(p.Path), DateTime.Now))).ToList()
                : Array.Empty<ProfileOption>();
            profile = set is { Profiles.Count: 1 } ? set.Profiles[0].Id : null;
            RaiseAll(nameof(Profile), nameof(CanGo));
            GoCommand.Changed();
        }
    }

    public IReadOnlyList<ProfileOption> Profiles
    {
        get => profiles;
        private set
        {
            profiles = value;
            RaiseAll(nameof(Profiles), nameof(HasProfiles), nameof(ProfileHint));
        }
    }

    public bool HasProfiles => profiles.Count > 0;
    public string ProfileHint => PickedSet is { } s ? $"{s.Display.Replace(" / ", ", ")} has saves in {s.Profiles.Count} profiles. Pick the one you play on." : "";

    public string? Profile
    {
        get => profile;
        set
        {
            if (!Set(ref profile, value)) return;
            foreach (var p in profiles) p.Changed();
            Raise(nameof(CanGo));
            GoCommand.Changed();
        }
    }

    public bool CanGo => pick != null && (PickedSet is not { Profiles.Count: > 1 } || profile != null);

    async Task GoAsync()
    {
        var main = Page.Main;
        var op = new SaveOp { Mode = Mode, Target = Page.Target };
        if (Mode == SaveMode.Copy) op.SourceBuild = Page.Builds.First(b => b.Id == pick);
        else if (pick == "folder")
        {
            if (main.Host is not { } host) return;
            var folder = await host.PickFolderAsync(Mode == SaveMode.Import ? "The folder with the saves" : "The folder to export to", null);
            if (folder == null) return;
            op.Folder = folder;
        }
        else
        {
            op.Set = PickedSet;
            op.Folder = PickedSet?.Folder;
            op.Profile = profile;
        }
        main.Modal = null;
        main.Navigate(new SavesConfirmViewModel(Page, op));
    }
}

/// What a copy replaces, kept settings (option.sav), where the backup goes; nothing changes until the verb is pressed
public sealed class SavesConfirmViewModel : ViewModelBase
{
    public SavesConfirmViewModel(SavesViewModel page, SaveOp op)
    {
        Page = page;
        Op = op;
        BackCommand = new Command(() => page.Main.Navigate(page));
        RunCommand = new Command(Run);
        Refresh();
    }

    public SavesViewModel Page { get; }
    public SaveOp Op { get; }
    LauncherService Service => Page.Main.Service;
    public SaveSide From { get; private set; } = null!;
    public SaveSide To { get; private set; } = null!;
    public IReadOnlyList<SlotViewModel> FromSlots => SaveViews.Slots(From.Slots);
    public IReadOnlyList<SlotViewModel> ToSlots => SaveViews.Slots(To.Slots);
    public bool FromEmpty => From.Slots.Count == 0;
    public bool ToEmpty => To.Slots.Count == 0;
    public string FromWhere => SaveViews.Short(From.Where);
    public string ToWhere => SaveViews.Short(To.Where);
    public string? Backup { get; private set; }
    public string? Problem { get; private set; }

    public string Title => Op.Mode switch
    {
        SaveMode.Copy => "Copy saves from " + From.Name,
        SaveMode.Import => "Import saves from " + From.Name,
        _ => "Export saves to " + To.Name,
    };

    public string Sub => $"Check what gets replaced. Nothing changes until you choose {Op.Verb}.";
    public string Verb => Op.Verb;

    public string Replaced => $"Replaced in {To.Name}: " + (To.Slots.Count == 0 ? "nothing saved yet" : SaveText.Long(To.Slots, DateTime.Now)) +
                              ", its account.sav" + (Op.KeepSettings ? "" : ", and its game settings (option.sav)") + ".";

    public string KeepLabel => "Keep " + (Op.Mode == SaveMode.Export ? To.Name + "'s" : Page.Linux ? "Hookshot's" : "this build's") +
                               " game settings (option.sav)";

    public bool KeepSettings
    {
        get => Op.KeepSettings;
        set
        {
            Op.KeepSettings = value;
            RaiseAll(nameof(KeepSettings), nameof(Replaced));
        }
    }

    public string BackupLine => Backup != null ? "Backup first, to " + ShownPath.Of(Backup) : Problem ?? "Nothing there yet to back up.";
    public string CloseFirst => "Close the game" + (Op.Set != null ? " and " + (Op.Set.Display.Contains('/') ? "the emulator" : Op.OtherName) : "") + " first.";

    public Command BackCommand { get; }
    public Command RunCommand { get; }

    SaveSide BuildSide(BuildEntry b, string? profile)
    {
        var set = Saves.ForBuild(b.Folder, Page.Env);
        var path = set.Found ? set.Profiles[0].Path : profile != null ? Saves.BuildSavePath(b.Folder, profile) : null;
        var name = Page.Linux ? "Hookshot" : b.Version is { } v ? Versions.Shown(v) : b.Name;
        return new SaveSide(name, path ?? Path.Combine(b.Folder, "user", "save"), set.Found ? Saves.Describe(set.Profiles[0].Path) : Array.Empty<SaveSlot>(), path, false);
    }

    void Refresh()
    {
        Problem = null;
        if (Op.Mode == SaveMode.Copy)
        {
            var src = BuildSide(Op.SourceBuild!, null);
            var srcProfile = src.Path != null ? Path.GetFileName(src.Path) : null;
            From = src;
            To = BuildSide(Op.Target, srcProfile);
        }
        else
        {
            SaveSide other;
            try
            {
                var (folder, found) = Saves.Locate(Op.Program, Op.Folder, Page.Env);
                var p = Saves.Pick(found, Op.OtherName, Op.Profile);
                other = new SaveSide(Op.OtherName, p.Path + (found.Count > 1 ? $" (profile {p.Id[..Math.Min(8, p.Id.Length)]}…)" : ""), Saves.Describe(p.Path), p.Path, true);
            }
            catch (SaveFailure e)
            {
                Problem = e.Message;
                other = new SaveSide(Op.OtherName, Op.Folder ?? "", Array.Empty<SaveSlot>(), null, true);
            }
            var me = BuildSide(Op.Target, null);
            (From, To) = Op.Mode == SaveMode.Import ? (other, me) : (me, other);
        }
        // (where the backup goes: the dry run of the same copy)
        if (From.Path != null && To.Path != null)
        {
            try
            {
                var (command, name) = Op.Mode == SaveMode.Copy ? ("build", Path.GetFileName(Path.TrimEndingDirectorySeparator(Op.Target.Folder)))
                    : (Op.Mode == SaveMode.Import ? "import" : "export", Op.Program.Name);
                Backup = Saves.Copy(From.Path, To.Path, Page.Env.BackupRoot, command, name, Op.KeepSettings, dryRun: true, force: true).Backup;
            }
            catch (Exception e) when (e is SaveFailure or IOException or UnauthorizedAccessException) { Problem = e.Message; }
        }
    }

    void Run()
    {
        var main = Page.Main;
        try
        {
            SaveCopy done = Op.Mode == SaveMode.Copy
                ? Saves.BetweenBuilds(Op.SourceBuild!.Folder, Op.Target.Folder, Page.Env, Op.KeepSettings)
                : Saves.ImportExport(Op.Mode == SaveMode.Import, Op.Target.Folder, Op.Program, Op.Folder, Page.Env, Op.Profile, Op.KeepSettings);
            main.Navigate(new SavesResultViewModel(this, SaveResult.Done, done.Backup, null));
        }
        catch (SaveFailure e)
        {
            var kind = e.Reason switch { SaveRefusal.Running => SaveResult.Running, SaveRefusal.Waiting => SaveResult.Waiting, _ => SaveResult.Failed };
            main.Navigate(new SavesResultViewModel(this, kind, null, e.Message, e.Who));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            main.Navigate(new SavesResultViewModel(this, SaveResult.Failed, null, e.Message));
        }
    }

    public void Retry() => Run();

    /// After the copy: the target's saves again
    public IReadOnlyList<SaveSlot> TargetSlotsNow() => To.Path != null ? Saves.Describe(To.Path) : Array.Empty<SaveSlot>();
}

public enum SaveResult { Done, Running, Waiting, Failed }

/// The copy's outcome: done (with the backup one click away), or refused, with nothing changed
public sealed class SavesResultViewModel : ViewModelBase
{
    public SavesResultViewModel(SavesConfirmViewModel confirm, SaveResult kind, string? backup, string? message, IReadOnlyList<string>? who = null)
    {
        Confirm = confirm;
        Kind = kind;
        Backup = backup;
        Message = message;
        Who = who ?? Array.Empty<string>();
        var main = confirm.Page.Main;
        BackCommand = new Command(() =>
        {
            confirm.Page.Reload();
            main.Navigate(confirm.Page);
        });
        RetryCommand = new Command(confirm.Retry);
        BackupCommand = new Command(() => { if (Backup != null) main.Host?.Open(Backup); });
    }

    public SavesConfirmViewModel Confirm { get; }
    public SaveResult Kind { get; }
    public string? Backup { get; }
    public string? Message { get; }
    public IReadOnlyList<string> Who { get; }
    public bool Done => Kind == SaveResult.Done;
    public bool Refused => !Done;
    public bool HasBackup => Backup != null;

    public string Heading => Kind switch
    {
        SaveResult.Done => Confirm.Op.Mode switch
        {
            SaveMode.Copy => "Saves copied from " + Confirm.From.Name,
            SaveMode.Import => "Saves imported from " + Confirm.From.Name,
            _ => "Saves exported to " + Confirm.To.Name,
        },
        SaveResult.Running => "Close the game first",
        SaveResult.Waiting => "NX2navi has a save waiting",
        _ => "The saves were not copied",
    };

    public string Sub => Kind switch
    {
        SaveResult.Done => $"{Confirm.To.Name} now has {SaveText.Long(Confirm.TargetSlotsNow(), DateTime.Now)}" +
                           (Confirm.Op.KeepSettings ? ", with its own game settings kept." : "."),
        SaveResult.Running => $"{(Who.Count > 0 ? string.Join(" and ", Who) : "A game")} is running. A running game can write its save again when it quits, " +
                              "and undo the copy. Close it, then try again.",
        SaveResult.Waiting => "NX2navi writes a save to profile/save-tx first and moves it into place at its next start; until then that is the save it " +
                              "loads. Start NX2navi and quit it, then try again.",
        _ => Message ?? "",
    };

    public string Faint => Done ? (HasBackup ? "The saves it replaced are backed up." : "There was nothing there to back up.") : "Nothing was changed.";

    public Command BackCommand { get; }
    public Command RetryCommand { get; }
    public Command BackupCommand { get; }
}
