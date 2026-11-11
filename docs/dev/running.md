# Running the game from the command line

> This page is the readme's old "Running the game from the command line" section, moved here as it was. It is
> rewritten in the developer-docs pass of the docs rewrite.


The built game is `bin\hookshot.exe` on Windows and `bin/hookshot` in a packaged Linux game folder. A build from a
source checkout has `hookshot.exe` or `hookshot` in its build folder, with no launcher beside it. The `hookshot.exe`
or `hookshot` launcher in the game folder does not pass these arguments to the game, so start the game directly to
use them.

| Argument | What it does |
|---|---|
| `image.bin` | The game image to run. Without it the game uses the `image.bin`, `romfs` and `user` in its folder |
| `--romfs <folder>` | The extracted RomFS folder |
| `--user <folder>` | Where the game keeps per-user data: saves, caches and temp files (and `mouse_sens.txt` when `HOS_MOUSE_SENS` is set; the menu keeps the sensitivity otherwise). Default: `user` in the game folder on Windows, `~/.local/share/hookshot` on Linux; with an explicit image path, `user` in the current folder |
| `--trace` | Log every Horizon OS call (the same as `HOS_TRACE=1`) |
| `--prepare-shaders` | Prepare the shaders as the first start does, then exit without starting the game (no build step runs it) |

For example, keep saves somewhere else:

```powershell
cd "C:\Games\Hookshot"
.\bin\hookshot.exe --user "D:\Hookshot data"
```

```sh
cd ~/Games/Hookshot
./bin/hookshot --user "$HOME/hookshot-data"
```

`hookshot_env.txt` and the other test switches are documented below.

<details>
<summary>AMD FSR 3.1 (every build)</summary>

**AMD FSR 3.1** in the Graphics tab's **Upscaler** row (`dlss=3` in `hookshot_options.ini`; `RX_VK_FSR=1` for one run,
`RX_VK_FSR=0` never) upscales in place of the game's own network, and with **Frame generation** at 2x its frame
interpolation can make the frame in between with `RX_VK_FSR_FG=1` (opt-in: the game draws its HUD into the same picture
and FSR 3.1 has no HUD-less colour to keep it still, so the HUD smears; by default the renderer's own makes it). Both are AMD's FidelityFX
SDK 1.1.4 shaders (`third_party/fidelityfx-sdk`, MIT), compiled into the renderer: no AMD runtime or DLL is needed, and
the row offers FSR 3.1 on any Vulkan 1.3 GPU with push descriptors, extended storage image formats and storage writes
without a format (`vk: fsr3: ... can run on this GPU` in the log). It upscales on the same inputs as DLSS and FSR 4: the
scene's colour, its depth, motion from the camera with the skinned characters' own drawn over it (Link, his shield and
the people around him keep their outline while they move), and the same camera jitter (`RX_VK_FSR_JITTER=0` turns it
off). Things that move by themselves and are not skinned (Navi's body, props) can still trail. `RX_VK_FSR3_CHAIN=0` gives
it the camera's motion alone, as before.

</details>

<details>
<summary>NVIDIA DLSS (optional, NVIDIA's SDK under NVIDIA's license)</summary>

A game built with NVIDIA's DLSS SDK offers **NVIDIA DLSS** and **NVIDIA DLAA** in the Graphics tab's **Upscaler** row on
NVIDIA RTX cards (`dlss=` in `hookshot_options.ini`, `RX_VK_DLSS=0/1/2` for one run): DLSS Super Resolution in place of
the game's own upscaler, with camera jitter (**NVIDIA Reflex** needs no SDK and is in
every build). NVIDIA's files are NVIDIA's, under its own license, the
[NVIDIA RTX SDKs license](https://github.com/NVIDIA/DLSS/blob/9981c6350dc58c0ce21f024fd65e31e851c31689/LICENSE.txt).
Hookshot never includes or redistributes them: each build downloads them for itself, and only after you accept that
license. Never share a game folder built with them, and do not post builds made with them.

- **Windows, the Hookshot Builder:** tick **NVIDIA DLSS (downloads NVIDIA's SDK under NVIDIA's license)** before you
  build. It is off by default and shows NVIDIA's license when you tick it. The build downloads the SDK into
  `%LOCALAPPDATA%\HookshotBuilder\ngx-sdk` and puts DLSS's runtime, `nvngx_dlss.dll`, in the game folder's `ngx` folder.
- **Windows, the command line:** `python -I scripts/ngx_sdk.py --platform windows <sdk folder>` asks you to accept
  NVIDIA's license (or pass `--accept-nvidia-license`) and fetches the headers, `nvsdk_ngx_s.lib` and `nvngx_dlss.dll`,
  each checked against its SHA-256. Then build with `HOOKSHOT_NGX_SDK=<sdk folder>`: `make_game.py` copies
  `nvngx_dlss.dll` into `<game folder>\ngx`. NGX is also pointed at `%LOCALAPPDATA%\Hookshot\ngx`, and at
  `RX_VK_DLSS_DIR` when it is set, and uses the newest `nvngx_dlss.dll` it finds across them (to try a given one, make
  sure no other folder has a newer one). Never copy NGX's core, `_nvngx.dll`, beside the game: it comes with NVIDIA's
  driver.
- **Linux, the Hookshot Builder (the AppImage):** the same tick as on Windows, and nothing else to do: no variable is
  passed to the AppImage and no file is copied by hand. The build downloads the SDK into the builder's cache folder,
  `$XDG_CACHE_HOME/hookshot-builder/ngx-sdk` (`~/.cache/hookshot-builder/ngx-sdk`), builds the game with it and puts
  DLSS's runtime, `libnvidia-ngx-dlss.so.<version>`, in the game folder's `ngx` folder. The builder's settings
  (`~/.config/hookshot/builder.ini`) remember that you accepted NVIDIA's license, so an update keeps DLSS even after the
  cache was emptied: it downloads the SDK again.
- **Linux, a build from source:** `scripts/linux/ngx-sdk.sh <sdk folder>` (it runs `scripts/ngx_sdk.py --platform
  linux`) asks you to accept NVIDIA's license and fetches `libnvsdk_ngx.a` for the build and
  `libnvidia-ngx-dlss.so.<version>`, DLSS's runtime. Then run the build with `HOOKSHOT_NGX_SDK=<sdk folder>` set:
  `make_game.py` puts the runtime in the game folder's `ngx` folder. NGX's core comes with NVIDIA's Linux driver.
- **Linux, a game folder with other than plain English letters in its path:** NVIDIA's NGX on Linux loads DLSS's
  runtime only from a folder whose whole path is ASCII. A game in `~/Games/Zelda` needs nothing more. A game in a
  folder such as `~/Games/Zelda-Café` (the `é` is not ASCII) cannot use the runtime in its own `ngx` folder, and the
  log and the Insert menu say so: copy `libnvidia-ngx-dlss.so.*` from there into `$XDG_DATA_HOME/hookshot/ngx`
  (`~/.local/share/hookshot/ngx`), which the game also looks in, or into any other folder with an ASCII path and name
  that folder in `RX_VK_DLSS_DIR`.

The **DLSS model** row chooses DLSS's network, by NVIDIA's preset letter, for every mode: **Transformer (K)**, the
transformer model and the one tested most here (the default); **Transformer 2 (M)**, NVIDIA's second-generation
transformer as it picks it for Performance; **Transformer 2 (L)**, the same generation as tuned for Ultra Performance;
**NVIDIA default**, which lets DLSS's runtime pick for each mode, so it can change with a runtime update. The runtime
the SDK comes with (310.9.16) has all of them. A change applies at once, without a restart (`dlss_model=` in
`hookshot_options.ini`, 0 to 3 in that order). `RX_VK_DLSS_PRESET=<letter>` (or `0` for NVIDIA's default) chooses
the preset for one run and wins over the row; the log's `vk: NVIDIA DLSS: ... preset <letter>` line says which one was
asked for, and with `RX_VK_DLSS_LOG=1` NGX's own `Using App hint Preset <letter>` lines say which it took.

**8 GB cards:** DLAA draws the 3D at full size, and DLSS and DLAA add images of their own beside the game's. In busy
areas that can pass what the driver allows the game, and the game then stutters: Quality or Balanced is safer there.
The log's `vk: status:` line every 600 frames shows the video memory in use of the allowance, with DLSS's details
(its images' size, the jitter and the actor motion) appended while it draws, and a `vk: over the driver's
video-memory allowance` line says when the game passed it.

The game's log says whether DLSS started, and why not when it did not (`vk: NVIDIA DLSS:` lines in `hookshot_log.txt`).
NGX starts on demand: when the Insert menu first opens (to know whether the Upscaler can offer DLSS) or at the first
gameplay upscale with DLSS chosen, so on the title screen the log has no NGX lines yet.
With `RX_VK_DLSS_LOG=1` NGX's own lines go there too (`ngx:`). Some of them look like errors but are not:
`failed to load NGXCore: 126` (twice) is NVIDIA's library looking for `_nvngx.dll` and `nvngx.dll` beside the game, which
are not there on purpose, before it loads the driver's; `Feature ... failed to load from cache` is NGX finding no model
of its own downloads (`C:\ProgramData\NVIDIA\NGX\models`) before it uses the game folder's `nvngx_dlss.dll`.

</details>

<details>
<summary>AMD FSR 4 (optional, AMD's DLLs not included)</summary>

A game built with FSR 4 offers **AMD FSR 4** and **AMD FSR 4 Native AA** in the Graphics tab's **Upscaler** row: AMD's
machine-learned temporal upscaler in place of the game's own, on the same inputs as DLSS (the scene's colour and depth, the
camera's and the characters' motion, and the camera jitter). Native AA draws the 3D at full size and smooths its edges.
With HDR on **Native**, it takes the game's HDR colour as linear light, so highlights above the paper white are kept
(`RX_VK_FSR4_HDR=0`: cut at the paper white, as before).
Unlike FSR 3.1 (above), it runs AMD's own DLLs, so it needs them beside the game. Choose it in the menu (`dlss=4` or `5`
in `hookshot_options.ini`, `RX_VK_FSR4=1` or `2` for one run, `RX_VK_FSR4=0` to leave it out).

- **Windows, the Hookshot Builder:** tick **AMD FSR 4** before you build. It shows AMD's license first and stays ticked
  only if you accept; the build then fetches AMD's DLLs for you (below), into the builder's `ffx-sdk` folder and the
  game folder.
- **The command line:** `make_game.py --fsr` (or `HOOKSHOT_FSR=1` for `nx2/build.py`). Rebuilding a folder built with
  it (`--update`) keeps it; `--no-fsr` takes it out.
- **AMD's files:** Hookshot includes only AMD's MIT-licensed API headers (`third_party/fidelityfx`). Put
  `amd_fidelityfx_loader_vk.dll` and `amd_fidelityfx_upscaler_vk.dll` from the
  [FidelityFX SDK](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK) 2.4 release (`Kits/FidelityFX/signedbin`)
  beside the game's exe (`bin\hookshot.exe`) or in the game folder. (`RX_VK_FSR_DIR` names another folder, for testing:
  set it in the environment or a `HOS_ENV_FILE` file; `hookshot_env.txt` takes no paths.) Without them the Upscaler row
  says so and offers the rest. On Linux, a Vulkan FSR 4 library (`amd_fidelityfx_upscaler_vk.so`, such as fsr4-vk's)
  takes their place.
- **Or fetched for you (Windows):** `make_game.py --fsr --fetch-fsr-runtime --accept-amd-license` downloads those two
  DLLs and AMD's license from AMD's FidelityFX SDK repository at the v2.4.0 tag, checks each against its SHA-256, and
  puts them in the game folder (`HOOKSHOT_FFX_SDK=<folder>` keeps them between builds, default `workfx-sdk`).
  `python -I scripts/ffx_sdk.py --accept-amd-license --game <game folder>` does the same for a game built already.
  Without `--accept-amd-license` nothing is downloaded (the script asks when run by hand), as with NVIDIA's license for
  DLSS. AMD DLLs already in the game folder that are not v2.4.0's (a newer SDK put there by hand) are left alone, and
  the build says so. They are AMD's, under
  [AMD's FSR SDK license](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/blob/7539ae8b6568ad29ed78a753404351daf3b5cca7/Kits/FidelityFX/docs/license.md)
  (`LICENSE-AMD-FSR-SDK.md` beside them). On Linux the flag does nothing but say so.

The log's `vk: AMD FSR 4:` lines say which of AMD's upscalers it found for the card and which it uses (the newest FSR 4;
never an older FSR in its place), and why not when it did not start. Tested on an RX 6700 XT (RDNA 2).

</details>

<details>
<summary>Switches for testing (hookshot_env.txt)</summary>

Switches to test (`RX_VK_NOOPT=1` and the others) go in `hookshot_env.txt` in the game folder, one `NAME=VALUE` a
line, on Windows and Linux. The running game reads it, not the builder. A test run can name another file with
`HOS_ENV_FILE=<path>`; the game then reads that one instead and leaves `hookshot_env.txt` alone.
`hookshot_env.txt` sets only the game's own switches (names that start with `RX_`, `HOS_`, `HOOKSHOT_`, `EDEN_`,
`NVN_` or `SDL_`, but not SDL's library names such as `SDL_VULKAN_LIBRARY`), and no value that is a path except
`RX_VK_DLSS_DIR`'s (a folder on this PC, not a network share), and no value at all for the switches that name a file
or folder (`HOS_AUDIO_DUMP`, `RX_VK_SHOTS` and the like) or press buttons on a timer (`HOS_INPUT_SCRIPT`,
`RX_VK_MENU_SCRIPT`); other lines are left out, and the log says so. Switches that need a path, or other variables, go
in a file named by `HOS_ENV_FILE`.
A switch set in the environment the game is started with (a shell's `HOS_FULLSCREEN=0 ./hookshot`, a launcher's own)
goes over the same name in the file, and the log says so; within the file, a later line replaces an earlier one. A
switch that stands in for a menu row (`RX_VK_PACE`, `RX_VK_UPSCALE`, `RX_VK_FRAMEGEN_MULT`, `HOS_REALTIME_STEP`,
`HOS_DYNRES`, `HOS_OUTPUT_RES`) applies to that run only: `hookshot_options.ini` keeps the value chosen in the menu.

To enable the debug tools, turn **Debug tools** on in the menu's Debug tab (or add `HOS_OOT_CHEATS=1` to
`hookshot_env.txt` in the game folder). The tools then show below it on the same tab. The tab's Developer section,
always shown, has the Descriptor reuse, Sampler cache, Robust buffers and ASTC as RGBA8 rows.

`RX_VK_UI_GAME_FONT=0` draws the menu with the included font (Libertinus) even when the game's files have the game's own
menu font. `RX_VK_UI_PROMPTS=pad` (or `keys`) shows the controller's (or the keyboard's) prompts whatever was used last,
for screenshots.

For a first run on a 4 GB card such as a GTX 1650, `RX_VK_LOW_SPEC=1` starts with the Minimum (4GB) preset, with
frame generation and upscaling off. It applies only when there is no `hookshot_options.ini` yet; after that, change
the settings in the in-game menu.

`RX_VK_MOVIE_UPSCALE=linear` replaces the movies' neural upscale with the plain linear one. The default, `auto`, keeps
the networks and falls back only when a movie frame takes the GPU more than 40 ms. That is on NVIDIA cards: on other
cards, or where the GPU cannot multiply the networks' matrices itself, the movies always get the linear upscale. It does
not change the gameplay upscaler, and movies can look softer. `RX_VK_NETWORK_KEEP=all` takes precedence and keeps the
movie networks.

`RX_VK_SKIP_CODE=<prefix,...>` leaves out the draws of every program with a stage whose native code key starts with one
of the prefixes (the keys the log prints after "its code:"). On a Steam Deck, `RX_VK_SKIP_CODE=2c56dc1006942f74`
avoids a GPU hang in Kokiri Forest and the Great Deku Tree: the GPU runs for seconds in the programs that share that
task shader (a culling pass) until the kernel resets it ([#817](https://github.com/NotRexed/Hookshot/issues/817)).
It is a workaround, not a fix: those are task + mesh programs, so the geometry they draw goes with them, and key models
are missing while it is set. Off unless set.

The switches for scripted test runs, screenshots, frame dumps and profiles are in the
[development guide](../development.md#switches-for-testing-and-debugging).

`RX_VK_WB_MAX_KB=<KB>` stops the write-back of written GPU bindings larger than that. OoT writes tens of MB of culling
lists and per-frame buffers through the GPU that the CPU never reads, and copying them back sat on every submit's
memory sync: in Hyrule Field on a 10 GB RTX 3080, about 66 MB and 4.4 ms a submit, holding the scene at 45-48 fps.
`RX_VK_WB_MAX_KB=1024` still copies the small results the CPU does read (the exposure meter and the like) and took the
same scene to 59-60 fps. `0` or unset copies every written binding, as before.

</details>

