"""Compile and run the Quality preset Auto (this PC)'s rules (gpu/vk/auto_settings.h; no game/GPU), and check that the menu and
the renderer still use them: the preset row reaches Auto, a first start starts on it, and the hardware is read once the window is
known.

python -I -B tests/test_auto_settings.py [--cxx g++]
"""
import argparse
import subprocess
import tempfile
from pathlib import Path

RUN_ALL_ARGS = ["--cxx", "g++"]

ROOT = Path(__file__).resolve().parents[1]


def need(where, text, needles):
    for needle in needles:
        if needle not in text:
            raise SystemExit(where + " no longer has: " + needle)


def check_source():
    menu = (ROOT / "gpu/vk/vk_menu.cpp").read_text(encoding="utf-8")
    need("vk_menu.cpp", menu, ['{TAB_GRAPHICS, "Quality preset", &opts.preset, 0, 6, 1, tx_preset,', '"Auto (this PC)"',
                               "constexpr int kAutoPreset = 6;", "if (first) opts.preset = kAutoPreset;",
                               "::auto_settings::choose(hw)", "if (fp == auto_for)", 'fprintf(f, "auto_for=%016llx\\n"',
                               # (save() keeps the lines it does not write: auto_for must be one it writes, or the old line,
                               # kept after the new one, wins at the next start and Auto chooses again every time)
                               '|| key == "memory_budget" || key == "auto_for" ||', 'key == "auto_tuned";',
                               # (the measured tuning in play: from the replay's windows, finished once for this PC)
                               "::auto_settings::tune(tuning,", 'fprintf(f, "auto_tuned=%016llx\\n"'])
    main = (ROOT / "gpu/vk/vk_main.cpp").read_text(encoding="utf-8")
    need("vk_main.cpp", main, ["auto_profile();   // (the Quality preset Auto (this PC)", "menu_auto_for_gpu(p, hw);"])
    need("vk_replay.cpp", (ROOT / "gpu/vk/vk_replay.cpp").read_text(encoding="utf-8"), ["menu_auto_tune(gpu_ms, draws, 120.0 / secs);"])
    api = (ROOT / "gpu/vk/vk_api.h").read_text(encoding="utf-8")
    need("vk_api.h", api, ["#define MKWVK_WINDOW_VERSION 3", "void (*desktop_size)(void *window, int *width, int *height);"])
    host = (ROOT / "host/posix/display_sdl.c").read_text(encoding="utf-8")
    need("host/posix/display_sdl.c", host, ["refresh_mhz, desktop_size};"])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cxx", default="g++")
    args = parser.parse_args()
    check_source()
    with tempfile.TemporaryDirectory(prefix="auto-settings-") as tmp:
        exe = Path(tmp) / "auto-settings"
        subprocess.run([args.cxx, "-std=c++20", "-Wall", "-Wextra", "-Werror",
                        str(ROOT / "tests/test_auto_settings.cpp"), "-o", str(exe)], check=True)
        subprocess.run([str(exe)], check=True)


if __name__ == "__main__":
    main()
