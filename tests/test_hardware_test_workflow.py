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
        self.assertIn("runs-on: [self-hosted, linux, hookshot-hardware,", run)
        self.assertRegex(run, r"\n    defaults:\n      run:\n        shell: bash\n")
        self.assertRegex(run, r"\n    permissions: \{\}\n")
        self.assertNotIn("actions/checkout", run)
        self.assertNotIn("github.token", run)
        self.assertNotIn("secrets.", run)
        self.assertIn("needs: check", run)

    def test_the_runner_refuses_without_its_sandbox(self):
        script = next(s for s in runs(self.test) if "HOOKSHOT_HW_SANDBOX" in s)
        self.assertLess(script.index("exit 1"), script.index('"$HOOKSHOT_HW_SANDBOX" --repo'))

    def test_the_runner_checks_the_approval_again_before_the_sandbox(self):
        run = jobs(self.test)["run"]
        recheck = run.index("- name: The commit is still the labelled head, after the queue")
        self.assertLess(recheck, run.index("- name: Run the commit in the runner's sandbox"))
        self.assertLess(run.index("- name: Run the commit in the runner's sandbox"), run.index("- uses:"))
        self.assertNotIn("continue-on-error", run)

    def test_manual_runs_come_from_the_default_branch(self):
        check = jobs(self.test)["check"]
        self.assertIn("DEFAULT_BRANCH: ${{ github.event.repository.default_branch }}", check)
        script = runs(check)[0]
        self.assertTrue(script.startswith('[ "$GITHUB_REF" = "refs/heads/$DEFAULT_BRANCH" ] ||'), script[:80])

    def test_the_pilot_is_fork_local_and_says_so(self):
        for text in (self.gate, self.test):
            self.assertIn("Fork-local pilot" if text is self.test else "fork-local pilot", text)
        self.assertIn("RUNNERS: ${{ vars.HOOKSHOT_HW_RUNNERS }}", self.gate)
        doc = (ROOT / "docs" / "hardware-runners.md").read_text(encoding="utf-8")
        for promise in ("test pull request opened on the fork itself", "Registering runners on NotRexed/Hookshot is "
                        "not the fix", "again before taking the GPU", "only regular files",
                        "the runner keeps its own session and artifact credentials", "`HOOKSHOT_HW_RUNNERS`",
                        "Runners are Linux"):
            self.assertIn(promise, re.sub(r"\s+", " ", doc))

    def test_the_cleanup_skips_old_pushes_and_newer_labels(self):
        unlabel = jobs(self.gate)["unlabel"]
        self.assertIn("PUSHED_AT: ${{ github.event.pull_request.updated_at }}", unlabel)
        self.assertIn('select(.event == "labeled")', unlabel)

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

    @staticmethod
    def stub(d, name, answers):
        """A stand-in command that logs its arguments and prints the answer of the first glob its arguments match
        (None: it fails)."""
        if isinstance(answers, str):
            answers = [("*", answers)] if answers else []
        cases = []
        for i, (glob, out) in enumerate(answers):
            if out is None:
                cases.append(f"  {glob}) exit 1 ;;\n")
            else:
                (Path(d) / f"{name}.{i}").write_text(out + "\n", encoding="utf-8")
                cases.append(f"  {glob}) cat '{d}/{name}.{i}' ;;\n")
        path = Path(d) / name
        path.write_text("#!/bin/sh\nprintf '%s\\n' \"$*\" >> \"$GH_LOG\"\ncase \"$*\" in\n" + "".join(cases)
                        + "esac\n", encoding="utf-8")
        path.chmod(0o755)

    def run_step(self, script, env, gh_out="", curl_out=None):
        with tempfile.TemporaryDirectory() as d:
            self.stub(d, "gh", gh_out)
            if curl_out is not None:
                self.stub(d, "curl", [("*", curl_out or None)])
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
                                                            "SHA": sha, "BRANCH": "main",
                                                            "RUNNERS": "nvidia-8gb steam-deck"})
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertEqual(log.strip(), f"workflow run hardware-test.yml --ref main -f pr=2368 -f sha={sha} "
                                      "-f hardware=steam-deck")

    def test_a_bad_label_dispatches_nothing(self):
        for label in ("hardware: Steam Deck", "hardware: x$(id)", "hardware: a;b", "hardware: ", "hardware: -x",
                      "hardware: " + "a" * 33, "hardware: ../x"):
            r, log, _, _ = self.run_step(self.gate_dispatch(), {"LABEL": label, "PR": "1", "SHA": "b" * 40,
                                                                "BRANCH": "main", "RUNNERS": "steam-deck"})
            self.assertNotEqual(r.returncode, 0, label)
            self.assertEqual(log, "", label)

    def test_a_repository_without_that_runner_only_leaves_a_notice(self):
        # (NotRexed/Hookshot has no runners: the pilot is on a tester's fork)
        for runners in ("", "steam-deck-oled", "nvidia-8gb steam"):
            r, log, _, _ = self.run_step(self.gate_dispatch(), {"LABEL": "hardware: steam-deck", "PR": "1",
                                                                "SHA": "b" * 40, "BRANCH": "main",
                                                                "RUNNERS": runners, "GH_REPO": "NotRexed/Hookshot"})
            self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
            self.assertEqual(log, "", runners)
            self.assertIn("::notice::NotRexed/Hookshot has no \"steam-deck\" runner", r.stdout)
            self.assertIn("fork", r.stdout)

    def gate_unlabel(self):
        return next(s for s in runs(GATE.read_text(encoding="utf-8")) if "--remove-label" in s)

    def unlabel(self, view, events):
        return self.run_step(self.gate_unlabel(), {"PR": "5", "SHA": "f" * 40, "GH_REPO": "o/r",
                                                   "PUSHED_AT": "2026-11-12T12:00:00Z"},
                             [("'pr view'*", view), ("'api '*", events), ("*", "")])

    def test_new_commits_take_the_labels_put_on_before_them_off(self):
        view = "f" * 40 + "\nhardware: steam-deck\nhardware: amd-rdna4"
        events = ("2026-11-12T10:00:00Z hardware: amd-rdna4\n2026-11-12T11:00:00Z hardware: steam-deck\n"
                  "2026-11-12T13:00:00Z hardware: amd-rdna4\n2026-11-12T13:30:00Z hardware: steam-deck-oled")
        r, log, _, _ = self.unlabel(view, events)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertIn("api --paginate repos/o/r/issues/5/events", log)
        self.assertIn("pr edit 5 --remove-label hardware: steam-deck", log)
        # (put on again after the push: a maintainer looked at the new head while this job waited)
        self.assertNotIn("--remove-label hardware: amd-rdna4", log)
        self.assertIn("pr comment 5", log)

    def test_a_late_cleanup_for_an_older_push_does_nothing(self):
        r, log, _, _ = self.unlabel("0" * 40 + "\nhardware: steam-deck", "2026-11-12T11:00:00Z hardware: steam-deck")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertEqual(len(log.strip().splitlines()), 1, log)
        self.assertTrue(log.startswith("pr view 5"))
        self.assertIn("the newer push's own run", r.stdout)

    def test_labels_put_on_after_the_push_stay_without_a_comment(self):
        r, log, _, _ = self.unlabel("f" * 40 + "\nhardware: steam-deck", "2026-11-12T12:00:01Z hardware: steam-deck")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertNotIn("pr edit", log)
        self.assertNotIn("pr comment", log)

    def test_unreadable_label_events_take_every_label_off(self):
        r, log, _, _ = self.unlabel("f" * 40 + "\nhardware: steam-deck\nhardware: amd-rdna4", None)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertIn("--remove-label hardware: steam-deck", log)
        self.assertIn("--remove-label hardware: amd-rdna4", log)
        self.assertIn("pr comment 5", log)

    def test_no_hardware_labels_no_comment(self):
        r, log, _, _ = self.unlabel("f" * 40, "")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertEqual(len(log.strip().splitlines()), 1)

    def runner_recheck(self):
        return next(s for s in runs(TEST.read_text(encoding="utf-8")) if "GITHUB_API_URL" in s)

    def test_the_runner_checks_the_approval_again_after_the_queue(self):
        sha = "1" * 40
        env = {"PR": "8", "SHA": sha, "HARDWARE": "steam-deck", "GITHUB_API_URL": "https://api.github.com",
               "GITHUB_REPOSITORY": "tester/Hookshot"}

        def pr(state="open", head=sha, labels=("bug", "hardware: steam-deck")):
            return ('{"state":"%s","head":{"sha":"%s"},"labels":[%s]}'
                    % (state, head, ",".join('{"name":"%s"}' % n for n in labels)))
        for curl_out, ok in ((pr(), True), (pr(state="closed"), False), (pr(head="2" * 40), False),
                             (pr(labels=("bug",)), False), (pr(labels=("hardware: steam-deck-oled",)), False),
                             ("", False), ("<html>rate limited</html>", False), ("{}", False)):
            r, log, _, _ = self.run_step(self.runner_recheck(), env, curl_out=curl_out)
            self.assertEqual(r.returncode == 0, ok, curl_out + r.stdout + r.stderr)
        self.assertIn("https://api.github.com/repos/tester/Hookshot/pulls/8", log)

    def check_env(self, **over):
        return {"PR": "7", "SHA": "c" * 40, "HARDWARE": "nvidia-8gb", "GITHUB_REF": "refs/heads/main",
                "DEFAULT_BRANCH": "main", **over}

    def test_a_manual_run_from_another_branch_stops(self):
        script = next(s for s in runs(TEST.read_text(encoding="utf-8")) if "headRefOid" in s)
        for ref in ("refs/heads/feature", "refs/tags/main", "refs/heads/main2"):
            r, log, _, _ = self.run_step(script, {**self.check_env(GITHUB_REF=ref), "GITHUB_OUTPUT": os.devnull},
                                         f"OPEN {'c' * 40} true")
            self.assertNotEqual(r.returncode, 0, ref)
            self.assertEqual(log, "", ref)

    def test_the_check_wants_the_labelled_head(self):
        script = next(s for s in runs(TEST.read_text(encoding="utf-8")) if "headRefOid" in s)
        sha = "c" * 40
        env = self.check_env()
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
        self.assertTrue(inner.endswith("<img src=x>"), inner[-40:])   # (no blank line added before the fence)
        self.assertNotIn("cut at", comment)

    def report(self, text):
        script = next(s for s in runs(TEST.read_text(encoding="utf-8")) if "comment.md" in s)
        r, _, comment, _ = self.run_step(
            "mkdir -p results && printf '%s' \"$TEXT\" > results/results.txt\n" + script,
            {"TEXT": text, "PR": "9", "SHA": "e" * 40, "HARDWARE": "amd-rdna4", "OUTCOME": "success",
             "GITHUB_SERVER_URL": "https://github.com", "GITHUB_REPOSITORY": "o/r", "GITHUB_RUN_ID": "1"})
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        return comment   # (read as strict UTF-8: a character cut in two would fail here)

    def test_the_comment_is_capped_after_escaping(self):
        # (each @ becomes 4 bytes: capping the input alone let 60,000 of them make a 240,000-byte comment)
        for text in ("@" * 60000, "x" + "@" * 60000, "````\n" * 20000):
            comment = self.report(text)
            self.assertLess(len(comment), 65536 - 4000)
            self.assertLess(len(comment.encode("utf-8")), 65536)
            inner = comment.split("````text\n", 1)[1].rsplit("\n````", 1)[0]
            self.assertLessEqual(len(inner.encode("utf-8")), 60000)
            self.assertNotRegex(inner, r"@\w")
            self.assertEqual(comment.count("````"), 2)
            self.assertIn("(cut at 60,000 bytes", comment)
            self.assertTrue(comment.rstrip().endswith("</details>"))

    def test_a_file_without_a_final_newline_still_closes_the_fence(self):
        comment = self.report("line one\nlast line, no newline")
        self.assertIn("last line, no newline\n````\n", comment)
        self.assertEqual(comment.count("````"), 2)

    def test_an_empty_file_still_closes_the_fence(self):
        comment = self.report("")
        self.assertIn("````text\n````\n", comment)


if __name__ == "__main__":
    unittest.main(verbosity=1)
