using HookshotLauncher.Core;

/// launcher/Core/Saves.cs over tests/test_oot_saves.py's vectors (the same folders, the same expectations), so the launcher and
/// scripts/oot_saves.py agree. Fake saves made here only: HOME, XDG_DATA_HOME, APPDATA and LOCALAPPDATA all point into one folder.
static class SavesChecks
{
    public static int Count;
    const string Uid = "0123456789abcdef0123456789abcdef";
    const string Profile = "FEDCBA9876543210FEDCBA9876543210";
    const string Account = "00112233445566778899aabbccddeeff";

    static void Require(bool ok, string message)
    {
        if (!ok) throw new Exception("saves: " + message);
        Count++;
    }

    static void Fails(Action action, string contains, string message)
    {
        try { action(); }
        catch (SaveFailure e)
        {
            Require(e.Message.Contains(contains), $"{message} (said: {e.Message})");
            return;
        }
        throw new Exception("saves: not refused: " + message);
    }

    static string P(params string[] parts) => Path.Combine(parts);

    static void MakeSave(string folder, string tag, params string[] slots)
    {
        if (slots.Length == 0) slots = new[] { "slot_00", "slot_01" };
        Directory.CreateDirectory(folder);
        File.WriteAllText(P(folder, "account.sav"), "account-" + tag);
        File.WriteAllText(P(folder, "option.sav"), "option-" + tag);
        foreach (var s in slots.Where(s => s != "-"))
        {
            Directory.CreateDirectory(P(folder, s));
            File.WriteAllText(P(folder, s, "progress.sav"), "progress-" + tag + s);
            File.WriteAllText(P(folder, s, "caption.sav"), "caption-" + tag + s);
        }
    }

    static string Tree(string folder) =>
        !Directory.Exists(folder) ? "" : string.Join("\n", Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(folder, f) + "=" + File.ReadAllText(f)).Order(StringComparer.Ordinal));

    static string EdenSave(string data, string profile = Profile) => P(data, "nand", "user", "save", Saves.NoAccount, profile, Saves.Title);

    sealed class Case
    {
        public readonly string Tmp, Home, Data, AppData, LocalAppData, Hs, Li, Backups;
        public readonly SaveEnvironment Linux, Windows;
        public IReadOnlyList<string> Busy = Array.Empty<string>();

        public Case(string root, string name)
        {
            Tmp = P(root, name);
            Home = P(Tmp, "home");
            Data = P(Home, ".local", "share");
            AppData = P(Home, "AppData", "Roaming");
            LocalAppData = P(Home, "AppData", "Local");
            Hs = P(Data, "hookshot", "save", Uid);
            Li = EdenSave(P(Data, "eden"));
            Backups = P(Tmp, "backups");
            MakeSave(Hs, "HS");
            MakeSave(Li, "LI", "slot_00");
            string? Env(string k) => k switch
            {
                "HOME" or "USERPROFILE" => Home, "XDG_DATA_HOME" => Data, "APPDATA" => AppData, "LOCALAPPDATA" => LocalAppData, _ => null,
            };
            Linux = new SaveEnvironment(Platform.Linux, Env);
            Windows = new SaveEnvironment(Platform.Windows, Env);
        }

        public IReadOnlyList<string> BackupDirs() =>
            Directory.Exists(Backups) ? Directory.EnumerateDirectories(Backups).Order(StringComparer.Ordinal).ToList() : Array.Empty<string>();

        /// oot_saves.main import|export with the name or folder: Hookshot's side on Linux
        public SaveCopy Run(bool import, string? name, string? folder = null, string? profile = null, bool keep = false, bool dry = false, bool force = false)
        {
            var other = name == null ? Saves.Folder : Saves.Named(name);
            var hs = Saves.Pick(Saves.Locate(Saves.Hookshot, null, Linux).Profiles, "Hookshot");
            var em = Saves.Pick(Saves.Locate(other, folder, Linux).Profiles, other.Display, profile);
            var (src, dst) = import ? (em.Path, hs.Path) : (hs.Path, em.Path);
            return Saves.Copy(src, dst, Backups, import ? "import" : "export", other.Name, keep, dry, force, () => Busy);
        }
    }

    public static void Run(string root)
    {
        Discovery(root);
        Copies(root);
        Nx2navi(root);
        Guard(root);
        Builds(root);
    }

    static void Discovery(string root)
    {
        // (test_every_entry_on_linux)
        var c = new Case(root, "linux");
        MakeSave(EdenSave(P(c.Data, "eglantine")), "EG");
        var nx = P(c.Home, "Games", "Ocarina", "profile", "save", Account);
        MakeSave(nx, "NX");
        var expect = new Dictionary<string, string>
        {
            ["lichii"] = c.Li, ["frizti"] = c.Li, ["orchid"] = c.Li, ["pokedinner"] = c.Li, ["eden"] = c.Li,
            ["eglantine"] = EdenSave(P(c.Data, "eglantine")), ["nx2navi"] = nx,
        };
        Require(expect.Keys.Order().SequenceEqual(Saves.Emulators.Select(p => p.Name).Order()), "Every emulator of the script.");
        foreach (var program in Saves.Emulators)
            Require(Saves.Locate(program, null, c.Linux).Profiles.Select(p => p.Path).SequenceEqual(new[] { expect[program.Name] }), program.Name + " on Linux");
        Require(Saves.Locate(Saves.Hookshot, null, c.Linux).Profiles.SequenceEqual(new[] { new SaveProfile(Uid, c.Hs) }), "Hookshot on Linux");

        // (test_every_entry_on_windows)
        var w = new Case(root, "windows");
        var winLi = EdenSave(P(w.AppData, "eden"));
        var winEg = EdenSave(P(w.AppData, "eglantine"));
        var winNx = P(w.LocalAppData, "Programs", "NX2navi", "game", "profile", "save", Account);
        foreach (var f in new[] { winLi, winEg, winNx }) MakeSave(f, "W");
        var wexpect = new Dictionary<string, string>
        {
            ["lichii"] = winLi, ["frizti"] = winLi, ["orchid"] = winLi, ["pokedinner"] = winLi, ["eden"] = winLi, ["eglantine"] = winEg, ["nx2navi"] = winNx,
        };
        foreach (var program in Saves.Emulators)
            Require(Saves.Locate(program, null, w.Windows).Profiles.Select(p => p.Path).SequenceEqual(new[] { wexpect[program.Name] }), program.Name + " on Windows");
        Fails(() => Saves.Locate(Saves.Hookshot, null, w.Windows), "game folder", "Windows: Hookshot's saves are per game folder");
        var game = P(w.Tmp, "Hookshot");
        MakeSave(P(game, "user", "save", Uid), "W");
        Require(Saves.Locate(Saves.Hookshot, game, w.Windows).Profiles.SequenceEqual(new[] { new SaveProfile(Uid, P(game, "user", "save", Uid)) })
                && Saves.ForBuild(game, w.Windows).Profiles.Single().Path == P(game, "user", "save", Uid), "A Windows build's own save.");

        // (test_lichii_s_own_folder_first)
        var l = new Case(root, "lichii-own");
        var own = EdenSave(P(l.Data, "lichii"));
        MakeSave(own, "L2", "slot_02");
        var located = Saves.Locate(Saves.Named("lichii"), null, l.Linux);
        Require(located.Folder == P(l.Data, "lichii") && located.Profiles.SequenceEqual(new[] { new SaveProfile(Profile, own) }), "Lichii's own folder first.");
        foreach (var name in new[] { "frizti", "orchid", "pokedinner", "eden" })
            Require(Saves.Locate(Saves.Named(name), null, l.Linux).Profiles.Single().Path == l.Li, name + " stays in Eden's folder");
        var winOwn = EdenSave(P(l.AppData, "lichii"));
        var winEden = EdenSave(P(l.AppData, "eden"));
        MakeSave(winOwn, "W2");
        MakeSave(winEden, "W1");
        Require(Saves.Locate(Saves.Named("lichii"), null, l.Windows).Profiles.Single().Path == winOwn
                && Saves.Locate(Saves.Named("orchid"), null, l.Windows).Profiles.Single().Path == winEden, "The same on Windows.");
        var groups = Saves.Emulator(l.Linux);
        Require(groups.Any(g => g.Display == "Lichii" && g.Folder == P(l.Data, "lichii"))
                && groups.Any(g => g.Display == "Frizti (Fritchii) / Orchid / PokeDinner / Eden" && g.Folder == P(l.Data, "eden"))
                && groups.Any(g => g.Display == "Eglantine" && !g.Found && g.Problem!.StartsWith("Nothing in")),
                "The list's groups with Lichii's own folder (test_list_with_lichii_s_own_folder).");
        var shared = Saves.Emulator(c.Linux);
        Require(shared.Any(g => g.Display == "Lichii / Frizti (Fritchii) / Orchid / PokeDinner / Eden" && g.Found)
                && shared.Any(g => g.Display == "NX2navi" && g.Profiles.Single().Id == Account), "The list's groups (test_list).");

        // (test_lichii_s_own_folder_without_a_save_leaves_the_older_one)
        var e = new Case(root, "lichii-empty");
        Directory.CreateDirectory(P(e.Data, "lichii", "nand", "user", "save"));
        Require(Saves.Locate(Saves.Named("lichii"), null, e.Linux).Profiles.Single().Path == e.Li, "An empty Lichii folder leaves Eden's.");

        // (test_aliases_of_names)
        Require(Saves.Named("Fritchii") == Saves.Named("frizti"), "Fritchii is Frizti.");
        Fails(() => Saves.Named("ryujinx"), "Unknown emulator", "An unknown emulator.");

        // (test_deeper_and_shallower_folders)
        var eden = P(c.Data, "eden");
        foreach (var r in new[] { eden, P(eden, "nand"), P(eden, "nand", "user", "save"), Path.GetDirectoryName(Path.GetDirectoryName(c.Li))!, Path.GetDirectoryName(c.Li)!, c.Li })
            Require(Saves.Find(r).SequenceEqual(new[] { new SaveProfile(Profile, c.Li) }), "Found from " + r);
        foreach (var r in new[] { P(c.Data, "hookshot"), Path.GetDirectoryName(c.Hs)!, c.Hs })
            Require(Saves.Find(r).SequenceEqual(new[] { new SaveProfile(Uid, c.Hs) }), "Found from " + r);

        // (test_empty_title_folder_is_a_target)
        var empty = EdenSave(P(c.Tmp, "portable", "user"), "11111111111111111111111111111111");
        Directory.CreateDirectory(empty);
        Require(Saves.Find(P(c.Tmp, "portable", "user")).SequenceEqual(new[] { new SaveProfile("11111111111111111111111111111111", empty) })
                && Saves.Find(empty).SequenceEqual(new[] { new SaveProfile("11111111111111111111111111111111", empty) }), "An empty title folder is a target.");

        // (test_eden_newer_layout)
        var newer = P(c.Tmp, "newer");
        const string uuid = "a1b2c3d4e5f60718293a4b5c6d7e8f90";
        var save = P(newer, "nand", "user", "save", "account", uuid, Saves.Title, "0");
        MakeSave(save, "NEW");
        foreach (var r in new[] { newer, Path.GetDirectoryName(save)!, save })
            Require(Saves.Find(r).SequenceEqual(new[] { new SaveProfile(uuid, save) }), "Eden's newer layout from " + r);

        // (test_unrelated_folders_are_not_saves, test_missing_folders)
        Directory.CreateDirectory(P(c.Tmp, "junk", "a", "0"));
        Directory.CreateDirectory(P(c.Tmp, "junk", "b", "save", Uid));
        Require(Saves.Find(P(c.Tmp, "junk")).Count == 0, "Unrelated folders are not saves.");
        Fails(() => Saves.Locate(Saves.Folder, P(c.Tmp, "nothing"), c.Linux), "No folder", "A missing folder.");
        Directory.CreateDirectory(P(c.Tmp, "empty"));
        Fails(() => Saves.Locate(Saves.Folder, P(c.Tmp, "empty"), c.Linux), "No Ocarina of Time save", "An empty folder.");
        var noEg = new Case(root, "no-eglantine");
        Fails(() => Saves.Locate(Saves.Named("eglantine"), null, noEg.Linux), P(noEg.Data, "eglantine"), "Where Eglantine was looked for.");

        // (test_ambiguous_profiles_listed)
        var a = new Case(root, "ambiguous");
        const string other = "11111111111111111111111111111111";
        MakeSave(EdenSave(P(a.Data, "eden"), other), "O");
        var before = Tree(a.Li);
        try
        {
            a.Run(false, "lichii");
            throw new Exception("saves: two profiles were not refused");
        }
        catch (SaveFailure f)
        {
            Require(f.Message.Contains(Profile) && f.Message.Contains(other) && Tree(a.Li) == before && a.BackupDirs().Count == 0, "Two profiles: listed, nothing copied.");
        }
        a.Run(false, "lichii", profile: other.ToLowerInvariant());
        Require(Tree(EdenSave(P(a.Data, "eden"), other)) == Tree(a.Hs), "The profile asked for.");

        // (describe: the slots and when each was saved)
        File.SetLastWriteTime(P(c.Hs, "slot_01", "progress.sav"), new DateTime(2026, 11, 1, 12, 30, 0));
        var slots = Saves.Describe(c.Hs);
        Require(slots.Select(s => s.Name).SequenceEqual(new[] { "slot_00", "slot_01" }) && slots[1].Saved == new DateTime(2026, 11, 1, 12, 30, 0), "The slots and their times.");
    }

    static void Copies(string root)
    {
        // (test_export)
        var c = new Case(root, "export");
        var before = Tree(c.Li);
        var done = c.Run(false, "lichii");
        var backup = c.BackupDirs().Single();
        Require(Tree(c.Li) == Tree(c.Hs) && Path.GetFileName(backup).StartsWith("export-lichii-") && Tree(backup) == before && done.Backup == backup,
                "Export: the copy and its backup, whose path is said.");
        // (test_import)
        var i = new Case(root, "import");
        before = Tree(i.Hs);
        i.Run(true, "orchid");
        Require(Tree(i.Hs) == Tree(i.Li) && !Tree(i.Hs).Contains("slot_01") && Path.GetFileName(i.BackupDirs().Single()).StartsWith("import-orchid-")
                && Tree(i.BackupDirs().Single()) == before, "Import: the target's own slots go.");
        // (test_lichii_s_copy_goes_to_its_own_folder)
        var o = new Case(root, "own");
        var own = EdenSave(P(o.Data, "lichii"));
        MakeSave(own, "L2", "slot_02");
        before = Tree(o.Li);
        o.Run(false, "lichii");
        Require(Tree(own) == Tree(o.Hs) && Tree(o.Li) == before, "Lichii's copy goes to its own folder.");
        // (test_from_a_save_folder_and_a_data_folder)
        var f = new Case(root, "from");
        var otherSave = P(f.Tmp, "backup of a save");
        MakeSave(otherSave, "F", "slot_03");
        f.Run(true, null, otherSave);
        Require(Tree(f.Hs) == Tree(otherSave), "From a save folder.");
        var portable = P(f.Tmp, "Lichii", "user");
        MakeSave(EdenSave(portable), "P", "slot_04");
        f.Run(true, null, portable);
        Require(Tree(f.Hs) == Tree(EdenSave(portable)) && f.BackupDirs().All(b => Path.GetFileName(b).StartsWith("import-folder-")), "From a portable user folder.");
        // (test_to_a_folder)
        var t = new Case(root, "to");
        var target = EdenSave(P(t.Tmp, "Eglantine", "user"));
        Directory.CreateDirectory(target);
        t.Run(false, "eglantine", P(t.Tmp, "Eglantine", "user"));
        Require(Tree(target) == Tree(t.Hs), "To a portable folder whose game never saved.");
        // (test_links_in_a_save_refused)
        if (!OperatingSystem.IsWindows())
        {
            var k = new Case(root, "links");
            var secret = P(k.Tmp, "secret.txt");
            File.WriteAllText(secret, "not a save");
            before = Tree(k.Hs);
            var makers = new Action<string>[]
            {
                d => { File.Delete(P(d, "slot_00", "progress.sav")); File.CreateSymbolicLink(P(d, "slot_00", "progress.sav"), secret); },
                d => { File.Delete(P(d, "account.sav")); File.CreateSymbolicLink(P(d, "account.sav"), secret); },
                d => Directory.CreateSymbolicLink(P(d, "slot_00", "deep"), k.Tmp),
            };
            for (var n = 0; n < makers.Length; n++)
            {
                var linked = P(k.Tmp, "linked-" + n);
                MakeSave(linked, "L", "slot_00");
                makers[n](linked);
                Fails(() => k.Run(true, null, linked), "is a link", "A link in a save.");
                Require(Tree(k.Hs) == before, "A refused link changed nothing.");
            }
            Require(File.ReadAllText(secret) == "not a save", "The link's target is untouched.");
        }
        // (test_same_folder_refused, test_backup_inside_target_refused_before_copy)
        var s = new Case(root, "same");
        Fails(() => s.Run(true, null, s.Hs), "both the source and the target", "One folder both ways.");
        before = Tree(s.Hs);
        foreach (var dry in new[] { false, true })
        {
            Fails(() => Saves.Copy(s.Li, s.Hs, P(s.Hs, "backups"), "import", "folder", dryRun: dry, running: () => s.Busy),
                  "outside the target save folder", "A backup inside the target.");
            Require(!Directory.Exists(P(s.Hs, "backups")) && Tree(s.Hs) == before, "Nothing copied for a backup inside the target.");
        }
        // (test_keep_settings)
        var ks = new Case(root, "keep");
        ks.Run(false, "lichii", keep: true);
        Require(File.ReadAllText(P(ks.Li, "option.sav")) == "option-LI" && File.ReadAllText(P(ks.Li, "account.sav")) == "account-HS"
                && File.ReadAllText(P(ks.Li, "slot_01", "progress.sav")) == "progress-HSslot_01", "Keep settings: option.sav stays.");
        // (test_dry_run_changes_nothing)
        var d = new Case(root, "dry");
        var both = Tree(d.Hs) + Tree(d.Li);
        d.Busy = new[] { "Lichii" };
        var dry2 = d.Run(false, "lichii", dry: true);
        Require(dry2.Dry && Tree(d.Hs) + Tree(d.Li) == both && d.BackupDirs().Count == 0 && dry2.Slots.SequenceEqual(new[] { "slot_00", "slot_01" }),
                "A dry run says and changes nothing.");
        // (test_source_without_slots)
        var n2 = new Case(root, "noslots");
        foreach (var slot in new[] { "slot_00", "slot_01" }) Directory.Delete(P(n2.Hs, slot), true);
        before = Tree(n2.Li);
        Fails(() => n2.Run(false, "lichii"), "no slot_* folders", "A source with no slots.");
        Require(Tree(n2.Li) == before, "Nothing copied from no slots.");
    }

    static void Nx2navi(string root)
    {
        var c = new Case(root, "nx2navi");
        var game = P(c.Home, "Games", "Ocarina");
        var nx = P(game, "profile", "save", Account);
        MakeSave(nx, "NX", "slot_05");
        File.WriteAllBytes(P(game, "profile", "account.uid"), new byte[16]);
        Directory.CreateDirectory(P(game, "profile", "save-tx"));
        // (test_both_ways)
        c.Run(true, "nx2navi");
        Require(Tree(c.Hs) == Tree(nx), "From NX2navi.");
        MakeSave(P(c.Hs, "slot_00"), "Z", "-");
        c.Run(false, "nx2navi", game);
        Require(Tree(nx) == Tree(c.Hs) && File.ReadAllBytes(P(game, "profile", "account.uid")).Length == 16, "To NX2navi's game folder.");
        // (test_waiting_save_refused)
        File.WriteAllText(P(game, "profile", "save-tx", "commit"), "pending");
        var before = Tree(c.Hs) + Tree(nx);
        var backups = c.BackupDirs().Count;
        foreach (var import in new[] { true, false })
            Fails(() => c.Run(import, "nx2navi"), "save-tx", "A save waiting in save-tx.");
        Require(Tree(c.Hs) + Tree(nx) == before && c.BackupDirs().Count == backups && Saves.PendingCommit(nx) == P(game, "profile", "save-tx"),
                "Nothing copied while NX2navi has a save waiting.");
        c.Run(true, "nx2navi", force: true);
        Require(Tree(c.Hs) == Tree(nx), "Forced.");
    }

    static void Guard(string root)
    {
        // (test_refuses_while_running, test_force)
        var c = new Case(root, "guard");
        c.Busy = new[] { "Hookshot" };
        var before = Tree(c.Hs) + Tree(c.Li);
        Fails(() => c.Run(false, "lichii"), "Hookshot", "A running game.");
        Require(Tree(c.Hs) + Tree(c.Li) == before && c.BackupDirs().Count == 0, "Nothing copied while a game runs.");
        c.Busy = new[] { "Lichii" };
        c.Run(false, "lichii", force: true);
        Require(Tree(c.Li) == Tree(c.Hs), "Forced while running.");

        // (test_every_process_name_on_windows)
        var names = Saves.ProcessNames();
        foreach (var program in Saves.Emulators.Prepend(Saves.Hookshot))
            foreach (var p in program.Processes) Require(names[p].Contains(program.Display), "The process names of " + program.Name);
        var images = names.Keys.Select((p, i) => i % 2 == 1 ? p.ToUpperInvariant() + ".exe" : p + ".exe").Append("notepad.exe");
        Require(Saves.RunningFrom(Platform.Windows, images).SequenceEqual(names.Values.Distinct().Order(StringComparer.Ordinal)), "Every program's process on Windows.");
        // (test_linux_processes_and_appimages: each process's comm, and its argv[0]'s file name)
        var linux = new[] { "orchid", "orchid", "AppRun", "eglantine-linux-x86_64.AppImage", "ocarina", "ocarina", "bash", "bash" };
        Require(Saves.RunningFrom(Platform.Linux, linux).SequenceEqual(new[] { "NX2navi", "Orchid", "PokeDinner or Eglantine" }), "Linux processes and AppImages.");
        Require(Saves.RunningFrom(Platform.Linux, new[] { "Hookshot.AppImage", "HookshotLauncher" }).Count == 0, "The launcher itself is not the game.");
    }

    static void Builds(string root)
    {
        // (Windows: each game folder its own saves; the launcher copies one build's into another, a new one too)
        var c = new Case(root, "builds");
        var a = P(c.Tmp, "Hookshot 2.3");
        var b = P(c.Tmp, "Hookshot 3.0");
        MakeSave(Saves.BuildSavePath(a, Uid), "A");
        Directory.CreateDirectory(b);
        var copied = Saves.BetweenBuilds(a, b, c.Windows, backupRoot: c.Backups, running: () => c.Busy);
        Require(Tree(Saves.BuildSavePath(b, Uid)) == Tree(Saves.BuildSavePath(a, Uid)) && copied.Backup == null, "Into a build with no save yet.");
        File.WriteAllText(P(Saves.BuildSavePath(a, Uid), "slot_00", "progress.sav"), "played on");
        copied = Saves.BetweenBuilds(a, b, c.Windows, keepSettings: true, backupRoot: c.Backups, running: () => c.Busy);
        Require(File.ReadAllText(P(Saves.BuildSavePath(b, Uid), "slot_00", "progress.sav")) == "played on" && copied.Backup != null && Directory.Exists(copied.Backup)
                && Path.GetFileName(copied.Backup).StartsWith("build-Hookshot 3.0-"), "Into a build with a save: backed up first.");
        Fails(() => Saves.BetweenBuilds(a, b, c.Linux), "same saves", "Linux builds share one save.");
        // (import and export for a build: its own save on Windows, the shared one on Linux)
        MakeSave(EdenSave(P(c.AppData, "eden")), "WE", "slot_07");
        Saves.ImportExport(true, b, Saves.Named("lichii"), null, c.Windows, backupRoot: c.Backups, running: () => c.Busy);
        Require(Tree(Saves.BuildSavePath(b, Uid)) == Tree(EdenSave(P(c.AppData, "eden"))), "Import into a Windows build.");
        Fails(() => Saves.ImportExport(true, P(c.Tmp, "unbuilt"), Saves.Named("lichii"), null, c.Windows, backupRoot: c.Backups, running: () => c.Busy),
              "Start this build once", "A build that never saved.");
        Saves.ImportExport(false, "/unused on Linux", Saves.Named("eden"), null, c.Linux, backupRoot: c.Backups, running: () => c.Busy);
        Require(Tree(c.Li) == Tree(c.Hs), "Export from Linux's shared save.");
        Require(Saves.MoveOnBuild(true, Platform.Windows, 1) && !Saves.MoveOnBuild(true, Platform.Linux, 3) && !Saves.MoveOnBuild(false, Platform.Windows, 2)
                && !Saves.MoveOnBuild(true, Platform.Windows, 0), "When a build takes another's saves.");
    }
}
