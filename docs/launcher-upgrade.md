# Moving from the 2.x Builder to the Hookshot Launcher (3.0)

Hookshot 3.0 replaces the Windows Builder (WPF) and the Linux Builder (Tk) with one launcher. The launcher keeps
several built versions side by side, on the release, rc and nightly tracks, and builds new ones itself. This page is
the contract between the 2.x Builder that hands over (2.2 or 2.3, the "upgrader") and the launcher that takes over.
Both sides test against the sample files in `tests/fixtures/launcher/`; a change to this page changes those files and
both sides' tests in the same change.

The rules for versions and tracks are the ones in [versioning.md](versioning.md) (`track_of`, `release_key`,
`newest_release` in `builder/linux/linux_update.py`, `TrackOf`, `Newer` in `builder/HookshotBuilder/Updater.cs`). The
launcher uses the same rules; nothing here adds new ones.

## 1. Telling a launcher release from a Builder release

A release (or the rolling `nightly` and `rc` releases) that ships the launcher carries one more file,
`hookshot-launcher.txt`. A release without it is a Builder release, and a 2.x Builder updates to it as it does today.

The upgrader asks for `releases/download/<release_of(tag)>/hookshot-launcher.txt` for the tag it would update to (for
`latest`, `releases/latest/download/hookshot-launcher.txt`). A 404 means a Builder release. It is a file download,
not an API call, so it does not count against GitHub's API limit. The version name is never used to decide: a nightly
that ships the launcher may still carry a 2.x name, and nightly names may change form. The upgrader reads a tag only to
find its release (`release_of`, so a nightly's file is on the rolling `nightly` release) and to check the file's `tag`.

The file, in the same line format as `hookshot-setup.txt` and `hookshot-linux.txt`:

```
tag v3.0.0
format 1
launcher windows-x64 <sha256> <bytes> <https url>
launcher linux-x64 <sha256> <bytes> <https url>
```

- `tag` must equal the tag asked for (as the setup lists' tag must), and follows the same name rule:
  `[0-9A-Za-z._+-]{1,128}`, not `.` or `..`.
- `format` is `1`. An upgrader that reads a higher number does not upgrade, and says the Builder is too old for this
  launcher (download it from the release page).
- One `launcher` line per platform: the platform, the file's SHA-256 (64 lowercase hex), its size in bytes, and an
  `https://` link. Unknown platforms are skipped. Lines starting with `#` and blank lines are skipped; any other line is
  an error.
- `windows-x64` is a zip whose root holds `HookshotLauncher.exe` and the rest of the self-contained app.
- `linux-x64` is the launcher AppImage itself.

The download is checked for size and SHA-256 against this file before anything is unpacked or run. The files are
assets of the release itself, so `SHA256SUMS` and the build attestation cover them too.

## 2. Where the launcher lives

One launcher per PC, not one per version (the versions it builds are its own business, below).

| | Launcher home | The launcher |
|---|---|---|
| Windows | `%LOCALAPPDATA%\HookshotLauncher\` | `<home>\app\HookshotLauncher.exe` |
| Linux | `${XDG_DATA_HOME:-~/.local/share}/hookshot/launcher/` | `<home>/Hookshot.AppImage` |

`HOOKSHOT_LAUNCHER_HOME` overrides the home on both (the tests use it). The home is apart from the 2.x Builder's
(`%LOCALAPPDATA%\HookshotBuilder`, `…/hookshot/builder`), so the Builder's pruning and "Free up space" never touch it.

Installing: on Windows, unpack into `<home>\app.part` (an entry that would land outside it is refused) and check that
it holds `HookshotLauncher.exe`; rename an old `app` to `app.old`, rename `app.part` to `app`, then remove `app.old`.
The old `app` is renamed rather than deleted because a delete stops at the first file in use and leaves half a
launcher; a rename fails whole, and the old `app` is put back if the second rename fails. On Linux, write
`<home>/Hookshot.AppImage.part`, make it executable, rename it over `Hookshot.AppImage`. A failed or cancelled upgrade
leaves no half-installed launcher and no `.part`.

A running launcher: on Windows its files cannot be replaced, so the upgrader does not start while a
`HookshotLauncher.exe` is running and asks the player to close it. On Linux the rename replaces the file a running
launcher has open without disturbing it; the new one runs from its next start.

Inside the home the launcher owns everything else: `builds.json` (its list of builds), `settings.json`, the tools for
each version it builds, its downloads. The upgrader writes only `app`/`Hookshot.AppImage` and `import/`.

## 3. Handing over the game

The upgrader does not write `builds.json`. It drops one file per game folder into `<home>/import/`, named
`<anything>.json` (write `<name>.json.part`, then rename), and the launcher reads and deletes those files when it starts,
before it shows anything. The launcher alone owns its list, so the two never write the same file.

`format 1`, every key present (null where nothing is known):

```json
{
  "format": 1,
  "folder": "/home/player/Games/Hookshot",
  "version": "v2.3.0",
  "track": "release",
  "default": true,
  "settings": {
    "nup": "/home/player/Downloads/game.nup",
    "romfs": null,
    "exefs": null,
    "language": "en-US",
    "dlss": false,
    "dlssLicense": false,
    "fsr": false,
    "fsrLicense": false,
    "prepareShaders": true,
    "memoryGb": null,
    "jobs": null,
    "work": null
  }
}
```

- `folder`: the game folder, absolute. The one the upgrade was started for (`--update <folder>`, `--launch <folder>`),
  else the saved install folder. A folder that is not a built game (no `bin/hookshot[.exe]`) is still handed over: the
  launcher shows it as "not built" rather than losing it.
- `version`: `version=` from the folder's `hookshot_version.txt`, or null.
- `track`: the track the player chose in the Builder, else `track_of(version)`.
- `default`: true. (The upgrader hands over one folder. The launcher keeps its own default if it already has one.)
- `settings`: what the Builder remembered, so the launcher's build settings start where the player left them. Paths are
  absolute; a path that no longer exists is still passed, and the launcher asks again.

Where each value comes from:

| Key | Windows (`%LOCALAPPDATA%\HookshotBuilder\builder-settings.json`) | Linux (`~/.config/hookshot/builder.ini`) |
|---|---|---|
| `folder` (fallback) | `Out` | `out` |
| `nup` / `romfs` / `exefs` | `Nup` / `Romfs` / `Exefs` | `kind=nup`: `nup`; `kind=folders`: `romfs`, `exefs` (the others null) |
| `language` | the folder's `language.txt`, else null | `language`, else the folder's `language.txt`, else null |
| `dlss` / `dlssLicense` | `Dlss` / `DlssLicense` | `dlss=1` / `nvidia_license=1`, or NVIDIA's licence in the old SDK folder (below) |
| `fsr` / `fsrLicense` | `Fsr` / `FsrLicense` | false / false (no FSR on Linux) |
| `prepareShaders` | not `PrepareLater` | `prepare` is not `0` |
| `memoryGb` | `MemoryGb` | null |
| `jobs` | null (not saved) | null (not saved) |
| `work` | the path in `work.txt`, else null | null |
| `track` | `Track`, else `track_of(version)` | `track`, else `track_of(version)` |

On Linux, older Builders kept NVIDIA's SDK, and with it the accepted licence, in
`${XDG_DATA_HOME:-~/.local/share}/hookshot/builder/ngx-sdk/LICENSE.txt` rather than as `nvidia_license=1`. The Builder
records it in `builder.ini` when it starts (`move_old_ngx_sdk`), and the upgrader counts it too (`license_accepted`), so
`dlssLicense` is true either way.

The launcher, on its first start with no `builds.json`, also reads these two files itself and the stamp of their saved
install folder, as if an import file had been dropped. That covers a player who downloads the launcher by hand.

## 4. The game folder after the hand-over

- `hookshot_version.txt`: the upgrader rewrites only the `builder=` line, to the launcher (section 2), and removes a
  `builder_args=` line. `version=` and every other line stay. A folder with `builder_local=1` (a portable install) is
  left as it is.
- The game's own update prompt (`host/hos_update.c`) runs `<builder> --update <folder> --tag <tag>`, so the launcher
  accepts the 2.x command line: `--update <folder> [--tag <tag>] [--fetched]` (update that build), `--launch <folder>`
  (open on that build), `--play [<folder>]` (start it with no window). On Linux, `hos_update.c` prefers an installed
  launcher over the installed 2.x Builder.
- The folder's own launcher (`hookshot.exe` on Windows, the `hookshot` script on Linux) is not replaced. It starts the
  installed 2.x Builder with `--launch`, and from 2.2/2.3 on that Builder passes `--launch` and `--update` straight to
  the launcher once the launcher is installed. A folder the launcher builds again gets the launcher's own. A portable
  Windows Builder (`HOOKSHOT_BUILDER_PORTABLE=1`) does not pass them on: its folders stay its own.
- Game files, saves, `hookshot_options.ini` and everything else in the folder are not touched.

## 5. Builders older than 2.2

They know nothing of `hookshot-launcher.txt` and update by `hookshot-setup.txt` (Windows) and `hookshot-linux.txt`
(Linux), which must name the tag they asked for. So every launcher release keeps those two files, and
`HookshotBuilder.exe`, under its own tag. Their payload is the 2.3 Builder without the build tools (no `nx2/`). That
Builder, finding no tools, opens on the hand-over screen instead of the build form: "Hookshot 3 uses the new launcher.
Your game folder stays as it is." with Upgrade (sections 2 to 4) and a link to the release page. An old Builder therefore
lands on the upgrader in one update, rather than on an error.

What the bridge payload must still hold, since the hand-over screen asks for the release it was shipped with:

- Windows: `HookshotBuilder.exe` and its runtime, and `tools/version.txt` naming the release's tag (the setup and the
  portable zip's install checks it too). Without `tools/nx2/make_game.py` the Builder is the bridge (`Tools.Find()` is
  null).
- Linux: the AppImage's `AppRun`, its Python with Tk, `usr/share/hookshot/builder/linux/` (`hookshot_builder.py`,
  `linux_update.py`, `linux_upgrade.py`, `hookshot-launch`) and `usr/share/hookshot/VERSION` naming the tag. Without
  `usr/share/hookshot/nx2/make_game.py`, `AppRun` opens `hookshot_builder.py --handover` for every start except the ones
  that only play (`--play`, a game folder).

Without its tag the bridge asks for the latest release's `hookshot-launcher.txt` instead.

## 6. Order of an upgrade

1. Find that the target release has `hookshot-launcher.txt`; read it; pick this platform's line.
2. Show the player what will happen, with Upgrade and Not now. Nothing changes before Upgrade.
3. Download, check size and SHA-256, install (section 2).
4. Write the import file (section 3), then rewrite the stamp (section 4).
5. Start the launcher (Windows: `HookshotLauncher.exe`; Linux: `Hookshot.AppImage`), with `--launch <folder>`, and
   close the Builder.

A failure at 3 leaves everything as it was. A failure at 4 leaves an installed launcher that imports the Builder's
settings itself on its first start (section 3).
