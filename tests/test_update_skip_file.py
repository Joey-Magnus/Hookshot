"""The update offer's "Skip this version" (host/hos_update.c write_skip, compiled here from its own text) writes
<game>/user/hookshot_update_skip.txt only into a real user folder and never through a symbolic link: a link put in the
file's place, or a user folder that is a link, leaves what it points to as it was. Windows refuses symbolic links
and junctions in the same place (checked by its text here).

python -I -B tests/test_update_skip_file.py [--cc gcc]
"""
import argparse
import os
import subprocess
import sys
import tempfile
from pathlib import Path

RUN_ALL_ARGS = ["--cc", "gcc"]
RUN_ALL_SKIP = {"windows": "symbolic links and the Linux branch of host/hos_update.c: checked on Linux"}

ROOT = Path(__file__).resolve().parents[1]

FIXTURE = r"""
#include <fcntl.h>
#include <stdio.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>
#define MAX_PATH 260
/* WRITE_SKIP */
int main(int argc, char **argv) { return argc == 3 && write_skip(argv[1], argv[2]) ? 0 : 1; }
"""


def block(text, marker):
    start = text.index(marker)
    end = text.index("{", start) + 1
    depth = 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start:end]


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--cc", default="gcc")
    a = p.parse_args()
    if os.name == "nt":
        print("SKIP: symbolic links: checked on Linux")
        return 0
    source = (ROOT / "host/hos_update.c").read_text(encoding="utf-8")
    # (Windows: the user folder and the file are looked at for reparse points before the write)
    win = source[source.index("#ifdef _WIN32\n#include \"hos_paths.h\""):source.index("#elif defined(__linux__)")]
    skip = win[win.index("if (answer == IDCANCEL) {"):]
    skip = skip[:skip.index("return;")]
    assert "is_link(user, ua)" in skip and "is_link(file, fa)" in skip and skip.index("is_link(user") < skip.index("_wfopen(file"), skip
    is_link = block(win, "static int is_link(")
    assert "IO_REPARSE_TAG_SYMLINK" in is_link and "IO_REPARSE_TAG_MOUNT_POINT" in is_link, is_link
    with tempfile.TemporaryDirectory(prefix="hookshot-skip-") as tmp:
        t = Path(tmp)
        (t / "t.c").write_text(FIXTURE.replace("/* WRITE_SKIP */", block(source, "static int write_skip(")), encoding="utf-8")
        exe = str(t / "t")
        subprocess.run([a.cc, "-std=gnu11", "-Wall", "-Wextra", "-Werror", str(t / "t.c"), "-o", exe], check=True)
        run = lambda game: subprocess.run([exe, str(game) + "/", "v2.1.0"]).returncode   # noqa: E731

        # an ordinary folder: the file is written (and the user folder made)
        game = t / "game"
        game.mkdir()
        assert run(game) == 0
        assert (game / "user/hookshot_update_skip.txt").read_text() == "skip=v2.1.0\n"
        assert run(game) == 0   # (again: replaced)
        assert (game / "user/hookshot_update_skip.txt").read_text() == "skip=v2.1.0\n"

        # the file is a link: the target stays as it was
        target = t / "precious.txt"
        target.write_text("keep me\n")
        (game / "user/hookshot_update_skip.txt").unlink()
        (game / "user/hookshot_update_skip.txt").symlink_to(target)
        assert run(game) != 0
        assert target.read_text() == "keep me\n"
        # (a dangling link: nothing is made where it points)
        (game / "user/hookshot_update_skip.txt").unlink()
        (game / "user/hookshot_update_skip.txt").symlink_to(t / "made.txt")
        assert run(game) != 0
        assert not (t / "made.txt").exists()

        # the user folder is a link to another folder: nothing is written there
        game2 = t / "game2"
        game2.mkdir()
        elsewhere = t / "elsewhere"
        elsewhere.mkdir()
        (elsewhere / "hookshot_update_skip.txt").write_text("keep me\n")
        (game2 / "user").symlink_to(elsewhere)
        assert run(game2) != 0
        assert (elsewhere / "hookshot_update_skip.txt").read_text() == "keep me\n"

        # a folder in the file's place: refused, left alone
        game3 = t / "game3"
        (game3 / "user/hookshot_update_skip.txt").mkdir(parents=True)
        assert run(game3) != 0
        assert (game3 / "user/hookshot_update_skip.txt").is_dir()
    print("OK: the skip file is written only as a plain file in a real user folder")
    return 0


if __name__ == "__main__":
    sys.exit(main())
