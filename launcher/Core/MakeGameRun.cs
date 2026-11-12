using System.Diagnostics;
using System.Globalization;

namespace HookshotLauncher.Core;

public enum GameCheckStatus { Supported, OtherVersion, Unreadable }

/// make_game.py --check's answer, in plain words: the supported version, another version of the game, or files it could not read
public sealed record GameCheck(GameCheckStatus Status, string Text)
{
    public bool Ok => Status == GameCheckStatus.Supported;

    /// The answer from the check's stdout lines, the last line it wrote to stderr and its exit code (the 2.x Builder's
    /// Runner.Check): @@check ok, @@check bad (another version), @@error or nothing said (the files could not be read: the
    /// stderr line says why)
    public static GameCheck Read(IEnumerable<string> stdout, string? lastError, int exitCode)
    {
        GameCheck? answer = null;
        foreach (var line in stdout)
            switch (MakeGameProtocol.Parse(line))
            {
                case MakeGameLine.Check { Ok: true } c:
                    answer = new GameCheck(GameCheckStatus.Supported, c.Text);
                    break;
                case MakeGameLine.Check c:
                    answer = new GameCheck(GameCheckStatus.OtherVersion, c.Text);
                    break;
                case MakeGameLine.Error e:
                    answer = new GameCheck(GameCheckStatus.Unreadable, e.Text);
                    break;
            }
        return answer ?? new GameCheck(GameCheckStatus.Unreadable,
            string.IsNullOrWhiteSpace(lastError) ? $"The build tools stopped without saying why (exit code {exitCode})." : lastError.Trim());
    }
}

/// make_game.py run: its stdout and stderr line by line, and its whole process tree stopped when the run is cancelled
public static class MakeGameRun
{
    /// The program run to its end; each line handed over as it comes (from a pool thread). Cancelling kills the process and
    /// everything it started (the compilers), then throws OperationCanceledException
    public static async Task<int> RunAsync(ProcessStartInfo start, Action<string> stdout, Action<string> stderr, CancellationToken cancel = default)
    {
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;
        using var p = new Process { StartInfo = start };
        var outDone = new TaskCompletionSource();
        var errDone = new TaskCompletionSource();
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) outDone.TrySetResult();
            else stdout(e.Data);
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) errDone.TrySetResult();
            else stderr(e.Data);
        };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using (cancel.Register(() =>
               {
                   try { p.Kill(entireProcessTree: true); }
                   catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
               }))
        {
            await p.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(outDone.Task, errDone.Task).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                .ContinueWith(_ => { }, TaskScheduler.Default).ConfigureAwait(false);
        }
        cancel.ThrowIfCancellationRequested();
        return p.ExitCode;
    }

    /// make_game.py --check run, and its answer
    public static async Task<GameCheck> CheckAsync(MakeGameCommand command, CancellationToken cancel = default)
    {
        var lines = new List<string>();
        string? lastError = null;
        try
        {
            var code = await RunAsync(command.StartInfo(), l => { lock (lines) lines.Add(l); },
                                      l => { if (!string.IsNullOrWhiteSpace(l)) lastError = l; }, cancel).ConfigureAwait(false);
            lock (lines) return GameCheck.Read(lines.ToList(), lastError, code);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return new GameCheck(GameCheckStatus.Unreadable, "The build tools could not start: " + e.Message);
        }
    }
}

/// A build's times: how long each stage took (or has run), and how long the whole build has left, from BuildProgress and a clock.
/// The time left is make_game's own estimate in the compile (@@eta, from what earlier builds measured on this PC) plus the stages
/// after it at the pace so far; elsewhere the pace so far over what is left of the bar
public sealed class BuildClock
{
    readonly Dictionary<string, TimeSpan> started = new();
    readonly Dictionary<string, TimeSpan> took = new();
    TimeSpan begun = TimeSpan.MinValue;
    TimeSpan last;

    /// The progress seen at now (a monotonic time: a Stopwatch's elapsed)
    public void Observe(BuildProgress p, TimeSpan now)
    {
        if (begun == TimeSpan.MinValue) begun = now;
        last = now;
        foreach (var s in p.Stages)
        {
            if (s.State == StageState.Running && !started.ContainsKey(s.Id)) started[s.Id] = now;
            if (s.State is StageState.Done or StageState.Skipped && started.TryGetValue(s.Id, out var from) && !took.ContainsKey(s.Id))
                took[s.Id] = now - from;
        }
    }

    /// A stage's time: what it took, or how long it has run; null before it started
    public TimeSpan? Of(string id) =>
        took.TryGetValue(id, out var t) ? t : started.TryGetValue(id, out var from) ? last - from : null;

    public TimeSpan Elapsed => begun == TimeSpan.MinValue ? TimeSpan.Zero : last - begun;

    /// The seconds left, or null while there is too little to go by (the first percent and the first 20 seconds)
    public double? SecondsLeft(BuildProgress p)
    {
        if (p.Finished) return 0;
        var elapsed = Elapsed.TotalSeconds;
        if (p.Current == "compile" && p.Eta is double eta && p.Stages.FirstOrDefault(s => s.Id == "compile") is { } compile)
        {
            var after = p.Stages.SkipWhile(s => s != compile).Skip(1).Where(s => s.State != StageState.Skipped).Sum(s => s.Weight);
            var pace = p.Overall > 0.01 ? elapsed / p.Overall : 0;
            return eta + after * pace;
        }
        if (p.Overall < 0.01 || elapsed < 20) return null;
        return elapsed / p.Overall * (1 - p.Overall);
    }

    /// A stage that has not started: about how long it takes at the pace so far; null while there is too little to go by
    public double? Estimate(BuildProgress p, BuildStage stage)
    {
        var elapsed = Elapsed.TotalSeconds;
        if (p.Overall < 0.01 || elapsed < 20) return null;
        return elapsed / p.Overall * stage.Weight;
    }
}

/// Times and sizes as the window writes them
public static class Durations
{
    /// m:ss (h:mm:ss past an hour)
    public static string Clock(TimeSpan t)
    {
        var s = (long)Math.Round(Math.Max(0, t.TotalSeconds));
        return s >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{s / 60}:{s % 60:00}");
    }

    /// "Under a minute", "About 12 min", "About 1 h 20 min"
    public static string About(double seconds)
    {
        if (seconds < 60) return "Under a minute";
        var m = (long)Math.Round(seconds / 60);
        if (m < 60) return string.Create(CultureInfo.InvariantCulture, $"About {m} min");
        return m % 60 == 0 ? string.Create(CultureInfo.InvariantCulture, $"About {m / 60} h")
            : string.Create(CultureInfo.InvariantCulture, $"About {m / 60} h {m % 60} min");
    }

    /// "~4 min" for a stage still to come
    public static string Roughly(double seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"~{Math.Max(1, (long)Math.Round(seconds / 60))} min");

    /// When something was saved or built, as the home writes it: "today 18:02", "yesterday 22:14", "6 Nov", "6 Nov 2025"
    public static string When(DateTime t, DateTime now)
    {
        var day = t.Date;
        if (day == now.Date) return "today " + t.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (day == now.Date.AddDays(-1)) return "yesterday " + t.ToString("HH:mm", CultureInfo.InvariantCulture);
        return t.ToString(t.Year == now.Year ? "d MMM" : "d MMM yyyy", CultureInfo.InvariantCulture);
    }

    /// The day only: "today", "yesterday", "3 Nov"
    public static string Day(DateTime t, DateTime now)
    {
        if (t.Date == now.Date) return "today";
        if (t.Date == now.Date.AddDays(-1)) return "yesterday";
        return t.ToString(t.Year == now.Year ? "d MMM" : "d MMM yyyy", CultureInfo.InvariantCulture);
    }

    /// "38.4 GB", "340 MB"
    public static string Size(long bytes) =>
        bytes >= 1L << 30 ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 30):0.0} GB")
        : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, bytes) >> 20} MB");
}
