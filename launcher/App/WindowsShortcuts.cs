using System.Diagnostics;

namespace HookshotLauncher;

/// The Ready screen's Windows shortcuts (Desktop, Start menu): Hookshot.lnk starting the launcher with --play, made by the shell's
/// own WScript.Shell through PowerShell (the launcher has no COM interop of its own), or removed when unticked.
/// (Not yet run on Windows: the launcher's first builds were tried on Linux.)
public static class WindowsShortcuts
{
    public static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Hookshot.lnk");
    public static string StartLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Hookshot.lnk");

    /// A PowerShell single-quoted string: each quote doubled, the typographic ones too (PowerShell ends a string on ‘ ’ ‚ ‛ as on ',
    /// and a folder named O’Brien would end it early)
    static string Quote(string s)
    {
        var sb = new System.Text.StringBuilder("'");
        foreach (var c in s)
        {
            if (c is '\'' or '‘' or '’' or '‚' or '‛') sb.Append(c);
            sb.Append(c);
        }
        return sb.Append('\'').ToString();
    }

    public static void Write(string launcher, bool desktop, bool start)
    {
        if (!OperatingSystem.IsWindows()) return;
        var script = new List<string> { "$s = New-Object -ComObject WScript.Shell" };
        foreach (var (link, want) in new[] { (DesktopLink, desktop), (StartLink, start) })
        {
            if (!want)
            {
                if (File.Exists(link)) File.Delete(link);
                continue;
            }
            script.Add($"$l = $s.CreateShortcut({Quote(link)}); $l.TargetPath = {Quote(launcher)}; $l.Arguments = '--play'; " +
                       $"$l.IconLocation = {Quote(launcher)}; $l.Description = 'The Legend of Zelda: Ocarina of Time, built by Hookshot'; $l.Save()");
        }
        if (script.Count == 1) return;
        var si = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", string.Join("; ", script) }) si.ArgumentList.Add(a);
        using var p = Process.Start(si);
        p?.WaitForExit(15000);
    }
}
