#!/usr/bin/env bash
# One-time setup: create the solution, add every project, init git with role branches.
set -euo pipefail
cd "$(dirname "$0")/.."

command -v dotnet >/dev/null || { echo "Install the .NET 8 SDK first: https://dotnet.microsoft.com/download"; exit 1; }

[ -f M2W.sln ] || dotnet new sln -n M2W
for p in src/*/*.csproj tests/*/*.csproj; do dotnet sln M2W.sln add "$p"; done
dotnet build M2W.sln

if [ ! -d .git ]; then
  git init -b main
  git add -A
  git commit -m "chore: initial skeleton and interface agreement v0.1"
  git tag lock-v0.1
  for b in gen/m2 check/m2 solve/m3; do git branch "$b"; done
fi
echo "Setup done. Next: scripts/validate.sh"
