#!/usr/bin/env bash
# The one command every agent runs after every change. Prints a short summary; exit code 0 only if all steps pass.
set -uo pipefail
cd "$(dirname "$0")/.."

status=0
step() { echo; echo "== $1"; }

step "build";  dotnet build M2W.sln -nologo -v q || status=1
step "tests";  dotnet test  M2W.sln -nologo -v q --no-build || status=1
step "cases";  dotnet run --project src/M2W.Cli --no-build -- run-all cases --out out || status=1
step "locks";  scripts/check-locks.sh || status=1

step "summary"
if [ -f out/summary.json ]; then cat out/summary.json; else echo "no out/summary.json yet"; fi

echo
[ $status -eq 0 ] && echo "VALIDATE: GREEN" || echo "VALIDATE: RED"
exit $status
