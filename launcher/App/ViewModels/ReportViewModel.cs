using HookshotLauncher.Core;

namespace HookshotLauncher.ViewModels;

/// A problem and the report for it (docs/launcher-reports.md): what happened in plain words, the fix, and Send report. The
/// first time, the player chooses whether reports are sent (Always, this once, Never), with the exact text shown first. With
/// Always chosen it sends at once; with no report inbox set up, Send copies the report and opens the issue form instead.
public sealed class ReportViewModel : ViewModelBase
{
    readonly MainViewModel main;
    readonly Action back;
    readonly Lazy<ProblemReport> report;
    bool showPreview, sending, sent;
    string? status;

    public ReportViewModel(MainViewModel main, Problem problem, Func<ProblemReport> make, Action back, bool crashed)
    {
        this.main = main;
        this.back = back;
        Problem = problem;
        Crashed = crashed;
        report = new Lazy<ProblemReport>(make);
        Fix = problem.Fixes.FirstOrDefault(f => f.Kind == FixKind.DriverPage);
        FixCommand = new Command(() => { if (Fix?.Link is { } link) main.Host?.Open(link); }, () => Fix?.Link != null);
        TogglePreviewCommand = new Command(() => ShowPreview = !ShowPreview);
        SendCommand = new Command(() => _ = Send(), () => !sending && !sent);
        AlwaysCommand = new Command(() => { Choose(ReportConsent.Always); _ = Send(); });
        OnceCommand = new Command(() => { Choose(ReportConsent.Ask); _ = Send(); });
        NeverCommand = new Command(() => { Choose(ReportConsent.Never); Close(); });
        CopyCommand = new Command(() => _ = Copy());
        CloseCommand = new Command(Close);
        if (Consent == ReportConsent.Always) _ = Send();
    }

    public Problem Problem { get; }
    public bool Crashed { get; }
    public Fix? Fix { get; }
    public string Headline => Problem.Headline;
    public string Detail => Problem.Detail;
    public string Sub => Crashed ? "This was the last time you played. Your saves are not touched." : "Your other versions and your saves are not touched.";

    string? Consent => main.Service.Settings.ReportProblems;
    /// The first time: Always send, Just this once, Never
    public bool AskConsent => Consent == null && !sent && !sending;
    /// Asked before: Send report each time
    public bool AskEach => Consent == ReportConsent.Ask && !sent;
    public bool ReportsOff => Consent == ReportConsent.Never;
    public bool HasInbox => ReportSender.Inbox() != null;
    public string SendLabel => HasInbox ? "Send report" : "Copy report and open the issue form";

    public bool ShowPreview { get => showPreview; set { if (Set(ref showPreview, value)) RaiseAll(nameof(PreviewButton), nameof(Preview)); } }
    public string PreviewButton => showPreview ? "Hide what gets sent" : "What gets sent";
    public string Preview => showPreview ? report.Value.Text : "";
    public string? Status { get => status; private set => Set(ref status, value); }

    public Command FixCommand { get; }
    public Command TogglePreviewCommand { get; }
    public Command SendCommand { get; }
    public Command AlwaysCommand { get; }
    public Command OnceCommand { get; }
    public Command NeverCommand { get; }
    public Command CopyCommand { get; }
    public Command CloseCommand { get; }

    void Choose(string consent)
    {
        main.Service.Settings.ReportProblems = consent;
        try { main.Service.SaveSettings(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { main.ShowToast("The settings could not be saved: " + e.Message); }
        RaiseAll(nameof(AskConsent), nameof(AskEach), nameof(ReportsOff));
    }

    async Task Send()
    {
        if (sending || sent) return;
        if (ReportSender.Inbox() is not { } inbox)
        {
            await Copy();
            main.Host?.Open(ReportSender.IssueForm);
            return;
        }
        sending = true;
        Status = "Sending the report...";
        RaiseAll(nameof(AskConsent), nameof(AskEach));
        SendCommand.Changed();
        var r = await ReportSender.Send(report.Value, inbox);
        sending = false;
        sent = r.Sent;
        Status = r.Message;
        RaiseAll(nameof(AskConsent), nameof(AskEach));
        SendCommand.Changed();
    }

    async Task Copy()
    {
        if (main.Host is { } host) await host.CopyAsync(report.Value.Text);
        main.ShowToast("Copied the report: paste it into the issue form.");
    }

    void Close() => back();
}
