#!/bin/bash
# The version of a build that is not a release: nightly-<count>-<commit>, where count is the number of commits in HEAD's
# history (git rev-list --count HEAD: it only grows along main, so a newer nightly has a larger number) and commit the
# first 8 hex digits of HEAD's. CI names every build it does not make for a release tag so (the nightly, pull requests,
# the Actions tab), the AppImage (scripts/linux/appimage.sh) and the Windows builder (.github/workflows/windows-builder.yml)
# alike, and nx2/version.py names a checkout the same way. A shallow checkout cannot count its history, so it is refused:
# check out with fetch-depth: 0. docs/versioning.md, "The version number".
#
#   scripts/nightly_name.sh [<checkout>]      prints e.g. nightly-2029-c544828f
set -euo pipefail
cd "${1:-.}"
if [[ $(git rev-parse --is-shallow-repository) != false ]]; then
    echo "nightly_name.sh: a shallow checkout cannot count its commits (fetch its whole history: fetch-depth: 0)" >&2
    exit 1
fi
count=$(git rev-list --count HEAD)
sha=$(git rev-parse HEAD)
printf 'nightly-%s-%s\n' "$count" "${sha:0:8}"
