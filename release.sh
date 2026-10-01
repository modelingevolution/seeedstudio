#!/bin/bash
#
# Cuts a release TAG (vX.Y.Z). It publishes nothing itself — the tag triggers
# .github/workflows/publish-nuget.yml. Never pack from a workstation.
#
#   ./release.sh            → next patch
#   ./release.sh -n         → next minor
#   ./release.sh 1.2.3      → exact version
#   ./release.sh --dry-run
set -e
cd "$(dirname "${BASH_SOURCE[0]}")"

DRY_RUN=false; VERSION=""; INCREMENT="patch"
while [[ $# -gt 0 ]]; do
    case $1 in
        -p|--patch) INCREMENT="patch"; shift ;;
        -n|--minor) INCREMENT="minor"; shift ;;
        -M|--major) INCREMENT="major"; shift ;;
        --dry-run) DRY_RUN=true; shift ;;
        -*) echo "Unknown option: $1"; exit 1 ;;
        *) VERSION="$1"; shift ;;
    esac
done

if ! git diff --quiet || ! git diff --staged --quiet; then
    echo "Error: uncommitted changes. Commit first."; exit 1
fi

# The tag goes on HEAD, so HEAD must be what is on origin/master — not a local commit nobody reviewed.
git fetch -q origin master
if [ "$(git rev-parse HEAD)" != "$(git rev-parse origin/master)" ]; then
    echo "Error: HEAD ($(git rev-parse --short HEAD)) is not origin/master ($(git rev-parse --short origin/master))."
    exit 1
fi

if [ -z "$VERSION" ]; then
    LATEST=$(git tag -l 'v*.*.*' | sed 's/^v//' | sort -V | tail -n1)
    if [ -z "$LATEST" ]; then VERSION="1.0.0"; else
        IFS='.' read -r major minor patch <<< "$LATEST"
        case $INCREMENT in
            major) VERSION="$((major + 1)).0.0" ;;
            minor) VERSION="$major.$((minor + 1)).0" ;;
            patch) VERSION="$major.$minor.$((patch + 1))" ;;
        esac
    fi
fi

TAG="v${VERSION}"
if git tag -l | grep -qx "$TAG"; then echo "Error: tag $TAG already exists"; exit 1; fi

echo "Release $TAG on $(git rev-parse --short HEAD)"
if [ "$DRY_RUN" = true ]; then echo "[DRY RUN] not tagging"; exit 0; fi

git tag "$TAG"
git push origin "$TAG"
echo "Tag $TAG pushed — GitHub Actions publishes ModelingEvolution.WeldingMachine.Seeed.Plugin $VERSION."
