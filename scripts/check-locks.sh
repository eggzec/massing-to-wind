#!/usr/bin/env bash
# Fails if any path in LOCKS differs from the owner-approved tag lock-v0.1 (committed or uncommitted).
# It never passes silently because git is unusable: a broken git (e.g. WSL git inside a Windows worktree) fails.
# A missing tag skips with a WARNING, unless M2W_REQUIRE_LOCK_TAG=1 (set in CI), which makes it a FAIL (D-046).
set -euo pipefail
cd "$(dirname "$0")/.."

if ! git_err=$(git rev-parse --git-dir 2>&1 >/dev/null); then
  echo "locks: FAIL — git cannot read this repository, so locks cannot be checked:"
  echo "  $git_err"
  echo "  (On Windows, run under Git Bash or scripts/validate.ps1, not WSL bash.)"
  exit 1
fi

if ! git rev-parse -q --verify lock-v0.1 >/dev/null 2>&1; then
  # D-046: CI sets M2W_REQUIRE_LOCK_TAG=1, so a missing tag can never let CI pass without checking locks.
  if [ "${M2W_REQUIRE_LOCK_TAG:-0}" = "1" ]; then
    echo "locks: FAIL — no lock-v0.1 tag and M2W_REQUIRE_LOCK_TAG=1 (CI mode): fetch tags or push the tag"
    echo "  (owner: git push -f origin lock-v0.1)"
    exit 1
  fi
  echo "locks: WARNING — no lock-v0.1 tag yet (or tags not fetched), skipping"; exit 0
fi

mapfile -t paths < <(grep -vE '^\s*(#|$)' LOCKS)
[ ${#paths[@]} -eq 0 ] && { echo "locks: nothing locked"; exit 0; }

if ! diffed=$(git diff --name-only lock-v0.1 -- "${paths[@]}"); then
  echo "locks: FAIL — git diff against lock-v0.1 failed"; exit 1
fi
if ! untracked=$(git ls-files --others --exclude-standard -- "${paths[@]}"); then
  echo "locks: FAIL — git ls-files failed"; exit 1
fi

changed=$(printf '%s\n%s\n' "$diffed" "$untracked" | grep -v '^$' || true)
if [ -n "$changed" ]; then
  echo "locks: FAIL — locked paths changed since lock-v0.1 (owner approval needed):"
  echo "$changed" | sort -u | sed 's/^/  /'
  exit 1
fi
echo "locks: OK"
