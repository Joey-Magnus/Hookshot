# Building from source

> This page is the readme's old "Build from source" section, moved here as it was. It is rewritten in the
> developer-docs pass of the docs rewrite.


For developers, and for players who prefer the command line. The builder windows run the same script,
`nx2/make_game.py`. [CONTRIBUTING.md](../../CONTRIBUTING.md) has more.

<details>
<summary>Windows, from the command line</summary>

```powershell
python -I nx2/make_game.py --nup "C:\path\your-game.nup" --out "C:\Games\Hookshot" --language en-US
python -I nx2/make_game.py --romfs "C:\path\romfs" --exefs "C:\path\exefs" --out "C:\Games\Hookshot" --language en-US
```

Add `--check` with your `.nup` or `--exefs` to only check the game's version. It builds nothing.
Add `--log "C:\path\build.log"` to also save the build's output to a file (UTF-8, replaced if it exists); it works
with `--check` and `--update` too, and on Linux.
`HOOKSHOT_JOBS=<n>` (or `--jobs <n>`) limits how many files compile at once.
`HOOKSHOT_MEM_LIMIT_GB=<n>` lets the compilers take that many GB together, whatever is free now; up to 4 always run.

You need:

- Python 3 from python.org (the Microsoft Store's Python installs packages where `python -I` does not look), with
  `pip install -r requirements.txt` (core tools plus the button-prompt packages).
- [llvm-mingw](https://github.com/mstorsjo/llvm-mingw) (the `ucrt-x86_64` build), unpacked as `llvm-mingw` in the
  clone or with `HOOKSHOT_MINGW` set to its folder. Without it the build uses the clang from
  [emsdk](https://github.com/emscripten-core/emsdk) at `C:\emsdk` with Visual Studio's C++ tools and the Windows SDK
  (`HOOKSHOT_MSVC=1` forces that route).
- What the builder's `tools` folder carries and a clone does not. Run `HookshotBuilder.exe` once: the folder is
  `%LOCALAPPDATA%\HookshotBuilder\versions\<version>\HookshotBuilder\tools`. The builder sets the variables itself;
  from the command line, set them before you run `make_game.py`:
  - `tools\cpm`: the Vulkan headers and FFmpeg. Set `RX_CPM` to this folder. Without it (and without system
    Vulkan / SPIR-V / VMA headers or `HOOKSHOT_*_HEADERS`), the standalone build stops instead of shipping a
    game with no renderer.
  - `tools\wgpu-native`: set `WGPU_NATIVE` to this folder.
  - `tools\third_party`: the prebuilt files a clone lacks (`a64emit`, `lld-link`, `zbic` and SDL2). Copy its `.dll`,
    `.exe`, `.lib` and `.sha1` files to the same places under `third_party\`.
  - `tools\work`: copy it to `work\`. It holds the hot-function list, the link order, the profile and the icon;
    without them the game builds but runs slower.
- Visual Studio 2022 with .NET 8, only if you want to change the builder itself.

</details>

<details>
<summary>Linux, the AppImage from the command line</summary>

```sh
./Hookshot-<version>-x86_64.AppImage --cli --nup ~/your-game.nup --out ~/Games/Hookshot   # make_game.py's options
./Hookshot-<version>-x86_64.AppImage --play                    # the game the builder built last, without the window
./Hookshot-<version>-x86_64.AppImage --play ~/Games/Hookshot   # the game in that folder
./Hookshot-<version>-x86_64.AppImage --update ~/Games/Hookshot # the new release, and that folder built again
```

`HOOKSHOT_JOBS=<n>` (or `--jobs <n>` with `--cli`) limits how many files compile at once.
`HOOKSHOT_MEM_LIMIT_GB=<n>` lets the compilers take that many GB together, whatever is free now. Linux stops a compiler
that does not fit; it is then compiled again alone at the end.

In a game folder, `hookshot` is the launcher and the game itself is `bin/hookshot`. The builder's settings are in
`~/.config/hookshot/builder.ini`.

</details>

<details>
<summary>Linux, without the AppImage</summary>

You need `clang` and `lld` (LLVM 23 or newer reads the play profile; an older one builds without it), Python 3 with
`pip install -r requirements.txt`, FFmpeg's development files (with dav1d, for the movies) and SDL3 (SDL2 works too).
Then run `python -I nx2/make_game.py ...` as on Windows. Vulkan / SPIR-V / VMA headers come from the distro, from
`HOOKSHOT_*_HEADERS`, or from a CPM cache via `RX_CPM`.

`scripts/linux/deps.sh` fetches and builds all of these into `build-linux/deps`, and `scripts/linux/appimage.sh` makes
the AppImage from them. A folder built from a source checkout has no launcher: `hookshot` there is the game.

</details>

<details>
<summary>The Windows game, built on Linux</summary>

Unpack llvm-mingw's Linux build (`ucrt-ubuntu-22.04-x86_64`), then run `make_game.py` with `RX_TARGET=windows` and
`HOOKSHOT_MINGW` set to its folder. The objects go to `~/.cache/hookshot-builder-windows`, apart from the Linux game's.
The game folder runs under Proton or on Windows.

</details>

