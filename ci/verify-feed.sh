#!/usr/bin/env bash
#
# Verify the plugin package is actually FETCHABLE at a given version from the ModelingEvolution
# feed — the only feed it is published to.
#
# ADAPTED from megmeet/ci/verify-feed.sh, which itself came from rocket-welder-sdk. Unchanged in
# substance: it checks ONLY the org feed, because the FairinoDo plugin is published there and NOWHERE
# else — no nuget.org step exists for it (design.md §7.2). A two-feed checker would wait 15 minutes
# for a nuget.org listing that is never coming and then fail a release that actually succeeded.
#
# Why it exists at all, unchanged from the original: `dotnet nuget push` returning 0 is not the
# same fact as "a device can install this", and a release status is read as if it were.
#
# Usage (from the directory containing ./nupkg):
#     VERSION=1.0.0 bash ci/verify-feed.sh
#
# Environment:
#   VERSION           required — the version the package must serve
#   TIMEOUT_SECONDS   optional, default 900 — total polling budget
#   POLL_SECONDS      optional, default 15  — interval between sweeps
#   PUSH_OUTCOME      optional — what the push reported, quoted in the failure message
#   NUPKG_DIR         optional, default ./nupkg
#
# What this proves and what it does not: it proves FETCHABILITY, never IDENTITY — a package with
# the right version and the wrong bits inside passes. Combined with `--skip-duplicate`, a re-run
# can pass having published nothing. The bits are checked separately, before the push, by the
# release workflow's ci/verify-plugin-package.sh step, which is the half this cannot see.
# See docs/backlog/publish-verification-proves-fetchability-not-identity.md in the docs repo.

set -u

VERSION="${VERSION:?VERSION must be set}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-900}"
POLL_SECONDS="${POLL_SECONDS:-15}"
PUSH_OUTCOME="${PUSH_OUTCOME:-not recorded}"
NUPKG_DIR="${NUPKG_DIR:-./nupkg}"

ids=""
for f in "$NUPKG_DIR"/*.nupkg; do
  [ -e "$f" ] || { echo "::error::No .nupkg files in $NUPKG_DIR — nothing was packed."; exit 1; }
  b="$(basename "$f")"
  ids="$ids ${b%".${VERSION}.nupkg"}"
done
total="$(echo $ids | wc -w)"
echo "Verifying $total package(s) at version $VERSION on the ModelingEvolution feed..."

# The org feed serves {"versions":[...]} under /v3/package/<id>/index.json, ids lowercased.
# Getting the path wrong is not hypothetical: a checker that always 404s always fails, and one
# that inverts the test always passes.
deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
while :; do
  missing=""
  for id in $ids; do
    lid="$(echo "$id" | tr '[:upper:]' '[:lower:]')"
    url="https://nuget.modelingevolution.com/v3/package/$lid/index.json"
    # `|| true` so one dropped connection cannot end the whole verification.
    body="$(curl -sS --max-time 20 "$url" 2>/dev/null || true)"
    # Quoted match: "1.0.1" must not be satisfied by "1.0.1-preview.abc1234".
    case "$body" in
      *"\"$VERSION\""*) ;;
      *) missing="$missing $id" ;;
    esac
  done

  if [ -z "$missing" ]; then break; fi
  now="$(date +%s)"
  if [ "$now" -ge "$deadline" ]; then break; fi
  echo "  still indexing ($(( deadline - now ))s left) — missing:$missing"
  sleep "$POLL_SECONDS"
done

if [ -n "$missing" ]; then
  echo "::error::Release $VERSION is NOT fetchable from the ModelingEvolution feed after \
${TIMEOUT_SECONDS}s. Missing:$missing (push step reported: ${PUSH_OUTCOME}). The tag exists. \
Verify the feed before re-running, and never re-cut the tag to fix a feed problem."
  exit 1
fi

echo "All $total package(s) serve $VERSION from the ModelingEvolution feed."
