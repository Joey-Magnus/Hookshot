"""The hardware test workflows keep a pull request's code off testers' PCs except through the runner owner's sandbox
(docs/hardware-runners.md).

Reads .github/workflows/hardware-test.yml and hardware-test-label.yml as text, and runs the gate's and the report's
shell steps with a stand-in gh. No network, no runner, no game.
"""
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest

# (the steps are bash)
RUN_ALL_SKIP = {"windows": "the workflow steps are bash; exercised on Linux"}

ROOT = Path(__file__).resolve().parents[1]
WF = ROOT / ".github" / "workflows"
TEST = WF / "hardware-test.yml"
GATE = WF / "hardware-test-label.yml"
NAME = r"^[a-z0-9][a-z0-9-]{0,31}$"


def triggers(text):
    """The event names under the top-level on:."""
    m = re.search(r"^on:\n((?:[ \t]+.*\n|\n)+)", text, re.M)
    assert m, "no on: block"
    return set(re.findall(r"^  ([a-z_]+):", m.group(1), re.M))


def jobs(text):
    """Job id -> its text."""
    body = text[text.index("\njobs:\n") + 7:]
    parts = re.split(r"^  ([a-z_-]+):\n", body, flags=re.M)
    return dict(zip(parts[1::2], parts[2::2]))


def runs(text):
    """Every run: | script, dedented."""
    out = []
    lines = text.splitlines()
    for i, line in enumerate(lines):
        m = re.match(r"^(\s*)(?:- )?run: \|$", line)
        if not m:
            continue
        indent, block = len(m.group(1)), []
        for nxt in lines[i + 1:]:
            if nxt.strip() and len(nxt) - len(nxt.lstrip()) <= indent:
                break
            block.append(nxt)
        width = min(len(b) - len(b.lstrip()) for b in block if b.strip())
        out.append("\n".join(b[width:] for b in block) + "\n")
    return out


class Workflows(unittest.TestCase):
    def setUp(self):
        self.test = TEST.read_text(encoding="utf-8")
        self.gate = GATE.read_text(encoding="utf-8")

    def test_only_a_dispatch_starts_the_hardware_test(self):
        self.assertEqual(triggers(self.test), {"workflow_dispatch"})

    def test_the_gate_reads_labels_and_pushes_only(self):
        self.assertEqual(triggers(self.gate), {"pull_request_target"})
        self.assertRegex(self.gate, r"types: \[labeled, synchronize\]")

    def test_no_other_workflow_uses_a_self_hosted_runner(self):
        for wf in sorted(WF.glob("*.yml")):
            if wf != TEST:
                for line in re.findall(r"runs-on:.*", wf.read_text(encoding="utf-8")):
                    self.assertNotIn("self-hosted", line, wf.name)

    def test_the_self_hosted_job_has_no_token_and_no_checkout(self):
        j = jobs(self.test)
        hosted = [k for k, v in j.items() if "self-hosted" in v]
        self.assertEqual(hosted, ["run"])
        run = j["run"]
        self.assertIn("runs-on: [self-hosted, hookshot-hardware,", run)
        self.assertRegex(run, r"\n    permissions: \{\}\n")
        self.assertNotIn("actions/checkout", run)
        self.assertNotIn("github.token", run)
        self.assertNotIn("secrets.", run)
        self.assertIn("needs: check", run)

    def test_the_runner_refuses_without_its_sandbox(self):
        script = next(s for s in runs(self.test) if "HOOKSHOT_HW_SANDBOX" in s)
        self.assertLess(script.index("exit 1"), script.index('"$HOOKSHOT_HW_SANDBOX" --repo'))

    def test_the_gate_never_checks_out_the_pull_request(self):
        self.assertNotIn("actions/checkout", self.gate)
        self.assertNotIn("head.ref", self.gate)
        self.assertNotIn("uses:", self.gate)

    def test_the_gate_dispatches_from_the_default_branch(self):
        self.assertIn("BRANCH: ${{ github.event.repository.default_branch }}", self.gate)
        self.assertIn('gh workflow run hardware-test.yml --ref "$BRANCH"', self.gate)

    def test_no_expression_inside_a_script(self):
        # (an expression in run: is pasted into the shell before it runs: a label or a branch name could be code)
        for name, text in (("gate", self.gate), ("test", self.test)):
            for script in runs(text):
                self.assertNotIn("${{", script, name)

    def test_actions_are_pinned_by_commit(self):
        for text in (self.gate, self.test):
            for use in re.findall(r"uses: (\S+)", text):
                self.assertRegex(use, r"@[0-9a-f]{40}$")

    def test_least_privilege(self):
        for text in (self.gate, self.test):
            self.assertRegex(text, r"\npermissions:\n  contents: read\n")
            self.assertNotIn("write-all", text)
            for job in jobs(text).values():
                self.assertIn("timeout-minutes:", job)
        self.assertNotIn("write", jobs(self.test)["check"].split("steps:")[0])

    def test_the_name_rule_is_the_same_everywhere(self):
        for text in (self.gate, self.test):
            self.assertIn(NAME.strip("^$"), text)


@unittest.skipUnless(shutil.which("bash") and shutil.which("jq"), "needs bash and jq")
class Steps(unittest.TestCase):
    """The scripts themselves, with a gh that records its arguments."""

    def run_step(self, script, env, gh_out=""):
        with tempfile.TemporaryDirectory() as d:
            gh = Path(d) / "gh"
            gh.write_text("#!/bin/sh\nprintf '%s\\n' \"$*\" >> \"$GH_LOG\"\n"
                          + (f"cat <<'EOF'\n{gh_out}\nEOF\n" if gh_out else ""), encoding="utf-8")
            gh.chmod(0o755)
            log = Path(d) / "log"
            log.touch()
            full = {"PATH": f"{d}:{os.environ['PATH']}", "GH_LOG": str(log), "HOME": d, **env}
            r = subprocess.run(["bash", "-e", "-o", "pipefail", "-c", script], cwd=d, env=full,
                               capture_output=True, text=True)
            comment = (Path(d) / "comment.md").read_text(encoding="utf-8") if (Path(d) / "comment.md").exists() else ""
            return r, log.read_text(encoding="utf-8"), comment, d

    def gate_dispatch(self):
        return next(s for s in runs(GATE.read_text(encoding="utf-8")) if "gh workflow run" in s)

    def test_a_good_label_dispatches_that_commit(self):
        sha = "a" * 40
        r, log, _, _ = self.run_step(self.gate_dispatch(), {"LABEL": "hardware: steam-deck", "PR": "2368",
                                                            "SHA": sha, "BRANCH": "main"})
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertEqual(log.strip(), f"workflow run hardware-test.yml --ref main -f pr=2368 -f sha={sha} "
                                      "-f hardware=steam-deck")

    def test_a_bad_label_dispatches_nothing(self):
        for label in ("hardware: Steam Deck", "hardware: x$(id)", "hardware: a;b", "hardware: ", "hardware: -x",
                      "hardware: " + "a" * 33, "hardware: ../x"):
            r, log, _, _ = self.run_step(self.gate_dispatch(), {"LABEL": label, "PR": "1", "SHA": "b" * 40,
                                                                "BRANCH": "main"})
            self.assertNotEqual(r.returncode, 0, label)
            self.assertEqual(log, "", label)

    def test_the_check_wants_the_labelled_head(self):
        script = next(s for s in runs(TEST.read_text(encoding="utf-8")) if "headRefOid" in s)
        sha = "c" * 40
        env = {"PR": "7", "SHA": sha, "HARDWARE": "nvidia-8gb"}
        for gh_out, ok in ((f"OPEN {sha} true", True), (f"OPEN {'d' * 40} true", False),
                           (f"OPEN {sha} false", False), (f"MERGED {sha} true", False)):
            r, _, _, d = self.run_step(script + 'cat "$GITHUB_OUTPUT"\n',
                                       {**env, "GITHUB_OUTPUT": os.devnull if not ok else "out"}, gh_out)
            self.assertEqual(r.returncode == 0, ok, gh_out + r.stdout + r.stderr)
        for bad in ({"HARDWARE": "a b"}, {"SHA": "short"}):
            r, log, _, _ = self.run_step(script, {**env, **bad, "GITHUB_OUTPUT": os.devnull}, f"OPEN {sha} true")
            self.assertNotEqual(r.returncode, 0, bad)
            self.assertEqual(log, "", bad)

    def test_the_jq_filter_counts_only_hardware_labels(self):
        script = next(s for s in runs(TEST.read_text(encoding="utf-8")) if "headRefOid" in s)
        jq = re.search(r"--jq '(.*)'\)", script.replace("'\"$HARDWARE\"'", "bug")).group(1)
        doc = '{"state":"OPEN","headRefOid":"x","labels":[{"name":"bug"},{"name":"hardware: steam-deck"}]}'
        out = subprocess.run(["jq", "-r", jq], input=doc, capture_output=True, text=True).stdout.split()
        self.assertEqual(out[2], "false")   # (the label "bug" is not hardware "bug")

    def test_the_report_quotes_the_runner_text(self):
        script = next(s for s in runs(TEST.read_text(encoding="utf-8")) if "comment.md" in s)
        evil = "fine\n````\n# injected\n@hckrman101 look\n<img src=x>\n"
        r, log, comment, _ = self.run_step(
            "mkdir -p results && printf '%s' \"$EVIL\" > results/results.txt\n" + script,
            {"EVIL": evil, "PR": "9", "SHA": "e" * 40, "HARDWARE": "amd-rdna4", "OUTCOME": "success",
             "GITHUB_SERVER_URL": "https://github.com", "GITHUB_REPOSITORY": "o/r", "GITHUB_RUN_ID": "1"})
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertIn("pr comment 9 --body-file comment.md", log)
        inner = comment.split("````text\n", 1)[1].rsplit("\n````", 1)[0]
        self.assertNotIn("````", inner)
        self.assertNotIn("@hckrman101", comment)
        self.assertIn("@\u200bhckrman101", inner)
        self.assertEqual(comment.count("````"), 2)


if __name__ == "__main__":
    unittest.main(verbosity=1)
