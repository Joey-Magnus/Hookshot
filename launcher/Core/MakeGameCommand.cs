using System.Diagnostics;
using System.Globalization;

namespace HookshotLauncher.Core;

/// What a build is made from: a .nup, the romfs and exefs folders, or (Update) a folder built before, from the game files it keeps
public abstract record BuildSource
{
    public sealed record Nup(string Path) : BuildSource;
    public sealed record Folders(string Romfs, string Exefs) : BuildSource;
    public sealed record Update(string Folder) : BuildSource;
}

/// A build's choices (the wizard's settings step, LauncherSettings). Out is not used by an update (the folder is the update's)
public sealed record BuildRequest(
    BuildSource Source, string? Out, string? Language = null, string? Work = null, int? Jobs = null, int? MemoryGb = null,
    bool PrepareShaders = true, bool Dlss = false, bool DlssLicense = false, bool Fsr = false, bool FsrLicense = false);

/// make_game.py as the 2.x Builders start it (Runner.Info and MainWindow.Run on Windows; builder/linux/AppRun and
/// hookshot_builder.py on Linux): the program, its arguments, its folder and the environment changes (null: removed)
public sealed record MakeGameCommand(
    string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory, IReadOnlyDictionary<string, string?> Environment)
{
    /// launcher: this launcher's own path, written into the game folder's stamp (builder=) so the game's update offer starts it
    /// (host/hos_update.c). directoryExists: the tests' stand-in for the disk
    public static MakeGameCommand For(Toolchain tools, BuildRequest r, Platform platform, LauncherHome home, string launcher,
                                      Func<string, bool>? directoryExists = null)
    {
        directoryExists ??= Directory.Exists;
        if (r.Jobs is <= 0) throw new ArgumentOutOfRangeException(nameof(r), "Build workers must be positive.");
        if (r.MemoryGb is <= 0) throw new ArgumentOutOfRangeException(nameof(r), "Build memory must be positive.");
        var windows = platform == Platform.Windows;
        var args = new List<string> { "-I", tools.MakeGame };
        switch (r.Source)
        {
            case BuildSource.Nup n:
                args.AddRange(new[] { "--nup", n.Path });
                break;
            case BuildSource.Folders f:
                args.AddRange(new[] { "--romfs", f.Romfs, "--exefs", f.Exefs });
                break;
            case BuildSource.Update u:
                args.AddRange(new[] { "--update", u.Folder });
                break;
        }
        if (r.Source is not BuildSource.Update)
        {
            args.AddRange(new[] { "--out", r.Out ?? throw new ArgumentException("A build needs its game folder (Out).", nameof(r)) });
            // (a code make_game knows: "de-DE" from a 2.x settings file is "de"; none known, make_game keeps the folder's or English)
            if (Languages.Normalize(r.Language) is { } language) args.AddRange(new[] { "--language", language });
        }
        args.AddRange(new[] { "--work", r.Work ?? home.Work });
        // (the build workers: HOOKSHOT_JOBS on Windows, BuildWorkers.Apply; --jobs on Linux, as hookshot_builder.py passes them)
        if (!windows && r.Jobs is int jobs) args.AddRange(new[] { "--jobs", jobs.ToString(CultureInfo.InvariantCulture) });
        if (!r.PrepareShaders) args.Add("--no-prepare-shaders");
        var dlss = r.Dlss && r.DlssLicense;
        if (dlss) args.AddRange(new[] { "--fetch-ngx-sdk", "--accept-nvidia-license" });
        // (AMD FSR 4: Windows only, as in the 2.x Builders; the AppImage's prebuilt back end has none)
        var fsr = windows && r.Fsr && r.FsrLicense;
        if (fsr) args.AddRange(new[] { "--fsr", "--fetch-fsr-runtime", "--accept-amd-license" });

        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PYTHONIOENCODING"] = "utf-8",
            ["HOOKSHOT_BUILDER_EXE"] = launcher,
            // (the 2.x setup's home and portable mark: make_game would take them for its work folder and stamp; the launcher passes --work)
            ["HOOKSHOT_SETUP_BASE"] = null,
            ["HOOKSHOT_BUILDER_PORTABLE"] = null,
            ["HOOKSHOT_BUILDER_ARGS"] = null,
            // (DLSS and FSR only when ticked: one set on the PC is not passed on either)
            ["HOOKSHOT_NGX_SDK"] = dlss ? home.NgxSdk : null,
            ["HOOKSHOT_FFX_SDK"] = fsr ? home.FfxSdk : null,
            ["HOOKSHOT_MEM_LIMIT_GB"] = r.MemoryGb?.ToString(CultureInfo.InvariantCulture),
        };
        if (windows && r.Jobs is int workers) env["HOOKSHOT_JOBS"] = workers.ToString(CultureInfo.InvariantCulture);
        if (windows)
        {
            // (the tools' own compiler, headers and libraries when they have them: nothing of the PC's but Windows' own)
            foreach (var (name, folder) in new[] { ("EMSDK", "emsdk"), ("RX_CPM", "cpm"), ("WGPU_NATIVE", "wgpu-native") })
            {
                var path = Path.Combine(tools.ToolsRoot, folder);
                if (directoryExists(path)) env[name] = path;
            }
        }
        else
        {
            // (builder/linux/AppRun: everything the AppImage builds with)
            var lib = tools.LibRoot;
            env["HOOKSHOT_TOOLS"] = tools.ToolsRoot;
            env["EMSDK"] = Path.Combine(lib, "emsdk");
            env["HOOKSHOT_SYSROOT"] = Path.Combine(lib, "sysroot");
            env["HOOKSHOT_FFMPEG"] = Path.Combine(lib, "ffmpeg");
            env["HOOKSHOT_SDL2"] = Path.Combine(lib, "libSDL2-2.0.so.0");
            // (DLSS: hookshot_vk.so built here with NGX, not the prebuilt one; hookshot_builder.make_game_env)
            env["HOOKSHOT_VK_PREBUILT"] = dlss ? null : Path.Combine(lib, "hookshot_vk.so");
        }
        return new MakeGameCommand(tools.Python, args, tools.ToolsRoot, env);
    }

    public ProcessStartInfo StartInfo()
    {
        var si = new ProcessStartInfo(FileName)
        {
            WorkingDirectory = WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in Arguments) si.ArgumentList.Add(a);
        foreach (var (k, v) in Environment)
            if (v == null) si.Environment.Remove(k);
            else si.Environment[k] = v;
        return si;
    }
}

/// Play: the build's game from its folder, as the 2.x Builders start it (MainWindow.StartGame; on Linux hookshot_builder.play with
/// make_game.game_env: without what the AppImage runtime and the tools put in the environment)
public static class GamePlay
{
    static readonly string[] Dropped = { "HOOKSHOT_", "PYTHON", "APPDIR", "APPIMAGE", "ARGV0", "OWD" };   // (make_game.GAME_ENV_DROP)

    /// The environment the game starts with, from the launcher's
    public static Dictionary<string, string> Environment(Platform platform, IReadOnlyDictionary<string, string> current)
    {
        var env = platform == Platform.Windows
            ? new Dictionary<string, string>(current, StringComparer.OrdinalIgnoreCase)
            : current.Where(kv => !Dropped.Any(p => kv.Key.StartsWith(p, StringComparison.Ordinal)) && kv.Key != "LD_LIBRARY_PATH")
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        env["HOOKSHOT_NO_UPDATE_CHECK"] = "1";   // (the launcher already checked for updates: the game does not ask again)
        return env;
    }

    public static ProcessStartInfo StartInfo(string folder, Platform platform, IReadOnlyDictionary<string, string>? current = null)
    {
        current ??= System.Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string?)e.Value ?? "");
        var si = new ProcessStartInfo(GameFolder.ExePath(folder, platform)) { UseShellExecute = false, WorkingDirectory = folder };
        si.Environment.Clear();
        foreach (var (k, v) in Environment(platform, current)) si.Environment[k] = v;
        return si;
    }
}
