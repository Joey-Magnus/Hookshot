using System.Text;

namespace HookshotLauncher.Core;

/// A report of a problem, for the developers: the answers the issue form asks for (its own headings, as the game's Bug report
/// writes them, gpu/vk/vk_bug_report.cpp) and the log of the run that went wrong, with the home folder and the user's name taken
/// out of every path (Redact, the game's own rule). Nothing else from the PC: no saves, no game files, no options file; a picture
/// of the game only when the player ticks it. Text: exactly what is sent, shown to the player before it goes.
public sealed record ProblemReport(string Kind, string? Signature, string Version, string Text, string? PicturePath);

public static class ProblemReports
{
    /// The most of one log a report carries: its end, where the problem is
    public const int LogTailBytes = 192 * 1024;

    static bool IsSep(char c) => c == '/' || c == '\\';
    static bool NameChar(char c) => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' || c >= 0x80;

    static bool MatchesAt(string s, int at, string what, bool windows)
    {
        if (what.Length == 0 || at + what.Length > s.Length) return false;
        for (var i = 0; i < what.Length; i++)
        {
            char a = s[at + i], b = what[i];
            if (IsSep(a) && IsSep(b)) { if (!windows && a != b) return false; continue; }
            if (windows ? char.ToLowerInvariant(a) != char.ToLowerInvariant(b) : a != b) return false;
        }
        return true;
    }

    /// A line with the home folder written as ~ (%USERPROFILE% on Windows) and the user's name as &lt;user&gt; wherever it is a
    /// folder of a path: the game's Bug report's rule (vk_bug_report.cpp redact), so a report from the launcher and one from the
    /// menu read the same
    public static string Redact(string line, string home, string user, bool windows)
    {
        var h = home;
        while (h.Length > 1 && IsSep(h[^1])) h = h[..^1];
        var withHome = h.Length > 1;
        var o = new StringBuilder(line.Length);
        for (var i = 0; i < line.Length;)
        {
            int afterHome = i + h.Length, afterUser = i + user.Length;
            if (withHome && (i == 0 || (!NameChar(line[i - 1]) && !IsSep(line[i - 1]))) && MatchesAt(line, i, h, windows) &&
                (afterHome >= line.Length || !NameChar(line[afterHome])))
            {
                o.Append(windows ? "%USERPROFILE%" : "~");
                i = afterHome;
                continue;
            }
            if (user.Length > 0 && i > 0 && IsSep(line[i - 1]) && MatchesAt(line, i, user, windows) &&
                (afterUser >= line.Length || !NameChar(line[afterUser])))
            {
                o.Append("<user>");
                i = afterUser;
                continue;
            }
            o.Append(line[i++]);
        }
        return o.ToString();
    }

    /// The end of a file, at most max bytes, from the start of a line
    public static string Tail(string path, int max = LogTailBytes)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var start = Math.Max(0, f.Length - max);
        f.Seek(start, SeekOrigin.Begin);
        var text = new StreamReader(f, Encoding.UTF8).ReadToEnd();
        if (start > 0 && text.IndexOf('\n') is var nl and >= 0) text = "[the log's start left out]\n" + text[(nl + 1)..];
        return text;
    }

    /// The graphics card the game named in its log ("vk: NVIDIA GeForce RTX 4070 Ti, Vulkan 1.4, driver ...; mesh shaders ...")
    public static string? GpuLine(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var at = line.IndexOf("vk: ", StringComparison.Ordinal);
            if (at >= 0 && line.Contains(", Vulkan ", StringComparison.Ordinal) && line.Contains("mesh shaders", StringComparison.Ordinal))
                return line[(at + 4)..].Trim();
        }
        return null;
    }

    /// The report for a problem. log: the run's log (null for a build problem: buildOutput, make_game.py's last lines, instead);
    /// home and user: this PC's, taken out of every path; system: the CPU, RAM and OS line
    public static ProblemReport Make(Problem problem, string version, string system, string? log, string? buildOutput,
                                     string home, string user, bool windows, string? picture = null)
    {
        var logText = log != null && File.Exists(log) ? Tail(log) : null;
        var lines = (logText ?? buildOutput ?? "").Split('\n');
        string R(string s) => string.Join("\n", s.Split('\n').Select(l => Redact(l.TrimEnd('\r'), home, user, windows)));
        var t = new StringBuilder();
        t.Append("### What happened\n\n").Append(problem.Headline);
        if (problem.Signature != null) t.Append(" (").Append(problem.Signature).Append(')');
        t.Append("\n\nSent from the Hookshot Launcher").Append(log != null ? " after the game closed unexpectedly" : " after a build stopped").Append(".\n\n");
        t.Append("### Hookshot version\n\n").Append(version).Append("\n\n");
        t.Append("### Graphics card and driver version\n\n").Append(GpuLine(lines) ?? "not in the log").Append("\n\n");
        t.Append("### CPU, RAM and operating system\n\n").Append(system).Append("\n\n");
        if (logText != null) t.Append("### hookshot_log.txt\n\n```\n").Append(R(logText)).Append("\n```\n\n");
        else if (buildOutput != null) t.Append("### The build's last lines\n\n```\n").Append(R(buildOutput)).Append("\n```\n\n");
        t.Append("### What this report holds\n\n").Append(picture != null ? "This text and one picture of the game. " : "This text only. ")
         .Append("No saves, no game files and no settings file; the home folder and the user's name are taken out of every path.\n");
        return new ProblemReport(problem.Kind.ToString(), problem.Signature, version, t.ToString(), picture);
    }

    /// This PC in one line, as the issue form asks: the OS, the CPU threads and the memory
    public static string SystemLine() =>
        $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim()}, {SystemInfo.Threads()} CPU threads, {SystemInfo.MemoryGb()} GB of RAM";
}

/// Whether the last run of a build closed unexpectedly: its log (hookshot_log.txt, or .1.txt when the game was started again
/// since) shows a crash the launcher has not offered to report yet. seen: the log's write time already handled (settings)
public static class CrashCheck
{
    public sealed record Found(Problem Problem, string Log, long WriteTicks);

    public static Found? Find(string buildFolder, long seenTicks, string? vendor = null)
    {
        foreach (var name in new[] { "hookshot_log.txt", "hookshot_log.1.txt" })
        {
            var path = Path.Combine(buildFolder, name);
            try
            {
                if (!File.Exists(path)) continue;
                var ticks = File.GetLastWriteTimeUtc(path).Ticks;
                if (ticks <= seenTicks) return null;   // (handled already; the older one, too)
                if (Problems.FromLog(ProblemReports.Tail(path).Split('\n'), vendor) is { } p) return new Found(p, path, ticks);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }
}
