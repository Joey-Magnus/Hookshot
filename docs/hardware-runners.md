# Hardware test runners

A tester's PC (a Steam Deck, an AMD, Intel or 8 GB card) can take a pull request's hardware test by itself, as a
self-hosted GitHub Actions runner, instead of someone picking it from the [test-request queue](test-requests.md). It is
opt-in, the queue stays the way for machines that are not always on, and a merge never waits for it. Idea and
discussion: [#2797](https://github.com/NotRexed/Hookshot/issues/2797).

A runner runs code from a pull request on someone's own PC, and that PC has their game dump on it. GitHub's own advice
is that self-hosted runners should almost never serve a public repository, because anyone can open a pull request
([Hardening for self-hosted runners](https://docs.github.com/en/actions/reference/security/secure-use#hardening-for-self-hosted-runners)).
Everything below is there so that it is safe anyway: read all of it before you register a runner.

## How a test starts

1. A maintainer looks at the pull request's commits, including what runs while it builds (`nx2/`, `scripts/`,
   `tests/`, `.github/`), not only the renderer.
2. They add a label `hardware: <name>`, where `<name>` is a runner label such as `steam-deck`, `amd-rdna4`,
   `intel-arc` or `nvidia-8gb` (lowercase letters, digits and dashes). Only people with triage access can add labels.
3. `.github/workflows/hardware-test-label.yml` starts `.github/workflows/hardware-test.yml` from `main` for the
   commit that was labelled. The pull request's own copy of the workflows never runs, so it cannot change them.
4. New commits on the pull request take every `hardware:` label off. The maintainer looks again and labels again.
5. The test waits in GitHub's queue until a runner with that label is online (GitHub gives up after 24 hours), runs,
   and the results are posted on the pull request as a comment.

Someone with write access can also start `Hardware test` by hand (Actions, Run workflow) with the pull request's
number, its head commit and the runner label; it still checks that the commit is the head and carries the label.

## What a runner owner must set up

The workflow gives the runner no token and checks nothing out on it. It calls one program you provide, named by
`HOOKSHOT_HW_SANDBOX` in the runner's `.env` file; without it the job fails before anything of the pull request runs:

```
$HOOKSHOT_HW_SANDBOX --repo https://github.com/NotRexed/Hookshot --sha <40-character commit> --pr <number> --out <folder>
```

That program is where the safety is. It must:

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
- **Have no credentials inside**: no GitHub token, no SSH agent, no cloud keys.

And the runner itself:

- Register it with the labels `self-hosted`, `hookshot-hardware` and your hardware's name, in its own OS user.
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
request as quoted text (no @mentions, at most 60,000 characters) and links the run, whose artifacts hold the
screenshots for 14 days. It never runs anything the runner produced.
