#!/usr/bin/env bash
# Prints the changelog section for a version, to be used as that release's notes.
#
#   ./scripts/release-notes.sh <tag> [changelog]
#
# Exits non-zero when the tag has no section, which is what stops a release whose changelog
# was never updated — the release workflow runs this before it builds anything. A tag with a
# suffix (2026.9.1-rc2) falls back to the section of its base version (2026.9.1), so cutting a
# second release candidate does not require copying the entry.
set -euo pipefail

tag="${1:?usage: release-notes.sh <tag> [changelog]}"
changelog="${2:-CHANGELOG.md}"

if [ ! -f "$changelog" ]; then
    echo "error: $changelog does not exist" >&2
    exit 1
fi

# The body of "## [<version>] — <date>", up to the next "## " heading.
section() {
    awk -v version="$1" '
        BEGIN {
            escaped = version
            gsub(/[.]/, "\\.", escaped)
            heading = "^## +\\[?" escaped "\\]?([ \t]|$)"
        }
        $0 ~ heading { inside = 1; next }
        inside && /^## / { exit }
        inside { print }
    ' "$changelog"
}

notes="$(section "$tag")"
if [ -z "${notes//[[:space:]]/}" ] && [[ "$tag" == *-* ]]; then
    notes="$(section "${tag%%-*}")"
fi

if [ -z "${notes//[[:space:]]/}" ]; then
    echo "error: $changelog has no section for $tag" >&2
    echo "" >&2
    echo "Add one before releasing, moving anything relevant out of Unreleased:" >&2
    echo "" >&2
    echo "    ## [$tag] - $(date -u +%Y-%m-%d)" >&2
    echo "" >&2
    echo "    ### Added" >&2
    echo "    - ..." >&2
    echo "" >&2
    echo "Sections currently in $changelog:" >&2
    grep -n '^## ' "$changelog" >&2 || true
    exit 1
fi

printf '%s\n' "$notes"
