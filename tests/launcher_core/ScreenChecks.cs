using System.Diagnostics;
using System.Text.Json;
using HookshotLauncher.Core;

/// What the launcher's screens use from the core: the game files found and checked, the tools of step 1, the build's run and its
/// times, the builds folder, an update taking its build's row, the space on the disk, the saves' refusals and summaries, the shortcuts
static partial class Program
{
    static async Task ScreenChecks()
    {
        GameFileChecks();
        GameCheckChecks();
        await RunChecks();
        ClockChecks();
        await ToolsSourceChecks();
        BuildsFolderChecks();
        ReplaceChecks();
        DiskChecks();
        SaveRefusalChecks();
        ShortcutChecks();
    }

    static void Touch(string path, string text = "x", DateTime? when = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        if (when is { } t) File.SetLastWriteTime(path, t);
    }

    static void GameFileChecks()
    {
        var root = Folder("found");
        var home = Path.Combine(root, "home");
        Touch(Path.Combine(home, "Downloads", "old.nup"), "o", new DateTime(2026, 1, 1));
        Touch(Path.Combine(home, "Downloads", "Switch", "new.NUP".ToLowerInvariant()), "nn", new DateTime(2026, 11, 1));
        Touch(Path.Combine(home, "Downloads", "Switch", "deeper", "too-deep.nup"));
        Touch(Path.Combine(home, "Downloads", ".hidden", "hidden.nup"));
        Touch(Path.Combine(home, "Downloads", "readme.txt"));
        Touch(Path.Combine(home, "Desktop", "oot", "exefs", "main"));
        Directory.CreateDirectory(Path.Combine(home, "Desktop", "oot", "romfs"));
        Touch(Path.Combine(home, "Desktop", "half", "exefs", "main"));
        var drive = Path.Combine(root, "drive");
        Touch(Path.Combine(drive, "Program Files", "x.nup"));
        Touch(Path.Combine(drive, "Dumps", "ゲーム Zélda.nup"), "zz", new DateTime(2026, 6, 1));
        var places = GameFiles.Places(Platform.Linux, k => k == "HOME" ? home : null, new[] { drive });
        Require(places.Take(4).Select(p => p.Name).SequenceEqual(new[] { "Downloads", "Desktop", "Documents", "Home" }) && places[^1].Dir == drive,
                "The places: Downloads, Desktop, Documents, the home folder, then the drives.");
        var found = GameFiles.Find(places.Append((Path.Combine(home, "Downloads"), "again")));
        var names = found.Select(f => f.Name).ToList();
        Require(names.Contains("old.nup") && names.Contains("new.nup") && names.Contains("ゲーム Zélda.nup") && names.Contains("oot")
                && !names.Contains("too-deep.nup") && !names.Contains("hidden.nup") && !names.Contains("x.nup") && !names.Contains("half")
                && found.Count == names.Distinct().Count(), "Found: .nup files one level down and dump folders; not deeper, hidden or Program folders, each once: "
                + string.Join(", ", names));
        var nups = found.Where(f => !f.Folders).Select(f => f.Name).ToList();
        Require(nups.IndexOf("new.nup") < nups.IndexOf("ゲーム Zélda.nup") && nups.IndexOf("ゲーム Zélda.nup") < nups.IndexOf("old.nup"), "Newest first.");
        Require(found.First(f => f.Name == "new.nup").Where == "Downloads" && found.First(f => f.Name == "ゲーム Zélda.nup").Where == drive,
                "Each found file says where it is.");
        var oot = found.First(f => f.Folders);
        Require(oot.Source is BuildSource.Folders { } fs && fs.Romfs == Path.Combine(home, "Desktop", "oot", "romfs")
                && fs.Exefs == Path.Combine(home, "Desktop", "oot", "exefs"), "A dump folder builds from its romfs and exefs.");
        Require(found.First(f => f.Name == "old.nup").Source is BuildSource.Nup { Path: var np } && np == Path.Combine(home, "Downloads", "old.nup"),
                "A .nup builds from itself.");
        Require(GameFiles.FromPath(Path.Combine(home, "Downloads", "old.nup"), "Dropped")?.Where == "Dropped"
                && GameFiles.FromPath(Path.Combine(home, "Downloads", "readme.txt"), "x") == null
                && GameFiles.FromPath(Path.Combine(home, "Desktop", "oot", "exefs"), "x")?.Path == Path.Combine(home, "Desktop", "oot")
                && GameFiles.FromPath(Path.Combine(home, "Desktop", "oot"), "x")?.Folders == true
                && GameFiles.FromPath(Path.Combine(home, "Desktop", "half"), "x") == null
                && GameFiles.FromPath(Path.Combine(root, "nothing"), "x") == null, "A dropped or browsed path: a .nup, a dump folder, its romfs or exefs.");
        var apart = GameFiles.FromFolders(Path.Combine(home, "Desktop", "oot", "romfs"), Path.Combine(home, "Desktop", "half", "exefs"), "Picked");
        Require(apart?.Source is BuildSource.Folders { Exefs: var ex } && ex == Path.Combine(home, "Desktop", "half", "exefs"),
                "romfs and exefs picked from two places.");
        Require(GameFiles.FromFolders(Path.Combine(home, "nope"), Path.Combine(home, "Desktop", "half", "exefs"), "x") == null,
                "Folders that are not there are not a dump.");
    }

    static void GameCheckChecks()
    {
        var ok = GameCheck.Read(new[] { "reading", "@@check ok This is the version Hookshot supports (build e0f3ccfa)." }, null, 0);
        Require(ok.Ok && ok.Text.StartsWith("This is the version"), "@@check ok is the supported version.");
        var bad = GameCheck.Read(new[] { "@@check bad This is build 12345678. Hookshot supports build e0f3ccfa." }, "warn", 0);
        Require(bad.Status == GameCheckStatus.OtherVersion && bad.Text.Contains("12345678"), "@@check bad is another version.");
        var err = GameCheck.Read(new[] { "@@error The .nup could not be read." }, null, 1);
        Require(err.Status == GameCheckStatus.Unreadable && err.Text == "The .nup could not be read.", "@@error: it could not be read.");
        Require(GameCheck.Read(Array.Empty<string>(), "Traceback: boom", 1).Text == "Traceback: boom"
                && GameCheck.Read(Array.Empty<string>(), null, 3).Text.Contains("exit code 3"), "Nothing said: stderr's last line, else the exit code.");

        var home = new LauncherHome(Path.Combine(scratch, "check-home"));
        var lin = new Toolchain("v3.0.0", Path.Combine(home.Toolchains, "v3.0.0"), ListKind.Linux, null);
        var c = MakeGameCommand.ForCheck(lin, new BuildSource.Nup("/d/game.nup"), Platform.Linux, home, "/L");
        Require(c.Arguments.SequenceEqual(new[] { "-I", lin.MakeGame, "--check", "--nup", "/d/game.nup", "--work", home.Work })
                && c.FileName == lin.Python && c.Environment["HOOKSHOT_TOOLS"] == lin.ToolsRoot && c.Environment["HOOKSHOT_NGX_SDK"] == null,
                "The check of a .nup: the tools' Python and environment, --work for its probe: " + string.Join(" ", c.Arguments));
        var f = MakeGameCommand.ForCheck(lin, new BuildSource.Folders("/d/romfs", "/d/exefs"), Platform.Linux, home, "/L", work: "/w");
        Require(f.Arguments.SequenceEqual(new[] { "-I", lin.MakeGame, "--check", "--exefs", "/d/exefs", "--work", "/w" }), "The check of folders: the exefs.");
        Throws<ArgumentException>(() => MakeGameCommand.ForCheck(lin, new BuildSource.Update("/g"), Platform.Linux, home, "/L"), "A folder is not checked.");
    }

    static ProcessStartInfo Shell(string script) =>
        OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/d", "/c", script } }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", script } };

    static async Task RunChecks()
    {
        var outLines = new List<string>();
        var errLines = new List<string>();
        var script = OperatingSystem.IsWindows()
            ? "echo @@stage read Reading& echo @@progress 0.5& echo oops 1>&2& echo @@done C:\\g& exit 0"
            : "echo '@@stage read Reading'; echo '@@progress 0.5'; echo oops >&2; echo '@@done /g'; exit 0";
        var code = await MakeGameRun.RunAsync(Shell(script), l => { lock (outLines) outLines.Add(l.TrimEnd()); }, l => { lock (errLines) errLines.Add(l.TrimEnd()); });
        Require(code == 0 && outLines.Count == 3 && outLines[0] == "@@stage read Reading" && errLines.SequenceEqual(new[] { "oops" }),
                "A run hands over stdout and stderr line by line: " + string.Join(" | ", outLines));
        var p = new BuildProgress(download: false, shaders: false);
        foreach (var l in outLines) p.Feed(l);
        Require(p.Finished, "The run's lines drive the bar.");
        Require(await MakeGameRun.RunAsync(Shell("exit 7"), _ => { }, _ => { }) == 7, "The exit code.");

        if (!OperatingSystem.IsWindows())
        {
            // (cancelled: the process and the one it started are stopped, and the run says so)
            var pidFile = Path.Combine(Folder("run"), "child.pid");
            using var cancel = new CancellationTokenSource();
            var started = Stopwatch.StartNew();
            var run = MakeGameRun.RunAsync(Shell($"sleep 60 & echo $! > '{pidFile}'; echo started; wait"), l => { if (l == "started") cancel.Cancel(); }, _ => { },
                                           cancel.Token);
            await ThrowsAsync<OperationCanceledException>(() => run, "A cancelled run did not say so.");
            Require(started.Elapsed < TimeSpan.FromSeconds(20), "The cancel stopped the run at once.");
            var pid = int.Parse(File.ReadAllText(pidFile).Trim());
            var gone = false;
            for (var i = 0; i < 50 && !gone; i++)
            {
                try { gone = Process.GetProcessById(pid).HasExited; }
                catch (ArgumentException) { gone = true; }
                if (!gone) await Task.Delay(100);
            }
            Require(gone, "The child process (a compiler) was stopped with the run.");
            var check = await MakeGameRun.CheckAsync(new MakeGameCommand("/bin/sh", new[] { "-c", "echo '@@check ok Supported.'" }, "/",
                                                                          new Dictionary<string, string?>()));
            Require(check.Ok && check.Text == "Supported.", "A check run's answer.");
        }
        var missing = await MakeGameRun.CheckAsync(new MakeGameCommand(Path.Combine(scratch, "no-python"), Array.Empty<string>(), scratch,
                                                                        new Dictionary<string, string?>()));
        Require(missing.Status == GameCheckStatus.Unreadable && missing.Text.StartsWith("The build tools could not start"), "Tools that do not start.");
    }

    static void ClockChecks()
    {
        Require(Durations.Clock(TimeSpan.FromSeconds(65)) == "1:05" && Durations.Clock(TimeSpan.FromSeconds(3725)) == "1:02:05"
                && Durations.Clock(TimeSpan.FromSeconds(-3)) == "0:00", "m:ss and h:mm:ss.");
        Require(Durations.About(30) == "Under a minute" && Durations.About(12 * 60 + 10) == "About 12 min" && Durations.About(80 * 60) == "About 1 h 20 min"
                && Durations.About(120 * 60) == "About 2 h", "About N min.");
        Require(Durations.Roughly(10) == "~1 min" && Durations.Roughly(300) == "~5 min", "~N min.");
        var now = new DateTime(2026, 11, 12, 19, 4, 0);
        Require(Durations.When(new DateTime(2026, 11, 12, 18, 2, 0), now) == "today 18:02" && Durations.When(new DateTime(2026, 11, 11, 22, 14, 0), now) == "yesterday 22:14"
                && Durations.When(new DateTime(2026, 11, 6, 9, 0, 0), now) == "6 Nov" && Durations.When(new DateTime(2025, 3, 2), now) == "2 Mar 2025",
                "When a save was made.");
        Require(Durations.Day(new DateTime(2026, 11, 3), now) == "3 Nov" && Durations.Day(now, now) == "today", "The day a build was made.");
        Require(Durations.Size(41231686042) == "38.4 GB" && Durations.Size(340L << 20) == "340 MB", "Sizes.");

        var p = new BuildProgress(download: true, shaders: true);
        var clock = new BuildClock();
        p.Download(0.5);
        clock.Observe(p, TimeSpan.FromSeconds(0));
        Require(clock.SecondsLeft(p) == null && clock.Of("download") == TimeSpan.Zero && clock.Of("read") == null, "Too early to say.");
        p.Feed("@@stage read Reading");
        clock.Observe(p, TimeSpan.FromSeconds(30));
        p.Feed("@@progress 1");
        clock.Observe(p, TimeSpan.FromSeconds(60));
        Require(clock.Of("download") == TimeSpan.FromSeconds(30) && clock.Of("read") == TimeSpan.FromSeconds(30), "Each stage's time: done and running.");
        var left = clock.SecondsLeft(p)!.Value;
        Require(Math.Abs(left - (60 / p.Overall * (1 - p.Overall))) < 1e-6, "Time left at the pace so far.");
        var lift = p.Stages.First(s => s.Id == "lift");
        Require(Math.Abs(clock.Estimate(p, lift)!.Value - 60 / p.Overall * lift.Weight) < 1e-6 && Math.Abs(p.Stages.Sum(s => s.Weight) - 1) < 1e-9,
                "A stage to come: its share at the pace so far.");
        p.Feed("@@stage compile Compiling");
        p.Feed("@@eta 600");
        clock.Observe(p, TimeSpan.FromSeconds(100));
        var after = p.Stages.SkipWhile(s => s.Id != "compile").Skip(1).Sum(s => s.Weight);
        Require(Math.Abs(clock.SecondsLeft(p)!.Value - (600 + after * 100 / p.Overall)) < 1e-6, "In the compile: make_game's eta, then the rest.");
        p.Feed("@@done /g");
        clock.Observe(p, TimeSpan.FromSeconds(200));
        Require(clock.SecondsLeft(p) == 0 && clock.Elapsed == TimeSpan.FromSeconds(200), "Done.");
    }

    static async Task ToolsSourceChecks()
    {
        var root = Folder("tools-source");
        var gh = new FakeFetcher();
        var tools = Zip(("nx2/make_game.py", "print('authored')\n", null, 0x1ED));
        var python = Zip(("bin/python3", "#!/bin/sh\n", null, 0x1ED));
        var list = "tag v3.0.0\n" + Line(gh, "https://x/t.zip", tools, "usr/share/hookshot") + "\ntree\n" +
                   Line(gh, "https://x/p.zip", python, "usr/python") + "\nfile bin/python3\n";
        var releases = new Releases(gh, Platform.Linux);
        gh.Texts[releases.ListUrl("v3.0.0")] = () => list;
        var chains = new Toolchains(Path.Combine(root, "toolchains"), gh);
        var seen = new List<ToolsDownloadProgress>();
        var t = await ToolsSource.EnsureAsync(chains, gh, Platform.Linux, "v3.0.0", null, seen.Add);
        Require(File.Exists(t.MakeGame) && seen.Count > 2 && seen[0].Fraction == 0 && seen.All(s => s.Total == tools.Length + python.Length)
                && Math.Abs(seen[^1].Fraction - 1) < 1e-9, "The version's tools from its release's list, with the bytes in all.");
        gh.Asked.Clear();
        seen.Clear();
        await ToolsSource.EnsureAsync(chains, gh, Platform.Linux, "v3.0.0", null, seen.Add);
        Require(gh.Asked.Count == 0 && seen.Single().Fraction == 1, "Installed tools are not asked for again.");
        gh.Texts[releases.ListUrl("v3.0.1")] = () => list;
        await ThrowsAsync<InvalidDataException>(() => ToolsSource.EnsureAsync(chains, gh, Platform.Linux, "v3.0.1"), "Another version's list was taken.");

        // (a list on this PC with file:// parts: HOOKSHOT_LAUNCHER_PARTS)
        var parts = Path.Combine(root, "parts ゲーム");
        Directory.CreateDirectory(parts);
        File.WriteAllBytes(Path.Combine(parts, "t.zip"), tools);
        File.WriteAllBytes(Path.Combine(parts, "p.zip"), python);
        var localList = Path.Combine(parts, "hookshot-linux.txt");
        File.WriteAllText(localList, "tag v3.1.0-local\n" +
                          $"part {ShaOf(tools)} {tools.Length} usr/share/hookshot {new Uri(Path.Combine(parts, "t.zip")).AbsoluteUri}\ntree\n" +
                          $"part {ShaOf(python)} {python.Length} usr/python {new Uri(Path.Combine(parts, "p.zip")).AbsoluteUri}\nfile bin/python3\n");
        Require(ToolsSource.LocalList(k => k == ToolsSource.LocalListVariable ? localList : null) == localList
                && ToolsSource.LocalList(_ => null) == null, "The local list is set by HOOKSHOT_LAUNCHER_PARTS.");
        var local = new LocalFileFetcher(gh);
        var chains2 = new Toolchains(Path.Combine(root, "toolchains2"), local);
        gh.Asked.Clear();
        var lt = await ToolsSource.EnsureAsync(chains2, local, Platform.Linux, "v3.1.0-local", localList);
        Require(File.ReadAllText(lt.MakeGame) == "print('authored')\n" && gh.Asked.Count == 0, "file:// parts are read from the disk, not fetched.");
        await ThrowsAsync<FetchException>(() => local.GetTextAsync(new Uri(Path.Combine(parts, "none.txt")).AbsoluteUri, null, default), "A missing file.");
        Require(await local.GetTextAsync(new Uri(localList).AbsoluteUri, null, default) == File.ReadAllText(localList), "A local text.");
        Throws<FormatException>(() => PartsManifest.ParseLinux(File.ReadAllText(localList)), "file:// links are only for a local list.");
    }

    static void BuildsFolderChecks()
    {
        var home = Path.Combine(scratch, "player");
        Func<string, string?> env = k => k == "HOME" ? home : null;
        Require(LauncherSettings.DefaultBuildsFolder(env) == Path.Combine(home, "Games", "Hookshot"), "Builds go to ~/Games/Hookshot by default.");
        var s = new LauncherSettings();
        Require(s.CheckUpdates && s.BuildsFolder == null && s.BuildsFolderOrDefault(env) == Path.Combine(home, "Games", "Hookshot"), "The defaults.");
        var path = Path.Combine(Folder("settings2"), "settings.json");
        s.BuildsFolder = "/mnt/fast/hookshot";
        s.CheckUpdates = false;
        s.Save(path);
        var t = LauncherSettings.Load(path);
        Require(t.BuildsFolder == "/mnt/fast/hookshot" && !t.CheckUpdates && t.BuildsFolderOrDefault(env) == "/mnt/fast/hookshot", "Saved and read back.");
        var taken = new HashSet<string> { Path.Combine("/b", "v3.0.0"), Path.Combine("/b", "v3.0.0 (2)") };
        Require(LauncherSettings.FolderFor("/b", "v3.1.0", taken.Contains) == Path.Combine("/b", "v3.1.0")
                && LauncherSettings.FolderFor("/b", "v3.0.0", taken.Contains) == Path.Combine("/b", "v3.0.0 (3)")
                && LauncherSettings.FolderFor("/b", "../x", _ => false) == Path.Combine("/b", "Hookshot"), "A new build's folder: its version's name, once.");
    }

    static void ReplaceChecks()
    {
        var root = Folder("replace");
        var path = Path.Combine(root, "builds.json");
        var r = BuildRegistry.Load(path);
        var a = r.Add(Path.Combine(root, "v3.0.0"), "v3.0.0", "release");
        var b = r.Add(Path.Combine(root, "v3.1.0-rc.2"), "v3.1.0-rc.2", "rc");
        a.Nup = "/d/oot.nup";
        a.Dlss = true;
        a.Bytes = 123;
        var id = a.Id;
        var u = r.Replace(id, Path.Combine(root, "v3.0.1"), "v3.0.1", "release");
        Require(u.Id == id && u.Default && r.Builds.Count == 2 && r.Builds[0] == u && u.Folder == Path.Combine(root, "v3.0.1") && u.Version == "v3.0.1"
                && u.Name == "v3.0.1" && u.Bytes == null && u.Nup == "/d/oot.nup", "An update takes its build's row, its place and the default.");
        r.SetDefault(b.Id);
        r.Replace(b.Id, Path.Combine(root, "v3.0.1"), "v3.1.0-rc.3", "rc");
        Require(r.Builds.Count == 1 && r.Builds[0].Id == b.Id && r.Builds[0].Default, "A row already on that folder goes (one entry per folder).");
        Throws<KeyNotFoundException>(() => r.Replace("nope", "/x", "v1", "release"), "No such build.");
        r.Save();
        var again = BuildRegistry.Load(path);
        Require(again.Builds[0].Nup == null && again.Builds[0].Version == "v3.1.0-rc.3", "Saved.");
        again.Builds[0].Dlss = true;
        again.Builds[0].Romfs = "/d/ゲーム/romfs";
        again.Builds[0].Bytes = 41231686042;
        again.Save();
        var third = BuildRegistry.Load(path).Builds[0];
        Require(third.Dlss == true && third.Fsr == null && third.Romfs == "/d/ゲーム/romfs" && third.Bytes == 41231686042, "What a build was made with, kept.");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Require(doc.RootElement.GetProperty("builds")[0].TryGetProperty("bytes", out _), "builds.json's new keys are camelCase.");

        // (Delete: never a folder that holds another build, the launcher's home or the home folder, nor a link)
        var games = Folder("delete-guard");
        var outer = Path.Combine(games, "Hookshot");
        var inner = Path.Combine(outer, "v3.0.0");
        Touch(Path.Combine(outer, GameFolder.StampName), "version=v2.3.0\n");
        Touch(Path.Combine(inner, GameFolder.StampName), "version=v3.0.0\n");
        Func<string, string?> env = k => k == "HOME" ? Path.Combine(games, "player") : null;
        Throws<IOException>(() => BuildRegistry.DeleteFolder(outer, Platforms.Current, new[] { inner }, env), "A 2.x folder holding a new build was deleted.");
        Throws<IOException>(() => BuildRegistry.DeleteFolder(outer, Platforms.Current, new[] { Path.Combine(outer, "launcher") }, env),
                            "A folder holding the launcher's home was deleted.");
        Throws<IOException>(() => BuildRegistry.DeleteFolder(outer, Platforms.Current, null, k => k == "HOME" ? Path.Combine(outer, "me") : null),
                            "A folder holding the home folder was deleted.");
        Require(Directory.Exists(inner), "Refused deletes left the folders.");
        if (!OperatingSystem.IsWindows())
        {
            var link = Path.Combine(games, "link");
            Directory.CreateSymbolicLink(link, inner);
            Throws<IOException>(() => BuildRegistry.DeleteFolder(link, Platforms.Current, null, env), "A link to a game folder was deleted.");
        }
        BuildRegistry.DeleteFolder(inner, Platforms.Current, new[] { outer, Path.Combine(games, "v3.1.0") }, env);
        Require(!Directory.Exists(inner) && Directory.Exists(outer), "A build inside another build's folder is deleted alone.");
    }

    static void DiskChecks()
    {
        var root = Folder("disk");
        File.WriteAllBytes(Path.Combine(root, "a"), new byte[1000]);
        Directory.CreateDirectory(Path.Combine(root, "sub", "deeper"));
        File.WriteAllBytes(Path.Combine(root, "sub", "deeper", "b"), new byte[24]);
        if (!OperatingSystem.IsWindows()) File.CreateSymbolicLink(Path.Combine(root, "link"), Path.Combine(root, "a"));
        Require(Disk.FolderBytes(root) == 1024 && Disk.FolderBytes(Path.Combine(root, "none")) == 0, "A folder's bytes, links not followed.");
        var free = Disk.FreeBytes(Path.Combine(root, "not", "made", "yet"));
        Require(free is > 0, "The free space where a folder would go, from the nearest folder that is there.");
        Require(Disk.DriveName(root).Length > 0, "The drive's name.");

        // (Free up space: only what make_game made in the build files folder, which may be one the player chose)
        var work = Folder("work");
        File.WriteAllBytes(Path.Combine(work, "system_headers.txt"), new byte[10]);
        Touch(Path.Combine(work, "objcache", "a.o"), new string('o', 100));
        Touch(Path.Combine(work, "sa", "gen", "f.c"), new string('c', 1000));
        Touch(Path.Combine(work, "My notes.txt"), "keep");
        Touch(Path.Combine(work, "photos", "p.jpg"), "keep");
        Require(Disk.WorkBytes(work) == 1110 && Disk.WorkBytes(Path.Combine(work, "none")) == 0, "What Free up space would let go.");
        Disk.FreeWork(work);
        Require(!Directory.Exists(Path.Combine(work, "objcache")) && !Directory.Exists(Path.Combine(work, "sa")) && !File.Exists(Path.Combine(work, "system_headers.txt"))
                && File.Exists(Path.Combine(work, "My notes.txt")) && File.Exists(Path.Combine(work, "photos", "p.jpg")) && Disk.WorkBytes(work) == 0,
                "Free up space let go of the build's files and nothing else.");

        var g = Folder("dlss");
        Require(!GameFolder.HasDlss(g), "No ngx/: no DLSS.");
        Touch(Path.Combine(g, "ngx", "libnvidia-ngx-dlss.so.310.2.1"));
        Require(GameFolder.HasDlss(g), "NVIDIA's runtime in ngx/: built with DLSS.");
    }

    static void SaveRefusalChecks()
    {
        var root = Folder("refusals");
        var src = Path.Combine(root, "src");
        Touch(Path.Combine(src, "slot_00", "progress.sav"));
        Touch(Path.Combine(src, "account.sav"));
        var dst = Path.Combine(root, "game", "profile", "save", "0123456789abcdef0123456789abcdef");
        Touch(Path.Combine(dst, "account.sav"));
        try
        {
            Saves.Copy(src, dst, Path.Combine(root, "b"), "import", "x", running: () => new[] { "Hookshot" });
            Require(false, "A copy went ahead while a game ran.");
        }
        catch (SaveFailure f) { Require(f.Reason == SaveRefusal.Running && f.Who.SequenceEqual(new[] { "Hookshot" }), "Refused: a game runs."); }
        Touch(Path.Combine(root, "game", "profile", "save-tx", "commit"));
        try
        {
            Saves.Copy(src, dst, Path.Combine(root, "b"), "import", "x", running: () => Array.Empty<string>());
            Require(false, "A copy went ahead with a save waiting.");
        }
        catch (SaveFailure f) { Require(f.Reason == SaveRefusal.Waiting, "Refused: NX2navi has a save waiting."); }
        try { Saves.Copy(src, src, Path.Combine(root, "b"), "import", "x"); }
        catch (SaveFailure f) { Require(f.Reason == SaveRefusal.Other, "Any other refusal."); }

        var now = new DateTime(2026, 11, 12, 19, 0, 0);
        var slots = new[] { new SaveSlot("slot_00", new DateTime(2026, 11, 6, 9, 0, 0)), new SaveSlot("slot_01", new DateTime(2026, 11, 11, 22, 14, 0)),
                            new SaveSlot("slot_02", null) };
        Require(SaveText.Long(slots, now) == "3 slots, saved yesterday 22:14" && SaveText.Short(slots, now) == "3 saves, last yesterday 22:14"
                && SaveText.Long(slots[..1], now) == "1 slot, saved 6 Nov" && SaveText.Long(Array.Empty<SaveSlot>(), now) == "No saves yet",
                "A save folder summed up.");
        Require(SaveText.Count(0) == "no saves" && SaveText.Count(1) == "1 save slot" && SaveText.Number("slot_02") == 3 && SaveText.Number("x") == 0,
                "Slot counts and numbers.");
    }

    static void ShortcutChecks()
    {
        // (the quoting's backslashes doubled again for the file's string escapes: an argument's " is \\" in the file, its \ is \\\\)
        Require(Shortcuts.Quote("/a b/\"c\" $x 100%") == "\"/a b/\\\\\"c\\\\\" \\\\$x 100%%\"" && Shortcuts.Quote(@"a\b`") == "\"a\\\\\\\\b\\\\`\"",
                "Exec quoting.");
        var root = Folder("shortcuts");
        var home = Path.Combine(root, "home");
        Directory.CreateDirectory(Path.Combine(home, "Desktop"));
        Func<string, string?> env = k => k switch { "HOME" => home, "XDG_DATA_HOME" => Path.Combine(root, "data"), _ => null };
        var game = Path.Combine(root, "Games", "ゲーム v3.0.0");
        Directory.CreateDirectory(game);
        Shortcuts.WriteLinux("/apps/Hookshot.AppImage", game, desktop: true, menu: true, getenv: env);
        var (d, m) = Shortcuts.LinuxPaths(env);
        Require(m == Path.Combine(root, "data", "applications", "hookshot.desktop") && File.Exists(d) && File.Exists(m), "Both shortcuts made.");
        var text = File.ReadAllText(m);
        Require(text.Contains("Exec=\"/apps/Hookshot.AppImage\" --play \"" + game + "\"") && text.StartsWith("[Desktop Entry]\n"),
                "The entry starts the launcher's Play of that folder: " + text);
        Shortcuts.WriteLinux("/apps/Hookshot.AppImage", null, desktop: false, menu: true, icon: "/i.png", getenv: env);
        Require(!File.Exists(d) && File.Exists(m), "Unticked: removed.");
        Require(File.ReadAllText(m).Contains("Exec=\"/apps/Hookshot.AppImage\" --play\n") && File.ReadAllText(m).Contains("Icon=/i.png\n")
                && !File.ReadAllText(m).Contains("Path="), "With no folder: Play of the default build.");

        // (SystemInfo: the GPU makers from /sys/class/drm)
        var drm = Folder("drm");
        Touch(Path.Combine(drm, "card1", "device", "vendor"), "0x10de\n");
        Touch(Path.Combine(drm, "card0", "device", "vendor"), "0x1002\n");
        Touch(Path.Combine(drm, "card0-DP-1", "device", "vendor"), "0x8086\n");
        Touch(Path.Combine(drm, "card2", "device", "vendor"), "0x1af4\n");
        Require(SystemInfo.GpuVendors(Platform.Linux, drm).SequenceEqual(new[] { SystemInfo.Amd, SystemInfo.Nvidia })
                && SystemInfo.GpuVendors(Platform.Windows, drm).Count == 0 && SystemInfo.GpuVendors(Platform.Linux, Path.Combine(drm, "none")).Count == 0,
                "The GPU makers, by card; outputs and other devices left out.");
        Require(SystemInfo.MemoryGb() > 0 && SystemInfo.Threads() > 0, "Memory and threads.");
    }
}
