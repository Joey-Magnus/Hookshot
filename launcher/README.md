# Hookshot Launcher

The Hookshot Launcher (3.0) replaces the 2.x Builders, the Windows one (WPF, `builder/HookshotBuilder`) and the Linux one
(Tk, `builder/linux`). Players download it instead of a builder. It keeps several built versions side by side, each on
its channel (release, release candidate, nightly), plays the default one, offers each its updates, and builds new ones
with a wizard: the version's build tools download while the game files are picked, then the settings, then the build
with one progress bar. It takes over from a 2.x Builder as [docs/launcher-upgrade.md](../docs/launcher-upgrade.md)
says, and moves saves between builds and emulators as [docs/saves.md](../docs/saves.md) describes.

This first version has the core library and the window's home screen; the wizard's steps are placeholders until their
design is done.

## Layout

| | |
|---|---|
| `Core/` | `HookshotLauncher.Core`: the logic, no UI and no package. Versions and channels, the newest version per channel (`Releases`), the parts lists (`PartsManifest`), each version's build tools (`Toolchains`), game folders, `builds.json` (`BuildRegistry`), the 2.x hand-over (`LegacyImport`), remembered settings, make_game.py's command line and `@@` lines (`MakeGameCommand`, `MakeGameProtocol`, `BuildProgress`), update offers (`UpdatePolicy`), saves (`Saves`, a port of `scripts/oot_saves.py`), the launcher's home and command line |
| `App/` | `HookshotLauncher`: the Avalonia window (home, wizard placeholders, settings, saves) |
| `Directory.Packages.props` | The packages, pinned exactly (Avalonia); `packages.lock.json` beside each project |
| `NuGet.Config` | nuget.org only |

The window uses the 2.x Builder's palette ("Slate, frosted", `builder/HookshotBuilder/App.xaml`) and its Inter font
files, linked from `builder/HookshotBuilder/Fonts`.

## Build and run

The .NET 10 SDK, on Windows or Linux:

```sh
dotnet build launcher/HookshotLauncher.slnx
dotnet run --project launcher/App
```

`HOOKSHOT_LAUNCHER_HOME=<folder>` puts the launcher's home (its `builds.json`, `settings.json`, `import/`,
`toolchains/`) somewhere else, for trying it out. `HOOKSHOT_LAUNCHER_OFFLINE=1` skips the update check.

The 2.x command line still works: `--launch <folder>`, `--update <folder> [--tag <tag>] [--fetched]`,
`--play [<folder>]` (starts the game with no window).

## Tests

`python -I tests/test_launcher_core.py --dotnet <dotnet>` (and `tests/run_all.py`) builds `tests/launcher_core`,
which compiles the core's sources with no package and checks them with no game, no network and no window: the version
order against `tests/test_update_tracks.py`'s vectors, the hand-over against `tests/fixtures/launcher`, the saves
against `tests/test_oot_saves.py`'s cases, and the rest over temporary folders.
