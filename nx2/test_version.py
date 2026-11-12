"""nx2/version.py, game-free: where the release name comes from, and a fingerprint that ignores line endings.

    python -I -B nx2/test_version.py
"""
import os
import re
import shutil
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import version  # noqa: E402


def tree(root, eol):
    for rel in ("host/a.c", "gpu/vk/b.cpp", "nx2/c.py"):
        p = os.path.join(root, rel)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "wb") as f:
            f.write(("line one%sline two%s" % (eol, eol)).encode())


def checkout():
    """A checkout's name: its release tag exactly on one, else nightly-<commit count>-<commit, 8 hex>, -dirty with changes"""
    if shutil.which("git") is None:
        print("version: no git here, the checkout's name is not checked")
        return
    with tempfile.TemporaryDirectory() as repo:
        env = dict(os.environ, GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@t", GIT_COMMITTER_NAME="t", GIT_COMMITTER_EMAIL="t@t",
                   GIT_CONFIG_NOSYSTEM="1", GIT_CONFIG_GLOBAL=os.devnull)

        def git(*args):
            return subprocess.run(["git", "-C", repo, *args], env=env, check=True, capture_output=True, text=True).stdout.strip()
        git("init", "-q")
        tree(repo, "\n")
        git("add", ".")
        git("commit", "-qm", "one")
        git("commit", "-q", "--allow-empty", "-m", "two")
        git("tag", "v2.1.0-rc.3")
        os.environ.pop("HOOKSHOT_VERSION", None)
        assert version.release(repo) == "v2.1.0-rc.3", version.release(repo)
        git("commit", "-q", "--allow-empty", "-m", "three")
        sha = git("rev-parse", "HEAD")[:8]
        assert version.release(repo) == "nightly-3-%s" % sha, version.release(repo)
        with open(os.path.join(repo, "host", "a.c"), "a") as f:
            f.write("changed\n")
        assert version.release(repo) == "nightly-3-%s-dirty" % sha, version.release(repo)
        name = version.release(repo)
        assert re.fullmatch(r"[0-9A-Za-z._+-]{1,128}", name), name   # (the setup lists' tag rule)
        git("tag", "not-a-version")
        git("checkout", "-q", "--", ".")
        assert version.release(repo) == "nightly-3-%s" % sha, "only v tags name a release"


def main():
    os.environ.pop("HOOKSHOT_VERSION", None)
    with tempfile.TemporaryDirectory() as lf, tempfile.TemporaryDirectory() as crlf:
        tree(lf, "\n")
        tree(crlf, "\r\n")
        assert version.release(lf) == "unknown", version.release(lf)
        assert version.fingerprint(lf) == version.fingerprint(crlf), "line endings changed the fingerprint"
        with open(os.path.join(lf, "nx2", "c.py"), "a") as f:
            f.write("changed\n")
        assert version.fingerprint(lf) != version.fingerprint(crlf), "a changed source kept the fingerprint"

        with open(os.path.join(lf, "VERSION"), "w") as f:
            f.write("movie-sync\nignored second line\n")
        assert version.release(lf) == "movie-sync"
        os.environ["HOOKSHOT_VERSION"] = "from-env"
        assert version.release(lf) == "from-env", "HOOKSHOT_VERSION comes first"

        os.environ["HOOKSHOT_VERSION"] = 'v1 "quoted" \\ & | %PATH%'
        d = version.describe(lf)
        assert not set(d) & set('"\\&|%'), d
        assert version.define(lf).startswith('-DHOOKSHOT_VERSION="') and version.define(lf).endswith(')"')
        os.environ.pop("HOOKSHOT_VERSION")
    checkout()
    print("version: ok (%s)" % version.describe())


if __name__ == "__main__":
    main()
