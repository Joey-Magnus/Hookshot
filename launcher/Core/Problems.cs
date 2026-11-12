using System.Globalization;
using System.Text.RegularExpressions;

namespace HookshotLauncher.Core;

/// What went wrong, in the words a player needs, and what they can do about it: from a build that stopped (make_game.py's last
/// message, ProblemViewModel) or a game that closed unexpectedly (its log, CrashCheck). Matched on the messages make_game.py and
/// the game write, so a new message shows as Unknown rather than as a wrong fix.
public enum ProblemKind
{
    Unknown,
    Antivirus,          // the antivirus removed or held the build tools' files
    WrongGameVersion,   // the game files are another build than the supported one
    IncompleteDump,     // the dump has damaged files with nothing to put in their place
    DiskFull,           // no room left on the drive the build writes to
    BuildMemory,        // the PC ran out of memory while compiling
    NoMeshShaders,      // the graphics card or its driver has no mesh shaders, and not even the fallback runs
    GpuLost,            // the graphics driver stopped answering (device lost)
    GameCrashed,        // the game closed with a crash report in its log
}

/// What a fix button does (the screen decides how)
public enum FixKind { None, Repair, PickOtherFiles, ChooseOtherFolder, LowerBuildMemory, DriverPage, BuildAgain }

public sealed record Fix(FixKind Kind, string Label, string? Link = null);

/// A problem as the screens show it. Signature: what identifies the same crash on another PC (the exception and where it
/// happened, never a path), for grouping reports; null for a build problem
public sealed record Problem(ProblemKind Kind, string Headline, string Detail, IReadOnlyList<Fix> Fixes, string? Signature = null);

public static partial class Problems
{
    static readonly Fix BuildAgain = new(FixKind.BuildAgain, "Build again");

    /// The driver download page for a maker (SystemInfo's names); null for another
    public static string? DriverPage(string? vendor) => vendor switch
    {
        SystemInfo.Nvidia => "https://www.nvidia.com/Download/index.aspx",
        SystemInfo.Amd => "https://www.amd.com/en/support/download/drivers.html",
        SystemInfo.Intel => "https://www.intel.com/content/www/us/en/download-center/home.html",
        _ => null,
    };

    static Fix DriverFix(string? vendor) =>
        DriverPage(vendor) is { } link ? new Fix(FixKind.DriverPage, "Get the latest graphics driver", link)
                                       : new Fix(FixKind.None, "Update your graphics driver");

    /// A build that stopped: why, make_game.py's last message (or the launcher's own)
    public static Problem FromBuild(string why)
    {
        var w = why ?? "";
        bool Has(string s) => w.Contains(s, StringComparison.OrdinalIgnoreCase);
        if (Has("antivirus") || Has("quarantine"))
            return new Problem(ProblemKind.Antivirus, "Your antivirus removed some of Hookshot's files",
                "This is a false alarm: Hookshot's build tools aren't signed, so some antivirus programs flag them. Restore the " +
                "files from your antivirus's quarantine (Windows Security: Protection history), or allow Hookshot's folder, then " +
                "repair and build again.", [new Fix(FixKind.Repair, "Repair and build again")]);
        if (Has("Hookshot supports build") || Has("isn't supported") || Has("not supported"))
            return new Problem(ProblemKind.WrongGameVersion, "These game files are a different version",
                "Hookshot needs Ocarina of Time with update 2.0.0 installed (program 0400486024DEC000, update v131072). Pick " +
                "the files of that version, or dump your game again with the update.", [new Fix(FixKind.PickOtherFiles, "Pick other game files")]);
        if (Has("dump is incomplete") || Has("still has") && Has("damaged file"))
            return new Problem(ProblemKind.IncompleteDump, "Your game dump is incomplete",
                "Some of the game's files are damaged or missing (a complete dump is about 23.8 GB). Dump the game again and " +
                "build from the new dump.", [new Fix(FixKind.PickOtherFiles, "Pick other game files")]);
        if (Has("No space left") || Has("not enough space") || Has("free space") || Has("disk full"))
            return new Problem(ProblemKind.DiskFull, "The drive is full",
                "The build needs about 45 GB free while it runs. Free up space on that drive, or build to another drive.",
                [new Fix(FixKind.ChooseOtherFolder, "Build to another folder"), BuildAgain]);
        if (Has("ran out of memory") || Has("MemoryError") || Has("out of memory"))
            return new Problem(ProblemKind.BuildMemory, "Your PC ran out of memory while building",
                "Close other programs, or set Build memory lower in the build's Advanced settings, then build again: what was " +
                "built so far is kept.", [new Fix(FixKind.LowerBuildMemory, "Use less memory"), BuildAgain]);
        return new Problem(ProblemKind.Unknown, "The build stopped",
            "Something went wrong that Hookshot doesn't recognise. Sending a report helps us fix it.", [BuildAgain]);
    }

    [GeneratedRegex(@"CRASH: exception (0x[0-9a-fA-F]+) \((read|write)[^)]*\) at (\S+)")]
    private static partial Regex CrashLine();

    /// A run that closed unexpectedly, from its log's lines (oldest first); null when the log shows no crash. vendor: the
    /// graphics card's maker, for the driver link
    public static Problem? FromLog(IEnumerable<string> lines, string? vendor = null)
    {
        string? crash = null, module = null;
        bool lost = false, noMesh = false;
        foreach (var line in lines)
        {
            if (crash == null && CrashLine().Match(line) is { Success: true } m)
            {
                crash = m.Groups[1].Value.ToLowerInvariant();
                module = m.Groups[3].Value;
            }
            if (line.Contains("device lost", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("VK_ERROR_DEVICE_LOST", StringComparison.Ordinal)) lost = true;
            if (line.Contains("this GPU has no mesh shaders", StringComparison.Ordinal)) noMesh = true;
        }
        if (noMesh)
            return new Problem(ProblemKind.NoMeshShaders, "Your graphics card can't draw this game",
                "It, or its driver, has no mesh shaders, which the game needs. An NVIDIA RTX 20 series or newer, an AMD Radeon " +
                "RX 6000 or newer, or an Intel Arc card can. If your card is one of those, update its driver.", [DriverFix(vendor)]);
        if (lost)
            return new Problem(ProblemKind.GpuLost, "The graphics driver stopped responding",
                "This is often fixed by the latest graphics driver. If it keeps happening, sending a report helps us find the cause.",
                [DriverFix(vendor)], Signature(crash, module, "device lost"));
        if (crash == null) return null;
        return new Problem(ProblemKind.GameCrashed, "The game closed unexpectedly",
            "Sending a report helps us fix it. If it happens again at the same place, the graphics driver is worth updating.",
            [DriverFix(vendor)], Signature(crash, module, null));
    }

    /// The same crash on any PC: the exception code and the module and offset it happened at (the module's name only: no
    /// folder), or what was lost
    static string Signature(string? code, string? module, string? what)
    {
        var where = module == null ? "" : Path.GetFileName(module.Replace('\\', '/'));
        var parts = new[] { code, where.Length > 0 ? where : null, what }.Where(p => !string.IsNullOrEmpty(p));
        return string.Join(" ", parts).Trim().ToLower(CultureInfo.InvariantCulture);
    }
}
