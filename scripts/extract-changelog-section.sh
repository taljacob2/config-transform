#!/usr/bin/env bash
# Extracts one version's section from docs/CHANGELOG.md (Keep a Changelog format) for use as
# GitHub Release notes -- so release notes are sourced from the changelog, not written twice.
# Usage: extract-changelog-section.sh <version>  (e.g. 0.1.0-alpha)
set -euo pipefail

VERSION="$1"

awk -v version="$VERSION" '
  /^## \[/ {
    if (found) exit
    if (index($0, "[" version "]") > 0) { found = 1; next }
    next
  }
  found { print }
' docs/CHANGELOG.md
