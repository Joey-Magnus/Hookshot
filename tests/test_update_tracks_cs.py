"""The Windows builder's update tracks (builder/HookshotBuilder/Updater.cs) against the vectors of tests/test_update_tracks.py,
which the game, the setup and the Linux updater are checked against: which track a version is on, the version order (a
nightly and a release not ordered at all), a change of track, a nightly as players see it and what the Nightly track
updates to. The rules are cut out of Updater.cs by their text (from its track constants to LatestTag) and built alone.

Built for net8.0, the builder's framework, as tests/test_updater_asset_digest.py is, or for this SDK's own framework when
it has no .NET 8 reference pack (not on GitHub's runners).

    python -I -B tests/test_update_tracks_cs.py [--dotnet dotnet]
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

RUN_ALL_ARGS = ["--dotnet", "{dotnet}"]

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
import test_update_tracks as shared  # noqa: E402

PROJECT = """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>%s</TargetFramework><ImplicitUsings>enable</ImplicitUsings>
<Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><NuGetAudit>false</NuGetAudit>
<UseSharedCompilation>false</UseSharedCompilation>
</PropertyGroup></Project>"""

# (one question a line, tab-separated; one answer a line)
PROGRAM = """static class Program
{
    static void Main()
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            var a = line.Split('\\t');
            static string? Or(string s) => s.Length == 0 ? null : s;
            Console.WriteLine(a[0] switch
            {
                "track" => Updater.TrackOf(a[1]),
                "newer" => Updater.Newer(a[1], a[2]) ? "1" : "0",
                "other" => Updater.OtherTrack(a[1], a[2]) ? "1" : "0",
                "shown" => Updater.Shown(a[1]),
                "target" => Updater.Target(Or(a[1]), Or(a[2]), a[3]) ?? "",
                _ => throw new InvalidOperationException(line),
            });
        }
    }
}
"""

# (latest, tools, track, what Updater.Target updates to: the Nightly track only goes forward, a release candidate never
# back to an older release, another track than the tools' own takes its newest)
TARGETS = [
    ("nightly-2030-bbbbbbbb", "nightly-2029-aaaaaaaa", "nightly", "nightly-2030-bbbbbbbb"),
    ("nightly-2028-bbbbbbbb", "nightly-2029-aaaaaaaa", "nightly", "nightly-2029-aaaaaaaa"),
    ("nightly-2029-aaaaaaaa", "nightly-2029-aaaaaaaa-dirty", "nightly", "nightly-2029-aaaaaaaa-dirty"),
    ("nightly-2029-aaaaaaaa", "v2.1.0-rc.1-3-gaaaaaaa", "nightly", "nightly-2029-aaaaaaaa"),
    ("nightly-2029-aaaaaaaa", "v2.1.0", "nightly", "nightly-2029-aaaaaaaa"),
    ("v2.0.14", "nightly-2029-aaaaaaaa", "release", "v2.0.14"),
    ("v2.1.0-rc.2", "v2.1.0-rc.1", "rc", "v2.1.0-rc.2"),
    ("v2.0.14", "v2.1.0-rc.1", "rc", "v2.1.0-rc.1"),
    ("", "nightly-2029-aaaaaaaa", "nightly", "nightly-2029-aaaaaaaa"),
]


def region(source):
    start = source.index("    public const string Release")
    return source[start:source.index("    /// The newest build of a track, quietly", start)]


def build(dotnet, folder):
    source = (ROOT / "builder/HookshotBuilder/Updater.cs").read_text(encoding="utf-8")
    (folder / "Updater.cs").write_text("static class Updater\n{\n" + region(source) + "}\n", encoding="utf-8")
    (folder / "Program.cs").write_text(PROGRAM, encoding="utf-8")
    (folder / "NuGet.Config").write_text("<configuration><packageSources><clear/></packageSources></configuration>", encoding="utf-8")
    project = folder / "Tracks.csproj"
    project.write_text(PROJECT % "net8.0", encoding="utf-8")
    restore = subprocess.run([dotnet, "restore", str(project), "--disable-build-servers"], capture_output=True, text=True)
    if restore.returncode and "NU1100" in restore.stdout + restore.stderr:
        if os.environ.get("GITHUB_ACTIONS") == "true":
            raise SystemExit("on a GitHub runner the updater must build for the builder's net8.0, and its reference pack is "
                             "missing\n" + restore.stdout + restore.stderr)
        sdk = subprocess.run([dotnet, "--version"], capture_output=True, text=True, check=True).stdout.strip()
        framework = "net%s.0" % sdk.split(".")[0]
        print("this .NET SDK (%s) has no .NET 8 reference pack: built for %s" % (sdk, framework), flush=True)
        project.write_text(PROJECT % framework, encoding="utf-8")
    elif restore.returncode:
        raise SystemExit(restore.stdout + restore.stderr)
    out = folder / "out"
    built = subprocess.run([dotnet, "build", str(project), "--configuration", "Release", "--disable-build-servers",
                            "--output", str(out)], capture_output=True, text=True)
    if built.returncode:
        raise SystemExit(built.stdout + built.stderr)
    return [dotnet, str(out / "Tracks.dll")]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    dotnet = shutil.which(args.dotnet)
    if not dotnet:
        parser.error("a .NET SDK is required (the builder's is .NET 8)")
    questions, wanted = [], []
    for version, track in shared.TRACKS + [("nightly", "release"), ("rc", "release"), ("dev", "release")]:
        questions.append(("track", version))
        wanted.append(track)
    for newer, older in shared.ORDER:
        questions += [("newer", newer, older), ("newer", older, newer), ("newer", newer, newer)]
        wanted += ["1", "0", "0"]
    for a, b in shared.UNORDERED:
        questions += [("newer", a, b), ("newer", b, a)]
        wanted += ["0", "0"]
    for a, b, other in shared.OTHER_TRACK:
        questions += [("other", a, b), ("other", b, a)]
        wanted += ["1" if other else "0"] * 2
    for version, shown in shared.SHOWN:
        questions.append(("shown", version))
        wanted.append(shown)
    for latest, tools, track, target in TARGETS:
        questions.append(("target", latest, tools, track))
        wanted.append(target)
    with tempfile.TemporaryDirectory(prefix="hookshot-tracks-cs-") as folder:
        run = build(dotnet, Path(folder))
        answers = subprocess.run(run, input="".join("\t".join(q) + "\n" for q in questions), capture_output=True,
                                 encoding="utf-8", check=True).stdout.splitlines()
    failed = [(q, w, a) for q, w, a in zip(questions, wanted, answers) if w != a]
    if len(answers) != len(questions) or failed:
        for q, w, a in failed:
            print("Updater.cs: %s gave %r, not %r" % (" ".join(q), a, w))
        raise SystemExit("update tracks: Updater.cs does not agree with tests/test_update_tracks.py (%d of %d answers)"
                         % (len(answers), len(questions)))
    print("update tracks: Updater.cs agrees (%d answers)" % len(answers))


if __name__ == "__main__":
    main()
