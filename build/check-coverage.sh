#!/usr/bin/env bash
# Fails when a test project's coverage is below the minimum.
#
#   build/check-coverage.sh <results-directory> <min-line-percent> <min-branch-percent>
#
# Reads the Cobertura reports that `dotnet test --collect:"XPlat Code Coverage"` writes. Each report is one test project,
# which covers one package, so the gate applies to every package separately: a well-covered package cannot hide a thin one.
set -euo pipefail

directory="${1:?results directory}"
min_line="${2:?minimum line coverage percent}"
min_branch="${3:?minimum branch coverage percent}"

reports=$(find "$directory" -name 'coverage.cobertura.xml' | sort)
if [ -z "$reports" ]; then
  echo "No coverage reports under $directory: the tests did not collect coverage." >&2
  exit 1
fi

failed=0
while IFS= read -r report; do
  root=$(grep -m1 -oE '<coverage [^>]*' "$report")
  line=$(grep -oE 'line-rate="[0-9.]+"' <<<"$root" | grep -oE '[0-9.]+')
  branch=$(grep -oE 'branch-rate="[0-9.]+"' <<<"$root" | grep -oE '[0-9.]+')
  line_percent=$(awk -v v="$line" 'BEGIN { printf "%.1f", v * 100 }')
  branch_percent=$(awk -v v="$branch" 'BEGIN { printf "%.1f", v * 100 }')

  ok=$(awk -v l="$line_percent" -v b="$branch_percent" -v ml="$min_line" -v mb="$min_branch" 'BEGIN { print (l >= ml && b >= mb) ? "yes" : "no" }')
  if [ "$ok" = "yes" ]; then
    echo "ok      line ${line_percent}% (min ${min_line}%), branch ${branch_percent}% (min ${min_branch}%)  $report"
  else
    echo "TOO LOW line ${line_percent}% (min ${min_line}%), branch ${branch_percent}% (min ${min_branch}%)  $report" >&2
    failed=1
  fi
done <<<"$reports"

exit "$failed"
