#!/usr/bin/env bash
# Determinism check (D-045). Same inputs must give byte-identical output.
#
#   scripts/determinism.sh                 build, run `run-all cases` twice (out/determinism/run1, run2), compare,
#                                          write out/hashes.txt
#   scripts/determinism.sh --compare A B   only compare two existing run-all output folders (no build, no hashes.txt)
#
# Compared by SHA-256, file by file: repaired.obj, result.json, summary.json as raw bytes; report.json with the value
# of "runtime_ms" set to 0 first, because runtime_ms is measured (D-028). A file present in only one run is a
# difference. Exit 1 on any difference, naming the files.
#
# out/hashes.txt: one line per case, "caseId sha256(repaired.obj)", sorted by byte value, LF line endings.
# scripts/determinism.ps1 writes the same file byte for byte, so CI can compare Linux and Windows.
set -euo pipefail
cd "$(dirname "$0")/.."

# Hash command for xargs (a shell function cannot be run by xargs): prints "hash *path" lines.
if command -v sha256sum >/dev/null 2>&1; then SHA=(sha256sum -b); else SHA=(shasum -a 256 -b); fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# manifest DIR NAME -> $work/NAME.sha: "relative/path sha256" lines, sorted by path (byte order).
manifest() {
  local dir=$1 name=$2 norm="$work/$2.norm"
  mkdir -p "$norm"
  # report.json: copy with runtime_ms zeroed, then hash the copy. One awk process for all files.
  ( cd "$dir" && find . -type f -name report.json -printf '%h\n' ) | ( cd "$norm" && xargs -r mkdir -p )
  ( cd "$dir" && find . -type f -name report.json -print0 | xargs -0 -r awk -v norm="$norm" '
      FNR == 1 { if (out != "") close(out); out = norm "/" FILENAME }
      { gsub(/"runtime_ms"[ \t]*:[ \t]*[-+0-9.eE]+/, "\"runtime_ms\": 0"); print > out }' )
  {
    ( cd "$dir"  && find . -type f \( -name repaired.obj -o -name result.json -o -name summary.json \) -print0 | xargs -0 -r "${SHA[@]}" )
    ( cd "$norm" && find . -type f -name report.json -print0 | xargs -0 -r "${SHA[@]}" )
  } | awk '{ p = $2; sub(/^\*/, "", p); sub(/^\.\//, "", p); print p, $1 }' | LC_ALL=C sort > "$work/$name.sha"
}

# compare A B -> prints differences, returns 1 if any.
compare() {
  local a=$1 b=$2
  for d in "$a" "$b"; do
    [ -d "$d" ] || { echo "determinism: FAIL — folder not found: $d"; return 1; }
  done
  manifest "$a" a
  manifest "$b" b
  local n_obj
  n_obj=$(grep -c '/repaired\.obj ' "$work/a.sha" || true)
  if [ "$n_obj" -eq 0 ]; then
    echo "determinism: FAIL — no repaired.obj under $a, nothing to compare"; return 1
  fi

  cut -d' ' -f1 "$work/a.sha" > "$work/a.paths"
  cut -d' ' -f1 "$work/b.sha" > "$work/b.paths"
  local only_a only_b differ
  only_a=$(LC_ALL=C comm -23 "$work/a.paths" "$work/b.paths")
  only_b=$(LC_ALL=C comm -13 "$work/a.paths" "$work/b.paths")
  differ=$(LC_ALL=C join "$work/a.sha" "$work/b.sha" | awk '$2 != $3 { print $1 }')

  if [ -z "$only_a$only_b$differ" ]; then
    echo "determinism: OK — $(wc -l < "$work/a.sha" | tr -d ' ') files identical ($n_obj repaired.obj; report.json without runtime_ms)"
    return 0
  fi
  echo "determinism: FAIL — the two runs differ:"
  [ -n "$differ" ] && echo "$differ" | sed "s|^|  differs:      |"
  [ -n "$only_a" ] && echo "$only_a" | sed "s|^|  only in $a: |"
  [ -n "$only_b" ] && echo "$only_b" | sed "s|^|  only in $b: |"
  return 1
}

if [ "${1:-}" = "--compare" ]; then
  [ $# -eq 3 ] || { echo "usage: scripts/determinism.sh [--compare DIR_A DIR_B]"; exit 2; }
  compare "$2" "$3"
  exit $?
fi
[ $# -eq 0 ] || { echo "usage: scripts/determinism.sh [--compare DIR_A DIR_B]"; exit 2; }

echo "determinism: build"
dotnet build M2W.sln -nologo -v q > "$work/build.log" 2>&1 || { cat "$work/build.log"; echo "determinism: FAIL — build failed"; exit 1; }

rm -rf out/determinism
mkdir -p out/determinism
for run in run1 run2; do
  echo "determinism: run-all cases --out out/determinism/$run"
  if ! dotnet run --project src/M2W.Cli --no-build -- run-all cases --out "out/determinism/$run" > "$work/$run.log" 2>&1; then
    tail -n 20 "$work/$run.log"; echo "determinism: FAIL — run-all failed ($run)"; exit 1
  fi
done

status=0
compare out/determinism/run1 out/determinism/run2 || status=1

# hashes.txt from run1 (equal to run2 when the check passed): "caseId sha256", caseId = output folder of the case.
awk '$1 ~ /\/repaired\.obj$/ { id = $1; sub(/\/repaired\.obj$/, "", id); print id, $2 }' "$work/a.sha" \
  | LC_ALL=C sort > out/hashes.txt
echo "determinism: wrote out/hashes.txt ($(wc -l < out/hashes.txt | tr -d ' ') cases)"
exit $status
