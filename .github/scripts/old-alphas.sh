#!/usr/bin/env bash
# Prints the tags of the alpha pre-releases to delete (release-publishing spec, design decision 6): every pre-release
# whose tag is vX.Y.Z-alpha, except the 5 newest by creation time. Only the tag form selects, so stable releases and the
# `models` pre-release are never printed.
# Input on stdin: gh release list --json tagName,isPrerelease,createdAt
set -euo pipefail

keep="${1:-5}"

jq -r --argjson keep "$keep" '
  [ .[] | select(.isPrerelease and (.tagName | test("^v[0-9]+\\.[0-9]+\\.[0-9]+-alpha$"))) ]
  | sort_by(.createdAt) | reverse | .[$keep:] | .[].tagName'
