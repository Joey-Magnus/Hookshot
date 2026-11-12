"""Which Hookshot a build is: the release it came from and a fingerprint of its sources.

The game prints it as the first line of hookshot_log.txt and in the Insert menu, so a report says what was run.

    python -I nx2/version.py            prints it for this folder

The release name comes from, in order: HOOKSHOT_VERSION; a VERSION file at the root (written when a release is packaged,
since the builder's tools folder has no git history); in a checkout, its release tag when it is exactly on one (only
version tags, `v0.1.0`, not the branch-like tags from before the scheme), else the nightly's form,
`nightly-<commit count>-<commit>[-dirty]` (checkout_name). The
fingerprint is a hash of the sources in this folder (host/, gpu/, nx2/), line endings aside: every build from the
same release zip gives the same one, so even a build that says "unknown" can be traced to the zip it came from. (A
checkout and a release's tools folder hold different sets of files, so their fingerprints differ.)
"""
import hashlib
import os
import re
import subprocess

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCES = ("host", "gpu", "nx2")
SOURCE_EXT = (".c", ".cc", ".cpp", ".h", ".hpp", ".inc", ".py", ".glsl", ".comp", ".vert", ".frag", ".spv")


def release(root=ROOT):
    name = os.environ.get("HOOKSHOT_VERSION", "").strip()
    if not name:
        for file in ("VERSION", "version.txt"):   # (version.txt: the release's tag, written into the builder's tools when it is packed)
            try:
                with open(os.path.join(root, file), encoding="utf-8") as f:
                    name = f.readline().strip()
            except OSError:
                pass
            if name:
                break
    if not name and os.path.exists(os.path.join(root, ".git")):
        name = checkout_name(root)
    return name or "unknown"


def _git(root, *args):
    try:
        r = subprocess.run(["git", "-C", root, *args], capture_output=True, text=True, timeout=20)
    except (OSError, subprocess.SubprocessError):
        return None
    return r.stdout.strip() if r.returncode == 0 else None


def checkout_name(root=ROOT):
    """A checkout's name: its release tag when HEAD is exactly on one (v2.1.0, v2.1.0-rc.3, -dirty with changes), else the
    nightly's form, nightly-<commits in HEAD's history>-<HEAD's commit, 8 hex>[-dirty] (nightly-2029-c544828f), the name
    CI gives every build that is not a release (scripts/nightly_name.sh). A shallow clone cannot count its history: its
    commit alone, which names no track. "" when git does not answer."""
    tag = _git(root, "describe", "--tags", "--exact-match", "--dirty", "--match", "v[0-9]*")
    if tag and re.fullmatch(r"v\d+\.\d+\.\d+(-rc\.\d+)?(-dirty)?", tag):
        return tag
    sha = _git(root, "rev-parse", "HEAD")
    if not sha or not re.fullmatch(r"[0-9a-f]{40,64}", sha):
        return ""
    status = _git(root, "status", "--porcelain", "--untracked-files=no")
    dirty = "-dirty" if status else ""
    if _git(root, "rev-parse", "--is-shallow-repository") != "false":
        return sha[:8] + dirty
    count = _git(root, "rev-list", "--count", "HEAD")
    return "nightly-%s-%s%s" % (count, sha[:8], dirty) if count and count.isdigit() else sha[:8] + dirty


def fingerprint(root=ROOT):
    h = hashlib.sha1(usedforsecurity=False)
    for top in SOURCES:
        base = os.path.join(root, top)
        for d, dirs, files in os.walk(base):
            dirs[:] = sorted(x for x in dirs if x != "__pycache__")
            for f in sorted(files):
                if not f.endswith(SOURCE_EXT):
                    continue
                p = os.path.join(d, f)
                with open(p, "rb") as src:
                    data = src.read().replace(b"\r\n", b"\n")
                h.update(os.path.relpath(p, root).replace("\\", "/").encode() + b"\0" + data + b"\0")
    return h.hexdigest()[:8]


def describe(root=ROOT):
    """'movie-sync (source 1a2b3c4d)': safe to pass as a C string literal on any command line."""
    text = "%s (source %s)" % (release(root), fingerprint(root))
    return re.sub(r"[^A-Za-z0-9 ._+()/-]", "_", text)[:80]


def define(root=ROOT):
    return '-DHOOKSHOT_VERSION="%s"' % describe(root)


if __name__ == "__main__":
    print(describe())
