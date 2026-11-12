"""docs/user/settings.md has a line for every row of the Insert menu's Graphics, Audio, Gameplay and Controls tabs, and
of the Motion and Controls pages.

Four rows once had none: Ultrawide menus, Mesh shaders, Discord status (which is on by default) and the Controls page's
Button prompts. The rows are read from gpu/vk/vk_menu.cpp's table and the lines from the page's
"| Setting | Choices | Default | What it does |" tables between its Graphics and Debug sections; nothing runs. Restart
and Quit are actions with no choice to list, so a line for them is allowed but not asked for; the Graphics tab's
section titles have no line, and the Debug tab's tables, after the checked ones, are left out.

    python -I -B tests/test_settings_menu_rows.py
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
MENU = (ROOT / "gpu" / "vk" / "vk_menu.cpp").read_text(encoding="utf-8")
SETTINGS = (ROOT / "docs" / "user" / "settings.md").read_text(encoding="utf-8")
ACTIONS = {"Restart", "Quit"}
NO_DEFAULT = ACTIONS | {"Motion", "Controls", "Bug report"}   # (they open a page or do a thing: no choice, no default)
HEADER = "| Setting | Choices | Default | What it does |"


# (the Graphics tab's section titles: rows with heading, the last field, set; nothing to choose on them)
HEADING = r'\{TAB_\w+,\s*"([^"]+)",\s*nullptr,[^{}]*,\s*-1,\s*true\}'
HEADINGS = re.findall(HEADING, MENU)
ROWS = re.sub(HEADING, "", MENU)


def menu_rows(tabs="GRAPHICS|AUDIO|GAMEPLAY|CONTROLS|MOTION|PAGE"):
    return re.findall(r'\{TAB_(%s),\s*"([^"]+)"' % tabs, ROWS)


def table_lines():
    """Each settings line as (its section heading, its cells), in the page's order, from Graphics up to Debug."""
    page = SETTINGS[SETTINGS.index("\n## Graphics\n"):SETTINGS.index("\n## Debug\n")]
    lines, section, in_table = [], "", False
    for line in page.split("\n"):
        if line.startswith("#"):
            section = line.lstrip("#").strip()
        if line.strip() == HEADER:
            in_table = True
            continue
        if in_table and line.startswith("|---"):
            continue
        if in_table and line.startswith("|"):
            lines.append((section, [cell.strip() for cell in line.strip().strip("|").split("|")]))
        else:
            in_table = False
    return lines


def names(section_test=lambda section: True):
    return [cells[0] for section, cells in table_lines() if section_test(section)]


class SettingsMenuRowsTests(unittest.TestCase):
    def test_the_tables_are_found(self):
        self.assertGreater(len(menu_rows()), 40)
        self.assertGreater(len(table_lines()), 40)
        self.assertIn(("GRAPHICS", "Quality preset"), menu_rows())
        self.assertIn("Resolution and upscaling", HEADINGS)

    def test_every_row_has_a_line(self):
        listed = set(names())
        missing = [name for _, name in menu_rows() if name not in listed and name not in ACTIONS]
        self.assertEqual(missing, [], "menu rows with no line in docs/user/settings.md")

    def test_every_line_is_a_row_the_menu_has(self):
        rows = {name for _, name in menu_rows()}
        gone = [name for name in names() if name not in rows]
        self.assertEqual(gone, [], "lines in docs/user/settings.md for rows the menu no longer has")

    def test_the_lines_keep_the_menus_order(self):
        tabs = ["GRAPHICS", "AUDIO", "GAMEPLAY", "CONTROLS"]   # (a tab shows its rows in the table's order, wherever they sit)
        rows = sorted(menu_rows("GRAPHICS|AUDIO|GAMEPLAY|CONTROLS"), key=lambda row: tabs.index(row[0]))
        order = [name for _, name in rows if name not in ACTIONS]
        pages = ("The Motion page", "The Controls page")   # (the pages' rows: tables of their own after the tabs')
        listed = [name for name in names(lambda section: section not in pages) if name not in ACTIONS]
        self.assertEqual(listed, order)
        for tab, section in (("MOTION", "The Motion page"), ("PAGE", "The Controls page")):
            with self.subTest(page=section):
                self.assertEqual(names(lambda s: s == section), [name for _, name in menu_rows(tab)])

    def test_every_line_has_its_four_cells_and_a_default(self):
        for _, cells in table_lines():
            with self.subTest(option=cells[0]):
                self.assertEqual(len(cells), 4)
                self.assertTrue(cells[3])
                if cells[0] not in NO_DEFAULT:
                    self.assertTrue(cells[1])
                    self.assertTrue(cells[2])


if __name__ == "__main__":
    unittest.main()
