"""The launcher's core library (launcher/Core): tests/launcher_core compiles its sources and checks them over authored inputs, with
no game, no network and no window. The version vectors are tests/test_update_tracks.py's own (TRACKS, ORDER, UNORDERED, LIST, read
here with ast), so the launcher keeps the order the game, the setup and the Linux updater are held to; the hand-over samples are
tests/fixtures/launcher's (docs/launcher-upgrade.md). Its scratch folder is named "ゲーム Zélda 🎮", as tests/test_utf8_paths.py's.

    python -I tests/test_launcher_core.py [--dotnet <dotnet>] [--strict]

With no dotnet here the check is skipped (exit 0, saying why), unless --strict.
"""
import argparse
import ast
import json
import os
import re
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
RUN_ALL_ARGS = ["--dotnet", "{dotnet}"]


def vectors():
    """TRACKS, ORDER, UNORDERED and LIST from tests/test_update_tracks.py, as literals (the file is not imported: it imports the Linux
    updater)"""
    tree = ast.parse((ROOT / "tests" / "test_update_tracks.py").read_text(encoding="utf-8"))
    found = {}
    for node in tree.body:
        if isinstance(node, ast.Assign) and len(node.targets) == 1 and isinstance(node.targets[0], ast.Name) \
                and node.targets[0].id in ("TRACKS", "ORDER", "UNORDERED", "LIST"):
            found[node.targets[0].id] = ast.literal_eval(node.value)
    missing = {"TRACKS", "ORDER", "UNORDERED", "LIST"} - set(found)
    if missing:
        raise SystemExit("tests/test_update_tracks.py has no %s" % ", ".join(sorted(missing)))
    return {"tracks": found["TRACKS"], "order": found["ORDER"], "unordered": found["UNORDERED"], "list": found["LIST"]}


def check_languages():
    """launcher/Core/Languages.cs lists make_game.py's LANGUAGES, in its order (the codes --language takes)"""
    tree = ast.parse((ROOT / "nx2" / "make_game.py").read_text(encoding="utf-8"))
    want = next(ast.literal_eval(n.value) for n in tree.body if isinstance(n, ast.Assign) and len(n.targets) == 1
                and isinstance(n.targets[0], ast.Name) and n.targets[0].id == "LANGUAGES")
    source = (ROOT / "launcher" / "Core" / "Languages.cs").read_text(encoding="utf-8")
    body = source[source.index("All = new[]"):source.index("};", source.index("All = new[]"))]
    have = re.findall(r'\("([^"]+)", "([^"]+)"\)', body)
    if have != [tuple(x) for x in want]:
        raise SystemExit("launcher/Core/Languages.cs differs from make_game.py's LANGUAGES:\n%s\n%s" % (have, want))


def main():
    check_languages()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=None)
    parser.add_argument("--strict", action="store_true")
    args = parser.parse_args()
    dotnet = args.dotnet or shutil.which("dotnet") or (str(Path.home() / ".dotnet" / "dotnet")
                                                       if (Path.home() / ".dotnet" / "dotnet").is_file() else None)
    if not dotnet:
        if args.strict:
            raise SystemExit("no dotnet found (pass --dotnet)")
        print("skipped: no dotnet here (pass --dotnet <path>)")
        return
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1")
    if not env.get("DOTNET_ROOT") and os.path.isabs(dotnet):
        env["DOTNET_ROOT"] = os.path.dirname(os.path.realpath(dotnet))
    with tempfile.TemporaryDirectory(prefix="launcher-core-") as folder:
        root = Path(folder)
        (root / "vectors.json").write_text(json.dumps(vectors(), ensure_ascii=False), encoding="utf-8")
        out = root / "build"
        subprocess.run([dotnet, "build", str(ROOT / "tests" / "launcher_core" / "LauncherCoreTests.csproj"), "--configuration", "Release",
                        "--artifacts-path", str(out), "--disable-build-servers", "-nologo", "-v:q"], cwd=root, env=env, check=True)
        dll = next((out / "bin").rglob("LauncherCoreTests.dll"))
        scratch = root / "ゲーム Zélda 🎮"
        subprocess.run([dotnet, str(dll), str(root / "vectors.json"), str(ROOT / "tests" / "fixtures" / "launcher"), str(scratch)],
                       cwd=root, env=env, check=True, timeout=300)


if __name__ == "__main__":
    sys.exit(main())
