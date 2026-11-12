using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HookshotLauncher.Core;

/// Whether problem reports go to the developers: asked once, the first time something goes wrong (null until then)
public static class ReportConsent
{
    public const string Always = "always", Ask = "ask", Never = "never";
    public static bool IsChoice(string? v) => v is Always or Ask or Never;
}

/// Sends a ProblemReport to the developers' report inbox (docs/launcher-reports.md: one https POST, the inbox files it where
/// the developers read it and answers with an id the player can quote). No inbox is set up until the developers run one: then
/// Inbox is null and the launcher offers to copy the report and open the issue form instead.
public static class ReportSender
{
    /// The inbox; HOOKSHOT_REPORT_URL over it (for trying an inbox out). https only
    public const string? DefaultInbox = null;

    public static Uri? Inbox(Func<string, string?>? getenv = null)
    {
        var url = (getenv ?? Environment.GetEnvironmentVariable)("HOOKSHOT_REPORT_URL") ?? DefaultInbox;
        return Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? u : null;
    }

    /// The issue form a player can fill in by hand when there is no inbox (the report copied to paste in it)
    public const string IssueForm = "https://github.com/NotRexed/Hookshot/issues/new/choose";

    /// The largest picture a report takes (a PNG of the game's frame)
    public const long PictureMaxBytes = 4L << 20;

    public sealed record Result(bool Sent, string? Id, string Message);

    /// The request's body: format 1, the kind, the signature for grouping, the version, the text, and the picture when there is
    /// one (base64)
    public static JsonObject Body(ProblemReport r)
    {
        var o = new JsonObject
        {
            ["format"] = 1,
            ["kind"] = r.Kind,
            ["signature"] = r.Signature,
            ["version"] = r.Version,
            ["text"] = r.Text,
        };
        if (r.PicturePath != null && File.Exists(r.PicturePath) && new FileInfo(r.PicturePath).Length <= PictureMaxBytes)
            o["picture_png"] = Convert.ToBase64String(File.ReadAllBytes(r.PicturePath));
        return o;
    }

    public static async Task<Result> Send(ProblemReport report, Uri inbox, HttpClient? http = null, CancellationToken cancel = default)
    {
        if (inbox.Scheme != Uri.UriSchemeHttps) return new Result(false, null, "The report inbox must be https.");
        var own = http == null;
        http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            using var content = new StringContent(Body(report).ToJsonString(), System.Text.Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var res = await http.PostAsync(inbox, content, cancel).ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return new Result(false, null, $"The report inbox answered {(int)res.StatusCode}.");
            string? id = null;
            try { id = JsonNode.Parse(body)?["id"]?.GetValue<string>(); } catch (Exception e) when (e is JsonException or InvalidOperationException) { }
            return new Result(true, id, id == null ? "Sent. Thank you!" : $"Sent as report {id}. Thank you!");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            return new Result(false, null, "The report could not be sent: " + e.Message);
        }
        finally { if (own) http.Dispose(); }
    }
}
