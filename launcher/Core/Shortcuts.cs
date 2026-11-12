using System.Text;

namespace HookshotLauncher.Core;

/// The shortcuts a build's Ready screen makes: on the desktop and in the Start menu (Windows) or the applications menu (Linux).
/// They start the launcher with --play (the default build's game, so they follow "Play starts this version" and an update's
/// new folder), or --play &lt;folder&gt; for one build. Linux writes freedesktop .desktop files here; Windows' .lnk files are
/// written by the window (the shell makes them)
public static class Shortcuts
{
    public const string Name = "Hookshot";

    /// A .desktop entry that starts launcher --play [folder]
    public static string DesktopEntry(string launcher, string? folder, string? icon)
    {
        var sb = new StringBuilder();
        sb.Append("[Desktop Entry]\nType=Application\nName=").Append(Name).Append('\n');
        sb.Append("Comment=The Legend of Zelda: Ocarina of Time, built by Hookshot\n");
        sb.Append("Exec=").Append(Quote(launcher)).Append(" --play");
        if (folder != null) sb.Append(' ').Append(Quote(folder));
        sb.Append('\n');
        if (folder != null) sb.Append("Path=").Append(folder).Append('\n');
        if (icon != null) sb.Append("Icon=").Append(icon).Append('\n');
        sb.Append("Terminal=false\nCategories=Game;\n");
        return sb.ToString();
    }

    /// An Exec argument quoted as the desktop entry specification says: in double quotes, with " ` $ \ escaped (and % doubled).
    /// The file's own string escapes are read before the quoting, so each of those backslashes is written twice (a literal \ is
    /// four): GLib does not start an Exec line holding \" or \$
    public static string Quote(string arg)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in arg)
        {
            if (c is '"' or '`' or '$') sb.Append(@"\\").Append(c);
            else if (c == '\\') sb.Append(@"\\\\");
            else if (c == '%') sb.Append("%%");
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    /// Where the two Linux shortcuts go: ~/Desktop/hookshot.desktop and $XDG_DATA_HOME/applications/hookshot.desktop
    public static (string Desktop, string Menu) LinuxPaths(Func<string, string?>? getenv = null)
    {
        getenv ??= Environment.GetEnvironmentVariable;
        var home = LauncherHome.Home(getenv);
        var data = getenv("XDG_DATA_HOME") is { Length: > 0 } x ? x : Path.Combine(home, ".local", "share");
        return (Path.Combine(home, "Desktop", "hookshot.desktop"), Path.Combine(data, "applications", "hookshot.desktop"));
    }

    /// The Linux shortcuts made (or removed) as the two ticks say; the desktop one only when there is a Desktop folder
    public static void WriteLinux(string launcher, string? folder, bool desktop, bool menu, string? icon = null, Func<string, string?>? getenv = null)
    {
        var (d, m) = LinuxPaths(getenv);
        var text = DesktopEntry(launcher, folder, icon);
        foreach (var (path, want) in new[] { (d, desktop), (m, menu) })
        {
            if (!want)
            {
                if (File.Exists(path)) File.Delete(path);
                continue;
            }
            if (path == d && !Directory.Exists(Path.GetDirectoryName(path))) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AtomicFile.Write(path, text);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
    }
}
