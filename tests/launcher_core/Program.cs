using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HookshotLauncher.Core;

/// The launcher's core (launcher/Core) over authored inputs: no game, no network, no window.
///     LauncherCoreTests <vectors.json> <tests/fixtures/launcher> <scratch folder>
/// vectors.json: tests/test_update_tracks.py's TRACKS, ORDER and LIST, read there with ast (test_launcher_core.py), so the
/// launcher is held to the same vectors as the game, the setup and the Linux updater.
static partial class Program
{
    static int checks;
    static string scratch = "";

    static void Require(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        checks++;
    }

    static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { checks++; return; }
        throw new Exception(message);
    }

    static async Task ThrowsAsync<T>(Func<Task> action, string message) where T : Exception
    {
        try { await action(); }
        catch (T) { checks++; return; }
        throw new Exception(message);
    }

    static string Folder(string name)
    {
        var path = Path.Combine(scratch, name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
    }

    static async Task<int> Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("usage: LauncherCoreTests <vectors.json> <fixtures folder> <scratch folder>");
            return 2;
        }
        using var vectors = JsonDocument.Parse(File.ReadAllText(args[0]));
        var fixtures = args[1];
        scratch = args[2];
        Directory.CreateDirectory(scratch);

        VersionChecks(vectors.RootElement);
        await ReleaseChecks(vectors.RootElement.GetProperty("list").GetString()!);
        ManifestChecks(fixtures);
        await ToolchainChecks();
        RegistryChecks();
        ImportChecks(fixtures);
        SettingsChecks();
        ProgressChecks();
        CommandChecks();
        UpdatePolicyChecks();
        CommandLineChecks();
        SavesChecks.Run(Folder("saves"));
        Utf8Checks();
        await ScreenChecks();
        checks += SavesChecks.Count;
        Console.WriteLine($"launcher core: {checks} checks passed");
        return 0;
    }

    // ---- Versions: the shared vectors ----

    static void VersionChecks(JsonElement v)
    {
        var tracks = v.GetProperty("tracks").EnumerateArray().Select(t => (t[0].GetString()!, t[1].GetString()!)).ToList();
        Require(tracks.Count >= 8, "The track vectors did not come through.");
        foreach (var (version, track) in tracks.Concat(new[] { ("nightly", "release"), ("rc", "release"), ("dev", "release") }))
            Require(Versions.TrackOf(version) == track, $"TrackOf({version}) is not {track}.");
        Require(Versions.TrackOf(null) == Versions.Release, "No version is a release's track.");
        var order = v.GetProperty("order").EnumerateArray().Select(o => (o[0].GetString()!, o[1].GetString()!)).ToList();
        Require(order.Count >= 6, "The order vectors did not come through.");
        foreach (var (newer, older) in order)
        {
            Require(Versions.Newer(newer, older), $"{newer} is not newer than {older}.");
            Require(!Versions.Newer(older, newer), $"{older} is newer than {newer}.");
            Require(!Versions.Newer(newer, newer), $"{newer} is newer than itself.");
            Require(Versions.Compare(newer, older) == 1 && Versions.Compare(older, newer) == -1, "Compare disagrees with Newer.");
        }
        var unordered = v.GetProperty("unordered").EnumerateArray().Select(o => (o[0].GetString()!, o[1].GetString()!)).ToList();
        Require(unordered.Count >= 6, "The unordered vectors did not come through.");
        foreach (var (a, b) in unordered)
            Require(!Versions.Newer(a, b) && !Versions.Newer(b, a) && Versions.Compare(a, b) == 0, $"{a} and {b} were ordered.");
        Require(Versions.Shown("nightly-2029-c544828f") == "Nightly 2029 \u00b7 c544828f"
                && Versions.Shown("nightly-2029-c544828f-dirty") == "Nightly 2029 \u00b7 c544828f-dirty"
                && Versions.Shown("v2.1.0-rc.3") == "v2.1.0-rc.3" && Versions.Shown("nightly-2029-c544828") == "nightly-2029-c544828"
                && Versions.Shown(null) == "", "A nightly as players see it: its number and its commit.");
        Require(!Versions.Newer("v2.1.0", "dev") && !Versions.Newer("dev", "v2.1.0") && !Versions.Newer("v99999999999999999999.0.0", "v1.0.0"),
                "A name that is no version is never newer, nor older.");
        Require(Versions.ReleaseOf("nightly-2029-c544828f") == "nightly" && Versions.ReleaseOf("v2.1.0-rc.1-3-g1a2b3c4") == "nightly"
                && Versions.ReleaseOf("v2.1.0-rc.2") == "v2.1.0-rc.2",
                "ReleaseOf: a nightly's files are on the rolling nightly.");
        // (Updater.Target: a release candidate is never taken back to an older release)
        Require(Versions.Target("v2.0.14", "v2.1.0-rc.1", "rc") == "v2.1.0-rc.1", "An RC was taken back to an older release.");
        Require(Versions.Target("v2.1.0-rc.2", "v2.1.0-rc.1", "rc") == "v2.1.0-rc.2", "An RC was not offered the next RC.");
        Require(Versions.Target("v2.1.0", "v2.1.0-rc.2", "rc") == "v2.1.0", "An RC was not offered its release.");
        Require(Versions.Target("nightly-2030-bbbbbbbb", "nightly-2029-aaaaaaaa", "nightly") == "nightly-2030-bbbbbbbb"
                && Versions.Target("nightly-2029-aaaaaaaa", "v2.1.0-rc.1-3-gaaaaaaa", "nightly") == "nightly-2029-aaaaaaaa",
                "A nightly was not offered the next nightly.");
        Require(Versions.Target("nightly-2028-bbbbbbbb", "nightly-2029-aaaaaaaa", "nightly") == "nightly-2029-aaaaaaaa",
                "A nightly was taken back to an older nightly.");
        Require(Versions.Target("v2.0.14", "nightly-2029-aaaaaaaa", "release") == "v2.0.14"
                && Versions.Target("nightly-2029-aaaaaaaa", "v2.1.0", "nightly") == "nightly-2029-aaaaaaaa",
                "A switch between the nightly and the releases takes that track's newest.");
        Require(Versions.Target("v2.0.14", "v2.1.0-rc.1", "release") == "v2.0.14" && Versions.Target(null, "v2.0.1", "release") == "v2.0.1",
                "A switch of track takes its newest; no answer keeps the tools'.");

        using var list = JsonDocument.Parse(v.GetProperty("list").GetString()!);
        var tags = list.RootElement.EnumerateArray().Select(r => r.GetProperty("tag_name").GetString()).ToList();
        Require(Versions.NewestRelease(tags) == "v2.1.0-rc.10", "The newest release candidate of the list.");
        Require(Versions.NewestRelease(new[] { "rc", "nightly" }) == null, "The rolling releases are not versions.");
        Require(Versions.IsTagName("v2.1.0-rc.1-3-g1a2b3c4") && Versions.IsTagName("nightly-2029-c544828f-dirty") && !Versions.IsTagName("..") && !Versions.IsTagName("a/b")
                && !Versions.IsTagName(new string('a', 129)), "The tag name rule.");
    }

    // ---- Releases, over a fake GitHub ----

    sealed class FakeFetcher : IFetcher
    {
        public readonly Dictionary<string, Func<string>> Texts = new();
        public readonly Dictionary<string, byte[]> Files = new();
        public readonly List<string> Asked = new();
        public Action<long>? DuringDownload;

        public Task<string> GetTextAsync(string url, string? accept, CancellationToken cancel)
        {
            Asked.Add(url);
            return Texts.TryGetValue(url, out var f) ? Task.FromResult(f()) : throw new FetchException("not found: " + url, 404);
        }

        public async Task DownloadAsync(string url, Stream to, Action<long>? progress, CancellationToken cancel)
        {
            Asked.Add(url);
            if (!Files.TryGetValue(url, out var bytes)) throw new FetchException("not found: " + url, 404);
            for (var at = 0; at < bytes.Length; at += 7)
            {
                cancel.ThrowIfCancellationRequested();
                var n = Math.Min(7, bytes.Length - at);
                await to.WriteAsync(bytes.AsMemory(at, n), cancel);
                progress?.Invoke(at + n);
                DuringDownload?.Invoke(at + n);
            }
        }
    }

    static Func<string> Fail(int? status) => () => throw new FetchException("authored failure", status);

    static async Task ReleaseChecks(string list)
    {
        static Task NoWait(TimeSpan t, CancellationToken c) => Task.CompletedTask;
        var gh = new FakeFetcher();
        var linuxNightly = "https://github.com/NotRexed/Hookshot/releases/download/nightly/hookshot-linux.txt";
        gh.Texts[Releases.LatestApi] = () => """{"tag_name": "v2.0.14"}""";
        gh.Texts[Releases.ReleasesApi] = () => list;
        gh.Texts[linuxNightly] = () => $"tag nightly-2029-c544828f\npart {new string('a', 64)} 10 usr/share/hookshot https://example.invalid/a.zip\ntree\n";
        var linux = new Releases(gh, Platform.Linux, NoWait);
        Require(await linux.NewestAsync("release") == "v2.0.14", "release: the latest release.");
        Require(gh.Asked.SequenceEqual(new[] { Releases.LatestApi }), "release asks the latest release only.");
        Require(await linux.NewestAsync("rc") == "v2.1.0-rc.10", "rc: the newest release candidate of the list.");
        Require(await linux.NewestAsync("nightly") == "nightly-2029-c544828f", "nightly: the nightly list's tag.");
        Require(linux.ListUrl("nightly-2029-c544828f") == linuxNightly && linux.ListUrl("v2.1.0-rc.1-3-g1a2b3c4") == linuxNightly
                && linux.ListUrl("v2.1.0-rc.2") == "https://github.com/NotRexed/Hookshot/releases/download/v2.1.0-rc.2/hookshot-linux.txt"
                && linux.ListUrl(null) == "https://github.com/NotRexed/Hookshot/releases/latest/download/hookshot-linux.txt",
                "Each version's list is on the release that holds it.");
        // (a draft is no release: the newest is the next one)
        gh.Texts[Releases.ReleasesApi] = () => """[{"tag_name":"v9.0.0","draft":true},{"tag_name":"v2.1.0-rc.2"}]""";
        Require(await linux.NewestAsync("rc") == "v2.1.0-rc.2", "A draft was taken for the newest.");
        // (the API's limit: the latest release's list, a file download)
        gh.Texts[Releases.LatestApi] = Fail(403);
        gh.Texts[Releases.ReleasesApi] = Fail(403);
        gh.Texts["https://github.com/NotRexed/Hookshot/releases/latest/download/hookshot-linux.txt"] = () => "tag v2.0.15\n";
        gh.Asked.Clear();
        Require(await linux.NewestAsync("release") == "v2.0.15", "The list's tag when the API refuses.");
        Require(gh.Asked.Count(u => u == Releases.LatestApi) == 1, "A 403 from the API is not asked again.");
        Require(await linux.NewestAsync("rc") == "v2.0.15", "rc with no API answer: the latest release.");
        gh.Texts["https://github.com/NotRexed/Hookshot/releases/download/rc/hookshot-linux.txt"] = () => "tag v2.1.0-rc.3\n";
        Require(await linux.NewestAsync("rc") == "v2.1.0-rc.3", "rc with no API answer: the rolling rc when it is newer.");
        // (server errors: tried again)
        var tries = 0;
        gh.Texts[Releases.LatestApi] = () => ++tries < 3 ? throw new FetchException("authored", 502) : """{"tag_name":"v2.0.16"}""";
        Require(await linux.NewestAsync("release") == "v2.0.16" && tries == 3, "A 5xx is tried again.");
        var none = new Releases(new FakeFetcher(), Platform.Windows, NoWait);
        foreach (var channel in Versions.Channels)
            Require(await none.NewestAsync(channel) == null, $"{channel}: no answer is null, not an exception.");
        var windows = new FakeFetcher();
        windows.Texts["https://github.com/NotRexed/Hookshot/releases/download/nightly/hookshot-setup.txt"] = () => "tag nightly-2030-abcdef01\nrun x\n";
        Require(await new Releases(windows, Platform.Windows, NoWait).NewestAsync("nightly") == "nightly-2030-abcdef01",
                "Windows reads the setup list's tag.");
    }

    // ---- the parts lists ----

    const string Sha = "552191b5754c49a5a20723276a6fc56541ea1922c4c50b7b669022333fc1b40f";

    static void ManifestChecks(string fixtures)
    {
        var setup = PartsManifest.ParseSetup($"""
            tag v2.3.0
            part {Sha} 100 . - - https://github.com/NotRexed/Hookshot/releases/download/builder-parts/HookshotBuilder-app-552191b5754c.zip
            part {Sha} 191037046 HookshotBuilder/tools llvm-mingw-20261104-ucrt-x86_64 llvm-mingw https://github.com/mstorsjo/llvm-mingw/releases/download/20261104/llvm-mingw-20261104-ucrt-x86_64.zip
            run HookshotBuilder/HookshotBuilder.exe
            """);
        Require(setup.Tag == "v2.3.0" && setup.Run == "HookshotBuilder/HookshotBuilder.exe" && setup.Parts.Count == 2, "The setup list's lines.");
        Require(setup.Parts[0].Dest == "." && setup.Parts[0].From == null && setup.Parts[1].From == "llvm-mingw-20261104-ucrt-x86_64"
                && setup.Parts[1].To == "llvm-mingw" && setup.Parts[1].Size == 191037046, "The setup list's part fields.");
        foreach (var bad in new[]
                 {
                     $"tag v1\npart {Sha} 1 . - - https://x/y\n",                                   // (no run)
                     $"tag v1\nrun a\n",                                                              // (no part)
                     $"tag ..\nrun a\npart {Sha} 1 . - - https://x/y\n",
                     $"tag v1\nrun a\npart {Sha.ToUpperInvariant()} 1 . - - https://x/y\n",
                     $"tag v1\nrun a\npart {Sha} 1 . - - http://x/y\n",
                     $"tag v1\nrun a\npart {Sha} 1 ../up - - https://x/y\n",
                     $"tag v1\nrun a\npart {Sha} 1 . a/b - https://x/y\n",
                     $"tag v1\nrun /abs\npart {Sha} 1 . - - https://x/y\n",
                     $"tag v1\nrun a\npart {Sha} -1 . - - https://x/y\n",
                     $"tag v1\nrun a\npart {Sha} 1 . - https://x/y\n",
                 })
            Throws<FormatException>(() => PartsManifest.ParseSetup(bad), "A setup list the setup refuses was read: " + bad);
        Require(PartsManifest.ParseSetup($"# note\n\ntag v1\nrun a\nfuture line\npart {Sha} 1 . - - https://x/y\n").Parts.Count == 1,
                "The setup reads past lines it does not know.");

        var linux = PartsManifest.ParseLinux($"tag nightly-2029-c544828f\npart {Sha} 10 usr/share/hookshot https://example.invalid/a.zip\ntree\n" +
                                             $"part {Sha} 20 usr/python https://example.invalid/b.zip\nfile bin/python3\nfile lib/x.py\n");
        Require(linux.Kind == ListKind.Linux && linux.Parts[0].Files == null && linux.Parts[1].Files!.SequenceEqual(new[] { "bin/python3", "lib/x.py" }),
                "The Linux list's tree and file lines.");
        foreach (var bad in new[]
                 {
                     $"tag v1\nfile x\n",
                     $"tag v1\npart {Sha} 10 a https://x/a.zip\nunknown\n",
                     $"part {Sha} 10 a https://x/a.zip\n",
                     $"tag v1\npart {Sha} 10 a https://x/a.zip extra\n",
                     $"tag v1\npart {Sha} 10 /abs https://x/a.zip\n",
                     $"tag v1\npart {Sha} 10 a/../.. https://x/a.zip\n",
                     $"tag v1\npart {Sha} 10 a https://x/a.zip\nfile ../x\n",
                     $"tag v1\npart {Sha} 10 a file:///tmp/a.zip\n",
                     $"tag v1\npart {Sha} 10 a https://x/a.zip\ntree\nfile x\n",
                 })
            Throws<FormatException>(() => PartsManifest.ParseLinux(bad), "A list linux_update.parse_list refuses was read: " + bad);
        Require(PartsManifest.ParseLinux($"tag v1\npart {Sha} 10 a file:///tmp/a.zip\n", local: true).Parts.Count == 1, "A local list's file link.");

        var launcher = PartsManifest.ParseLauncher(File.ReadAllText(Path.Combine(fixtures, "hookshot-launcher.txt")));
        Require(launcher.Tag == "v3.0.0" && launcher.Format == 1 && launcher.Assets.Count == 3
                && launcher.Assets[0].Platform == "windows-x64" && launcher.Assets[1].Size == 73400320, "The launcher list fixture.");
    }

    // ---- Toolchains ----

    static byte[] Zip(params (string Name, string? Text, string? Link, int Mode)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text, link, mode) in entries)
            {
                var e = zip.CreateEntry(name);
                e.ExternalAttributes = (link != null ? 0xA000 | 0x1FF : 0x8000 | mode) << 16;
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(link ?? text);
            }
        return ms.ToArray();
    }

    static string ShaOf(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    static string Line(FakeFetcher f, string url, byte[] bytes, string dest)
    {
        f.Files[url] = bytes;
        return $"part {ShaOf(bytes)} {bytes.Length} {dest} {url}";
    }

    static async Task ToolchainChecks()
    {
        var root = Folder("toolchains");
        var gh = new FakeFetcher();
        var tools = Zip(("nx2/make_game.py", "print('authored')\n", null, 0x1ED), ("nx2/link.py", null, "make_game.py", 0),
                        ("README", "authored\n", null, 0x1A4));
        var python = Zip(("bin/python3", "#!/bin/sh\n", null, 0x1ED));
        var python2 = Zip(("bin/python3", "#!/bin/sh\n# 2\n", null, 0x1ED));
        var sysroot = Zip(("lib/libc.so.6", "authored", null, 0x1A4), ("lib64/ld.so", null, "/lib/libc.so.6", 0));
        var v1 = PartsManifest.ParseLinux("tag v2.3.0\n" +
                                          Line(gh, "https://x/tools.zip", tools, "usr/share/hookshot") + "\ntree\n" +
                                          Line(gh, "https://x/python.zip", python, "usr/python") + "\nfile bin/python3\n" +
                                          Line(gh, "https://x/sysroot.zip", sysroot, Toolchains.SysrootPart) + "\ntree\n");
        var chains = new Toolchains(root, gh);
        var seen = new List<double>();
        var t1 = await chains.InstallAsync(v1, p => seen.Add(p.Fraction));
        Require(File.ReadAllText(t1.MakeGame) == "print('authored')\n" && t1.Python == Path.Combine(root, "v2.3.0", "usr", "python", "bin", "python3")
                && File.Exists(t1.Python), "The tools are where make_game.py and Python are looked for.");
        Require(seen.Count > 3 && seen.Zip(seen.Skip(1)).All(p => p.Second >= p.First) && Math.Abs(seen[^1] - 1) < 1e-9,
                "The install's progress goes up to 1.");
        Require(new FileInfo(Path.Combine(t1.ToolsRoot, "nx2", "link.py")).LinkTarget == "make_game.py", "A part's relative link.");
        var rebased = new FileInfo(Path.Combine(root, "v2.3.0", "usr", "lib", "hookshot", "sysroot", "lib64", "ld.so"));
        Require(rebased.LinkTarget == "../lib/libc.so.6", "A sysroot's rooted link is rebased inside it: " + rebased.LinkTarget);
        if (!OperatingSystem.IsWindows())
            Require((File.GetUnixFileMode(t1.MakeGame) & UnixFileMode.UserExecute) != 0
                    && (File.GetUnixFileMode(Path.Combine(t1.ToolsRoot, "README")) & (UnixFileMode.UserExecute | UnixFileMode.OtherWrite)) == 0,
                    "The parts' modes: executables stay so, nothing is world-writable.");
        Require(chains.IsInstalled("v2.3.0") && !Directory.Exists(Path.Combine(root, "v2.3.0.part")), "Installed, and no staging left.");

        // (the next version: the unchanged parts come from v2.3.0, only the changed one is downloaded)
        gh.Asked.Clear();
        var v2 = PartsManifest.ParseLinux("tag v2.3.1\n" +
                                          Line(gh, "https://x/tools.zip", tools, "usr/share/hookshot") + "\ntree\n" +
                                          Line(gh, "https://x/python2.zip", python2, "usr/python") + "\nfile bin/python3\n" +
                                          Line(gh, "https://x/sysroot.zip", sysroot, Toolchains.SysrootPart) + "\ntree\n");
        var t2 = await chains.InstallAsync(v2);
        Require(gh.Asked.SequenceEqual(new[] { "https://x/python2.zip" }), "Unchanged parts were downloaded again: " + string.Join(", ", gh.Asked));
        Require(File.ReadAllText(t2.MakeGame) == "print('authored')\n" && File.ReadAllText(t2.Python).Contains("# 2")
                && new FileInfo(Path.Combine(t2.ToolsRoot, "nx2", "link.py")).LinkTarget == "make_game.py"
                && new FileInfo(Path.Combine(root, "v2.3.1", "usr", "lib", "hookshot", "sysroot", "lib64", "ld.so")).LinkTarget == "../lib/libc.so.6",
                "A kept part is the same files, links included.");
        Require(File.ReadAllText(t1.Python) == "#!/bin/sh\n", "The older version kept its own Python.");

        // (a part whose bytes differ from its list: nothing is left, the version is not installed)
        var python3 = Zip(("bin/python3", "#!/bin/sh\n# 3\n", null, 0x1ED));
        var tampered = PartsManifest.ParseLinux("tag v2.4.0\n" + Line(gh, "https://x/tools.zip", tools, "usr/share/hookshot") + "\ntree\n" +
                                                $"part {ShaOf(python3)} {python3.Length} usr/python https://x/python3.zip\nfile bin/python3\n");
        gh.Files["https://x/python3.zip"] = Zip(("bin/python3", "#!/bin/sh\n# 4\n", null, 0x1ED));
        await ThrowsAsync<InvalidDataException>(() => chains.InstallAsync(tampered), "A part with another checksum was installed.");
        Require(!Directory.Exists(Path.Combine(root, "v2.4.0")) && !Directory.Exists(Path.Combine(root, "v2.4.0.part"))
                && !Directory.EnumerateFiles(root, "*.zip.part", SearchOption.AllDirectories).Any(), "A failed install left files.");
        gh.Files["https://x/python3.zip"] = python3;
        var longer = PartsManifest.ParseLinux("tag v2.4.0\n" + $"part {ShaOf(python3)} {python3.Length - 1} usr/python https://x/python3.zip\nfile bin/python3\n");
        await ThrowsAsync<InvalidDataException>(() => chains.InstallAsync(longer), "A part larger than its list was installed.");
        Require(!Directory.Exists(Path.Combine(root, "v2.4.0.part")), "A part too large left its staging.");
        // (a zip with a name outside its folder, or that its list does not name)
        var evil = Zip(("../evil", "x", null, 0x1A4));
        var slip = PartsManifest.ParseLinux("tag v2.4.0\n" + Line(gh, "https://x/evil.zip", evil, "usr/share/hookshot") + "\ntree\n");
        await ThrowsAsync<InvalidDataException>(() => chains.InstallAsync(slip), "A zip's ../ name was unpacked.");
        Require(!File.Exists(Path.Combine(root, "evil")) && !File.Exists(Path.Combine(root, "v2.4.0.part", "usr", "share", "evil")), "../evil was written.");
        var unlisted = PartsManifest.ParseLinux("tag v2.4.0\n" + Line(gh, "https://x/tools.zip", tools, "usr/share/hookshot") + "\nfile nx2/make_game.py\n");
        await ThrowsAsync<InvalidDataException>(() => chains.InstallAsync(unlisted), "A zip's file its list does not name was unpacked.");
        var outward = Zip(("x", null, "../../../../outside", 0));
        var outwardList = PartsManifest.ParseLinux("tag v2.4.0\n" + Line(gh, "https://x/out.zip", outward, "usr/share/hookshot") + "\ntree\n");
        await ThrowsAsync<InvalidDataException>(() => chains.InstallAsync(outwardList), "A link out of the version's folder was made.");
        var absolute = Zip(("x", null, "/etc/passwd", 0));
        var absoluteList = PartsManifest.ParseLinux("tag v2.4.0\n" + Line(gh, "https://x/abs.zip", absolute, "usr/share/hookshot") + "\ntree\n");
        await ThrowsAsync<InvalidDataException>(() => chains.InstallAsync(absoluteList), "An absolute link outside the sysroot was made.");
        Require(!Directory.Exists(Path.Combine(root, "v2.4.0")) && !Directory.Exists(Path.Combine(root, "v2.4.0.part")), "A refused part left files.");

        // (cancelled half way through a download: nothing is left)
        using (var cancel = new CancellationTokenSource())
        {
            var big = Zip(("nx2/make_game.py", string.Concat(Enumerable.Range(0, 200).Select(_ => Guid.NewGuid().ToString())), null, 0x1A4));
            var cancelled = PartsManifest.ParseLinux("tag v2.5.0\n" + Line(gh, "https://x/big.zip", big, "usr/share/hookshot") + "\ntree\n");
            gh.DuringDownload = n => { if (n > 100) cancel.Cancel(); };
            await ThrowsAsync<OperationCanceledException>(() => chains.InstallAsync(cancelled, null, cancel.Token), "The cancel was not heard.");
            gh.DuringDownload = null;
            Require(!Directory.Exists(Path.Combine(root, "v2.5.0")) && !Directory.Exists(Path.Combine(root, "v2.5.0.part"))
                    && !Directory.EnumerateFiles(root, "*.zip.part", SearchOption.AllDirectories).Any(), "A cancelled install left files.");
        }

        // (Windows' list: dest . with the zip's HookshotBuilder/ folder, and llvm-mingw's top folder renamed)
        var app = Zip(("HookshotBuilder/HookshotBuilder.exe", "MZ", null, 0x1A4), ("HookshotBuilder/tools/nx2/make_game.py", "w", null, 0x1A4),
                      ("HookshotBuilder/tools/python/python.exe", "MZ", null, 0x1A4));
        var llvm = Zip(("llvm-mingw-20261104-ucrt-x86_64/bin/clang.exe", "MZ", null, 0x1A4));
        var setupLines = "tag v2.3.0-rc.2\n" + Line(gh, "https://x/app.zip", app, ".").Replace(" . ", " . - - ") + "\n" +
                         $"part {ShaOf(llvm)} {llvm.Length} HookshotBuilder/tools llvm-mingw-20261104-ucrt-x86_64 llvm-mingw https://x/llvm.zip\n" +
                         "run HookshotBuilder/HookshotBuilder.exe\n";
        gh.Files["https://x/llvm.zip"] = llvm;
        var w = await chains.InstallAsync(PartsManifest.ParseSetup(setupLines));
        Require(w.ToolsRoot == Path.Combine(root, "v2.3.0-rc.2", "HookshotBuilder", "tools") && File.Exists(w.MakeGame)
                && w.Python == Path.Combine(w.ToolsRoot, "python", "python.exe")
                && File.Exists(Path.Combine(w.ToolsRoot, "llvm-mingw", "bin", "clang.exe")), "The Windows list's layout.");
        Require(chains.Find("v2.3.0-rc.2")?.Kind == ListKind.Setup, "The record says which list made it.");

        Require(chains.Installed().SequenceEqual(new[] { "v2.3.0", "v2.3.0-rc.2", "v2.3.1" }), "The installed versions.");
        Directory.CreateDirectory(Path.Combine(root, "v9.part"));
        var removed = chains.Prune(new[] { "v2.3.1" });
        Require(chains.Installed().SequenceEqual(new[] { "v2.3.1" }) && removed.Contains("v2.3.0") && removed.Contains("v9.part"),
                "Prune keeps the versions builds use and removes the rest.");
        Require(File.ReadAllText(Path.Combine(root, "v2.3.1", "usr", "share", "hookshot", "nx2", "make_game.py")) == "print('authored')\n",
                "A version that shared files with a removed one keeps them.");
    }

    // ---- BuildRegistry ----

    static void Game(string folder, string? version, bool built = true, string? language = null)
    {
        Directory.CreateDirectory(Path.Combine(folder, "bin"));
        if (version != null) File.WriteAllText(Path.Combine(folder, "hookshot_version.txt"), $"version={version}\nbuilder=/authored/builder\n");
        if (built) File.WriteAllText(GameFolder.ExePath(folder, Platforms.Current), "authored");
        if (language != null) File.WriteAllText(Path.Combine(folder, "language.txt"), language + "\n");
    }

    static void RegistryChecks()
    {
        var home = Folder("registry");
        var path = Path.Combine(home, "builds.json");
        var clock = new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
        var r = BuildRegistry.Load(path, () => clock);
        Require(!r.Existed && r.Builds.Count == 0 && r.Default == null, "No builds.json: an empty list, and a first start.");
        var a = r.Add(Path.Combine(home, "a"), "v2.3.0", null);
        var b = r.Add(Path.Combine(home, "b"), "v2.3.1-rc.1", "rc");
        var c = r.Add(Path.Combine(home, "c"), "nightly-2031-abcdef01", null, name: "Nightly");
        Require(r.Default == a && r.Builds.Count(x => x.Default) == 1, "The first build is the default.");
        Require(a.Channel == "release" && b.Channel == "rc" && c.Channel == "nightly" && c.Name == "Nightly" && a.Name == "a",
                "A channel defaults to the version's track.");
        Require(r.Add(Path.Combine(home, "a") + Path.DirectorySeparatorChar, "v2.3.2", null) == a && r.Builds.Count == 3 && a.Version == "v2.3.2",
                "One entry per folder.");
        r.SetDefault(c.Id);
        Require(r.Default == c && r.Builds.Count(x => x.Default) == 1, "Set default.");
        Require(r.Remove(c.Id) && r.Default == a && r.Builds.Count == 2, "The default removed: the first one left is it.");
        Require(!r.Remove("nope"), "Removing what is not there.");
        Throws<KeyNotFoundException>(() => r.SetDefault("nope"), "A default that is no build.");
        r.SetChannel(b.Id, "nightly");
        r.Save();
        Require(!File.Exists(path + ".tmp"), "The atomic write left its temporary file.");
        var again = BuildRegistry.Load(path);
        Require(again.Existed && again.Builds.Count == 2 && again.Default?.Id == a.Id && again.Find(b.Id)?.Channel == "nightly"
                && again.Find(a.Id)?.Created == clock, "builds.json round trip.");
        var text = File.ReadAllText(path);
        Require(text.Contains("\"format\": 1") && text.Contains("\"folder\"") && text.Contains("\"default\": true"), "builds.json's keys.");
        r.Remove(a.Id);
        r.Remove(b.Id);
        Require(r.Default == null && r.Builds.Count == 0, "An empty list has no default.");

        // (two marked defaults on disk: one is kept)
        File.WriteAllText(path, """{"format":1,"builds":[{"id":"x","folder":"/x","default":true,"channel":"weird"},{"id":"y","folder":"/y","default":true}]}""");
        var two = BuildRegistry.Load(path);
        Require(two.Builds.Count(x => x.Default) == 1 && two.Default!.Id == "x" && two.Find("x")!.Channel == "release", "Exactly one default after load.");
        File.WriteAllText(path, "{ not json");
        var broken = BuildRegistry.Load(path);
        Require(broken.Problem != null && broken.Builds.Count == 0 && File.Exists(path + ".broken"), "A broken builds.json is kept aside.");

        // (deleting files: only a game folder)
        var game = Path.Combine(home, "game");
        Game(game, "v2.3.0");
        var other = Path.Combine(home, "other");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "keep.txt"), "x");
        Throws<IOException>(() => BuildRegistry.DeleteFolder(other, Platforms.Current), "A folder that is no game was deleted.");
        Require(File.Exists(Path.Combine(other, "keep.txt")), "The other folder's file is gone.");
        Throws<IOException>(() => BuildRegistry.DeleteFolder(Path.GetPathRoot(home)!, Platforms.Current), "A drive's root.");
        BuildRegistry.DeleteFolder(game, Platforms.Current);
        Require(!Directory.Exists(game), "The game folder was not deleted.");
        var unbuilt = Path.Combine(home, "unbuilt");
        Game(unbuilt, "v2.3.0", built: false);
        Require(GameFolder.IsGameFolder(unbuilt, Platforms.Current) && !GameFolder.IsBuilt(unbuilt, Platforms.Current), "A stamped folder not built.");
    }

    // ---- the hand-over ----

    static void ImportChecks(string fixtures)
    {
        var importWindows = File.ReadAllText(Path.Combine(fixtures, "import-windows.json"));
        var importLinux = File.ReadAllText(Path.Combine(fixtures, "import-linux.json"));
        var w = LegacyImport.Parse(importWindows);
        Require(w.Folder == @"C:\Users\player\Games\Hookshot" && w.Version == "v2.3.0-rc.2" && w.Track == "rc" && w.Default
                && w.Settings.Nup == @"C:\Users\player\Downloads\game.nup" && w.Settings.MemoryGb == 12 && w.Settings.Work == @"D:\HookshotWork"
                && w.Settings.Dlss && w.Settings.Language == "fr-FR" && w.Settings.PrepareShaders, "import-windows.json read.");
        var l = LegacyImport.Parse(importLinux);
        Require(l.Track == "nightly" && l.Settings.Romfs == "/home/player/dump/romfs" && l.Settings.Nup == null && !l.Settings.PrepareShaders
                && l.Settings.DlssLicense && !l.Settings.Dlss && l.Settings.Jobs == null, "import-linux.json read.");
        Require(JsonNode.DeepEquals(LegacyImport.ToJson(w), JsonNode.Parse(importWindows)) && JsonNode.DeepEquals(LegacyImport.ToJson(l), JsonNode.Parse(importLinux)),
                "An import file written back is the same file.");
        foreach (var bad in new[]
                 {
                     importLinux.Replace("\"format\": 1", "\"format\": 2"),
                     importLinux.Replace("\"jobs\": null,", ""),
                     importLinux.Replace("\"track\": \"nightly\"", "\"track\": \"beta\""),
                     importLinux.Replace("\"dlss\": false", "\"dlss\": \"no\""),
                     importLinux.Replace("\"folder\": \"/home/player/Games/Hookshot\"", "\"folder\": null"),
                     "[]",
                 })
            Throws<FormatException>(() => LegacyImport.Parse(bad), "An import file outside the contract was read.");

        // (the upgrader's files are reproduced from the 2.x settings files: the same reading on both sides)
        var winHome = Folder("winbuilder");
        File.Copy(Path.Combine(fixtures, "builder-settings.json"), Path.Combine(winHome, "builder-settings.json"));
        File.WriteAllText(Path.Combine(winHome, "work.txt"), "D:\\HookshotWork\r\n");
        var winGame = Path.Combine(winHome, "game");
        Game(winGame, "v2.3.0-rc.2", language: "fr-FR");
        var fromWindows = LegacyImport.FromWindowsSettings(Path.Combine(winHome, "builder-settings.json"),
                                                           f => f == @"C:\Users\player\Games\Hookshot" ? winGame : f)!;
        // (builder-settings.json saved rc; the import file's track is what the player chose: rc, the same here)
        Require(JsonNode.DeepEquals(LegacyImport.ToJson(fromWindows), JsonNode.Parse(importWindows)),
                "builder-settings.json does not give import-windows.json: " + LegacyImport.ToJson(fromWindows).ToJsonString());
        var linHome = Folder("linbuilder");
        var linGame = Path.Combine(linHome, "game");
        Game(linGame, "nightly-2025-1a2b3c4d", language: "en-GB");   // (the ini's language wins over the folder's)
        var fromLinux = LegacyImport.FromLinuxIni(Path.Combine(fixtures, "builder.ini"), f => f == "/home/player/Games/Hookshot" ? linGame : f)!;
        Require(JsonNode.DeepEquals(LegacyImport.ToJson(fromLinux), JsonNode.Parse(importLinux)),
                "builder.ini does not give import-linux.json: " + LegacyImport.ToJson(fromLinux).ToJsonString());
        File.WriteAllText(Path.Combine(linHome, "nup.ini"), $"kind=nup\nnup=/a.nup\nromfs=/r\nout={linGame}\nprepare=1\nlanguage=\n");
        var nup = LegacyImport.FromLinuxIni(Path.Combine(linHome, "nup.ini"))!;
        Require(nup.Settings.Nup == "/a.nup" && nup.Settings.Romfs == null && nup.Settings.PrepareShaders && nup.Settings.Language == "en-GB"
                && nup.Version == "nightly-2025-1a2b3c4d" && nup.Track == "nightly", "kind=nup, and the folder's language.");

        // (at start: the import files consumed, then the 2.x settings on a first start)
        var home = new LauncherHome(Folder("home-import"));
        Directory.CreateDirectory(home.Import);
        var game = Path.Combine(home.Root, "Games", "Hookshot");
        Game(game, "nightly-2025-1a2b3c4d");
        File.WriteAllText(Path.Combine(home.Import, "a.json"), importLinux.Replace("/home/player/Games/Hookshot", game.Replace("\\", "\\\\")));
        File.WriteAllText(Path.Combine(home.Import, "b.json"), importWindows);   // (a folder not on this PC: shown as not built)
        File.WriteAllText(Path.Combine(home.Import, "c.json"), "{ broken");
        File.WriteAllText(Path.Combine(home.Import, "d.json.part"), "{ half written");
        var registry = BuildRegistry.Load(home.Builds);
        var config = Folder("xdg-config");
        var report = LegacyImport.Run(home, registry, Platform.Linux, k => k == "XDG_CONFIG_HOME" ? config : null);
        Require(report.Folders.Count == 2 && report.Problems.Count == 1 && !report.ReadLegacy && report.SettingsImported, "The import's report.");
        Require(!File.Exists(Path.Combine(home.Import, "a.json")) && !File.Exists(Path.Combine(home.Import, "b.json"))
                && File.Exists(Path.Combine(home.Import, "c.json.bad")) && File.Exists(Path.Combine(home.Import, "d.json.part")),
                "Imported files are deleted, a broken one set aside, a .part left to its writer.");
        var saved = BuildRegistry.Load(home.Builds);
        var first = saved.FindByFolder(game)!;
        Require(saved.Builds.Count == 2 && first.Default && first.Channel == "nightly" && first.Version == "nightly-2025-1a2b3c4d",
                "The first import is the default, on its track.");
        Require(saved.FindByFolder(@"C:\Users\player\Games\Hookshot") is { Default: false, Channel: "rc", Version: "v2.3.0-rc.2" },
                "The second import keeps the launcher's default.");
        var s = LauncherSettings.Load(home.Settings);
        Require(s.Romfs == "/home/player/dump/romfs" || s.Nup == @"C:\Users\player\Downloads\game.nup", "The imported settings were saved.");
        Require(s.Nup == @"C:\Users\player\Downloads\game.nup" && s.MemoryGb == 12 && s.Dlss && s.Language == "fr-FR" && s.Work == @"D:\HookshotWork",
                "The last import's settings are the launcher's.");
        // (a second start: nothing to import, the list as it was, the settings the launcher's own)
        s.Language = "ja";
        s.Save(home.Settings);
        File.WriteAllText(Path.Combine(home.Import, "e.json"), importLinux.Replace("/home/player/Games/Hookshot", game.Replace("\\", "\\\\")));
        var later = LegacyImport.Run(home, BuildRegistry.Load(home.Builds), Platform.Linux, k => k == "XDG_CONFIG_HOME" ? config : null);
        Require(later.Folders.Count == 1 && !later.SettingsImported && LauncherSettings.Load(home.Settings).Language == "ja"
                && BuildRegistry.Load(home.Builds).Builds.Count == 2, "A later import updates its build and leaves the launcher's settings.");
        // (a list that cannot be saved: the import file stays for the next start)
        var stuck = new LauncherHome(Folder("home-stuck"));
        Directory.CreateDirectory(stuck.Builds);   // (a folder where builds.json goes: the save fails)
        Directory.CreateDirectory(stuck.Import);
        File.WriteAllText(Path.Combine(stuck.Import, "a.json"), importLinux.Replace("/home/player/Games/Hookshot", game.Replace("\\", "\\\\")));
        // (IOException or UnauthorizedAccessException, as the system says it: the window shows either)
        Throws<SystemException>(() => LegacyImport.Run(stuck, BuildRegistry.Load(stuck.Builds), Platform.Linux, k => k == "XDG_CONFIG_HOME" ? config : null),
                            "An unsaved list said nothing.");
        Require(File.Exists(Path.Combine(stuck.Import, "a.json")), "A hand-over that could not be saved was deleted.");

        // (the first start with no builds.json and no import: the 2.x Builder's own settings)
        var fresh = new LauncherHome(Folder("home-legacy"));
        var cfg = Path.Combine(config, "hookshot");
        Directory.CreateDirectory(cfg);
        File.WriteAllText(Path.Combine(cfg, "builder.ini"), File.ReadAllText(Path.Combine(fixtures, "builder.ini")).Replace("/home/player/Games/Hookshot", linGame));
        var legacy = LegacyImport.Run(fresh, BuildRegistry.Load(fresh.Builds), Platform.Linux, k => k == "XDG_CONFIG_HOME" ? config : null);
        Require(legacy.ReadLegacy && legacy.Folders.SequenceEqual(new[] { linGame }) && File.Exists(fresh.Builds), "The 2.x Builder's settings on a first start.");
        Require(BuildRegistry.Load(fresh.Builds).Default is { Channel: "nightly" } && LauncherSettings.Load(fresh.Settings) is { Romfs: "/home/player/dump/romfs", PrepareShaders: false },
                "The legacy build and settings.");
        var once = LegacyImport.Run(fresh, BuildRegistry.Load(fresh.Builds), Platform.Linux, k => k == "XDG_CONFIG_HOME" ? config : null);
        Require(!once.ReadLegacy, "The 2.x settings are read once.");
        var winFresh = new LauncherHome(Folder("home-legacy-windows"));
        var local = Folder("localappdata");
        Directory.CreateDirectory(Path.Combine(local, "HookshotBuilder"));
        File.WriteAllText(Path.Combine(local, "HookshotBuilder", "builder-settings.json"), """{"Out":null,"Nup":"C:\\g.nup","Dlss":true,"DlssLicense":false}""");
        var noFolder = LegacyImport.Run(winFresh, BuildRegistry.Load(winFresh.Builds), Platform.Windows, k => k == "LOCALAPPDATA" ? local : null);
        Require(noFolder.ReadLegacy && noFolder.Folders.Count == 0 && LauncherSettings.Load(winFresh.Settings) is { Nup: @"C:\g.nup", Dlss: false },
                "Settings with no install folder: the settings alone, and no DLSS without its license.");
    }

    static void SettingsChecks()
    {
        var path = Path.Combine(Folder("settings"), "settings.json");
        var d = LauncherSettings.Load(path);
        Require(d.MoveSaves && d.PrepareShaders && !d.Dlss && d.DefaultChannel == "release" && d.MemoryGb == null, "The defaults.");
        var s = new LauncherSettings
        {
            Nup = "/n.nup", Language = "de", Dlss = true, DlssLicense = true, Fsr = true, FsrLicense = true, PrepareShaders = false,
            MemoryGb = 16, BuildWorkers = 6, Work = "/w", DefaultChannel = "nightly", MoveSaves = false,
        };
        s.Save(path);
        var t = LauncherSettings.Load(path);
        Require(t.Nup == "/n.nup" && t.Language == "de" && t.Dlss && t.DlssLicense && t.Fsr && t.FsrLicense && !t.PrepareShaders && t.MemoryGb == 16
                && t.BuildWorkers == 6 && t.Work == "/w" && t.DefaultChannel == "nightly" && !t.MoveSaves, "settings.json round trip.");
        File.WriteAllText(path, """{"dlss":true,"dlssLicense":false,"defaultChannel":"beta","memoryGb":0}""");
        var u = LauncherSettings.Load(path);
        Require(!u.Dlss && u.DefaultChannel == "release" && u.MemoryGb == null, "Saved values outside their range are not taken.");
        File.WriteAllText(path, "nope");
        Require(LauncherSettings.Load(path).PrepareShaders, "Unreadable settings are the defaults.");
    }

    // ---- make_game.py's lines and the overall bar ----

    static void ProgressChecks()
    {
        Require(MakeGameProtocol.Parse("@@stage compile Compiling") is MakeGameLine.Stage { Id: "compile", Text: "Compiling" }, "@@stage");
        Require(MakeGameProtocol.Parse("@@progress 0.250") is MakeGameLine.Progress { Fraction: 0.25 }, "@@progress");
        Require(MakeGameProtocol.Parse("@@progress 7") is MakeGameLine.Progress { Fraction: 1 }, "@@progress is clamped");
        Require(MakeGameProtocol.Parse("@@progress nan") is MakeGameLine.Plain, "@@progress nan is no progress");
        Require(MakeGameProtocol.Parse("@@plan 12 3400") is MakeGameLine.Plan { Changed: 12, Total: 3400 }, "@@plan");
        Require(MakeGameProtocol.Parse("@@file a.c 1.5 new") is MakeGameLine.File { Name: "a.c", Kind: "new" }, "@@file");
        Require(MakeGameProtocol.Parse("@@eta 95") is MakeGameLine.Eta { Seconds: 95 }, "@@eta");
        Require(MakeGameProtocol.Parse("@@times 600 400 -1") is MakeGameLine.Times { Previous: -1 }, "@@times");
        Require(MakeGameProtocol.Parse("@@check bad Not the supported version") is MakeGameLine.Check { Ok: false, Text: "Not the supported version" }, "@@check");
        Require(MakeGameProtocol.Parse("@@done /g/Hookshot é") is MakeGameLine.Done { Folder: "/g/Hookshot é" }, "@@done");
        Require(MakeGameProtocol.Parse("@@error Out of space\r") is MakeGameLine.Error { Text: "Out of space" }, "@@error");
        Require(MakeGameProtocol.Parse("the shaders were not prepared now (no usable Vulkan here): the game prepares them at its first start")
                is MakeGameLine.NotPrepared { Reason: "no usable Vulkan here" }, "make_game's not-prepared line");
        Require(MakeGameProtocol.Parse("build: up to 16 compilers") is MakeGameLine.Plain, "A plain line");

        var transcript = new[]
        {
            "@@stage read Reading your game files", "@@progress 0.5", "@@progress 1.0", "@@stage lift Translating the game to PC code",
            "@@progress 0.3", "@@progress 0.2", "@@progress 1", "@@stage compile Compiling", "@@plan 3 10", "@@eta 120",
            "@@file a.c 1.0 new", "@@progress 0.4", "@@file b.c 1.0 cached", "@@file c.c 2.0 again", "@@progress 0.9",
            "@@stage lift Translating the game to PC code", "@@stage compile Compiling", "@@progress 1",
            "@@stage finish Putting the game folder together", "@@stage shaders Preparing shaders", "@@progress 0.5",
            "@@times 600 400 -1", "@@done /authored/game",
        };
        var p = new BuildProgress(download: true, shaders: true);
        Require(p.Stages.Select(s => s.Id).SequenceEqual(new[] { "download", "read", "lift", "compile", "finish", "shaders" }), "The stages of a first build.");
        var bars = new List<double> { p.Overall };
        for (var i = 0; i <= 10; i++)
        {
            p.Download(i / 10.0);
            bars.Add(p.Overall);
        }
        Require(Math.Abs(p.Overall - 0.04) < 1e-9, "The tools' download fills its weight.");
        foreach (var line in transcript)
        {
            p.Feed(line);
            bars.Add(p.Overall);
            if (line.StartsWith("@@eta")) Require(p.Eta == 120, "The ETA passes through.");
            if (line.StartsWith("@@file c.c")) Require(p.Stages.First(s => s.Id == "compile").Note == "2 of 3 changed files", "The compile's note.");
        }
        Require(bars.Zip(bars.Skip(1)).All(x => x.Second >= x.First), "The bar went back: " + string.Join(" ", bars.Select(b => b.ToString("0.000"))));
        Require(bars[^1] == 1 && p.Finished && p.Folder == "/authored/game" && p.Stages.All(s => s.State == StageState.Done), "The bar ends full.");
        Require(Math.Abs(bars[bars.Count - 3] - 0.96) < 1e-9, "The shaders' half: 0.92 + 0.08 / 2.");

        var q = new BuildProgress(download: false, shaders: true);
        q.Feed("@@stage read x");
        Require(q.Overall == 0 && q.Current == "read", "With no download, the read starts at 0.");
        q.Feed("@@stage compile x");
        Require(Math.Abs(q.Overall - (0.22 - 0.04) / 0.96) < 1e-9, "The weights scale to the stages a build has.");
        q.Feed("the shaders were not prepared now (\"Prepare shaders now\" is unticked): the game prepares them at its first start");
        q.Feed("@@done /g");
        var shaders = q.Stages.First(s => s.Id == "shaders");
        Require(q.Overall == 1 && shaders.State == StageState.Skipped && shaders.Note!.Contains("unticked"), "Shaders left to the first start.");
        var r = new BuildProgress(download: false, shaders: true);
        r.Feed("@@stage finish x");
        r.Feed("@@done /g");
        Require(r.Stages.First(s => s.Id == "shaders").State == StageState.Skipped, "No @@stage shaders is no check mark.");
        var e = new BuildProgress(download: false, shaders: false, update: true);
        e.Feed("@@stage read x");
        e.Feed("@@error No space left");
        Require(e.Error == "No space left" && !e.Finished && e.Overall < 1 && e.Stages[0].Title.Contains("kept"), "A failed update.");
    }

    // ---- make_game.py's command line ----

    static void CommandChecks()
    {
        var home = new LauncherHome(Path.Combine(scratch, "cmd-home"));
        var win = new Toolchain("v2.3.0", Path.Combine(home.Toolchains, "v2.3.0"), ListKind.Setup, "HookshotBuilder/HookshotBuilder.exe");
        var request = new BuildRequest(new BuildSource.Nup("C:/g/game.nup"), "C:/Games/Hookshot", Language: "fr", Jobs: 6, MemoryGb: 16,
                                       PrepareShaders: false, Dlss: true, DlssLicense: true, Fsr: true, FsrLicense: true);
        var w = MakeGameCommand.For(win, request, Platform.Windows, home, "C:/L/HookshotLauncher.exe", d => d.EndsWith("cpm"));
        Require(w.FileName == Path.Combine(win.ToolsRoot, "python", "python.exe") && w.WorkingDirectory == win.ToolsRoot, "Windows: the tools' Python.");
        Require(w.Arguments.SequenceEqual(new[]
                {
                    "-I", win.MakeGame, "--nup", "C:/g/game.nup", "--out", "C:/Games/Hookshot", "--language", "fr", "--work", home.Work,
                    "--no-prepare-shaders", "--fetch-ngx-sdk", "--accept-nvidia-license", "--fsr", "--fetch-fsr-runtime", "--accept-amd-license",
                }), "Windows arguments: " + string.Join(" ", w.Arguments));
        Require(w.Environment["HOOKSHOT_JOBS"] == "6" && w.Environment["HOOKSHOT_MEM_LIMIT_GB"] == "16" && w.Environment["HOOKSHOT_NGX_SDK"] == home.NgxSdk
                && w.Environment["HOOKSHOT_FFX_SDK"] == home.FfxSdk && w.Environment["HOOKSHOT_BUILDER_EXE"] == "C:/L/HookshotLauncher.exe"
                && w.Environment["RX_CPM"] == Path.Combine(win.ToolsRoot, "cpm") && !w.Environment.ContainsKey("EMSDK")
                && w.Environment["PYTHONIOENCODING"] == "utf-8" && w.Environment["HOOKSHOT_SETUP_BASE"] == null, "Windows environment.");
        var licenceless = MakeGameCommand.For(win, request with { DlssLicense = false, FsrLicense = false, Jobs = null, MemoryGb = null, PrepareShaders = true },
                                              Platform.Windows, home, "L", _ => false);
        Require(!licenceless.Arguments.Any(a => a.StartsWith("--fetch") || a.StartsWith("--accept") || a == "--fsr" || a == "--no-prepare-shaders")
                && licenceless.Environment["HOOKSHOT_NGX_SDK"] == null && licenceless.Environment["HOOKSHOT_FFX_SDK"] == null
                && !licenceless.Environment.ContainsKey("HOOKSHOT_JOBS") && licenceless.Environment["HOOKSHOT_MEM_LIMIT_GB"] == null,
                "No license, no DLSS or FSR; Automatic sets nothing.");

        var lin = new Toolchain("v2.3.0", Path.Combine(home.Toolchains, "v2.3.0"), ListKind.Linux, null);
        var l = MakeGameCommand.For(lin, request with { Source = new BuildSource.Folders("/d/romfs", "/d/exefs"), Out = "/g", Work = "/w" },
                                    Platform.Linux, home, "/L/Hookshot.AppImage");
        Require(l.FileName == Path.Combine(lin.Folder, "usr", "python", "bin", "python3") && l.WorkingDirectory == Path.Combine(lin.Folder, "usr", "share", "hookshot"),
                "Linux: the AppImage layout's Python.");
        Require(l.Arguments.SequenceEqual(new[]
                {
                    "-I", lin.MakeGame, "--romfs", "/d/romfs", "--exefs", "/d/exefs", "--out", "/g", "--language", "fr", "--work", "/w",
                    "--jobs", "6", "--no-prepare-shaders", "--fetch-ngx-sdk", "--accept-nvidia-license",
                }), "Linux arguments (no FSR): " + string.Join(" ", l.Arguments));
        Require(!l.Environment.ContainsKey("HOOKSHOT_JOBS") && l.Environment["HOOKSHOT_MEM_LIMIT_GB"] == "16"
                && l.Environment["HOOKSHOT_VK_PREBUILT"] == null && l.Environment["HOOKSHOT_TOOLS"] == lin.ToolsRoot
                && l.Environment["HOOKSHOT_SYSROOT"] == Path.Combine(lin.LibRoot, "sysroot") && l.Environment["EMSDK"] == Path.Combine(lin.LibRoot, "emsdk")
                && l.Environment["HOOKSHOT_FFX_SDK"] == null, "Linux environment (DLSS builds its own Vulkan back end).");
        var u = MakeGameCommand.For(lin, new BuildRequest(new BuildSource.Update("/g"), null), Platform.Linux, home, "/L");
        Require(u.Arguments.SequenceEqual(new[] { "-I", lin.MakeGame, "--update", "/g", "--work", home.Work })
                && u.Environment["HOOKSHOT_VK_PREBUILT"] == Path.Combine(lin.LibRoot, "hookshot_vk.so"), "An update: the folder, no --out or --language.");
        Throws<ArgumentOutOfRangeException>(() => MakeGameCommand.For(lin, request with { Jobs = 0 }, Platform.Linux, home, "/L"), "Zero workers.");
        var si = l.StartInfo();
        Require(si.ArgumentList.SequenceEqual(l.Arguments) && !si.Environment.ContainsKey("HOOKSHOT_VK_PREBUILT") && si.RedirectStandardOutput,
                "The process start info.");

        var env = new Dictionary<string, string>
        {
            ["PATH"] = "/usr/bin", ["HOOKSHOT_TOOLS"] = "/t", ["PYTHONHOME"] = "/p", ["APPDIR"] = "/a", ["LD_LIBRARY_PATH"] = "/l", ["HOME"] = "/h",
        };
        var game = GamePlay.Environment(Platform.Linux, env);
        Require(game.Keys.OrderBy(k => k).SequenceEqual(new[] { "HOME", "HOOKSHOT_NO_UPDATE_CHECK", "PATH" }) && game["HOOKSHOT_NO_UPDATE_CHECK"] == "1",
                "Linux Play drops the AppImage's and the tools' environment.");
        Require(GamePlay.Environment(Platform.Windows, env).ContainsKey("HOOKSHOT_TOOLS"), "Windows Play keeps the environment, as the Builder's.");
        var play = GamePlay.StartInfo("/g", Platform.Linux, env);
        Require(play.FileName == Path.Combine("/g", "bin", "hookshot") && play.WorkingDirectory == "/g" && !play.Environment.ContainsKey("APPDIR"),
                "Play starts bin/hookshot in its folder.");
    }

    static void UpdatePolicyChecks()
    {
        Require(UpdatePolicy.UpdateTo("v2.3.0", "release", "v2.3.1") == "v2.3.1", "release: the newer release.");
        Require(UpdatePolicy.UpdateTo("v2.3.1", "release", "v2.3.1") == null, "release: up to date.");
        Require(UpdatePolicy.UpdateTo("v2.4.0-rc.1", "release", "v2.3.1") == null, "release: an RC newer than the release is not taken back.");
        Require(UpdatePolicy.UpdateTo("v2.4.0-rc.1", "rc", "v2.4.0-rc.2") == "v2.4.0-rc.2", "rc: the next RC.");
        Require(UpdatePolicy.UpdateTo("v2.4.0-rc.2", "rc", "v2.4.0") == "v2.4.0", "rc: its release.");
        Require(UpdatePolicy.UpdateTo("v2.4.0-rc.2", "rc", "v2.3.9") == null, "rc: never back to an older release.");
        Require(UpdatePolicy.UpdateTo("v2.3.0", "rc", "v2.4.0-rc.1") == "v2.4.0-rc.1", "A release on the rc channel: the next RC.");
        Require(UpdatePolicy.UpdateTo("nightly-2029-aaaaaaaa", "nightly", "nightly-2031-bbbbbbbb") == "nightly-2031-bbbbbbbb", "nightly: a later one.");
        Require(UpdatePolicy.UpdateTo("nightly-2031-bbbbbbbb", "nightly", "nightly-2029-aaaaaaaa") == null, "nightly: not an older one.");
        Require(UpdatePolicy.UpdateTo("nightly-2029-aaaaaaaa-dirty", "nightly", "nightly-2029-aaaaaaaa") == null,
                "nightly: a changed build of the nightly's own commit is not taken back to it.");
        Require(UpdatePolicy.UpdateTo("v2.4.0-rc.1-3-gaaaaaaa", "nightly", "nightly-2029-aaaaaaaa") == "nightly-2029-aaaaaaaa",
                "nightly: one named the old way (git describe) takes the first new one.");
        Require(UpdatePolicy.UpdateTo("v2.5.0", "nightly", "nightly-2029-aaaaaaaa") == null
                && UpdatePolicy.UpdateTo("nightly-2029-aaaaaaaa", "release", "v2.5.0") == null,
                "A nightly and a release are not ordered: neither is offered for the other.");
        Require(UpdatePolicy.UpdateTo(null, "release", "v2.3.1") == null && UpdatePolicy.UpdateTo("v2.3.0", "release", null) == null,
                "No version on either side: no offer.");
        Require(UpdatePolicy.UpdateTo("dev", "release", "v2.3.1") == "v2.3.1" && UpdatePolicy.UpdateTo("1a2b3c4d", "nightly", "nightly-2029-aaaaaaaa")
                == "nightly-2029-aaaaaaaa", "A version on no track (a hash, dev): its channel's newest (docs/versioning.md).");
        Require(UpdatePolicy.ChannelOf(null, "v2.4.0-rc.1") == "rc" && UpdatePolicy.ChannelOf("nightly", "v2.4.0") == "nightly", "A build's channel.");
        var b = new BuildEntry { Version = "v2.4.0-rc.1", Channel = "rc" };
        Require(UpdatePolicy.UpdateTo(b, new Dictionary<string, string?> { ["release"] = "v2.5.0", ["rc"] = "v2.4.0-rc.3" }) == "v2.4.0-rc.3"
                && UpdatePolicy.UpdateTo(b, new Dictionary<string, string?> { ["release"] = "v2.5.0" }) == null, "Each build asks its own channel.");
    }

    static void CommandLineChecks()
    {
        StartRequest P(params string[] a) => StartRequest.Parse(a);
        Require(P() is { Kind: StartKind.Window, Folder: null, Problem: null }, "No arguments: the window.");
        Require(P("--launch", "/g") is { Kind: StartKind.Launch, Folder: "/g" }, "--launch");
        Require(P("--update", "/g", "--tag", "v2.3.0", "--fetched") is { Kind: StartKind.Update, Folder: "/g", Tag: "v2.3.0", Fetched: true, Problem: null },
                "--update --tag --fetched");
        Require(P("--update", "C:\\Games\\Hookshot é") is { Kind: StartKind.Update, Tag: null, Fetched: false }, "--update alone");
        Require(P("--play") is { Kind: StartKind.Play, Folder: null }, "--play: the default build");
        Require(P("--play", "/g") is { Kind: StartKind.Play, Folder: "/g" }, "--play <folder>");
        Require(P("--launch") is { Kind: StartKind.Window, Problem: not null }, "--launch with no folder");
        Require(P("--update", "--tag", "v1") is { Problem: not null }, "--update with no folder");
        Require(P("--tag", "../x", "--update", "/g") is { Tag: null, Problem: not null }, "A tag that is no version name");
        Require(P("--play", "/g", "--update", "/h") is { Problem: not null }, "Two at once");
        Require(P("--launch", "/g", "--tag", "v1") is { Problem: not null }, "--tag with --launch");
        Require(P("--some-avalonia-flag", "--launch", "/g").Ignored.SequenceEqual(new[] { "--some-avalonia-flag" }), "Unknown arguments are kept aside.");
    }

    // ---- a folder named outside any one code page ----

    static void Utf8Checks()
    {
        Require(scratch.Contains("ゲーム Zélda 🎮"), "The scratch folder is not the UTF-8 one: " + scratch);
        var home = new LauncherHome(Path.Combine(scratch, "home ゲーム"));
        var game = Path.Combine(scratch, "ゲーム Zélda 🎮 game");
        Game(game, "v2.3.0", language: "ja");
        Require(GameFolder.Version(game) == "v2.3.0" && GameFolder.Language(game) == "ja" && GameFolder.IsBuilt(game, Platforms.Current), "The stamp read.");
        var r = BuildRegistry.Load(home.Builds);
        r.Add(game, GameFolder.Version(game), null, name: "Zélda 🎮");
        r.Save();
        var back = BuildRegistry.Load(home.Builds);
        Require(back.Default is { Name: "Zélda 🎮" } d && d.Folder == game && back.FindByFolder(game) != null, "builds.json keeps the UTF-8 folder.");
        Require(!File.ReadAllBytes(home.Builds).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "builds.json has no BOM.");
        Require(StartRequest.Parse(new[] { "--launch", game }).Folder == game, "The command line keeps it.");
        var s = new LauncherSettings { Nup = Path.Combine(game, "ゲーム.nup") };
        s.Save(home.Settings);
        Require(LauncherSettings.Load(home.Settings).Nup == Path.Combine(game, "ゲーム.nup"), "settings.json keeps it.");
    }
}
