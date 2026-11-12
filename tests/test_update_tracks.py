"""The update tracks (release, release candidate, nightly): which track a version is on and the version order, the same in
the game's update check (host/hos_update.c, compiled here from its own text), the setup (builder/setup/setup.c) and the
Linux updater (builder/linux/linux_update.py). Updater.cs has the same rules, checked against these vectors by
tests/test_update_tracks_cs.py.
docs/versioning.md, "Update tracks": a nightly is nightly-<commit count>-<commit>; nightlies are ordered by their count,
releases and release candidates in semver order, and a nightly and a release are not ordered at all."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

RUN_ALL_ARGS = ["--cc", "gcc"]

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "builder" / "linux"))
import linux_update  # noqa: E402

CC = "gcc"

# (version, track)
TRACKS = [
    ("v2.0.7", "release"), ("v2.1.0", "release"),
    ("v2.1.0-rc.1", "rc"), ("v2.1.0-rc.12", "rc"),
    ("v2.1.0-rc.1-3-g1a2b3c4", "nightly"), ("v2.1.0-rc.1-0-g1a2b3c4", "nightly"), ("v2.0.14-12-gabcdef01", "nightly"),
    ("v2.0.5-3-g1a2b3c4-dirty", "nightly"),
    ("nightly-2029-c544828f", "nightly"), ("nightly-2029-c544828f-dirty", "nightly"), ("nightly-1-00000000", "nightly"),
    # (not a nightly's name: the count, then 8 lowercase hex)
    ("nightly-2029", "release"), ("nightly-2029-c544828", "release"), ("nightly-2029-C544828F", "release"),
    ("nightly-x-c544828f", "release"), ("nightly-1234567890-c544828f", "release"),
]
# newer, older (semver: a release candidate before its release, rc.N by number; nightlies by their count, a changed
# checkout after the build of its commit, and every one after the git describe names nightlies had before)
ORDER = [
    ("v2.1.0-rc.2", "v2.1.0-rc.1"), ("v2.1.0-rc.10", "v2.1.0-rc.9"), ("v2.1.0", "v2.1.0-rc.2"), ("v2.1.0-rc.1", "v2.0.14"),
    ("v2.0.10", "v2.0.9"), ("v3.0.0-rc.1", "v2.9.9"),
    ("nightly-2030-0a1b2c3d", "nightly-2029-c544828f"), ("nightly-10000-aaaaaaaa", "nightly-9999-bbbbbbbb"),
    ("nightly-2029-c544828f-dirty", "nightly-2029-c544828f"), ("nightly-2-aaaaaaaa", "v2.1.0-rc.3-248-gc544828f"),
    ("nightly-1-aaaaaaaa", "v9.0.0-rc.1-999-gabcdef01-dirty"),
]
# neither newer than the other: a nightly and a release or release candidate (a nightly's name has no version in it),
# the git describe names among themselves, and a name that is no version
UNORDERED = [
    ("nightly-2029-c544828f", "v2.1.0"), ("nightly-2029-c544828f", "v2.1.0-rc.3"), ("nightly-1-aaaaaaaa", "v0.0.1"),
    ("v2.1.0-rc.3-248-gc544828f", "v2.1.0"), ("v2.1.0-rc.3-248-gc544828f", "v2.0.14-12-gabcdef01"),
    ("nightly-2029-c544828f", "dev"), ("v2.1.0", "dev"), ("nightly-2029-c544828f", "nightly"),
]
# (a, b, a nightly and a release or release candidate: one built over the other changes the folder's track, which only a
# Track chosen in the menu does, and is asked first. A name that is no version is on no track)
OTHER_TRACK = [
    ("nightly-2029-c544828f", "v2.1.0", True), ("v2.1.0-rc.3", "nightly-2029-c544828f-dirty", True),
    ("v2.1.0-rc.3-248-gc544828f", "v2.1.0", True),
    ("nightly-2029-c544828f", "nightly-2030-0a1b2c3d", False), ("v2.1.0-rc.3-248-gc544828f", "nightly-2029-c544828f", False),
    ("v2.1.0", "v2.1.0-rc.3", False), ("nightly-2029-c544828f", "dev", False), ("v2.1.0", "dev", False),
    ("nightly-2029-c544828f", "nightly", False),
]
# (a version, as players see it)
SHOWN = [
    ("nightly-2029-c544828f", "Nightly 2029 \u00b7 c544828f"), ("nightly-2029-c544828f-dirty", "Nightly 2029 \u00b7 c544828f-dirty"),
    ("v2.1.0-rc.3", "v2.1.0-rc.3"), ("v2.1.0-rc.1-3-g1a2b3c4", "v2.1.0-rc.1-3-g1a2b3c4"),
    ("nightly-2029-c544828", "nightly-2029-c544828"),
]

# (the rolling releases, nightly and rc, and the parts releases are not versions: every track's list leaves them out)
LIST = """[{"tag_name":"nightly","draft":false},{"tag_name":"rc","draft":false,"prerelease":true},
{"tag_name":"v2.1.0-rc.2","draft":false},{"tag_name":"linux-parts"},
{"tag_name":"v2.1.0-rc.10","draft":false},{"tag_name":"v2.0.14","draft":false},{"tag_name":"v2.1.0-rc.9"}]"""

FIXTURE = r"""
#include <stdio.h>
/* PORTABLE */
/* SETUP */
int main(int argc, char **argv) {
    if (argc == 3 && argv[1][0] == 'n') return printf("%d %d\n", nightly_version(argv[2]), nightly_tag(argv[2])), 0;
    if (argc == 4 && argv[1][0] == 'o') return printf("%d\n", newer_version(argv[2], argv[3])), 0;
    if (argc == 3 && argv[1][0] == 's') {
        char shown[160];
        shown_version(argv[2], shown, sizeof shown);
        return printf("%s\n", shown), 0;
    }
    if (argc == 3 && argv[1][0] == 'l') {
        char tag[128] = "";
        FILE *f = fopen(argv[2], "rb");
        static char body[1 << 16];
        body[fread(body, 1, sizeof body - 1, f)] = 0;
        fclose(f);
        return printf("%s\n", newest_tag(body, tag, sizeof tag) ? tag : "-"), 0;
    }
    return 2;
}
"""


def portable(source):
    start = source.index("#if defined(_WIN32) || defined(__linux__)")
    end = source.index("#endif", source.index("static int newest_tag("))
    return source[start:end].replace("#if defined(_WIN32) || defined(__linux__)", "", 1)


def setup_part(source):
    start = source.index("static int nightly_tag(")
    return source[start:source.index("\n}\n", start) + 3]


class Tracks(unittest.TestCase):
    exe = None

    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory(prefix="hookshot-tracks-")
        folder = Path(cls.tmp.name)
        code = FIXTURE.replace("/* PORTABLE */", "#include <ctype.h>\n#include <stdlib.h>\n#include <string.h>\n" +
                               portable((ROOT / "host/hos_update.c").read_text(encoding="utf-8")))
        code = code.replace("/* SETUP */", setup_part((ROOT / "builder/setup/setup.c").read_text(encoding="utf-8")))
        (folder / "t.c").write_text(code, encoding="utf-8")
        cls.exe = str(folder / "t")
        subprocess.run([CC, "-std=c11", "-Wall", "-Wextra", "-Werror", str(folder / "t.c"), "-o", cls.exe], check=True)

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def c(self, *args):
        return subprocess.run([self.exe, *args], capture_output=True, text=True, check=True).stdout.split()

    def test_the_track_of_a_version(self):
        for version, track in TRACKS + [("nightly", "release"), ("rc", "release"), ("dev", "release")]:
            self.assertEqual(linux_update.track_of(version), track, version)
            nightly = "1" if track == "nightly" else "0"
            self.assertEqual(self.c("n", version), [nightly, nightly], version)

    def test_order(self):
        for newer, older in ORDER:
            self.assertTrue(linux_update.newer(newer, older), (newer, older))
            self.assertFalse(linux_update.newer(older, newer), (older, newer))
            self.assertFalse(linux_update.newer(newer, newer), newer)
            self.assertEqual(self.c("o", newer, older), ["1"], (newer, older))
            self.assertEqual(self.c("o", older, newer), ["0"], (older, newer))
            self.assertEqual(self.c("o", newer, newer), ["0"])
        for a, b in UNORDERED:
            self.assertFalse(linux_update.newer(a, b) or linux_update.newer(b, a), (a, b))
            self.assertEqual(self.c("o", a, b) + self.c("o", b, a), ["0", "0"], (a, b))
        # (semver's own key, as before; a nightly names no release)
        self.assertGreater(linux_update.release_key("v2.1.0"), linux_update.release_key("v2.1.0-rc.2"))
        self.assertIsNone(linux_update.release_key("nightly-2029-c544828f"))
        self.assertIsNone(linux_update.release_key("v2.1.0-rc.1-3-g1a2b3c4"))

    def test_another_track(self):
        for a, b, other in OTHER_TRACK:
            self.assertEqual(linux_update.other_track(a, b), other, (a, b))
            self.assertEqual(linux_update.other_track(b, a), other, (b, a))

    def test_a_nightly_as_players_see_it(self):
        for version, shown in SHOWN:
            self.assertEqual(linux_update.shown(version), shown, version)
            out = subprocess.run([self.exe, "s", version], capture_output=True, check=True).stdout.decode("utf-8")
            self.assertEqual(out.strip(), shown, version)

    def test_the_newest_release_candidate_or_release(self):
        path = Path(self.tmp.name) / "list.json"
        path.write_text(LIST, encoding="utf-8")
        self.assertEqual(self.c("l", str(path)), ["v2.1.0-rc.10"])
        self.assertIsNone(linux_update.release_key("rc"))
        self.assertEqual(linux_update.newest_release(["rc", "nightly"]), None)
        get = lambda url: LIST.encode() if url == linux_update.RELEASES_API else b"{}"   # noqa: E731
        self.assertEqual(linux_update.latest_tag(get, "rc"), "v2.1.0-rc.10")
        # (its version's release ships: the release is the newest)
        released = LIST.replace("v2.0.14", "v2.1.0")
        path.write_text(released, encoding="utf-8")
        self.assertEqual(self.c("l", str(path)), ["v2.1.0"])
        self.assertEqual(linux_update.latest_tag(lambda url: released.encode(), "rc"), "v2.1.0")

    def test_each_track_asks_its_own_release(self):
        asked = []

        def get(url):
            asked.append(url)
            if url == linux_update.RELEASES_API:
                raise OSError("rate limited")
            if url == linux_update.LATEST_API:
                return b'{"tag_name": "v2.0.14"}'
            if url.endswith("/releases/download/nightly/hookshot-linux.txt"):
                return ("tag nightly-2029-c544828f\npart %s 10 usr/share/hookshot https://example.invalid/a.zip\nfile x\n"
                        % ("a" * 64)).encode()
            raise OSError(url)
        self.assertEqual(linux_update.latest_tag(get), "v2.0.14")
        self.assertEqual(asked, [linux_update.LATEST_API])   # (a release: as before, the latest release only)
        self.assertEqual(linux_update.latest_tag(get, "rc"), "v2.0.14")   # (no API answer: the latest release)
        self.assertEqual(linux_update.latest_tag(get, "nightly"), "nightly-2029-c544828f")
        self.assertEqual(linux_update.list_url("nightly-2029-c544828f"),
                         "https://github.com/NotRexed/Hookshot/releases/download/nightly/hookshot-linux.txt")
        self.assertEqual(linux_update.list_url("v2.1.0-rc.1-3-g1a2b3c4"),
                         "https://github.com/NotRexed/Hookshot/releases/download/nightly/hookshot-linux.txt")
        self.assertEqual(linux_update.list_url("v2.1.0-rc.2"),
                         "https://github.com/NotRexed/Hookshot/releases/download/v2.1.0-rc.2/hookshot-linux.txt")
        self.assertEqual(linux_update.list_url(None), linux_update.LATEST_LIST)

    def test_what_each_track_updates_to(self):
        target = linux_update.update_target
        # a release candidate is never taken back to an older release, and is offered the next one and its release
        self.assertEqual(target("v2.0.14", "v2.1.0-rc.1"), "v2.1.0-rc.1")
        self.assertEqual(target("v2.1.0-rc.2", "v2.1.0-rc.1"), "v2.1.0-rc.2")
        self.assertEqual(target("v2.1.0", "v2.1.0-rc.2"), "v2.1.0")
        # a nightly takes the next nightly, and never an older one; an install named the old way takes the first new one
        self.assertEqual(target("nightly-2030-bbbbbbbb", "nightly-2029-aaaaaaaa"), "nightly-2030-bbbbbbbb")
        self.assertEqual(target("nightly-2028-bbbbbbbb", "nightly-2029-aaaaaaaa"), "nightly-2029-aaaaaaaa")
        self.assertEqual(target("nightly-2029-aaaaaaaa", "nightly-2029-aaaaaaaa-dirty"), "nightly-2029-aaaaaaaa-dirty")
        self.assertEqual(target("nightly-2029-aaaaaaaa", "v2.1.0-rc.1-3-gaaaaaaa"), "nightly-2029-aaaaaaaa")
        # a release and a nightly are not ordered: neither is taken for newer than the other
        self.assertEqual(target("v2.1.0", "nightly-2029-aaaaaaaa"), "nightly-2029-aaaaaaaa")
        self.assertEqual(target("nightly-2029-aaaaaaaa", "v2.1.0"), "v2.1.0")
        # a switch of track takes its newest, an older version too, and across the nightly and the releases
        self.assertEqual(target("v2.0.14", "v2.1.0-rc.1", switch=True), "v2.0.14")
        self.assertEqual(target("v2.0.14", "v2.1.0-rc.1-3-gaaaaaaa", switch=True), "v2.0.14")
        self.assertEqual(target("v2.0.14", "nightly-2029-aaaaaaaa", switch=True), "v2.0.14")
        self.assertEqual(target("nightly-2029-aaaaaaaa", "v2.1.0", switch=True), "nightly-2029-aaaaaaaa")
        self.assertEqual(linux_update.choose_mode(None, "v2.1.0-rc.1", "v2.0.14", False, False), "build")
        self.assertEqual(linux_update.choose_mode(None, "v2.1.0-rc.1", "v2.0.14", False, False, True), "update-builder")
        self.assertEqual(linux_update.choose_mode("v2.1.0-rc.1", "v2.1.0-rc.1", "v2.1.0-rc.2", False, True), "update-game")

    def test_the_game_offers_a_release_candidate_only_a_newer_one(self):
        source = (ROOT / "host/hos_update.c").read_text(encoding="utf-8")
        guard = "if (strchr(have, '-') && !nightly_version(have) && !newer_version(tag, have)) return;"
        self.assertEqual(source.count(guard), 2)   # (Windows and Linux)
        # and a release (or one) newer than the latest is offered nothing: a test build of the next release, not the last one
        self.assertEqual(source.count("if (!nightly_version(have) && newer_version(have, tag)) return;"), 2)
        # and a nightly only a newer nightly (by its count)
        self.assertEqual(source.count("if (nightly_version(have) && !newer_version(tag, have)) return;"), 2)
        self.assertEqual(source.count("if (!latest_tag(have, tag, sizeof tag)) {"), 2)


if __name__ == "__main__":
    if "--cc" in sys.argv:
        i = sys.argv.index("--cc")
        CC = sys.argv[i + 1]
        del sys.argv[i:i + 2]
    os.environ.setdefault("PYTHONDONTWRITEBYTECODE", "1")
    unittest.main()
