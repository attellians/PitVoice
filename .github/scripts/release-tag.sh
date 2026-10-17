#!/usr/bin/env bash
# Decides what a pushed tag releases (release-publishing spec, design decisions 1-2):
#   vX.Y.Z        stable release, the tagged commit must be in origin/main
#   vX.Y.Z-alpha  pre-release, the tagged commit must be in origin/development
# X.Y.Z must match VERSION of the tagged commit; the suffix takes no part in that check.
# Usage: release-tag.sh <tag> <commit>, run in a checkout of the tagged commit with the full history.
# Prints kind=, version= and base= lines (and appends them to $GITHUB_OUTPUT when it is set).
set -euo pipefail

tag="$1"
sha="$2"

if [[ "$tag" =~ ^v([0-9]+\.[0-9]+\.[0-9]+)(-alpha)?$ ]]; then
  base="${BASH_REMATCH[1]}"
else
  echo "::error::tag $tag is not a release tag; the allowed forms are vX.Y.Z (stable, from main) and vX.Y.Z-alpha (pre-release, from development)"
  exit 1
fi

if [ -n "${BASH_REMATCH[2]}" ]; then
  kind=alpha
  branch=development
  version="$base-alpha"
else
  kind=stable
  branch=main
  version="$base"
fi

file_version=$(tr -d '[:space:]' < VERSION)
if [ "$base" != "$file_version" ]; then
  echo "::error::tag $tag does not match VERSION $file_version of the tagged commit; tag the commit whose VERSION is the one to release"
  exit 1
fi

if ! git merge-base --is-ancestor "$sha" "origin/$branch"; then
  if [ "$kind" = stable ]; then
    echo "::error::tag $tag: a stable release comes only from main, and the tagged commit is not in main; release it as v$base-alpha from development, or move main to it (git merge --ff-only development) and tag again"
  else
    echo "::error::tag $tag: a pre-release comes only from development, and the tagged commit is not in development; tag a commit of development"
  fi
  exit 1
fi

for line in "kind=$kind" "version=$version" "base=$base"; do
  echo "$line"
  if [ -n "${GITHUB_OUTPUT:-}" ]; then echo "$line" >> "$GITHUB_OUTPUT"; fi
done
