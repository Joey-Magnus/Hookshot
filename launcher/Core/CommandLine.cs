namespace HookshotLauncher.Core;

public enum StartKind { Window, Launch, Update, Play }

/// How the launcher was started. The 2.x command line stays (docs/launcher-upgrade.md, section 4): the game's update offer runs
/// &lt;builder&gt; --update &lt;folder&gt; --tag &lt;tag&gt;, a folder's own launcher and the 2.x Builder pass --launch &lt;folder&gt;.
///   (nothing)                                    the window
///   --launch &lt;folder&gt;                            the window, on that build
///   --update &lt;folder&gt; [--tag &lt;tag&gt;] [--fetched]   update that build (to tag)
///   --play [&lt;folder&gt;]                           start that build's game (else the default build's), with no window
/// Unknown arguments are kept in Ignored (and do not stop the start).
public sealed record StartRequest(StartKind Kind, string? Folder, string? Tag, bool Fetched, IReadOnlyList<string> Ignored, string? Problem)
{
    public static StartRequest Parse(IReadOnlyList<string> args)
    {
        StartKind kind = StartKind.Window;
        string? folder = null, tag = null, problem = null;
        var fetched = false;
        var ignored = new List<string>();

        void Want(StartKind k, string? f)
        {
            if (kind != StartKind.Window && kind != k) problem ??= "--launch, --update and --play go one at a time.";
            else kind = k;
            folder = f ?? folder;
        }

        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            var next = i + 1 < args.Count ? args[i + 1] : null;
            switch (a)
            {
                case "--launch" or "--update" when next == null || next.StartsWith("--", StringComparison.Ordinal):
                    problem ??= a + " needs a game folder.";
                    break;
                case "--launch":
                    Want(StartKind.Launch, next);
                    i++;
                    break;
                case "--update":
                    Want(StartKind.Update, next);
                    i++;
                    break;
                case "--play":
                    var named = next != null && !next.StartsWith("--", StringComparison.Ordinal);
                    Want(StartKind.Play, named ? next : null);
                    if (named) i++;
                    break;
                case "--tag" when next != null && Versions.IsTagName(next):
                    tag = next;
                    i++;
                    break;
                case "--tag":
                    problem ??= "--tag needs a version name.";
                    if (next != null && !next.StartsWith("--", StringComparison.Ordinal)) i++;
                    break;
                case "--fetched":
                    fetched = true;
                    break;
                default:
                    ignored.Add(a);
                    break;
            }
        }
        if (kind != StartKind.Update && (tag != null || fetched)) problem ??= "--tag and --fetched go with --update.";
        return new StartRequest(kind, folder, tag, fetched, ignored, problem);
    }
}
