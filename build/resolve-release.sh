#!/usr/bin/env bash
# Works out which package a release tag is for.
#
#   build/resolve-release.sh results-v1.2.3
#
# A tag is <prefix>-v<semver>. The prefix is the MinVerTagPrefix (without the "v") of exactly one project under src/.
# Prints key=value lines, ready to append to $GITHUB_OUTPUT: package, project, version, prerelease.
# Exits 1, with the reason on stderr, when the tag is not a release tag or names no package.
set -euo pipefail

tag="${1:?release tag}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

pattern='^([a-z][a-z0-9-]*)-v([0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?)$'
if [[ ! "$tag" =~ $pattern ]]; then
  echo "'$tag' is not a release tag. Expected <prefix>-v<major>.<minor>.<patch>[-prerelease], for example results-v1.2.3." >&2
  exit 1
fi

prefix="${BASH_REMATCH[1]}"
version="${BASH_REMATCH[2]}"

matches=$(grep -l -F "<MinVerTagPrefix>${prefix}-v</MinVerTagPrefix>" "$root"/src/*/*.csproj 2>/dev/null || true)
count=$(grep -c . <<<"$matches" || true)

if [ "$count" -eq 0 ]; then
  echo "No project under src/ has <MinVerTagPrefix>${prefix}-v</MinVerTagPrefix>, so the tag '$tag' names no package." >&2
  exit 1
fi
if [ "$count" -gt 1 ]; then
  echo "More than one project has the tag prefix '${prefix}-v'; each package needs its own." >&2
  exit 1
fi

project="${matches#"$root"/}"
package="$(basename "$project" .csproj)"

echo "package=$package"
echo "project=$project"
echo "version=$version"
if [[ "$version" == *-* ]]; then echo "prerelease=true"; else echo "prerelease=false"; fi
