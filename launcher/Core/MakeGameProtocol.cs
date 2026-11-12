using System.Globalization;
using System.Text.RegularExpressions;

namespace HookshotLauncher.Core;

/// A line of make_game.py's stdout (its docstring: the @@ lines a builder reads, and the plain "the shaders were not prepared
/// now (why): ..." line)
public abstract record MakeGameLine
{
    public sealed record Stage(string Id, string Text) : MakeGameLine;
    public sealed record Progress(double Fraction) : MakeGameLine;
    public sealed record Plan(int Changed, int Total) : MakeGameLine;
    public sealed record File(string Name, double Seconds, string Kind) : MakeGameLine;
    public sealed record Eta(double Seconds) : MakeGameLine;
    public sealed record Times(long Total, long Compile, long Previous) : MakeGameLine;
    public sealed record Check(bool Ok, string Text) : MakeGameLine;
    public sealed record Done(string Folder) : MakeGameLine;
    public sealed record Error(string Text) : MakeGameLine;
    public sealed record NotPrepared(string Reason) : MakeGameLine;
    public sealed record Plain(string Text) : MakeGameLine;
}

public static class MakeGameProtocol
{
    public const string ShadersStage = "shaders";
    static readonly Regex NotPreparedLine = new(@"^the shaders were not prepared now \((.*)\): ", RegexOptions.CultureInvariant);

    static bool Number(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && double.IsFinite(v);

    static bool Whole(string s, out long v) => long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v);

    /// The line read; an @@ line that does not have its shape is Plain (shown in the details, as the Builders do)
    public static MakeGameLine Parse(string line)
    {
        line = line.TrimEnd('\r');
        if (!line.StartsWith("@@", StringComparison.Ordinal))
            return NotPreparedLine.Match(line) is { Success: true } m ? new MakeGameLine.NotPrepared(m.Groups[1].Value) : new MakeGameLine.Plain(line);
        var space = line.IndexOf(' ');
        var word = space < 0 ? line[2..] : line[2..space];
        var rest = space < 0 ? "" : line[(space + 1)..];
        var p = rest.Split(' ');
        switch (word)
        {
            case "stage" when p[0].Length > 0:
                return new MakeGameLine.Stage(p[0], p.Length > 1 ? rest[(p[0].Length + 1)..] : "");
            case "progress" when p.Length == 1 && Number(p[0], out var f):
                return new MakeGameLine.Progress(Math.Clamp(f, 0, 1));
            case "plan" when p.Length == 2 && Whole(p[0], out var changed) && Whole(p[1], out var total):
                return new MakeGameLine.Plan((int)changed, (int)total);
            case "file" when p.Length == 3 && Number(p[1], out var secs):
                return new MakeGameLine.File(p[0], secs, p[2]);
            case "eta" when p.Length == 1 && Number(p[0], out var eta):
                return new MakeGameLine.Eta(eta);
            case "times" when p.Length == 3 && Whole(p[0], out var all) && Whole(p[1], out var compile) && Whole(p[2], out var previous):
                return new MakeGameLine.Times(all, compile, previous);
            case "check" when p[0] is "ok" or "bad":
                return new MakeGameLine.Check(p[0] == "ok", p.Length > 1 ? rest[(p[0].Length + 1)..] : "");
            case "done":
                return new MakeGameLine.Done(rest);
            case "error":
                return new MakeGameLine.Error(rest);
        }
        return new MakeGameLine.Plain(line);
    }
}

public enum StageState { Waiting, Running, Done, Skipped }

public sealed class BuildStage
{
    public string Id { get; }
    public string Title { get; }
    public StageState State { get; internal set; }
    /// Under the row: the compile's files, or why the shaders were left to the first start
    public string? Note { get; internal set; }

    internal double From, Size;

    internal BuildStage(string id, string title, double from, double size)
    {
        Id = id;
        Title = title;
        From = from;
        Size = size;
    }
}

/// One overall 0..1 for the whole build, from the stages' weights of the Windows Builder (MainWindow.xaml.cs: WithShaders,
/// WithoutShaders): the tools' download first when the version's tools are not installed, then make_game.py's stages. Only the
/// stages this build has count, scaled to fill the bar; the bar never goes back (make_game may run a stage again), and it is 1
/// once make_game says @@done.
public sealed class BuildProgress
{
    static readonly (string Id, string Title)[] Titles =
    {
        ("download", "Download the build tools"),
        ("read", "Read your game files"),
        ("lift", "Translate the game to PC code"),
        ("compile", "Compile"),
        ("finish", "Put the game together"),
        (MakeGameProtocol.ShadersStage, "Prepare the shaders"),
    };
    static readonly Dictionary<string, double> WithShaders = new()
    {
        ["download"] = 0.04, ["read"] = 0.04, ["lift"] = 0.14, ["compile"] = 0.68, ["finish"] = 0.02, [MakeGameProtocol.ShadersStage] = 0.08,
    };
    static readonly Dictionary<string, double> WithoutShaders = new()
    {
        ["download"] = 0.04, ["read"] = 0.04, ["lift"] = 0.14, ["compile"] = 0.74, ["finish"] = 0.04,
    };

    readonly List<BuildStage> stages = new();
    BuildStage? current;

    /// download: the tools are downloaded first; shaders: this build prepares the shaders; update: make_game --update (its
    /// read step reads the files the folder keeps)
    public BuildProgress(bool download, bool shaders, bool update = false)
    {
        var weights = shaders ? WithShaders : WithoutShaders;
        var included = Titles.Where(t => weights.ContainsKey(t.Id) && (download || t.Id != "download")).ToList();
        var sum = included.Sum(t => weights[t.Id]);
        var from = 0.0;
        foreach (var (id, title) in included)
        {
            var size = weights[id] / sum;
            stages.Add(new BuildStage(id, update && id == "read" ? "Read the game files kept in the folder" : title, from, size));
            from += size;
        }
    }

    public IReadOnlyList<BuildStage> Stages => stages;
    public double Overall { get; private set; }
    public string? Current => current?.Id;
    /// The compile's time left in seconds (@@eta), as build.py worked it out; null before it says
    public double? Eta { get; private set; }
    public int? FilesChanged { get; private set; }
    public int? FilesTotal { get; private set; }
    public int FilesCompiled { get; private set; }
    public bool Finished { get; private set; }
    public string? Error { get; private set; }
    public string? Folder { get; private set; }

    void Set(double f)
    {
        f = Math.Clamp(f, 0, 1);
        if (f > Overall) Overall = f;
    }

    void Begin(string id)
    {
        var stage = stages.FirstOrDefault(s => s.Id == id);
        if (stage == null) return;   // (a stage this build does not show: the bar stays where it is)
        if (current != null && current != stage && current.State == StageState.Running) current.State = StageState.Done;
        // (stages before it that never said so are done too: an older make_game, or one with nothing to do)
        foreach (var s in stages.TakeWhile(s => s != stage))
            if (s.State is StageState.Waiting or StageState.Running) s.State = StageState.Done;
        current = stage;
        stage.State = StageState.Running;
        Set(stage.From);
    }

    /// The tools' download, 0..1
    public void Download(double fraction)
    {
        if (stages.FirstOrDefault(s => s.Id == "download") is not { } d) return;
        if (current != d) Begin("download");
        Set(d.From + d.Size * Math.Clamp(fraction, 0, 1));
    }

    public MakeGameLine Feed(string line)
    {
        var parsed = MakeGameProtocol.Parse(line);
        Apply(parsed);
        return parsed;
    }

    public void Apply(MakeGameLine line)
    {
        switch (line)
        {
            case MakeGameLine.Stage s:
                Begin(s.Id);
                break;
            case MakeGameLine.Progress p when current != null:
                Set(current.From + current.Size * p.Fraction);
                break;
            case MakeGameLine.Plan plan:
                FilesChanged = plan.Changed;
                FilesTotal = plan.Total;
                FilesCompiled = 0;
                Note();
                break;
            case MakeGameLine.File f when f.Kind is "new" or "again":
                FilesCompiled++;
                Note();
                break;
            case MakeGameLine.Eta e:
                Eta = Math.Max(0, e.Seconds);
                break;
            case MakeGameLine.NotPrepared n:
                if (stages.FirstOrDefault(s => s.Id == MakeGameProtocol.ShadersStage) is { } shaders)
                {
                    shaders.State = StageState.Skipped;
                    shaders.Note = "Left to the game's first start: " + n.Reason;
                }
                break;
            case MakeGameLine.Done d:
                Folder = d.Folder;
                Complete();
                break;
            case MakeGameLine.Error e:
                Error = e.Text;
                if (current != null && current.State == StageState.Running) current.State = StageState.Waiting;
                break;
        }
    }

    void Note()
    {
        if (stages.FirstOrDefault(s => s.Id == "compile") is not { } c || FilesChanged is not int changed || FilesTotal is not int total) return;
        c.Note = changed == 0 ? "Everything is already built"
            : changed >= total ? $"{FilesCompiled} of {changed} files"
            : $"{FilesCompiled} of {changed} changed files";
    }

    /// make_game said @@done: every stage is done (the shaders' row skipped when they were not prepared) and the bar is full
    public void Complete()
    {
        foreach (var s in stages)
            if (s.State != StageState.Skipped)
            {
                // (a build that prepares shaders, whose game never said @@stage shaders: left to the first start, ShaderStep.Skipped)
                if (s.Id == MakeGameProtocol.ShadersStage && s.State == StageState.Waiting)
                {
                    s.State = StageState.Skipped;
                    s.Note ??= "Left to the game's first start";
                }
                else s.State = StageState.Done;
            }
        current = null;
        Finished = true;
        Overall = 1;
    }
}
