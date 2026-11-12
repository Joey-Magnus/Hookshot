# Problem reports from the launcher

When a build stops or the game closes unexpectedly, the launcher explains what happened in plain words, offers the fix it
knows, and can send a report to the developers with one button, so a player needs no GitHub account and no idea where the
log is. This page is the contract between the launcher (`launcher/Core/Problems.cs`, `ProblemReport.cs`,
`ReportSender.cs`) and the report inbox that receives the reports.

## What the player sees

- **A build stopped:** the problem screen's headline and advice come from `Problems.FromBuild` (make_game.py's last
  message): the antivirus removed the build tools, the game files are another version, the dump is incomplete, the drive
  is full, the PC ran out of memory. Each has a fix button (Repair, pick other files, another folder, less build memory,
  build again). A message it doesn't know shows as "The build stopped", never as a wrong fix.
- **The game closed unexpectedly:** the launcher can't watch a game started from a shortcut, so at its next start it reads
  the last run's log (`CrashCheck`): a `CRASH: exception ... at <module>+<offset>` line, a lost device, or no mesh
  shaders. The home screen then says so, with the fix (the graphics driver's download page for the card's maker) and
  **Send report**. Each crashed log is offered once.
- **Consent, asked once:** the first time, the player chooses **Always send**, **Ask each time** or **Never**, after a
  **What gets sent** preview of the exact text. The choice is in the launcher's settings (`reportProblems` in
  `settings.json`) and can be changed there.

## What a report holds

`ProblemReports.Make` writes it under the issue form's headings, as the game's own Bug report does
(`gpu/vk/vk_bug_report.cpp`):

- what happened (the headline, and the crash's signature: its exception code and `module+offset`)
- the Hookshot version, the graphics card and driver (the game's own log line), the OS, CPU threads and RAM
- the end of the run's `hookshot_log.txt` (at most 192 KB), or the build's last lines

Every path has the home folder replaced by `~` (`%USERPROFILE%` on Windows) and the user's name by `<user>`, by the game's
rule. Never sent: saves, game files, the options file, anything from `work/`. A picture of the game is sent only when the
player ticks it, at most 4 MB.

## The inbox

The launcher sends one request; nothing is sent while no inbox is set (`ReportSender.DefaultInbox` is null). Then the
problem screen offers **Copy report** and opens the issue form instead. `HOOKSHOT_REPORT_URL=<https url>` points a launcher
at an inbox for testing. https only, also after redirects.

```
POST <inbox>
Content-Type: application/json

{
  "format": 1,
  "kind": "GameCrashed",                  // ProblemKind: Unknown, Antivirus, WrongGameVersion, IncompleteDump,
                                          // DiskFull, BuildMemory, NoMeshShaders, GpuLost, GameCrashed
  "signature": "0xc0000005 nvoglv64.dll+0x1234",   // null for a build problem; the same crash on any PC
  "version": "v2.2.0",
  "text": "### What happened\n\n...",     // Markdown, at most about 200 KB
  "picture_png": "<base64>"               // only when the player ticked it
}
```

The answer is `200` with `{"id": "<short id>", "url": "<where it was filed>"}`; the launcher shows the id ("Sent as report
AB12"), which the player can quote in Discord. Any other status shows "could not be sent" and keeps the report to copy.

What the inbox should do, for whoever runs it:

- **File it where the developers read it:** a GitHub issue through a bot account, with the `kind` and `signature` as labels
  or in the title, and the text as its body; a picture as an attachment.
- **Group by signature:** a report whose signature has an open issue adds a comment there (or a count) instead of a new
  issue. Most crashes repeat.
- **Limits:** the body at most 5 MB, a few reports an hour from one address, and a reply of `429` beyond it.
- **Keep nothing else:** the reports hold no personal data by design; the inbox should not log addresses longer than its
  rate limit needs.
