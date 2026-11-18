#!/usr/bin/env bash
# Fails if any path in LOCKS differs from the owner-approved tag lock-v0.1 (committed or uncommitted).
set -euo pipefail
cd "$(dirname "$0")/.."

if ! git rev-parse -q --verify lock-v0.1 >/dev/null 2>&1; then
  echo "locks: no lock-v0.1 tag yet, skipping"; exit 0
fi

mapfile -t paths < <(grep -vE '^\s*(#|$)' LOCKS)
[ ${#paths[@]} -eq 0 ] && { echo "locks: nothing locked"; exit 0; }

changed=$(git diff --name-only lock-v0.1 -- "${paths[@]}"; git ls-files --others --exclude-standard -- "${paths[@]}")
if [ -n "$changed" ]; then
  echo "locks: FAIL — locked paths changed since lock-v0.1 (owner approval needed):"
  echo "$changed" | sort -u | sed 's/^/  /'
  exit 1
fi
echo "locks: OK"
