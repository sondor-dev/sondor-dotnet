#!/usr/bin/env bash
# Works out the next release of a package.
#
#   build/next-release.sh <PackageId> <patch|minor|major> [version] [prerelease]
#
#   build/next-release.sh Sondor.Results patch                  1.2.3 -> 1.2.4
#   build/next-release.sh Sondor.Results minor                  1.2.3 -> 1.3.0
#   build/next-release.sh Sondor.Results patch 1.0.0            an explicit version, for a first release or a jump
#   build/next-release.sh Sondor.Results minor "" preview.1     the next version as a pre-release: 1.3.0-preview.1
#
# The version is the last STABLE release tag of the package plus the bump. A package with no stable release yet has nothing
# to bump from, so its first release names the version. The tag is <prefix>-v<version>, where the prefix is the project's
# MinVerTagPrefix. Prints key=value lines for $GITHUB_OUTPUT: package, project, version, tag, previous, prerelease.
# Exits 1, with the reason on stderr, when the request cannot be released.
set -euo pipefail

fail() { echo "$1" >&2; exit 1; }

package="${1:?package id, for example Sondor.Results}"
bump="${2:?bump: patch, minor or major}"
explicit="${3:-}"
prerelease="${4:-}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

[[ "$package" =~ ^Sondor\.[A-Za-z0-9]+(\.[A-Za-z0-9]+)*$ ]] || fail "'$package' is not a Sondor package id, for example Sondor.Results."
[[ "$bump" =~ ^(patch|minor|major)$ ]] || fail "The bump must be patch, minor or major, not '$bump'."
[[ -z "$prerelease" || "$prerelease" =~ ^[0-9A-Za-z]+(\.[0-9A-Za-z]+)*$ ]] || fail "'$prerelease' is not a valid pre-release label, for example preview.1."
[[ -z "$explicit" || "$explicit" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || fail "'$explicit' is not a version of the form major.minor.patch."

project="src/$package/$package.csproj"
[ -f "$root/$project" ] || fail "There is no project at $project."

prefix="$(sed -n 's|.*<MinVerTagPrefix>\(.*\)-v</MinVerTagPrefix>.*|\1|p' "$root/$project" | head -n 1)"
[ -n "$prefix" ] || fail "$project does not set <MinVerTagPrefix>, so its releases have no tag prefix."

stable_tags="$(git -C "$root" tag --list "${prefix}-v*" | grep -E "^${prefix}-v[0-9]+\.[0-9]+\.[0-9]+$" | sort -V || true)"
last_stable="$(tail -n 1 <<<"$stable_tags")"

if [ -n "$explicit" ]; then
  version="$explicit"
elif [ -z "$last_stable" ]; then
  fail "$package has no stable release yet, so there is nothing to bump from. Name the version of its first release."
else
  IFS=. read -r major minor patch <<<"${last_stable#"${prefix}-v"}"
  case "$bump" in
    major) version="$((major + 1)).0.0" ;;
    minor) version="${major}.$((minor + 1)).0" ;;
    patch) version="${major}.${minor}.$((patch + 1))" ;;
  esac
fi

full="${version}${prerelease:+-$prerelease}"
tag="${prefix}-v${full}"

if git -C "$root" rev-parse -q --verify "refs/tags/${tag}" >/dev/null; then
  fail "The tag $tag already exists, so $package $full has already been released."
fi

# For the release notes: the last stable release, or failing that the last tag of any kind.
previous="$last_stable"
[ -n "$previous" ] || previous="$(git -C "$root" tag --list "${prefix}-v*" | sort -V | tail -n 1)"

echo "package=$package"
echo "project=$project"
echo "version=$full"
echo "tag=$tag"
echo "previous=$previous"
if [ -n "$prerelease" ]; then echo "prerelease=true"; else echo "prerelease=false"; fi
