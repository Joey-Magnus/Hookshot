"""What the builder reads back from a game folder it built before stays inside that folder.

A game folder can come from elsewhere (copied, unpacked from an archive). An update or a rebuild into it reads its
.hookshot_romfs_heads list (the damaged textures the extraction found), copies its kept .hookshot_exefs and hard-links
a romfs into its romfs folder. Checked here without a game:
- a heads list line that leaves the romfs (.., an absolute path, a link on the way) is not written to: the list is
  dropped and every file is checked instead;
- a stand-in is copied into a new file of its own, not through a planted <name>.zs.part link;
- a link inside the kept .hookshot_exefs, or inside the romfs the links go into, stops the build.

python -I -B tests/test_game_folder_links.py
"""
import contextlib
import importlib.util
import io
import os
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "nx2"))
spec = importlib.util.spec_from_file_location("make_game_under_test", ROOT / "nx2" / "make_game.py")
make_game = importlib.util.module_from_spec(spec)
spec.loader.exec_module(make_game)

ZSTD = b"\x28\xb5\x2f\xfd" + b"S" * 12
DAMAGED = b"\0\0\0\0" + b"D" * 12
STAMP = "unpacked by this builder: game.nup 123"


@unittest.skipIf(os.name == "nt", "symbolic links need a privilege on Windows")
class GameFolderLinks(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory(prefix="hookshot-folder-links-")
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name).resolve()
        self.out = self.root / "game"
        self.romfs = self.out / "romfs"
        (self.romfs / "Model").mkdir(parents=True)
        (self.romfs / "Model" / "a_Alb.bntx.zs").write_bytes(DAMAGED)
        (self.romfs / "Model" / "b_Alb.bntx.zs").write_bytes(ZSTD)
        self.outside = self.root / "outside"
        self.outside.mkdir()
        (self.outside / "x_Alb.bntx.zs").write_bytes(b"\0\0\0\0outside, not a game file")
        (self.outside / "y_Alb.bntx.zs").write_bytes(ZSTD)
        (self.out / ".hookshot_romfs").write_text(STAMP + "\n", encoding="utf-8")

    def heads(self, *lines):
        (self.out / make_game.HEADS).write_text(STAMP + "\n" + "".join(l + "\n" for l in lines), encoding="utf-8")

    def stand_in(self):
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            make_game.stand_in_damaged_files(str(self.out), str(self.romfs))

    def outside_untouched(self):
        self.assertEqual((self.outside / "x_Alb.bntx.zs").read_bytes(), b"\0\0\0\0outside, not a game file")

    def test_list_lines_outside_the_romfs_are_not_written(self):
        for i, line in enumerate(("../../outside/x_Alb.bntx.zs", str(self.outside / "x_Alb.bntx.zs"),
                                  "Model/../../../outside/x_Alb.bntx.zs")):
            with self.subTest(line=line):
                (self.out / ".hookshot_romfs_checked").unlink(missing_ok=True)
                (self.romfs / "Model" / "a_Alb.bntx.zs").write_bytes(DAMAGED)
                self.heads("Model/a_Alb.bntx.zs", line)
                self.stand_in()
                self.outside_untouched()
                # (the list was dropped and every file checked: the real damaged one still got its stand-in)
                self.assertEqual((self.romfs / "Model" / "a_Alb.bntx.zs").read_bytes(), ZSTD)

    def test_list_through_a_linked_folder_is_not_written(self):
        os.symlink(self.outside, self.romfs / "Linked")
        self.heads("Linked/x_Alb.bntx.zs")
        self.stand_in()
        self.outside_untouched()

    def test_a_planted_part_link_is_not_written_through(self):
        victim = self.root / "victim.txt"
        victim.write_bytes(b"victim")
        os.symlink(victim, self.romfs / "Model" / "a_Alb.bntx.zs.part")
        for listed in (True, False):
            with self.subTest(listed=listed):
                (self.out / ".hookshot_romfs_checked").unlink(missing_ok=True)
                (self.romfs / "Model" / "a_Alb.bntx.zs").write_bytes(DAMAGED)
                if listed:
                    self.heads("Model/a_Alb.bntx.zs")
                else:
                    (self.out / make_game.HEADS).unlink(missing_ok=True)
                self.stand_in()
                self.assertEqual(victim.read_bytes(), b"victim")
                self.assertEqual((self.romfs / "Model" / "a_Alb.bntx.zs").read_bytes(), ZSTD)
                self.assertEqual([p.name for p in (self.romfs / "Model").glob(".standin-*")], [])

    def test_links_in_what_the_builder_made_stop_the_build(self):
        kept = self.out / ".hookshot_exefs"
        kept.mkdir()
        (kept / "main").write_bytes(b"NSO0")
        os.symlink(self.outside / "y_Alb.bntx.zs", kept / "sdk")
        os.symlink(self.outside, self.romfs / "Pack")
        for folder in (kept, self.romfs):
            with self.subTest(folder=folder.name), contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaises(SystemExit):
                    make_game.refuse_folder_links(str(folder))
        clean = self.root / "clean"
        (clean / "sub").mkdir(parents=True)
        (clean / "sub" / "f").write_bytes(b"f")
        make_game.refuse_folder_links(str(clean))
        make_game.refuse_folder_links(str(self.root / "missing"))
        source = (ROOT / "nx2" / "make_game.py").read_text(encoding="utf-8")
        # (each copy from the kept exefs, and the links into an existing romfs, come after the check)
        self.assertEqual(source.count("shutil.copytree(kept, exefs)"),
                         source.count("refuse_folder_links(kept)\n" + " " * 12 + "shutil.copytree(kept, exefs)") +
                         source.count("refuse_folder_links(kept)\n" + " " * 8 + "shutil.copytree(kept, exefs)"))
        self.assertIn("refuse_folder_links(romfs)", source.split("link_or_copy(a.romfs, romfs)")[0][-300:])


if __name__ == "__main__":
    unittest.main()
