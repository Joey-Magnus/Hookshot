using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace HookshotLauncher.Core;

/// A program that runs the game and keeps its saves (scripts/oot_saves.py Program): where it keeps its data on each platform,
/// in the order tried ({data}, {home}, {appdata}, {localappdata}), its process names (lower case, no .exe) and other names
public sealed record SaveProgram(string Name, string Display, IReadOnlyDictionary<Platform, string[]> Folders, string[] Processes, string[] Aliases);

/// One profile's save folder (account.sav, option.sav, slot_NN/): its id (the folder above it that names the profile)
public sealed record SaveProfile(string Id, string Path);

/// A slot and when its progress.sav was last written (null: no progress.sav in it)
public sealed record SaveSlot(string Name, DateTime? Saved);

/// Where one program's (or one build's) saves were found: the data folder searched and its profiles; Problem: why none
public sealed record SaveSet(string Owner, string Display, string? Folder, IReadOnlyList<SaveProfile> Profiles, string? Problem)
{
    public bool Found => Profiles.Count > 0;
}

/// What a copy did (or, dry, would do): Backup is the copy of the target taken first (null: the target had nothing yet)
public sealed record SaveCopy(string Source, string Target, string? Backup, IReadOnlyList<string> Slots, IReadOnlyList<string> Files, bool KeptSettings, bool Dry);

/// A copy refused, and why (oot_saves.Fail)
public sealed class SaveFailure(string message) : Exception(message);

/// The environment the save folders are worked out from: the platform and its variables (HOME, XDG_DATA_HOME, APPDATA,
/// LOCALAPPDATA), so the tests point them all into one temporary folder as tests/test_oot_saves.py does
public sealed record SaveEnvironment(Platform Platform, Func<string, string?> Getenv)
{
    public static SaveEnvironment Current() => new(Platforms.Current, Environment.GetEnvironmentVariable);

    string Home => Getenv("HOME") is { Length: > 0 } h ? h : Getenv("USERPROFILE") is { Length: > 0 } u ? u
                   : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// oot_saves.data_home
    public string Data => Getenv("XDG_DATA_HOME") is { Length: > 0 } x ? x : Path.Combine(Home, ".local", "share");

    string AppData => Getenv("APPDATA") is { Length: > 0 } a ? a : Path.Combine(Home, "AppData", "Roaming");
    string LocalAppData => Getenv("LOCALAPPDATA") is { Length: > 0 } a ? a : Path.Combine(Home, "AppData", "Local");

    /// oot_saves.default_backup_dir: the script's and the launcher's backups in one place
    public string BackupRoot => Platform == Platform.Windows
        ? Path.Combine(LocalAppData, "Hookshot", "oot-save-backups")
        : Path.Combine(Data, "oot-save-backups");

    /// oot_saves.folders: a program's data folders on this platform, in the order they are tried
    public IReadOnlyList<string> FoldersOf(SaveProgram program) =>
        program.Folders.TryGetValue(Platform, out var list)
            ? list.Select(t => Path.GetFullPath(t.Replace("{data}", Data).Replace("{home}", Home).Replace("{appdata}", AppData)
                                                 .Replace("{localappdata}", LocalAppData).Replace('/', Path.DirectorySeparatorChar))).ToList()
            : Array.Empty<string>();
}

/// The game's saves, the same files in Hookshot and in the emulators and ports that run it (docs/saves.md): found, summed up and
/// copied one way after a backup. A port of scripts/oot_saves.py, rule for rule (each piece names the function it mirrors), so
/// that the launcher needs no Python and no installed tools for it. Hookshot keeps its saves per game folder on Windows
/// (&lt;folder&gt;\user\save\&lt;user id&gt;\: each build its own) and in one place on Linux ($XDG_DATA_HOME/hookshot/save/&lt;user id&gt;/:
/// every build shares them).
public static class Saves
{
    public const string Title = "0400486024DEC000";
    public const string NoAccount = "0000000000000000";

    static Dictionary<Platform, string[]> On(string[] linux, string[] windows) => new() { [Platform.Linux] = linux, [Platform.Windows] = windows };

    // (oot_saves.EDEN, LICHII, EMULATORS, HOOKSHOT, FOLDER; their sources are in docs/saves.md)
    static readonly string[] EdenLinux = { "{data}/eden" }, EdenWindows = { "{appdata}/eden" };
    public static readonly IReadOnlyList<SaveProgram> Emulators = new[]
    {
        // (Lichii has a data folder of its own since its build 150, 2026-11-10; a build from before keeps its saves in Eden's)
        new SaveProgram("lichii", "Lichii", On(new[] { "{data}/lichii" }.Concat(EdenLinux).ToArray(), new[] { "{appdata}/lichii" }.Concat(EdenWindows).ToArray()),
                        new[] { "lichii" }, Array.Empty<string>()),
        new SaveProgram("frizti", "Frizti (Fritchii)", On(EdenLinux, EdenWindows), new[] { "lichii", "fritchii" }, new[] { "fritchii" }),
        new SaveProgram("orchid", "Orchid", On(EdenLinux, EdenWindows), new[] { "orchid" }, Array.Empty<string>()),
        new SaveProgram("pokedinner", "PokeDinner", On(EdenLinux, EdenWindows), new[] { "eglantine" }, Array.Empty<string>()),
        new SaveProgram("eden", "Eden", On(EdenLinux, EdenWindows), new[] { "eden" }, Array.Empty<string>()),
        new SaveProgram("eglantine", "Eglantine", On(new[] { "{data}/eglantine" }, new[] { "{appdata}/eglantine" }), new[] { "eglantine" }, Array.Empty<string>()),
        new SaveProgram("nx2navi", "NX2navi", On(new[] { "{home}/Games/Ocarina" }, new[] { "{localappdata}/Programs/NX2navi/game", "C:/Games/Ocarina" }),
                        new[] { "nx2navi", "ocarina" }, Array.Empty<string>()),
    };
    public static readonly SaveProgram Hookshot = new("hookshot", "Hookshot", On(new[] { "{data}/hookshot" }, Array.Empty<string>()), new[] { "hookshot" }, Array.Empty<string>());
    public static readonly SaveProgram Folder = new("folder", "the folder", On(Array.Empty<string>(), Array.Empty<string>()), Array.Empty<string>(), Array.Empty<string>());

    // (oot_saves.LAYOUTS: where the save folders sit under a data folder; "<id>" is one folder per profile. A folder deeper down
    // the same path is accepted too; the first layout that finds a profile wins)
    static readonly string[][] Layouts =
    {
        new[] { "nand", "user", "save", "account", "<id>", Title, "0" },   // (Eden's newer layout: used first when it exists)
        new[] { "nand", "user", "save", NoAccount, "<id>", Title },        // (Eden and its forks: Lichii, Eglantine, ...)
        new[] { "profile", "save", "<id>" },                                // (NX2navi's game folder)
        new[] { "user", "save", "<id>" },                                   // (Hookshot's game folder on Windows)
        new[] { "save", "<id>" },                                           // (Hookshot's data folder)
    };

    // ---- finding them ----

    /// oot_saves.subdirs: the folders in path, sorted; none when it cannot be read
    static List<string> Subdirs(string path)
    {
        try { return Directory.EnumerateDirectories(path).Select(System.IO.Path.GetFileName).OfType<string>().OrderBy(n => n, StringComparer.Ordinal).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return new List<string>(); }
    }

    /// oot_saves.slots
    public static IReadOnlyList<string> SlotNames(string path) => Subdirs(path).Where(n => n.StartsWith("slot_", StringComparison.Ordinal)).ToList();

    /// oot_saves.is_save_folder
    public static bool IsSaveFolder(string path) => File.Exists(System.IO.Path.Combine(path, "account.sav")) || SlotNames(path).Count > 0;

    static string Base(string path) => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));

    static string Up(string path) => System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(path)) ?? path;

    /// oot_saves.walk
    static IEnumerable<(string? Key, string Path)> Walk(string root, ArraySegment<string> parts, string? key)
    {
        if (parts.Count == 0)
        {
            yield return (key, root);
            yield break;
        }
        foreach (var n in Subdirs(root))
        {
            if (parts[0] == "<id>")
                foreach (var x in Walk(System.IO.Path.Combine(root, n), parts[1..], n)) yield return x;
            else if (string.Equals(n, parts[0], StringComparison.OrdinalIgnoreCase))
                foreach (var x in Walk(System.IO.Path.Combine(root, n), parts[1..], key)) yield return x;
        }
    }

    /// oot_saves.find_saves: the game's save folders under root, one per profile id, in the order found; root may be the save
    /// folder itself
    public static IReadOnlyList<SaveProfile> Find(string root)
    {
        var norm = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(root));
        var titleFolder = Base(norm).ToUpperInvariant() == Title;
        if (IsSaveFolder(norm) || (titleFolder && !Subdirs(norm).Contains("0")))
        {
            var key = norm;
            while (Base(key).ToUpperInvariant() is Title or "0" && Up(key) != key) key = Up(key);
            return new[] { new SaveProfile(Base(key) is { Length: > 0 } b ? b : norm, norm) };
        }
        var found = new List<SaveProfile>();
        foreach (var layout in Layouts)
        {
            var idpos = Array.IndexOf(layout, "<id>");
            for (var start = 0; start < layout.Length; start++)
            {
                // (below the title id folder, only the title id folder itself is a place to start)
                if (layout.Contains(Title) && !layout[start..].Contains(Title) && !titleFolder) continue;
                string? key = null;
                if (start > idpos)   // (root is at or below the <id> folder: the id is a folder above it)
                {
                    var up = norm;
                    for (var i = 0; i < start - idpos - 1; i++) up = Up(up);
                    key = Base(up);
                }
                foreach (var (k, path) in Walk(norm, new ArraySegment<string>(layout, start, layout.Length - start), key))
                {
                    // (a folder the layout names, the title id folder, counts as soon as it is there: the game may not have saved yet.
                    // A profile folder counts only when it holds a save: Eden's profile folders do not)
                    if ((layout[^1] != "<id>" || IsSaveFolder(path)) && k != null && found.All(f => f.Id != k && f.Path != path))
                        found.Add(new SaveProfile(k, path));
                }
            }
        }
        return found;
    }

    /// oot_saves.locate: (folder, profiles) for program: the override when given, else the first of its folders with saves
    public static (string Folder, IReadOnlyList<SaveProfile> Profiles) Locate(SaveProgram program, string? folder, SaveEnvironment env)
    {
        if (folder != null)
        {
            if (!Directory.Exists(folder)) throw new SaveFailure($"No folder at {folder}.");
            var found = Find(folder);
            if (found.Count == 0)
                throw new SaveFailure($"No Ocarina of Time save under {folder} (a folder with account.sav and slot_*, or {Title} under an " +
                                      "emulator's nand/user/save): start the game there once and save.");
            return (folder, found);
        }
        var tried = env.FoldersOf(program);
        foreach (var d in tried)
            if (Directory.Exists(d) && Find(d) is { Count: > 0 } found) return (d, found);
        if (program == Hookshot && tried.Count == 0)
            throw new SaveFailure("Hookshot's saves are in the user folder of each game folder on Windows: choose the build.");
        throw new SaveFailure($"No {program.Display} save found (looked in {(tried.Count > 0 ? string.Join(", ", tried) : "no default folder")}): " +
                              $"start the game in {program.Display} once and save, or choose its folder.");
    }

    /// oot_saves.pick: the one profile, or the one asked for; several and none asked for is an error that lists them
    public static SaveProfile Pick(IReadOnlyList<SaveProfile> found, string name, string? wanted = null)
    {
        if (wanted != null)
            return found.FirstOrDefault(p => string.Equals(p.Id, wanted, StringComparison.OrdinalIgnoreCase))
                   ?? throw new SaveFailure($"{name} has no profile {wanted}; it has: {string.Join(", ", found.Select(p => p.Id).Order(StringComparer.Ordinal))}");
        if (found.Count > 1)
            throw new SaveFailure($"{name} has Ocarina of Time saves in {found.Count} profiles, so pick one:" +
                                  string.Concat(found.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p => $"\n  {p.Id}  {p.Path}")));
        return found.Count == 1 ? found[0] : throw new SaveFailure($"{name} has no save.");
    }

    /// oot_saves.program_named: an emulator by its name or another name
    public static SaveProgram Named(string name) =>
        Emulators.FirstOrDefault(p => p.Name == name.ToLowerInvariant() || p.Aliases.Contains(name.ToLowerInvariant()))
        ?? throw new SaveFailure($"Unknown emulator {name}: use one of {string.Join(", ", Emulators.Select(p => p.Name))}, or choose a folder.");

    /// oot_saves.pending_commit: NX2navi writes a save to profile/save-tx first and moves it into profile/save/&lt;account&gt; after;
    /// while something waits there, that is the save the game loads next
    public static string? PendingCommit(string save)
    {
        var parent = Up(System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(save)));
        var tx = System.IO.Path.Combine(Up(parent), "save-tx");
        if (Base(parent).ToLowerInvariant() == "save" && Directory.Exists(tx)
            && Directory.EnumerateFiles(tx, "*", SearchOption.AllDirectories).Any())
            return tx;
        return null;
    }

    /// oot_saves.describe: each slot and when its progress.sav was written (local time)
    public static IReadOnlyList<SaveSlot> Describe(string path) =>
        SlotNames(path).Select(s =>
        {
            var p = System.IO.Path.Combine(path, s, "progress.sav");
            return new SaveSlot(s, File.Exists(p) ? File.GetLastWriteTime(p) : null);
        }).ToList();

    // ---- by build, and everything at once (oot_saves.list_all) ----

    /// Hookshot's saves for a build: in its game folder on Windows (user\save\&lt;user id&gt;), the shared ones on Linux
    public static SaveSet ForBuild(string folder, SaveEnvironment env)
    {
        try
        {
            if (env.Platform == Platform.Windows)
            {
                var found = Directory.Exists(System.IO.Path.Combine(folder, "user")) ? Find(System.IO.Path.Combine(folder, "user")) : Array.Empty<SaveProfile>();
                return new SaveSet("hookshot", "Hookshot", folder, found, found.Count == 0 ? "No save in this build's folder yet." : null);
            }
            var (where, profiles) = Locate(Hookshot, null, env);
            return new SaveSet("hookshot", "Hookshot", where, profiles, null);
        }
        catch (SaveFailure e)
        {
            return new SaveSet("hookshot", "Hookshot", null, Array.Empty<SaveProfile>(), e.Message);
        }
    }

    /// Where a build's save goes for a profile on Windows when it has none yet: &lt;folder&gt;\user\save\&lt;id&gt;
    public static string BuildSavePath(string folder, string profile) => System.IO.Path.Combine(folder, "user", "save", profile);

    /// Every emulator's saves, grouped by the folder they come from (the emulators that share Eden's folder in one group), as
    /// list_all shows them: a group with nothing names the folders it looked in
    public static IReadOnlyList<SaveSet> Emulator(SaveEnvironment env)
    {
        var groups = new List<(string Key, string? Folder, List<SaveProgram> Programs, IReadOnlyList<string> Tried)>();
        foreach (var program in Emulators)
        {
            var tried = env.FoldersOf(program);
            var used = tried.FirstOrDefault(d => Directory.Exists(d) && Find(d).Count > 0);
            var key = used ?? "\0" + string.Join("\0", tried);
            var at = groups.FindIndex(g => g.Key == key);
            if (at < 0) groups.Add((key, used, new List<SaveProgram> { program }, tried));
            else groups[at].Programs.Add(program);
        }
        return groups.Select(g =>
        {
            var display = string.Join(" / ", g.Programs.Select(p => p.Display));
            return g.Folder != null
                ? new SaveSet(g.Programs[0].Name, display, g.Folder, Find(g.Folder), null)
                : new SaveSet(g.Programs[0].Name, display, null, Array.Empty<SaveProfile>(),
                              "Nothing in " + (g.Tried.Count > 0 ? string.Join(", ", g.Tried) : "(no default folder)"));
        }).ToList();
    }

    // ---- is a game running (oot_saves.process_names, running_games) ----

    /// {process name: what it belongs to}
    public static IReadOnlyDictionary<string, string> ProcessNames()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var program in Emulators.Prepend(Hookshot))
            foreach (var p in program.Processes)
                if (names.TryGetValue(p, out var was)) names[p] = was + " or " + program.Display;
                else
                {
                    names[p] = program.Display;
                    order.Add(p);
                }
        return names;
    }

    /// What is running, from the processes' names: on Windows the image names (with or without .exe), on Linux each process's
    /// comm and the file name of its argv[0] (an AppImage named after the program counts)
    public static IReadOnlyList<string> RunningFrom(Platform platform, IEnumerable<string> candidates)
    {
        var known = ProcessNames();
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var appImage = new Regex("^(" + string.Join("|", known.Keys.Select(Regex.Escape)) + @")[-_.].*\.appimage$", RegexOptions.CultureInvariant);
        foreach (var candidate in candidates)
        {
            var low = candidate.ToLowerInvariant();
            if (platform == Platform.Windows)
            {
                var image = low.EndsWith(".exe", StringComparison.Ordinal) ? low[..^4] : low;
                if (known.TryGetValue(image, out var w)) names.Add(w);
                continue;
            }
            if (known.TryGetValue(low, out var name)) names.Add(name);
            else if (appImage.Match(low) is { Success: true } m) names.Add(known[m.Groups[1].Value]);
        }
        return names.ToList();
    }

    /// The games running on this PC now, this launcher left out (its AppImage may be named Hookshot-...AppImage)
    public static IReadOnlyList<string> Running(Platform platform)
    {
        var own = Environment.ProcessId;
        var candidates = new List<string>();
        if (platform == Platform.Windows)
        {
            foreach (var p in Process.GetProcesses())
                using (p)
                    if (p.Id != own) candidates.Add(p.ProcessName);
        }
        else
        {
            IEnumerable<string> pids;
            try { pids = Directory.EnumerateDirectories("/proc").Select(System.IO.Path.GetFileName).OfType<string>().Where(n => n.All(char.IsAsciiDigit)).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { pids = Array.Empty<string>(); }
            foreach (var pid in pids)
            {
                if (pid == own.ToString(System.Globalization.CultureInfo.InvariantCulture)) continue;
                try
                {
                    var argv0 = File.ReadAllText($"/proc/{pid}/cmdline", Encoding.UTF8).Split('\0')[0];
                    candidates.Add(File.ReadAllText($"/proc/{pid}/comm", Encoding.UTF8).Trim());
                    candidates.Add(System.IO.Path.GetFileName(argv0));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        return RunningFrom(platform, candidates);
    }

    // ---- the copy (oot_saves.run, copy_saves, refuse_links) ----

    static bool IsLink(FileSystemInfo info) => info.LinkTarget != null || info.Attributes.HasFlag(FileAttributes.ReparsePoint);

    /// oot_saves.refuse_links: a save from someone else is only files and folders: a link in it would copy what it points to
    static void RefuseLinks(string path)
    {
        FileSystemInfo root = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (!root.Exists && root.LinkTarget == null) return;
        if (IsLink(root)) throw new SaveFailure($"{path} is a link: a save holds only files and folders, so nothing was copied.");
        if (root is not DirectoryInfo dir) return;
        foreach (var child in dir.EnumerateFileSystemInfos())
        {
            if (IsLink(child)) throw new SaveFailure($"{child.FullName} is a link: a save holds only files and folders, so nothing was copied.");
            if (child is DirectoryInfo) RefuseLinks(child.FullName);
        }
    }

    static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from)) File.Copy(f, System.IO.Path.Combine(to, System.IO.Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(from)) CopyTree(d, System.IO.Path.Combine(to, System.IO.Path.GetFileName(d)));
    }

    static string Real(string path)
    {
        var full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        try { if (new DirectoryInfo(full).ResolveLinkTarget(true) is { } t) full = t.FullName; } catch (IOException) { }
        return full;
    }

    static bool Inside(string root, string path)
    {
        var c = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.Equals(root, c) || path.StartsWith(root + System.IO.Path.DirectorySeparatorChar, c);
    }

    /// oot_saves.run (after the two sides are picked) and copy_saves: src's slots, account.sav and option.sav (not with
    /// keepSettings) into dst, which loses its own slots; dst is copied whole to &lt;backupRoot&gt;/&lt;command&gt;-&lt;name&gt;-&lt;yyyyMMdd-HHmmss&gt;
    /// first. Refused (SaveFailure) when the two are one folder, NX2navi still has a save waiting on either side, a game runs
    /// (running: the names; not asked on a dry run), the backup would go inside dst, src has no slot, or src holds a link.
    /// force skips the waiting save and the running game. dst is made when it is not there yet (a build that has no save).
    public static SaveCopy Copy(string src, string dst, string backupRoot, string command, string name, bool keepSettings = false,
                                bool dryRun = false, bool force = false, Func<IReadOnlyList<string>>? running = null, Func<DateTime>? now = null)
    {
        if (Real(src) == Real(dst)) throw new SaveFailure($"{src} is both the source and the target.");
        if (!force)
            foreach (var side in new[] { src, dst })
                if (PendingCommit(side) is { } tx)
                    throw new SaveFailure($"NX2navi still has a save waiting in {tx}, which it loads instead of {side}: start NX2navi and quit it from " +
                                          "the game, then copy.");
        if (!dryRun && !force && (running ?? (() => Running(Platforms.Current)))() is { Count: > 0 } busy)
            throw new SaveFailure($"Close {string.Join(" and ", busy)} first: a running game can write its save again and undo the copy.");
        var stamp = (now ?? (() => DateTime.Now))().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var backup = System.IO.Path.Combine(backupRoot, $"{command}-{name}-{stamp}");
        var at = backup;
        for (var n = 2; Directory.Exists(at) || File.Exists(at); n++) at = $"{backup}-{n}";   // (two copies in the same second)
        backup = at;

        if (Inside(Real(dst), System.IO.Path.GetFullPath(backup))) throw new SaveFailure($"Choose a backup folder outside the target save folder ({dst}).");
        var names = SlotNames(src);
        if (names.Count == 0) throw new SaveFailure($"{src} has no slot_* folders: nothing to copy.");
        foreach (var s in names) RefuseLinks(System.IO.Path.Combine(src, s));
        foreach (var f in new[] { "account.sav", "option.sav" }) RefuseLinks(System.IO.Path.Combine(src, f));
        var files = new[] { "account.sav", "option.sav" }.Where(f => File.Exists(System.IO.Path.Combine(src, f))).ToList();
        if (keepSettings) files.Remove("option.sav");
        var had = Directory.Exists(dst);
        var result = new SaveCopy(src, dst, had ? backup : null, names, files, keepSettings, dryRun);
        if (dryRun) return result;
        if (had)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backup)!);
            CopyTree(dst, backup);
            foreach (var s in SlotNames(dst)) Directory.Delete(System.IO.Path.Combine(dst, s), recursive: true);
        }
        else Directory.CreateDirectory(dst);
        foreach (var s in names) CopyTree(System.IO.Path.Combine(src, s), System.IO.Path.Combine(dst, s));
        foreach (var f in files) File.Copy(System.IO.Path.Combine(src, f), System.IO.Path.Combine(dst, f), overwrite: true);
        return result;
    }

    /// Import (an emulator's save into a build) and export (a build's save into an emulator), as oot_saves.run picks the two sides:
    /// the build's one profile, the emulator's (profile: when it has several); folder: the emulator's folder instead of where it is
    /// looked for (a portable user folder, NX2navi's game folder, any folder holding a save)
    public static SaveCopy ImportExport(bool import, string buildFolder, SaveProgram other, string? folder, SaveEnvironment env, string? profile = null,
                                        bool keepSettings = false, bool dryRun = false, bool force = false, string? backupRoot = null,
                                        Func<IReadOnlyList<string>>? running = null, Func<DateTime>? now = null)
    {
        var build = ForBuild(buildFolder, env);
        var em = Locate(other, folder, env).Profiles;
        var emulator = Pick(em, other.Display, profile);
        // (as the script: Hookshot's side has to exist, so the save lands in the user id folder the game made)
        if (!build.Found) throw new SaveFailure((build.Problem ?? "No Hookshot save found.") + " Start this build once and save first.");
        var hs = Pick(build.Profiles, "Hookshot").Path;
        var (src, dst) = import ? (emulator.Path, hs) : (hs, emulator.Path);
        return Copy(src, dst, backupRoot ?? env.BackupRoot, import ? "import" : "export", other.Name, keepSettings, dryRun, force, running, now);
    }

    /// Whether a build (a new one, or an update) takes its saves from another build first: the moveSaves setting, on Windows
    /// (Linux builds share one save folder), when there is another build to take them from
    public static bool MoveOnBuild(bool moveSaves, Platform platform, int otherBuilds) => moveSaves && platform == Platform.Windows && otherBuilds > 0;

    /// A build's save into another build (Windows: each game folder has its own). The target build's folder gets the source's
    /// profile when it has no save yet
    public static SaveCopy BetweenBuilds(string fromFolder, string toFolder, SaveEnvironment env, bool keepSettings = false, bool force = false,
                                         string? backupRoot = null, Func<IReadOnlyList<string>>? running = null, Func<DateTime>? now = null)
    {
        if (env.Platform != Platform.Windows) throw new SaveFailure("On Linux every build already uses the same saves.");
        var from = ForBuild(fromFolder, env);
        if (!from.Found) throw new SaveFailure(from.Problem ?? "The build has no save.");
        var source = Pick(from.Profiles, "That build");
        var to = ForBuild(toFolder, env);
        var target = to.Found ? Pick(to.Profiles, "This build").Path : BuildSavePath(toFolder, source.Id);
        return Copy(source.Path, target, backupRoot ?? env.BackupRoot, "build", Base(toFolder), keepSettings, false, force, running, now);
    }
}
