"""The files the game writes by itself are not written through a symbolic link at their name (Linux).

A game folder unpacked from an archive someone shared can hold links (tar and unzip keep them on Linux). The game rewrites
hookshot_options.ini when an option changes, and writes its pipeline caches, state lists, markers, button-prompt textures,
mouse_sens.txt, language.txt and the moved-saves note by itself: through such a link a plain fopen("w") emptied and rewrote
the file it named. host/hos_fopen_write.h opens those with O_NOFOLLOW and only as regular files.

Checked here, without a game:
- hos_fopen_write refuses a link (to a file, or dangling: nothing is made where it points) and a FIFO (without waiting),
  writes and empties a regular file for "w"/"wb" and appends for "a"/"ab";
- in the files that write those, every fopen for writing goes through it, except the debugging writes that a switch
  turns on (ALLOWED: their counts).

python -I -B tests/test_game_writes_no_links.py [--cc gcc]
"""
import argparse
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RUN_ALL_SKIP = {"windows": "the links checked are Linux's (an archive's links are not made on Windows)"}

# (the sources of the writes listed above, and how many plain fopen writes each keeps: switch-gated debugging only.
# vk_draw.cpp: RX_VK_PROBE's two files and RX_VK_DRAW_STATS' file)
ALLOWED = {"gpu/vk/vk_menu.cpp": 0, "gpu/vk/vk_draw.cpp": 3, "host/nn_applet.c": 0, "host/prompts.c": 0,
           "host/nn_hid.c": 0, "host/hos_main.c": 0}
# (fopen(<path>, "w"), or with the mode chosen by a ?: : the path's own text holds no string)
PLAIN_WRITE = re.compile(r'(?<![\w.])(?:std::)?fopen\s*\([^;"]*,[^;",]*"[wa]b?\+?"')

FIXTURE = r'''
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include "hos_fopen_write.h"
#define CHECK(x) do { if (!(x)) { fprintf(stderr, "FAIL:%d %s\n", __LINE__, #x); return 1; } } while (0)
static int text_is(const char *path, const char *want) {
    char got[64] = {0};
    FILE *f = fopen(path, "rb");
    if (!f) return 0;
    size_t n = fread(got, 1, sizeof got - 1, f);
    fclose(f);
    return n == strlen(want) && !memcmp(got, want, n);
}
int main(int argc, char **argv) {
    const char *dir = argv[1];
    char target[512], link[512], dangling[512], nowhere[512], plain[512], fifo[512];
    snprintf(target, sizeof target, "%s/target", dir);
    snprintf(link, sizeof link, "%s/hookshot_options.ini", dir);
    snprintf(dangling, sizeof dangling, "%s/language.txt", dir);
    snprintf(nowhere, sizeof nowhere, "%s/made_by_the_link", dir);
    snprintf(plain, sizeof plain, "%s/mouse_sens.txt", dir);
    snprintf(fifo, sizeof fifo, "%s/fifo", dir);
    const char *modes[] = {"w", "wb", "a", "ab"};
    for (int i = 0; i < 4; i++) {
        CHECK(hos_fopen_write(link, modes[i]) == NULL);
        CHECK(text_is(target, "the player's file"));
        CHECK(hos_fopen_write(dangling, modes[i]) == NULL);
        struct stat st;
        CHECK(lstat(nowhere, &st) != 0);
        CHECK(hos_fopen_write(fifo, modes[i]) == NULL);   /* (returns: a FIFO is not waited on) */
    }
    FILE *f = hos_fopen_write(plain, "w");
    CHECK(f && fputs("first", f) >= 0 && fclose(f) == 0 && text_is(plain, "first"));
    f = hos_fopen_write(plain, "ab");
    CHECK(f && fputs("+more", f) >= 0 && fclose(f) == 0 && text_is(plain, "first+more"));
    f = hos_fopen_write(plain, "wb");
    CHECK(f && fputs("new", f) >= 0 && fclose(f) == 0 && text_is(plain, "new"));
    CHECK(hos_fopen_write(plain, "r") == NULL);
    puts("PASS: hos_fopen_write refuses links and FIFOs, writes, empties and appends to regular files");
    return 0;
}
'''


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--cc", default="gcc")
    a = p.parse_args()
    bad = []
    for rel, keep in ALLOWED.items():
        text = (ROOT / rel).read_text(encoding="utf-8", errors="replace")
        plain = PLAIN_WRITE.findall(text)
        if len(plain) != keep:
            bad.append("%s: %d plain fopen writes, %d expected (switch-gated debugging only): %s" % (
                rel, len(plain), keep, "; ".join(plain)))
        if "hos_fopen_write(" not in text:
            bad.append("%s: does not use hos_fopen_write" % rel)
    if bad:
        sys.exit("\n".join(bad))
    print("PASS: the game's own writes in %d files go through hos_fopen_write" % len(ALLOWED))
    with tempfile.TemporaryDirectory() as tmp:
        tmp = Path(tmp)
        src = tmp / "fopen_write.c"
        src.write_text(FIXTURE, encoding="utf-8")
        exe = tmp / "fopen_write"
        subprocess.run([a.cc, "-std=c11", "-D_POSIX_C_SOURCE=200809L", "-Wall", "-Werror", "-I", str(ROOT / "host"),
                        str(src), "-o", str(exe)], check=True)
        files = tmp / "game"
        files.mkdir()
        (files / "target").write_text("the player's file", encoding="utf-8")
        os.symlink(files / "target", files / "hookshot_options.ini")
        os.symlink(files / "made_by_the_link", files / "language.txt")
        os.mkfifo(files / "fifo")
        subprocess.run([str(exe), str(files)], check=True, timeout=30)


if __name__ == "__main__":
    main()
