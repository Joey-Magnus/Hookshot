# Work list

The prioritized list of open work, scored as [docs/workflow.md](workflow.md) describes: **score = severity weight x
reach x confidence** (S0 8, S1 5, S2 3, S3 1; reach 0-1 from `scripts/hardware_report.py` and the Steam survey;
confidence 1.0 reproduced with a log, 0.7 two or more reporters with logs, 0.4 one report or no log). Work from the
top of **Open**. A GitHub issue stays the full record; this list is the order.

How to use it:

- **Pick:** take the highest-scored row with no owner that you can reproduce on your hardware, claim it in Discord
  #dev-work, and put your name in *Owner* in the same PR that starts the work (or a one-line PR).
- **Add:** a new report becomes a row in **Open** with its three parts filled in, so the score can be checked. Link the
  issue or the Discord thread. One row per problem: if a row already covers it, add the link to that row and raise its
  confidence instead of adding a second row.
- **Move:** when a PR merges, move the row to **Fixed, waiting for a release** with the PR; when a release ships it,
  delete the row (the release notes are the record). A row nobody can reproduce for two weeks, with logs asked for,
  goes to **Parked**.
- Keep each table sorted by score, highest first. Re-score when new logs or hardware numbers arrive.

Agents may add and re-score rows; a person reviews the change like any other PR.

## Open

| Score | Problem | S | Reach | Conf. | Owner | Links |
|---:|---|---|---:|---:|---|---|
| 3.0 | Shader programs that still do not translate (TLD multisample bit 78, LDG descriptor without `.E`, programs using the lowering scratch registers, uniform-addressed global loads) | S2 | 1.0 | 1.0 | | `--prepare-shaders` log; Legion Go 2 log in #bug-reporting |
| 2.1 | Camera turns in Kokiri Forest stutter: 46 fps minimum, dips to 38 (14700KF); goal is a 60 fps minimum | S2 | 1.0 | 0.7 | Joey-Magnus | Reddit report; #dev-work claim |
| 2.0 | Antivirus / VirusTotal flags the Windows downloads; players ask for signed builds | S1 | 0.57 | 0.7 | Joey-Magnus | Reddit r/decomps; #dev-work claim |
| 1.2 | Link keeps playing a jump animation in dialogue after the Gohma fight (gameplay fine) | S2 | 1.0 | 0.4 | | #bug-reporting "My link is jumping forever" |
| 1.2 | Ground in Kokiri Forest / Lost Woods looks smeared, without detail | S2 | 1.0 | 0.4 | | #bug-reporting "Ground design is not loading properly" (log asked for) |
| 1.2 | Trees float above the ground (Kakariko, Hyrule Field): reported as already fixed, check which change and that it shipped | S2 | 1.0 | 0.4 | | tracker #994 |
| 0.6 | Zora's Domain textures missing in the adult timeline (points to VRAM pressure, not the dump) | S2 | 0.3 | 0.7 | | #bug-reporting "Zora Domain textures missing?" |
| 0.5 | Sacred Forest Meadow fairy fountain grotto: floor and walls invisible (RTX 2070 Super) | S2 | 0.4 | 0.4 | | #bug-reporting thread |
| 0.4 | Voices in the wrong language | S3 | 1.0 | 0.4 | | #1460 (needs a save past Gohma) |

## Fixed, waiting for a release

| Problem | Fixed by | Notes |
|---|---|---|
| 1D surface loads (SULD.1D) stop 16-17 compute programs from translating; fits the green glow on a Legion Go 2 (890M) | #1447 | in `main`, not in v2.0.13 |
| Integer min/max with a wide immediate (IMNMX) does not translate | #1451 | in `main`, not in v2.0.13 |
| Insert menu purple with HDR10 on (v2.0.13 GDI overlay drawn into the HDR swapchain unconverted) | `main`'s ImGui overlay (`hdr10()`) | needs the release branch merged back into `main` |

## Parked

| Problem | Why parked | Links |
|---|---|---|
| | | |
