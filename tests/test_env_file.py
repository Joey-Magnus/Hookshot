"""HOS_ENV_FILE names the switches file a run reads instead of hookshot_env.txt beside the game.

A scripted or A/B test run brings its own switches this way, and the player's hookshot_env.txt is neither read nor
changed. Unset or empty, the game reads hookshot_env.txt as before. A named file that does not exist sets nothing: the
run does not fall back to the player's switches.

python -I -B tests/test_env_file.py [--cc gcc]
"""
import argparse
import os
import re
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def block(text, marker):
    start = text.index(marker)
    end = text.index("{", start) + 1
    depth = 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start:end]


# (a switch whose value is a path to write or read, as the sources read it: stored in a ..dir / ..path / ..file / ..folder
# / ..prefix variable, or handed straight to fopen, open, CreateFile, LoadLibrary or dlopen. Each must be one
# hookshot_env.txt cannot set; REVIEWED are the ones it may, on purpose)
GETENV = r'(?:std::)?getenv\("((?:RX|HOS|HOOKSHOT|EDEN|NVN|TOTK|SDL)_[A-Z0-9_]+)"\)'
PATH_VALUED = [re.compile(r"\b\w*(?:dir|path|file|folder|prefix)\w*\s*(?:\{|=)\s*" + GETENV, re.I),
               re.compile(r"\b(?:_w?fopen|fopen|open|CreateFile[AW]?|LoadLibrary[AW]?|dlopen)\s*\(\s*" + GETENV)]
REVIEWED = {"RX_VK_DLSS_DIR"}   # (a local folder for NVIDIA's runtime: env_line_allowed's own rule for it)


def path_valued_switches():
    found = {}
    for folder, globs in (("host", ("*.c", "*.h")), ("gpu", ("*.c", "*.cpp", "*.h"))):
        for g in globs:
            for f in sorted((ROOT / folder).rglob(g)):
                if "third_party" in f.parts or "tests" in f.parts:
                    continue
                text = f.read_text(encoding="utf-8", errors="replace")
                for pattern in PATH_VALUED:
                    for m in pattern.finditer(text):
                        found.setdefault(m.group(1), f.relative_to(ROOT).as_posix())
    return found


def check_path_valued(cc, main_c, tmp):
    found = path_valued_switches()
    if len(found) < 15:   # (the patterns stopped matching the sources: the check would pass on nothing)
        raise SystemExit("only %d path-valued switches found: update PATH_VALUED" % len(found))
    src = tmp / "allowed.c"
    src.write_text("#include <stdio.h>\n#include <string.h>\n" + block(main_c, "static int env_line_allowed(") +
                   '\nint main(int c, char **v) { for (int i = 1; i < c; i++) if (env_line_allowed(v[i], "x")) '
                   'puts(v[i]); return 0; }\n', encoding="utf-8")
    subprocess.run([cc, "-std=c11", "-Wall", "-Werror", str(src), "-o", str(tmp / "allowed")], check=True)
    names = sorted(set(found) - REVIEWED)
    taken = subprocess.run([str(tmp / "allowed"), *names], capture_output=True, text=True, check=True).stdout.split()
    if taken:
        raise SystemExit("hookshot_env.txt can set switches whose value is a path: %s (block them in env_line_allowed, "
                         "host/hos_main.c, or add them to REVIEWED)" % ", ".join("%s (%s)" % (n, found[n]) for n in taken))
    print("PASS: none of the %d switches whose value is a path can be set from hookshot_env.txt" % len(names))


# (RX_VK_DLSS_DIR, the one path a shared file may set: a folder named in full on this PC, nothing relative, no share,
# device path or .. step; True = taken)
DLSS_DIRS = {
    "linux": [("/opt/ngx", True), ("/home/x/.local/share/hookshot/ngx", True), ("/x/..dir/ngx", True),
              ("ngx", False), (".", False), ("..", False), ("../../Downloads", False), ("~/ngx", False),
              ("//server/share", False), ("/opt/../home/x", False), ("/opt/ngx/..", False), ("", False),
              ("C:\\ngx", False)],
    "windows": [("C:\\ngx", True), ("d:/games/ngx", True), ("C:\\x\\..dll\\ngx", True),
                ("ngx", False), ("C:ngx", False), ("\\ngx", False), ("\\\\server\\share", False),
                ("\\\\?\\C:\\ngx", False), ("//server/share", False), ("C:\\x\\..\\..\\Users", False),
                ("C:\\ngx\\..", False), ("..\\ngx", False), ("/opt/ngx", False), ("", False)],
}


DLSS_OK = "C:\\ngx" if os.name == "nt" else "/opt/ngx"   # (the fixture's own value, taken on this system)


def check_dlss_dir(cc, main_c, tmp):
    src = tmp / "dlss_dir.c"
    src.write_text("#include <stdio.h>\n#include <string.h>\n" + block(main_c, "static int env_line_allowed(") +
                   '\nint main(int c, char **v) { for (int i = 1; i < c; i++) '
                   'printf("%d\\n", env_line_allowed("RX_VK_DLSS_DIR", v[i])); return 0; }\n', encoding="utf-8")
    bad = []
    # (the rule is per system: Windows runs its own cases; Linux runs both, the Windows ones built with _WIN32 set)
    for system, cases in DLSS_DIRS.items():
        if os.name == "nt" and system != "windows":
            continue
        exe = tmp / ("dlss_dir_" + system)
        subprocess.run([cc, "-std=c11", "-Wall", "-Werror", *(["-D_WIN32"] if system == "windows" and os.name != "nt" else []), str(src),
                        "-o", str(exe)], check=True)
        got = subprocess.run([str(exe), *(v for v, _ in cases)], capture_output=True, text=True, check=True).stdout.split()
        bad += ["%s %r: %s" % (system, v, "taken" if g == "1" else "refused") for (v, want), g in zip(cases, got)
                if (g == "1") != want]
    if bad:
        raise SystemExit("RX_VK_DLSS_DIR from hookshot_env.txt: " + "; ".join(bad))
    print("PASS: a shared hookshot_env.txt sets RX_VK_DLSS_DIR only to a folder named in full on this PC")


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--cc", default="gcc")
    a = p.parse_args()
    main_c = (ROOT / "host" / "hos_main.c").read_text(encoding="utf-8")
    fn = block(main_c, "static int env_line_allowed(") + "\n" + block(main_c, "static void load_hookshot_env(void) {")

    with tempfile.TemporaryDirectory() as tmp:
        tmp = Path(tmp)
        check_path_valued(a.cc, main_c, tmp)
        check_dlss_dir(a.cc, main_c, tmp)
        game = tmp / "game"
        game.mkdir()
        # (the player's file sets the game's switches only: not the system's variables, HOS_ENV_FILE or paths)
        (game / "hookshot_env.txt").write_text(
            "HOS_T_BESIDE=1\nHOS_T_SHARED=beside\nVK_ADD_LAYER_PATH=/x\nLD_PRELOAD=/x.so\nPATH=C:\\x\nSDL_DYNAMIC_API=/x.so\n"
            "HOS_ENV_FILE=other.txt\nHOS_SAMPLE_FILE=/home/x/.bashrc\nRX_VK_DUMP_DIR=C:\\Users\\x\nRX_VK_SKIP_CODE=2c56,ab\n"
            "RX_VK_DLSS_DIR=" + DLSS_OK + "\nHOS_T_HOME=~\nSDL_VULKAN_LIBRARY=x.dll\nSDL_VIDEO_EGL_DRIVER=x.so\n"
            "SDL_AUDIODRIVER=pulseaudio\nHOS_T_UP=..\nHOS_SAMPLE_FILE=hookshot_vk.dll\nRX_VK_FRAME_LOG=x.txt\n"
            "RX_VK_DUMP_DIR=dumps\nHOS_PRESENT_TRACE=t.txt\nRX_VK_DLSS_DIR=\\\\server\\share\n"
            "HOS_AUDIO_DUMP=image.bin\nRX_VK_HANG_DUMP=hookshot_options.ini\nRX_VK_SHOTS=user\n"
            "HOS_INPUT_SCRIPT=1:A,2:A\nRX_VK_MENU_SCRIPT=1:INS\nRX_VK_DUMP_NATIVE=0\nRX_DUMP_IR=ir\nRX_SCREENSHOT=romfs\n"
            "RX_FLASH_LOG=user\nHOS_SCAN_DT=x\nRX_VK_FLASH_DUMPS=6\nRX_FRAMEGEN_DUMP=1\n", encoding="utf-8")
        (tmp / "test_env.txt").write_text("# a test run's own switches\nHOS_T_OTHER=1\nHOS_T_SHARED=other\n"
                                          "VK_T_LAYER=/y\n", encoding="utf-8")
        exe = str(game / "hookshot").replace("\\", "\\\\")
        fixture = r'''#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#ifdef _WIN32   /* (the test's own setenv/unsetenv: Windows has _putenv, which the game itself uses there) */
static int setenv(const char *name, const char *value, int overwrite) {
    char s[1024];
    (void)overwrite;
    snprintf(s, sizeof s, "%s=%s", name, value);
    return _putenv(s);
}
static int unsetenv(const char *name) { return setenv(name, "", 1); }
#endif
#define MAX_PATH 260
typedef unsigned long DWORD;
#define CHECK(x) do { if (!(x)) { fprintf(stderr, "FAIL:%d %s\n", __LINE__, #x); exit(1); } } while (0)
static DWORD hos_game_path_a(char *out, DWORD size) {   /* (host/hos_paths.h: the game folder's path) */
    snprintf(out, size, "%s", "''' + exe + r'''");
    return (DWORD)strlen(out);
}
''' + fn + r'''
static int is(const char *name, const char *want) {
    const char *v = getenv(name);
    return want ? v && !strcmp(v, want) : !v;
}
static void clear(void) { unsetenv("HOS_T_BESIDE"); unsetenv("HOS_T_OTHER"); unsetenv("HOS_T_SHARED"); unsetenv("VK_T_LAYER"); }
int main(int argc, char **argv) {
    (void)argc;
    unsetenv("HOS_ENV_FILE");                      /* out of the box: the file beside the game */
    load_hookshot_env();
    CHECK(is("HOS_T_BESIDE", "1") && is("HOS_T_SHARED", "beside") && is("HOS_T_OTHER", NULL));
    CHECK(is("VK_ADD_LAYER_PATH", NULL) && is("SDL_DYNAMIC_API", NULL) && is("HOS_ENV_FILE", NULL));
    CHECK(is("HOS_SAMPLE_FILE", NULL) && is("RX_VK_DUMP_DIR", NULL) && is("HOS_T_HOME", NULL));
    CHECK(is("HOS_AUDIO_DUMP", NULL) && is("RX_VK_HANG_DUMP", NULL) && is("RX_VK_SHOTS", NULL));
    CHECK(is("HOS_INPUT_SCRIPT", NULL) && is("RX_VK_MENU_SCRIPT", NULL));
    CHECK(is("RX_VK_DUMP_NATIVE", NULL) && is("RX_DUMP_IR", NULL) && is("RX_SCREENSHOT", NULL) && is("RX_FLASH_LOG", NULL));
    CHECK(is("HOS_SCAN_DT", NULL) && is("RX_VK_FLASH_DUMPS", NULL) && is("RX_FRAMEGEN_DUMP", NULL));
    CHECK(is("RX_VK_SKIP_CODE", "2c56,ab") && is("RX_VK_DLSS_DIR", "''' + DLSS_OK.replace("\\", "\\\\") + r'''"));
    CHECK(is("SDL_VULKAN_LIBRARY", NULL) && is("SDL_VIDEO_EGL_DRIVER", NULL) && is("HOS_T_UP", NULL));
    CHECK(is("SDL_AUDIODRIVER", "pulseaudio") && is("HOS_SAMPLE_FILE", NULL) && is("RX_VK_FRAME_LOG", NULL));
    CHECK(is("RX_VK_DUMP_DIR", NULL) && is("HOS_PRESENT_TRACE", NULL));
    CHECK(getenv("LD_PRELOAD") == NULL || strcmp(getenv("LD_PRELOAD"), "/x.so"));
    CHECK(getenv("PATH") == NULL || strcmp(getenv("PATH"), "C:\\x"));

    clear();
    setenv("HOS_ENV_FILE", "", 1);                 /* empty: as if unset */
    load_hookshot_env();
    CHECK(is("HOS_T_BESIDE", "1") && is("HOS_T_OTHER", NULL));

    clear();
    setenv("HOS_ENV_FILE", argv[1], 1);            /* a test run's own file, and only that one */
    load_hookshot_env();
    CHECK(is("HOS_T_OTHER", "1") && is("HOS_T_SHARED", "other") && is("HOS_T_BESIDE", NULL) && is("VK_T_LAYER", "/y"));

    clear();
    setenv("HOS_ENV_FILE", argv[2], 1);            /* a file that is not there: nothing, not the player's switches */
    load_hookshot_env();
    CHECK(is("HOS_T_BESIDE", NULL) && is("HOS_T_OTHER", NULL) && is("HOS_T_SHARED", NULL));
    printf("PASS: HOS_ENV_FILE replaces hookshot_env.txt; unset or empty reads it as before; the player's file sets only the game's switches\n");
    return 0;
}
'''
        src = tmp / "fixture.c"
        src.write_text(fixture, encoding="utf-8")
        out = tmp / "fixture"
        subprocess.run([a.cc, "-std=c11", "-D_POSIX_C_SOURCE=200809L", "-Wall", "-Werror", str(src), "-o", str(out)],
                       check=True)
        before = (game / "hookshot_env.txt").read_text(encoding="utf-8")
        subprocess.run([str(out), str(tmp / "test_env.txt"), str(tmp / "missing.txt")], check=True)
        if (game / "hookshot_env.txt").read_text(encoding="utf-8") != before:
            raise SystemExit("the player's hookshot_env.txt was changed")


if __name__ == "__main__":
    main()
