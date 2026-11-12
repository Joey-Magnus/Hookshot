# Hardware test runners

A tester's PC (a Steam Deck, an AMD, Intel or 8 GB card) can take a pull request's hardware test by itself, as a
self-hosted GitHub Actions runner, instead of someone picking it from the [test-request queue](test-requests.md). It is
opt-in, the queue stays the way for machines that are not always on, and a merge never waits for it. Idea and
discussion: [#2797](https://github.com/NotRexed/Hookshot/issues/2797).

A runner runs code from a pull request on someone's own PC, and that PC has their game dump on it. GitHub's own advice
is that self-hosted runners should almost never serve a public repository, because anyone can open a pull request
([Hardening for self-hosted runners](https://docs.github.com/en/actions/reference/security/secure-use#hardening-for-self-hosted-runners)).
Everything below is there so that it is safe anyway: read all of it before you register a runner.

## For now: a pilot on one fork

The first runs are a pilot, and everything happens in one tester's fork: the runner is registered on the fork, the
pull request to test is a **test pull request opened on the fork itself** (its branch can hold the commits of an
upstream pull request), and the label goes on that test pull request. The workflows dispatch, check and report in the
repository they run in, with that repository's token, so:

- A `hardware:` label on a NotRexed/Hookshot pull request does not reach a fork's runner. This repository has no
  runners, so the gate leaves a notice and starts nothing.
- Running the fork's `Hardware test` by hand with an upstream pull request's number does not bridge it either: it
  checks, fetches and reports that number in the fork.
- Registering runners on NotRexed/Hookshot is not the fix (see the end of the next section).

Testing an upstream pull request automatically would need a relay that is authorized on both sides and keeps the
repository that runs the test apart from the one that holds the pull request. That is not built, and waits on what the
pilot shows.

A repository starts tests only for the hardware names listed, separated by spaces, in its Actions variable
`HOOKSHOT_HW_RUNNERS` (Settings, Secrets and variables, Actions, Variables), for example `steam-deck`. A label for a
name that is not listed leaves a notice in the gate's run and starts nothing.

Runners are Linux, and the workflow's steps on them are Bash; they need `curl` and `jq`.

## How a test starts

1. A maintainer looks at the pull request's commits, including what runs while it builds (`nx2/`, `scripts/`,
   `tests/`, `.github/`), not only the renderer.
2. They add a label `hardware: <name>`, where `<name>` is a runner label such as `steam-deck`, `amd-rdna4`,
   `intel-arc` or `nvidia-8gb` (lowercase letters, digits and dashes). Only people with triage access can add labels.
3. `.github/workflows/hardware-test-label.yml` starts `.github/workflows/hardware-test.yml` from `main` for the
   commit that was labelled. The pull request's own copy of the workflows never runs, so it cannot change them.
4. New commits on the pull request take every `hardware:` label off. The maintainer looks again and labels again. If
   that cleanup runs late, it leaves alone a label put on after its push, and does nothing if a newer push came since.
5. The test waits in GitHub's queue until a runner with that label is online (GitHub gives up after 24 hours). Once a
   runner takes it, the job checks again that the pull request is still open, still at that commit and still carries
   the label, and stops if not. Then it runs, and the results are posted on the pull request as a comment.

Someone with write access can also start `Hardware test` by hand (Actions, Run workflow) with the pull request's
number, its head commit and the runner label, from the default branch only; it still checks that the commit is the
head and carries the label.

## What a runner owner must set up

The workflow gives the runner no token and checks nothing out on it. It calls one program you provide, named by
`HOOKSHOT_HW_SANDBOX` in the runner's `.env` file; without it the job fails before anything of the pull request runs:

```
$HOOKSHOT_HW_SANDBOX --repo https://github.com/NotRexed/Hookshot --sha <40-character commit> --pr <number> --out <folder>
```

The hardware name is in its environment as `HOOKSHOT_HW_HARDWARE`. That program is where the safety is. It must:

- **Check the approval again before the dump goes in, and again before taking the GPU** after a long build, from
  outside the sandbox: the pull request is open, its head is still `--sha`, and it still carries `hardware: <name>`
  (`GET /repos/<owner>/<repo>/pulls/<number>`, as the workflow's own check does). If any of that fails, or cannot be
  read, it stops.
- **Run everything in a throwaway VM or container**, made fresh for the job and deleted after it, as an OS user with
  no access to your home folder, SSH keys, browser profiles or other repositories. A dedicated test machine or VM is
  safer than a container on your desktop: passing the GPU into a container leaves the graphics driver as a way out.
  A device that holds nothing but the dump (a Steam Deck kept for testing) is the easiest case.
- **Build apart from the run.** The build is where most of the pull request's code runs (the Python build scripts),
  and it needs no GPU: it can go in a VM with no GPU at all. Only the built game needs the GPU, in a tight sandbox of
  its own.
- **Fetch exactly that commit, then cut the network.** The build, the game and the scene run with no network at all,
  so nothing can send your dump (or anything else) anywhere.
- **Give the dump read-only**, mounted into the sandbox, never copied out of it. A built game folder is made from the
  game and stays on your PC like the dump.
- **Write only `results.txt` and screenshots (`*.png`) to `--out`**: the hardware, the driver, the commit, the numbers
  and `hookshot_log.txt` lines, what CONTRIBUTING.md already allows in a comment. Leave out your user name and paths.
  Nothing else in `--out` is uploaded.
- **Export through a step of its own, outside the sandbox**, which puts in `--out` only regular files (no symlinks,
  folders or devices: the upload follows symlinks), only `results.txt` and `*.png` that really are PNG, with caps on
  their size and number (say 1 MB for `results.txt`, 20 screenshots of at most 8 MB). Cutting the network does not
  close this channel: what is in `--out` is published as the run's artifact before anything else looks at it.
- **Have no credentials inside**: no GitHub token, no SSH agent, no cloud keys. `permissions: {}` only takes the
  repository token's permissions away; the runner keeps its own session and artifact credentials, so start the
  workload in a fresh environment (not the runner's), as another user, with no access to the runner's folder or
  processes.

And the runner itself:

- Register it on Linux with the labels `self-hosted`, `hookshot-hardware` and your hardware's name, in its own OS
  user, and list that name in the fork's `HOOKSHOT_HW_RUNNERS` variable.
- Prefer a just-in-time or `--ephemeral` runner, which takes one job and is removed.
- Keep the runner software up to date, and stop it whenever you like: a test just waits in the queue.

**Register runners on your own fork, not on this repository.** A runner on this repository can be picked by any
workflow that runs here, and a pull request can add its own workflow (on `pull_request`) that asks for
`[self-hosted, hookshot-hardware]`: that job runs straight on the runner, past the label and the sandbox. Approving a
contributor's first workflow runs does not close this. On your fork only you push workflows, and you start the tests.
Runners on this repository would need runner groups limited to `hardware-test.yml`, which a personal account's
repository does not have; that, and whether to do it at all, is the maintainers' decision (#2797).

## What comes back

The `report` job runs on GitHub's own runner, never on yours. It posts the run's outcome and `results.txt` on the pull
request as quoted text (no @mentions, cut at 60,000 bytes after the escaping, which keeps the comment under GitHub's
limit) and links the run, whose artifacts hold the whole `results.txt` and the screenshots for 14 days. It never runs anything the runner produced.
