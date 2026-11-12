using HookshotLauncher.Core;

/// launcher/Core/Problems.cs, ProblemReport.cs and ReportSender.cs: the messages make_game.py and the game write mapped to a
/// problem and its fix, a crash found once in a build's logs, a report with no home folder or user name left in it, and the
/// request the inbox gets (an inbox of our own on localhost, http refused). No game, no network beyond 127.0.0.1.
static partial class Program
{
    static async Task ProblemChecks()
    {
        // build failures: make_game.py's own messages
        Require(Problems.FromBuild("Files of the builder's own compiler are missing (clang.exe in tools). Your antivirus most likely removed them").Kind == ProblemKind.Antivirus,
                "the antivirus message: Antivirus");
        Require(Problems.FromBuild("This is build 1a2b3c4d. Hookshot supports build e0f3ccfa.").Kind == ProblemKind.WrongGameVersion,
                "another build: WrongGameVersion");
        Require(Problems.FromBuild("Your game dump is incomplete: 12 game files in it are damaged").Kind == ProblemKind.IncompleteDump,
                "an incomplete dump");
        Require(Problems.FromBuild("OSError: [Errno 28] No space left on device").Kind == ProblemKind.DiskFull, "no space: DiskFull");
        var mem = Problems.FromBuild("the system stopped it, most likely because the PC ran out of memory. Close other programs and build again");
        Require(mem.Kind == ProblemKind.BuildMemory && mem.Fixes.Any(f => f.Kind == FixKind.LowerBuildMemory), "out of memory: lower the build memory");
        Require(Problems.FromBuild("something new").Kind == ProblemKind.Unknown, "an unknown message: Unknown, not a wrong fix");

        // a run's log
        Require(Problems.FromLog(["vk: start", "vk: 2400 presents, 60.0 fps"]) == null, "a clean log: no problem");
        var crash = Problems.FromLog(["vk: start",
            @"CRASH: exception 0xC0000005 (read 0x590, guest 0x0) at C:\Windows\System32\nvoglv64.dll+0x1234",
            "CRASH: host backtrace: nvoglv64.dll+0x1234 hookshot_vk.dll+0x99"], SystemInfo.Nvidia);
        Require(crash is { Kind: ProblemKind.GameCrashed } && crash.Signature == "0xc0000005 nvoglv64.dll+0x1234",
                "a crash: its code and module+offset, no folder, as the signature: " + crash?.Signature);
        Require(crash!.Fixes[0].Link == Problems.DriverPage(SystemInfo.Nvidia), "a crash: NVIDIA's driver page");
        Require(Problems.FromLog(["vk: VK_ERROR_DEVICE_LOST on submit"], SystemInfo.Amd)?.Kind == ProblemKind.GpuLost, "device lost: GpuLost");
        Require(Problems.FromLog(["vk: this GPU has no mesh shaders: the game's foliage, grass and many effects cannot be drawn"])?.Kind ==
                ProblemKind.NoMeshShaders, "no mesh shaders");

        // the game's redaction rule (vk_bug_report.cpp's tests' cases)
        Require(ProblemReports.Redact("/home/ann/Games/Hookshot/hookshot", "/home/ann", "ann", false) == "~/Games/Hookshot/hookshot", "home -> ~");
        Require(ProblemReports.Redact("/home/anna/x", "/home/ann", "ann", false) == "/home/anna/x", "not inside a longer name");
        Require(ProblemReports.Redact("/run/media/ann/disk", "/home/ann", "ann", false) == "/run/media/<user>/disk", "the user as a folder");
        Require(ProblemReports.Redact(@"C:\USERS\Ann\Games", @"C:\Users\Ann", "Ann", true) == @"%USERPROFILE%\Games", "Windows: any case");
        Require(ProblemReports.Redact("annual report", "/home/ann", "ann", false) == "annual report", "the name alone, not a folder: kept");

        // a crash found once, in the newest log or the one before it
        var game = Folder("problem-build");
        var log = Path.Combine(game, "hookshot_log.txt");
        File.WriteAllText(log, "vk: NVIDIA GeForce RTX 4070 Ti, Vulkan 1.4, driver 0x1; mesh shaders native, host memory import yes\n" +
                               "opened /home/ann/Games/Hookshot/romfs\nCRASH: exception 0xc0000005 (read 0x0, guest 0x1) at hookshot+0x10\n");
        var found = CrashCheck.Find(game, 0);
        Require(found != null && found.Log == log, "a crashed run's log: found");
        Require(CrashCheck.Find(game, found!.WriteTicks) == null, "offered once: not again");
        File.Move(log, Path.Combine(game, "hookshot_log.1.txt"));
        File.WriteAllText(log, "vk: a clean run\n");
        Require(CrashCheck.Find(game, 0)?.Log == Path.Combine(game, "hookshot_log.1.txt"), "started again since: the run before's crash");

        // the report: the form's headings, the GPU from the log, no home folder or user name
        var r = ProblemReports.Make(crash, "v2.2.0", "Linux, 16 CPU threads, 32 GB of RAM", Path.Combine(game, "hookshot_log.1.txt"), null,
                                    "/home/ann", "ann", false);
        Require(r.Text.Contains("### Hookshot version\n\nv2.2.0") && r.Text.Contains("NVIDIA GeForce RTX 4070 Ti"), "the form's answers");
        Require(r.Text.Contains("~/Games/Hookshot/romfs") && !r.Text.Contains("/home/ann") && !r.Text.Contains("ann/"), "no home folder left");
        Require(r.Text.Contains("This text only.") && r.PicturePath == null, "no picture unless ticked");

        // the settings: asked once, kept; an unknown choice is no choice
        var s = new LauncherSettings { ReportProblems = "sometimes" };
        var path = Path.Combine(Folder("problem-settings"), "settings.json");
        s.SeenCrashLogs[game] = 42;
        s.Save(path);
        var back = LauncherSettings.Load(path);
        Require(back.ReportProblems == null && back.SeenCrashLogs[game] == 42, "an unknown choice is asked again; the seen logs kept");

        // the inbox: https only; the request's body
        Require(ReportSender.Inbox(_ => "http://example.com/r") == null && ReportSender.Inbox(_ => "https://example.com/r") != null,
                "the inbox is https only");
        Require(ReportSender.Inbox(_ => null) == null, "no inbox until one is set up");
        var body = ReportSender.Body(r);
        Require(body["format"]!.GetValue<int>() == 1 && body["signature"]!.GetValue<string>() == r.Signature && body["picture_png"] == null,
                "the body: format, signature, text, no picture");
        var plain = await ReportSender.Send(r, new Uri("http://127.0.0.1:9/r"));
        Require(!plain.Sent && plain.Message.Contains("https"), "an http inbox is refused before anything is sent");
    }
}
