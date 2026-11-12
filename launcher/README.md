# Hookshot Launcher

The Hookshot Launcher (3.0) replaces the 2.x Builders, the Windows one (WPF, `builder/HookshotBuilder`) and the Linux one
(Tk, `builder/linux`). Players download it instead of a builder. It keeps several built versions side by side, each on
its channel (release, release candidate, nightly), plays the default one, offers each its updates, and builds new ones
with a wizard: the version's build tools download while the game files are picked, then the settings, then the build
with one progress bar. It takes over from a 2.x Builder as [docs/launcher-upgrade.md](../docs/launcher-upgrade.md)
says, and moves saves between builds and emulators as [docs/saves.md](../docs/saves.md) describes.

The screens follow the design mockup (`launcher/mockup` on the mockup's branch, live at
launcher-designs.magicbean.dev): home (empty, one or several versions, updates, the 2.x hand-over's notice, Set as
default, Delete), the channel picker, the wizard's three steps, Ready and the problem screen, the launcher's settings,
and the Saves page with its pick, confirm and result screens.

## Layout

| | |
|---|---|
| `Core/` | `HookshotLauncher.Core`: the logic, no UI and no package. Versions and channels, the newest version per channel (`Releases`), the parts lists (`PartsManifest`), each version's build tools (`Toolchains`, `ToolsSource`), game folders, `builds.json` (`BuildRegistry`), the 2.x hand-over (`LegacyImport`), remembered settings, make_game.py's command line, `@@` lines, run and check (`MakeGameCommand`, `MakeGameProtocol`, `BuildProgress`, `MakeGameRun`, `GameCheck`, `BuildClock`), the game files found on this PC (`GameFiles`, a port of the Builder's `Finder`), update offers (`UpdatePolicy`), saves (`Saves`, a port of `scripts/oot_saves.py`; `SaveText`), disk space (`Disk`), this PC (`SystemInfo`), Linux shortcuts (`Shortcuts`), the launcher's home and command line |
| `App/` | `HookshotLauncher`: the Avalonia window. `ViewModels/` (plain INotifyPropertyChanged, one per page and dialog), `Views/` (one .axaml each, compiled bindings), `Controls/` (the mockup's icons, bars and spinner), `App.axaml` (the palette and the styles) |
| `Directory.Packages.props` | The packages, pinned exactly (Avalonia); `packages.lock.json` beside each project |
| `Directory.Build.targets` | Avalonia.BuildServices' `AvaloniaStats` target replaced by an empty one: builds report nothing to Avalonia |
| `NuGet.Config` | nuget.org only |

The window draws its own title bar (logo, the wizard's step pill, the settings cog on the home, minimize and close in
the corner; drag the title to move it) in the 2.x Builder's palette ("Slate, frosted", `builder/HookshotBuilder/App.xaml`,
the mockup's `tokens.css` names) and its Inter font files, linked from `builder/HookshotBuilder/Fonts`.

## Build and run

The .NET 10 SDK, on Windows or Linux:

```sh
dotnet build launcher/HookshotLauncher.slnx
dotnet run --project launcher/App
```

`HOOKSHOT_LAUNCHER_HOME=<folder>` puts the launcher's home (its `builds.json`, `settings.json`, `import/`,
`toolchains/`) somewhere else, for trying it out. `HOOKSHOT_LAUNCHER_OFFLINE=1` skips the update check.
`HOOKSHOT_LAUNCHER_PARTS=<list>` builds from a parts list on this PC instead of a release's (Linux grammar, `file://`
parts allowed): every channel offers that list's tag, for trying the wizard out without a release.

The 2.x command line still works: `--launch <folder>`, `--update <folder> [--tag <tag>] [--fetched]` (opens the wizard on
that update; with no `--tag`, on the build's channel's newest once the update check answers), `--play [<folder>]` (starts
the game with no window; the shortcuts Ready makes use `--play`).

## Tests

`python -I tests/test_launcher_core.py --dotnet <dotnet>` (and `tests/run_all.py`) builds `tests/launcher_core`,
which compiles the core's sources with no package and checks them with no game, no network and no window: the version
order against `tests/test_update_tracks.py`'s vectors, the hand-over against `tests/fixtures/launcher`, the saves
against `tests/test_oot_saves.py`'s cases, and the rest over temporary folders (`ScreenChecks.cs`: what the screens use).
It also checks that the DLSS and FSR 4 licence texts name the SDK commits `scripts/ngx_sdk.py` and `scripts/ffx_sdk.py`
fetch. CI (`.github/workflows/source-regressions.yml`) runs it on Linux and Windows with .NET 10, and compiles the
solution there with warnings as errors.

## Planned next, not in this change

- **Profiles and a clean folder layout**: builds in `~/Games/Hookshot/versions/<version>` and the player's saves and
  settings in `~/Games/Hookshot/profiles`, with an importer that moves a 2.x install into that layout. Until then new
  builds go to `~/Games/Hookshot/<version>`, which is inside a 2.x install left at its default folder; Delete refuses
  a folder that holds another build for that reason.
- **Release packaging**: CI builds the launcher (the Windows zip, the Linux AppImage), publishes
  `hookshot-launcher.txt` and the per-platform tools lists, and ships the 2.x bridge payload
  ([docs/launcher-upgrade.md](../docs/launcher-upgrade.md), sections 1 and 5).

## Open design decisions

Calls made here, most of them the mockup's; each can be changed:

- **Saves are copied, never moved.** "Move my saves" in step 2 copies the chosen build's saves into the new one after a
  backup (`Saves.BetweenBuilds`); the old build keeps its own. On by default (`moveSaves`), from the build an update
  replaces, else the default build.
- **An update replaces its build's row.** An update builds the new version into its own folder (`<builds>/<version>`)
  from the game files the build was made from, then takes the old row's place, its default included
  (`BuildRegistry.Replace`). The old folder stays on the disk; nothing deletes it yet.
- **Building a version you already have is a rebuild** of that build, in its folder ("Rebuild vX" in the channel picker).
- **Play from a row's actions**: selecting a version shows Play this version, Set as default, Saves, Open folder and
  Delete; Play on the left always starts the default. The hero shows Play and, when the default has one, Update.
- **Deleting the default** makes a release build the default when one is left, else the first.
- **Delete takes the folder**: only a game folder make_game.py made, and never a link, the home folder, or a folder that
  holds another build or the launcher's home; not while Hookshot runs. On Windows it warns that the build's saves go with
  it.
- **Free up space** (the launcher's settings) lets go of what make_game made in the build files folder (`objcache`, `sa`,
  `exefs`, `probe`, as the 2.x Builder), never the folder itself, which the player may have chosen.
- **The shortcuts** Ready makes (desktop, Start menu / applications menu), ticked by default and made as Ready shows,
  start `--play`, the default build, so they follow "Play starts this version" and updates. On Windows they are written
  through PowerShell's WScript.Shell.
- **Play on Linux** starts `bin/hookshot` from its folder without the AppImage's variables, as `make_game.game_env` says,
  but not yet through `play-diagnostics.sh` or its own systemd scope as the 2.x Linux Builder does.
- **Closing the window during a build** asks first, as Cancel does; stopping kills make_game and everything it started.
- **Copy from another build** is Windows only (Linux builds share one save folder).
- **Time estimates**: step 2's "About 45 min, shaders included" (37 without) is the 2.x Builders' figure, not measured
  on this PC; step 3's time left is make_game's `@@eta` in the compile, else the pace so far.
- **Disk space**: step 2 wants 45 GB free where the game goes (the 2.x Builders' figure), and stops Build below it.
- **Slots are shown as "Slot N"** with the time they were saved: the place a slot was saved at (caption.sav) is not read.
- **"Your GPU"** comes from `/sys/class/drm` on Linux; on Windows the chip is not shown yet.
