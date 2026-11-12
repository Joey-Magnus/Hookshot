using System.Runtime.InteropServices;

namespace HookshotLauncher;

/// Whether the desktop asks for less motion: Windows' "Animation effects" off, GTK's gtk-enable-animations off, or
/// HOOKSHOT_LAUNCHER_REDUCED_MOTION=1. Then the window drops its transitions (App.axaml's Window.still) and pages change at once
static class Motion
{
    public static bool Reduced { get; } = Ask();

    static bool Ask()
    {
        if (Environment.GetEnvironmentVariable("HOOKSHOT_LAUNCHER_REDUCED_MOTION") is { Length: > 0 } v) return v != "0";
        try
        {
            if (OperatingSystem.IsWindows()) return SystemParametersInfo(SpiGetClientAreaAnimation, 0, out var on, 0) && !on;
            if (OperatingSystem.IsLinux())
            {
                var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x
                    ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                foreach (var gtk in new[] { "gtk-4.0", "gtk-3.0" })
                {
                    var ini = Path.Combine(config, gtk, "settings.ini");
                    if (!File.Exists(ini)) continue;
                    foreach (var line in File.ReadLines(ini))
                    {
                        var kv = line.Split('=', 2, StringSplitOptions.TrimEntries);
                        if (kv.Length == 2 && kv[0] == "gtk-enable-animations")
                            return kv[1] is "0" or "false" or "FALSE" or "False";
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or EntryPointNotFoundException or DllNotFoundException) { }
        return false;
    }

    const uint SpiGetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SystemParametersInfo(uint action, uint param, [MarshalAs(UnmanagedType.Bool)] out bool value, uint winIni);
}
