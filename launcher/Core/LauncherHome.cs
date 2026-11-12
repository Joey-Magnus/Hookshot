namespace HookshotLauncher.Core;

/// The launcher's own folder (docs/launcher-upgrade.md, section 2): %LOCALAPPDATA%\HookshotLauncher on Windows,
/// ${XDG_DATA_HOME:-~/.local/share}/hookshot/launcher on Linux, HOOKSHOT_LAUNCHER_HOME on both. The 2.x upgrader writes only
/// app/ (or Hookshot.AppImage) and import/ in it; everything else is the launcher's.
public sealed class LauncherHome
{
    public const string Variable = "HOOKSHOT_LAUNCHER_HOME";

    public string Root { get; }

    public LauncherHome(string root) => Root = Path.GetFullPath(root);

    public static LauncherHome Resolve(Platform platform, Func<string, string?>? getenv = null)
    {
        getenv ??= Environment.GetEnvironmentVariable;
        if (getenv(Variable) is { Length: > 0 } set) return new LauncherHome(set);
        if (platform == Platform.Windows)
        {
            var local = getenv("LOCALAPPDATA") is { Length: > 0 } l ? l : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new LauncherHome(Path.Combine(local, "HookshotLauncher"));
        }
        var data = getenv("XDG_DATA_HOME") is { Length: > 0 } x ? x : Path.Combine(Home(getenv), ".local", "share");
        return new LauncherHome(Path.Combine(data, "hookshot", "launcher"));
    }

    internal static string Home(Func<string, string?> getenv) =>
        getenv("HOME") is { Length: > 0 } h ? h : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string Builds => Path.Combine(Root, "builds.json");
    public string Settings => Path.Combine(Root, "settings.json");
    public string Import => Path.Combine(Root, "import");
    public string Toolchains => Path.Combine(Root, "toolchains");
    /// make_game.py's --work unless the player chose another: the build files, kept between builds for a faster rebuild
    public string Work => Path.Combine(Root, "work");
    /// NVIDIA's DLSS SDK, fetched by make_game.py --fetch-ngx-sdk (HOOKSHOT_NGX_SDK), and AMD's FSR runtime (HOOKSHOT_FFX_SDK)
    public string NgxSdk => Path.Combine(Root, "ngx-sdk");
    public string FfxSdk => Path.Combine(Root, "ffx-sdk");

    /// Where the upgrader installs the launcher (section 2)
    public string LauncherPath(Platform platform) =>
        platform == Platform.Windows ? Path.Combine(Root, "app", "HookshotLauncher.exe") : Path.Combine(Root, "Hookshot.AppImage");
}
