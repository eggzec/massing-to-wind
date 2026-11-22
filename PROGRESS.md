# Progress log

## Waiting for owner

_Agents: put questions that need the owner here (newest first). Owner: answer inline and delete when done._

_None open._


## Current status

| Milestone | Status | Pass rate | Last updated |
| --- | --- | --- | --- |
| M1 Foundations | ✅ Passed (gate approved, `lock-v0.1` tagged) | 0/1 (no solver or checker yet, expected) | 2026-11-18 |
| M2 Test cases and checker | ✅ Passed (gate approved, `cases/` and checker locked, `lock-v0.1` moved) | 0/291 (stub solver, expected until M3) | 2026-11-18 |
| M3 Core repair | ✅ Passed (midpoint review folded into the M4 gate review) | 252/291 at M3 (M3 faults without M4 faults 195/195; clean 10/10) | 2026-11-22 |
| M4 Hard faults | ✅ Passed (gate approved, feature freeze) | 311/311 visible · private hidden set 310/310 · owner benchmarks pass | 2026-11-22 |
| M5 Package | In progress (under feature freeze): solver problems 1–3 merged; `m2w render` + showcase images and CI (Linux, Windows, determinism, cross-OS) merged; CI green on GitHub (Linux, Windows, cross-OS identical; D-051); `solve/m5` (output order, options object, README) reviewed, not merged; demo pending | 311/311 | 2026-11-22 |

## Log (newest first)

### 2026-11-22 · owner · CI: `global.json` pins the .NET 8 SDK (CI had never compiled)
- First CI run after the M5 merges (`45015063357`) failed in all three jobs. Read with `gh`: one cause, and every
  earlier run on GitHub failed the same way (no successful run in the last 30). The runners have the .NET 10 SDK;
  without `global.json` it was used, so CI compiled C# 14 while all local runs used SDK 8.0.428 (C# 12). C# 14 binds
  `int[].Reverse()` to the `void` span method: CS1503 at `src/M2W.Geometry/Mesh.cs:28` on Linux and Windows. The
  validate jobs therefore wrote no `out/hashes.txt`, and `cross-OS` had nothing to compare.
- Fix (D-051): new `global.json`, SDK `8.0.400`, `rollForward: latestFeature`. No code change, no locked path.
  Checked every `.Reverse()`: two more array calls (`RenderTests.cs:37`, `Step1CleanupTests.cs:144`) would have
  failed to compile next; the statement-form calls are all on `List<T>`, so no output changed silently.
- **First green CI run: `45016451179`** (commit `283cde1`), checked with `gh` against the checker's checklist:
  - `validate (ubuntu-latest)` 2m05s and `validate (windows-latest)` 3m10s: tests 2946/2946, `locks: OK`,
    `"casesPassed": 311`, `VALIDATE: GREEN`, `determinism: OK — 934 files identical`, `wrote out/hashes.txt (311 cases)`;
  - `cross-OS determinism`: `cross-os: OK, 311 cases, repaired.obj byte-identical on Linux and Windows`;
  - artifacts `hashes-Linux` and `hashes-Windows` downloaded: both identical to the local Windows `out/hashes.txt`
    (SHA-256 `fdb5d2e4…`), so local, Linux CI and Windows CI produce the same 311 `repaired.obj`. D-009 now holds
    as a CI gate.
  - Only annotation: xUnit2013 warning (`Assert.Equal` on a collection size) at
    `tests/M2W.Repair.Tests/Step9WarningLocationTests.cs:102`; a warning, for the solver some time.
- validate (`scripts/validate.ps1`, SDK 8.0.428 via `global.json`): build ✅ · tests 2946/2946 (Geometry 88, Cli 48,
  Generator 2636, Checker 98, Repair 76) · cases 311/311 · locks OK · **VALIDATE: GREEN**.
- Blockers: none.

### 2026-11-22 · owner · M5 review; `gen/m5` and `check/m5` merged
- **Independent review** of the three M5 branches (generator, checker, solver). Checked independently rather than
  from the PROGRESS entries:
  - validate on each branch: all tests pass, 311/311, locks OK;
  - the checker's pinned action SHAs match their tags; `determinism.sh` passes, and fails naming the file on a
    planted one-byte difference;
  - solver guard (main vs `solve/m5`, visible cases): 311/311 `repaired.obj` change bytes, 0 change geometry
    (shells compared as sets of oriented triangles), 0 `pass` changes, `value` changes in 24 cases (21 tied shells
    + 3 at the 12th digit), as the solver reported;
  - object order: every visible case with its objects shuffled (two seeds) gives byte-identical output on
    `solve/m5` (0/310 differ); on `main` 3/310 differ;
  - showcase SVGs rendered and looked at: markers correct; pairs 2 and 4 do not show their repair at site scale
    (0.2 m gap, 0.24 m roof step), open below.
- Review finding sent back to the checker and fixed (`6aff62a`, D-050): `validate.ps1` found Git Bash only when
  `git` resolved to `Git\cmd\git.exe`; from `Git\bin` or `Git\mingw64\bin` (Git Bash, possibly GitHub's Windows
  runner) locks went FAIL with all code green. Re-checked by the owner: `locks: OK` with `git` from `mingw64\bin`.
- **Merged:** `gen/m5` fast-forwarded into `main` (`30e3d1d`). `main` merged into `check/m5` (conflicts in
  PROGRESS.md and DECISIONS.md were additions on both sides; kept both, newest first), then fast-forwarded into
  `main` (`18d1b21`). No locked path changed; `lock-v0.1` stays at `5c5c7a6`. Decision numbers unique: D-043–D-044
  generator, D-045, D-046, D-050 checker; D-047–D-049 are the solver's (not merged yet).
- **Seen once, not reproduced:** one `M2W.Repair.Tests` failure in a validate run on `check/m5` (75/76; the test name
  was not captured). 14 later runs (alone, whole solution in parallel, full validate) all pass. The only shared state
  in those tests is the static `GapCloser.FullScan`, which `solve/m5` removes (D-048). If CI shows a Repair test
  failure, note its name and send it to the solver.
- Next:
  - push `main` and check the first CI run against the checker's checklist (its entry below);
  - solver: merge `main` into `solve/m5`, fill in the captions from `docs/images/showcase.json`, check images and
    badge; Q-S2 answers recorded at that merge;
  - after `solve/m5` merges: rerun the showcase writer so the "after" images match its output; decide on pairs 2
    and 4.
- validate (`scripts/validate.ps1`, on this commit's tree): build ✅ · tests 2946/2946 (Geometry 88, Cli 48,
  Generator 2636, Checker 98, Repair 76) · cases 311/311 · locks OK · **VALIDATE: GREEN**. `determinism.ps1`: OK,
  934 files identical.
- Blockers: none.

### 2026-11-22 · checker · M5 follow-up: `validate.ps1` finds Git Bash from any `git.exe` location (branch `check/m5`, not merged)
- Changed: `scripts/validate.ps1` only (the Git Bash lookup in the locks step). No change to `check-locks.sh`,
  `ci.yml`, the determinism scripts or any locked path. D-050.
- The lookup used to go exactly two folders up from `git.exe`, which works only for `Git\cmd\git.exe`. It now walks
  up from the folder holding `git.exe`, at most three levels, and takes the first `bin\bash.exe` (then
  `usr\bin\bash.exe`). Still never plain `bash`; still `locks: FAIL - Git Bash not found next to git.exe; …` and
  exit 1 when none is found.
- **Local proof** (Windows 11, Windows PowerShell 5.1; `git = …` printed inside the PowerShell that ran validate):
  | Started as | `git = ` | locks | result |
  | --- | --- | --- | --- |
  | PowerShell, usual PATH | `C:\Program Files\Git\cmd\git.exe` | `locks: OK` | `VALIDATE: GREEN`, 311/311, exit 0 |
  | Git Bash: `powershell -NoProfile …` | `C:\Program Files\Git\mingw64\bin\git.exe` | `locks: OK` | `VALIDATE: GREEN`, 311/311, exit 0 |
  | PowerShell, `Git\bin` first on PATH | `C:\Program Files\Git\bin\git.exe` | `locks: OK` | `VALIDATE: GREEN`, 311/311, exit 0 |
  | PowerShell, `Git\mingw64\bin` first on PATH | `C:\Program Files\Git\mingw64\bin\git.exe` | `locks: OK` | `VALIDATE: GREEN`, 311/311, exit 0 |
  | PowerShell, a copied `git.exe` in a scratch folder outside the repo first on PATH | `…\scratchpad\fakegit\git.exe` | `locks: FAIL - Git Bash not found next to git.exe; install Git for Windows or run scripts/validate.sh in Git Bash` | `VALIDATE: RED`, exit 1 (build, tests, 311/311 cases all passed) |
  - Before the fix, rows 2–4 were `locks: FAIL` (reviewer's reproduction of row 2 on all three M5 branches).
  - `scripts/validate.sh` (Git Bash): `locks: OK`, `VALIDATE: GREEN`, exit 0.
- Pass counts (`out/summary.json`): 311/311 cases. Tests: Geometry 88, Cli 31, Repair 76, Generator 2636, Checker 98,
  all passing.
- Owner checklist for `check/m5` (entry below): unchanged. `locks: OK` in the `validate (windows-latest)` log no
  longer depends on which `git.exe` the runner puts first on PATH.
- Not tested locally: the GitHub Windows runner itself (its PATH puts `Git\bin` first, which is row 3 above).
- Next: owner review and merge of `check/m5`. Blockers: none.

### 2026-11-22 · checker · M5 CI on GitHub Actions with a determinism check (branch `check/m5`, not merged)
- Feature freeze respected: no change to `src/M2W.Checker/` or its tests. Changed: `.github/workflows/ci.yml`,
  `scripts/check-locks.sh` (one opt-in); new: `scripts/determinism.sh`, `scripts/determinism.ps1`. D-045 (CI design),
  D-046 (lock-tag rule).
- **Determinism script** (D-045): runs `run-all cases` twice into `out/determinism/run1|run2` and compares SHA-256 of
  every `repaired.obj`, `result.json`, `summary.json`, and `report.json` with `runtime_ms` zeroed. Exit 1 naming the
  files. Writes `out/hashes.txt` (`caseId sha256(repaired.obj)`, sorted). `--compare A B` / `-Compare A B` compares
  two existing folders.
- **CI** (D-045): `ubuntu-latest` runs `validate.sh` + `determinism.sh`; `windows-latest` runs `validate.ps1` +
  `determinism.ps1` (pwsh). Both upload `out/hashes.txt` (`hashes-Linux`, `hashes-Windows`). Job `cross-os` fails
  unless the two files are byte-identical. NuGet cache, timeouts (30 / 5 min), `fetch-depth: 0` + `fetch-tags`,
  actions pinned to commit SHAs, `M2W_REQUIRE_LOCK_TAG=1`.
- **Lock tag** (D-046): with `M2W_REQUIRE_LOCK_TAG=1` a missing `lock-v0.1` is `locks: FAIL`. Unset: WARNING and skip
  as before.
- **Local proof** (Windows 11, Git Bash + Windows PowerShell 5.1):
  - `determinism.sh`: OK, 934 files identical (311 `repaired.obj`). `determinism.ps1`: OK, 934 files identical.
  - The two `out/hashes.txt` (bash and PowerShell) are byte-identical (SHA-256 `fdb5d2e4…1262`), 311 lines.
  - Planted differences, on a copy of run2, with `--compare` / `-Compare`, same result in both shells:
    - one byte flipped in `case_0205/repaired.obj` (offset 40, `0x30` → `0x31`): FAIL, `differs: case_0205/repaired.obj`,
      exit 1;
    - only `runtime_ms` changed in `case_0301/report.json`: OK, exit 0 (the exclusion works, and is needed: 239 of
      311 raw reports differ between two runs);
    - `status` changed in that report and `case_0410/result.json` deleted: FAIL naming both, exit 1.
  - `check-locks.sh` in a scratch clone with the tag deleted: unset or `=0` gives WARNING and exit 0; `=1` gives FAIL
    and exit 1. The same FAIL when called from PowerShell through Git Bash (the Windows CI path).
  - The `cross-os` compare script, run locally on the two hash files: OK; with one hash changed and one line deleted:
    FAIL listing `case_0205` (different) and `case_0410` (MISSING); with a file missing: FAIL.
  - `ci.yml` parses (YamlDotNet, scratch project outside the repo): jobs `validate` (8 steps) and `cross-os` (2 steps).
  - **Not tested locally:** Linux (WSL has no .NET), PowerShell 7 (not installed here), the GitHub runs themselves.
- **Owner: what to look for after pushing `check/m5`** (Actions tab → workflow `ci`):
  1. Three jobs: `validate (ubuntu-latest)`, `validate (windows-latest)`, `cross-OS determinism (Linux vs Windows)`.
     All green.
  2. In each validate job's log: `locks: OK` (not WARNING, not FAIL), `VALIDATE: GREEN`, `"casesPassed": 311`, and
     `determinism: OK — 934 files identical (311 repaired.obj; …)` then `wrote out/hashes.txt (311 cases)`.
  3. Run page → Artifacts: `hashes-Linux` and `hashes-Windows`. The `cross-os` job prints and puts in the run summary:
     `cross-os: OK, 311 cases, repaired.obj byte-identical on Linux and Windows`.
  4. If `locks: FAIL — no lock-v0.1 tag`: the tag is not on GitHub; `git push -f origin lock-v0.1`. Today
     `origin` has `lock-v0.1` at `5c5c7a6`, the same as locally. After each future tag move, push it with `-f`,
     or CI goes RED on the locks step.
  5. If `cross-os` fails, it lists the cases whose `repaired.obj` differ between Linux and Windows. That is a real
     finding (platform floating point, D-009), not a CI fault. Send it to the solver; do not loosen the check.
  6. First run: the NuGet cache step reports a miss; later runs report a hit.
- **README badge** (README.md is the solver's; please add this line there):
  `![ci](https://github.com/rehman-saif/massing-to-wind/actions/workflows/ci.yml/badge.svg)`
- validate (`scripts/validate.ps1`, and `scripts/validate.sh` with `M2W_REQUIRE_LOCK_TAG=1`): build ✅ · tests
  2929/2929 (Geometry 88, Cli 31, Generator 2636, Checker 98, Repair 76) · cases 311/311 · locks OK ·
  **VALIDATE: GREEN** (61 s locally).
- Next: owner pushes `check/m5` and checks the first CI run as above, then merges.
### 2026-11-22 · generator · M5: `m2w render` and the README's before/after images
- Branch `gen/m5` from `main` (`8fe8bfd`). Under the feature freeze: no change to case generation, faults, expected
  answers or `cases/`. Only a rendering tool and images are added; no locked path is touched.
- **`m2w render <obj> --out <svg> [--frame <other.obj>] [--title <text>]`** (new: `src/M2W.Cli/Render/SvgRenderer.cs`;
  `CliApp` gains the command and one usage line; existing commands and public signatures are unchanged). D-043.
  - Camera and drawing:
    - fixed axonometric camera (30°, 45°), polygons painted back to front, flat shading with one fixed light, thin
      edges;
    - `--frame` fits the view to both files, so a pair shares viewBox, scale and position.
  - Input files: one colour per object, see-through; free edges in red; flipped faces in magenta. Flipped means a
    face that disagrees with its neighbours, or a whole object that is inside out.
  - Repaired files (`shell_NNN`): one opaque colour per shell; the same defect markers would show if any were left.
  - Determinism: two decimals, invariant culture, LF, no ids or dates. Coplanar triangles are merged, so the largest
    SVG over all visible cases is 94 KB.
  - Sweep over all 311 visible cases, inputs and repaired:
    - 622 renders, no failures;
    - every repaired file shows 0 free edges and 0 flipped faces;
    - markers appear on exactly the inputs whose faults include `openSurface` or `flippedFaces`.
- **Showcase** (`docs/images/`, D-044): `showcase-{1..4}-{before,after}.svg` and `showcase.json`. The names are the
  ones the solver's README embeds.
  - 1 `case_0301`: stacked floors and overlap (also openSurface and roofStep), 29 objects become 4 shells.
  - 2 `case_0177`: a 0.2 m gap is closed; one block stays separate.
  - 3 `case_0183`: inside out, 3 missing faces, floating 0.71 m.
  - 4 `case_0235`: a 0.24 m roof step is flushed.
  - Volume and footprint come from each `result.json`; all four pass with repaired totals equal to expected
    (case_0301 volume 59631.466 vs 59631.465 m³).
  - Regenerate after validate: `M2W_WRITE_SHOWCASE=1 dotnet test tests/M2W.Cli.Tests --filter Showcase`. Two runs
    are byte-identical.
- **Tests** (`tests/M2W.Cli.Tests`, 31 → 48):
  - the five the brief asks for: byte-identical SVG; a cube shows 3 faces; a hole shows red free edges; one flipped
    face is marked; `--frame` gives the same viewBox, and the same pixels as a drawing of both meshes;
  - plus: an inside-out object, duplicate vertices, a zero-area sliver, stacked floors, number format, bad
    arguments, and a check of the committed showcase files.
- `.gitattributes`: `*.svg text eol=lf`, so the committed images stay byte-identical on Windows.
- validate (`scripts/validate.ps1`): build ✅ · tests 2946/2946 (Geometry 88, Cli 48, Generator 2636, Checker 98,
  Repair 76) · cases 311/311 (`out/summary.json`) · locks OK · **VALIDATE: GREEN**.
- Next: the solver's README can embed the images; owner review and merge of `gen/m5`.
- Blockers: none.

### 2026-11-22 · owner · M4 gate: approved, feature freeze
- **M4 criteria met** (MILESTONES.md): gap closing below `minGap`, sliver and short-edge removal, combined-fault
  cases, randomised property tests (solver M4 steps 1–4). Target ≥ 80 % with every failure explained: 311/311
  visible, no failures to explain.
- **Evidence at the gate** (`main` @ `1515fc0`, owner-run; the private material is not shared, totals only):
  - validate: tests 2929/2929, cases 311/311, locks OK.
  - **New private hidden set** (new private seed; generator with `roofStep`; old hidden set retired because its
    details were discussed in the open): **310/310 pass**, all `repaired`. Worst tolerance use: volume 6.6 %,
    footprint 11.7 %, height 6.2 %. An independent geometry check finds every shell valid. Two runs are
    byte-identical. Runtime median 4 ms, max 196 ms per case.
  - **Owner benchmarks** (geometry not made by the generator): cases with exact answers all pass with zero error. A
    real district is valid and deterministic, takes about 2 s, and every change is explained by the gap-closing and
    short-edge rules. Out-of-scope probes (an overhang, a sloped roof) come back `partial` with located warnings,
    as KNOWN_LIMITATIONS.md says.
- **Feature freeze, from now until v0.1 ships (M5):**
  - Allowed: packaging (README with results table, CI, demo), documentation, bug fixes, and the two minor solver
    items (shell order still depends on the object split on real data; the static `GapCloser.FullScan` test
    switch).
  - Not allowed without the owner: new repair features, new faults, new checks, new `report.json` ops, or
    tolerance changes.
  - v0.2 backlog:
    - sloped roofs (still a height field: piecewise planar roofs);
    - overhangs and canopies (needs a 3D union);
    - minimum-angle grading and refinement (D-025, D-035);
    - a validity-only grading mode for real data without `expected.json`.
- M3 midpoint review is folded into this review (status row updated).
- Locks unchanged: `lock-v0.1` stays at `5c5c7a6` (AGREEMENT.md, LOCKS, `cases/`, checker).
- validate (`scripts/validate.ps1`, on this commit): build ✅ · tests 2929/2929 (Geometry 88, Cli 31,
  Generator 2636, Checker 98, Repair 76) · cases 311/311 · locks OK · **VALIDATE: GREEN**.
- Next: M5 (README, CI, demo; minor solver items).
- Blockers: none.

### 2026-11-22 · owner · Solver M5 problems 1–3 merged; all three branches in `main`
- `solve/m5` merged: `main` merged into it first (conflicts in PROGRESS.md and DECISIONS.md were additions on both
  sides; kept both, newest first), the solver's decisions renumbered **D-038/D-039/D-040 → D-040/D-041/D-042**
  (references in KNOWN_LIMITATIONS.md and three code comments updated), then fast-forwarded into `main`
  (`10bbfa0`). No locked path changed; `lock-v0.1` stays at `5c5c7a6`.
- Solver task 4 (`roofStep`), run by the owner at the merge: **29/29 pass**, worst margin 0.0 % of tolerance (exact).
  Worst margin over all 311 cases: 19 % of the footprint tolerance (a jog case, D-034).
- Output guard on cases the solver had not seen: all 311 `repaired.obj` from `solve/m5` are byte-identical to those
  of the previous solver on the regenerated cases.
- Decision numbers are now unique: D-037 owner (R1), D-038 generator (R1), D-039 checker (Q-S1), D-040–D-042 solver
  (runtime, object split, located warnings and `fillUnsupported`).
- Next:
  - **M4 gate (owner):** generate a new private hidden set (the old one predates `roofStep`); run it and the owner
    benchmarks on `main`; approve the feature freeze.
  - **M5:** README with results table; CI (validate on push and a determinism check); demo; owner's design-tool
    exports into the benchmarks. Minor solver items: shell order still depends on the object split on real data
    (geometry identical), and the `GapCloser.FullScan` static test switch.
- validate (`scripts/validate.ps1`, on this commit): build ✅ · tests 2929/2929 (Geometry 88, Cli 31,
  Generator 2636, Checker 98, Repair 76) · cases 311/311 · locks OK · **VALIDATE: GREEN**.
- Blockers: none.

### 2026-11-22 · owner · Checker Q-S1 merged; Q-S1 closed; lock moved
- `check/m4-qs1` merged: `main` merged into it first (conflicts in PROGRESS.md and DECISIONS.md were additions on
  both sides; kept both, newest first), the checker's decision renumbered **D-038 → D-039** (the generator's R1
  entry keeps D-038; references in `Shell.cs` and `AnchorContainmentTests.cs` comments updated), then
  fast-forwarded into `main` (`a469c64`).
- Checker task 5 (re-run on the regenerated cases), done by the owner at the merge: tests 2915/2915 incl. Checker
  98 and the clean-control zero-repair test · cases 311/311. All 311 `result.json` are byte-identical with and
  without the Q-S1 fix (same solver), so the fix changes no grade on the new cases either.
- **Q-S1 closed** and removed from "Waiting for owner".
- `lock-v0.1` moved to this commit (locked paths changed: `src/M2W.Checker/Shell.cs`,
  `tests/M2W.Checker.Tests/AnchorContainmentTests.cs`; approved for Q-S1).
- Next: solver (merge `main` into `solve/m5`, renumber its D-038–D-040 to D-040–D-042, `roofStep` results).
- validate (`scripts/validate.ps1`, after the commit and the tag move): build ✅ · tests 2915/2915 (Geometry 88,
  Cli 31, Generator 2636, Checker 98, Repair 62) · cases 311/311 · locks OK · **VALIDATE: GREEN**.
- Blockers: none.

### 2026-11-22 · owner · Approvals Q-G1 and `fillUnsupported`; generator R1 merged; lock moved
- Owner approvals (orchestrator review of all three branches, see below):
  - **Q-G1 approved and done:** AGREEMENT.md lists `roofStep` as the 13th fault ("Faults to cover in v0.1").
    Additive; `schemaVersion` stays 0.1. Q-G1 removed from "Waiting for owner".
  - **`fillUnsupported` approved** (solver D-042, numbered D-040 on `solve/m5` before the merge): a new op name in `report.json` for regions filled
    down to the ground, with `count`, `area_m2`, `bbox_m`, `regions[]`. Additive; existing ops unchanged.
- Orchestrator review of the three branches (independent, before merging):
  - Generator `gen/m4-r1`: 1 of 290 old expected answers changed (+0.079 % volume); 29 `roofStep` cases on both
    sides of the threshold; the current solver passes 311/311 (roofStep 29/29). Claims match the branch.
  - Checker `check/m4-qs1`: all 291 old `result.json` byte-identical; 12 new tests. Claims match the branch.
  - Solver `solve/m5`: on the owner's private benchmarks the real district now takes 2.5 s (was about 50 s),
    exact benchmarks unchanged (7/7), district output valid and unchanged in substance (2 m² of 54,000 m², from the
    object split), all canopy warnings now located. One object vs one object per part: same geometry, but a few
    shells are written in a different order (not byte-identical); minor, for M5.
  - No agent session read the private folders or the seed.
- `gen/m4-r1` fast-forwarded into `main` (`3b3b58a`). `lock-v0.1` moved to this commit (locked paths changed:
  `cases/` from R1, `AGREEMENT.md` from Q-G1; both approved).
- Merge order from here: checker (merge `main`, renumber its D-038 to D-039, re-run on the new cases), then solver
  (merge `main`, renumber its D-038–D-040 to D-040–D-042, `roofStep` results).
- validate (`scripts/validate.ps1`, after the commit and the tag move): build ✅ · tests 2903/2903 (Geometry 88,
  Cli 31, Generator 2636, Checker 86, Repair 62) · cases 311/311 · locks OK · **VALIDATE: GREEN**.
- Blockers: none.

### 2026-11-22 · solver · M5 problems 1–3: runtime, warnings that say where, report detail independent of the object split
- **1. Runtime.** New `SyntheticCity` (tests, fixed seed): city blocks of two terraced rows with shared walls, houses
  0.1–0.9 m apart now and then, alleys and gardens over 1 m, streets 12–18 m, varied heights, set-back tiers and a
  few raised slabs; as one object and as one object per part; optionally city blocks turned by up to 8° with
  heights to 1 cm (many more roof levels).
  - Profiled with Stopwatch per phase. The terms growing faster than linear: (a) the baked plan held one outline
    per roof level, 168k segments, mostly identical copies; (b) face labelling looped over every fill (69k gap
    bridges, 629 distinct); (c) gap closing rebuilt components and walls of the whole plan at every level and tested
    all wall pairs; (d) crossing tests swept on X only; (e) point location scanned all faces. All changed so the
    result is the same (D-040).
  - Times (this machine, Release, 500 blocks): **1.15 s** axis-aligned (was 9.0 s), **1.37 s** rotated (was
    30.3 s); 2000 parts 4.2 s. In the Debug test build: 1.3–2.6 s. Test limit 15 s (3× the 5 s target), as one
    object and as many. From the speed-ups seen (7.8× to 22×), the owner's 50 s district should take about 2.5–6.5 s;
    I cannot measure it.
  - Small cases pay about 1 ms more (warm, in-process: mean 6.2 ms vs 5.1 ms over the 290 visible cases; in the
    CLI run, with JIT, mean 18.6 ms vs 9.0 ms measured today before the change).
  - **Guard:** `repaired.obj` byte-identical before and after on all 291 visible cases and the unseen sets (seeds
    777, 2024, 99991: 870 cases), and against the old solver on synthetic cities of 250 and 500 parts. The one
    possible difference (curved outlines bending more than 5 mm from a wall's chord) is in D-040.
- **2. Warnings that say where** (D-042). Each region warning gives the plan box `[xmin, ymin, xmax, ymax]` in local
  coordinates and an area or count: support fill (per block: regions, m², box), holes left open, the per-object
  fallback, unbridged gaps, sloped triangles, untriangulated roofs, invalid shells, a short output edge, a block
  below ground. Operation entries: **new op `fillUnsupported`** (block, `count` regions, `area_m2`, `bbox_m`,
  `regions[]`); `closeHoles` gains `leftOpen` / `leftOpenRegions`; the fallback logs `rebuildShells` with
  `fallback` and `bbox_m`; `closeGap` gains `bbox_m`; `unionBlocks` gains `pointContacts`. No field renamed or
  removed. The owner may veto the new op name (then the data moves to `rebuildShells`).
- **3. Same report for one object or many** (D-041). An object holding several separate closed solids is split into
  parts `<object>#k`, each repaired as an object of its own would be (the ground snap still moves the whole object).
  Parts meeting only at a point are filled there as before but reported as `pointContacts` on `unionBlocks`;
  `closeGap` now lists only real gaps (width > 0) with width and `bbox_m`.
  - Test: the same city as one object and as one object per part gives byte-identical `repaired.obj` and the same
    operations, every field equal except block names (120 parts both layouts, 500 parts both layouts).
  - Visible reports: `closeGap` gains `bbox_m` (70 cases); two cases (`case_0202`, `case_0302`) hold an object with
    two solids 0.17 m apart and now report `closeGap [B01#1, B01#2]` plus `unionBlocks` instead of
    `closeGap [B01, B01]`, with a note. No other report change; `solverVersion` 0.7.0.
- **5. `KNOWN_LIMITATIONS.md`:** new Solver section: canopies filled down (`partial`, now located), sloped roofs not
  represented (`partial`), output slivers (D-035), flush volume change (D-030/D-037), jogs by least change (D-034),
  object splits (D-041), curved outlines (D-040), and the measured runtime.
- Tests: 76 solver tests (+14): 500-block timing (two layouts, both object splits, same output), one object vs many
  (120 parts, two layouts), fast gap closing vs full scan (`GapCloser.FullScan`, two layouts), city determinism,
  7 location/union tests (canopy region and op, map coordinates, two regions, gap box, corner contact, split object,
  JSON form).
- validate (`scripts/validate.ps1`): build ✅ · tests 2479/2479 (Geometry 88, Cli 31, Generator 2198, Checker 86,
  Repair 76) · cases 291/291 · locks OK · **VALIDATE: GREEN**.
- Per fault (passed/total): clean 10/10 · duplicateVertices 30/30 · farCoordinates 31/31 · flippedFaces 29/29 ·
  floatingBlock 30/30 · openSurface 31/31 · overlap 29/29 · shortEdge 30/30 · sliver 30/30 · smallGap 31/31 ·
  stackedFloors 32/32 · sunkBlock 30/30 · touchingWalls 30/30. All 291 reports `repaired`. Unseen: 290/290 for each
  of seeds 777, 2024, 99991.
- **4. `roofStep` results:** not yet: `main` has not moved since `658cc96` (the generator's R1 branch is not merged).
  After the merge I merge `main` into `solve/m5`, validate and report the `roofStep` pass count and worst margin.
- Next: problem 4 after the owner merges R1. Blockers: none (owner decision wanted on the `fillUnsupported` op name).

### 2026-11-22 · generator · R1: expected answers include the roof-step flush; new fault `roofStep` (`gen/m4-r1`)
- **Flush in every expected answer** (D-038), from the AGREEMENT text only (no solver code read). Applied last, to
  the corrected height field: regions = edge-connected cells of equal height; touching = sharing an edge or a
  corner; raise to the highest touching roof higher by more than 0 and less than the case's `minEdge`; repeat.
  Volumes and heights come from the flushed field. Layouts are redrawn if the result depends on the raising order
  or a touching step lies within 2.5 % of `minEdge` (neither happened at seed 1).
- **Effect on existing cases:** the old 290-case plan (same seeds and recipes), rebuilt with this code and compared
  byte for byte with the old `cases/`: **1 of 290 expected answers changed** (old `case_0137`, overlap, crossing bars;
  now `case_0141`), **largest volume change +0.079 %**. 289 identical, none redrawn, no input changed, no
  `maxHeight_m` changed. (Case IDs and combined recipes are reshuffled because the plan now has 13 faults, so the
  comparison was made on the old plan, not by ID.)
- **New fault `roofStep`:** touching blocks, overlapping blocks (crossing bars, corner overlap) or a block with a
  set-back roof deck as tall as the step. 20 single-fault cases: steps 0.4×, 0.8×, 0.96× `minEdge` (raised, 4 each)
  and 1.04×, 1.5× (kept, 4 each); 5 of them at `minEdge` 0.15 or 0.4. 9 combined cases (4 below the threshold).
  In the new set the flush raises roofs in 17 cases (16 `roofStep`, 1 natural overlap); the largest change is
  +1.313 %, so a solver that keeps such a step fails the volume check. Appended to `ExpectedSpec.KnownFaults`
  (13 names, comment updated; nothing else changed in M2W.Geometry). AGREEMENT fault list: Q-G1 above.
- **Cases per fault** (single / combined / total; clean 10; 310 cases, `case_0101`–`case_0410`): overlap 20/9/29,
  stackedFloors 20/9/29, touchingWalls 20/9/29, smallGap 20/9/29, sliver 20/9/29, shortEdge 20/9/29,
  duplicateVertices 20/10/30, openSurface 20/9/29, flippedFaces 20/9/29, floatingBlock 20/9/29, sunkBlock 20/10/30,
  farCoordinates 20/9/29, roofStep 20/9/29. (`out/summary.json` adds `case_0007` to three of these.)
- **Tests:** new `RoofStepTests.cs`:
  - the self-check: in every case's expected height field, no two roofed cells sharing an edge or corner differ by
    more than 0 and less than `minEdge` (written from the rule, not with the generator's helpers);
  - coverage of both sides of the threshold;
  - exact raised volume (step × lower-roof area) below the threshold, unchanged above;
  - hand-made flush cases (corner contact, repetition, an order-dependent chain).
  - Existing tests updated for 13 faults and 310 cases; the expected-answer test now also checks that the flush
    only raises roofs and keeps footprints and maximum heights.
  - The "no short edges by accident" check exempts only a `tier` deck lower than `minEdge` (that step is the
    injected fault).
  - The plan test no longer names the retired hidden seed (uses seed 2).
- **Cases regenerated** with seed 1; `_example/case_0007` unchanged; `cases/README.md` no longer names a seed
  ("Hidden cases use a private seed held by the owner.") and describes the flush and `roofStep`.
- validate (`scripts/validate.ps1`): build ✅ · tests 2903/2903 (Geometry 88, Cli 31, Generator 2636, Checker 86,
  Repair 62) · cases **311/311** (roofStep 29/29; worst volume tolerance use 13.5 %, roofStep 0.0 %) · locks
  **FAIL, approved for R1**: only `cases/` paths (README and regenerated cases) · VALIDATE: RED for locks only.
- Next: owner merges `gen/m4-r1` and re-tags; Q-G1 (fault list). The checker branch should re-run on these cases.
- Blockers: none.

### 2026-11-22 · checker · Q-S1: anchors on a shared triangle edge are contained (D-039)
- Fix: `Shell.ContainsInPlan` accepts barycentric weights ≥ −1e-9 (`Shell.PlanWeightTol`), edges still included.
  Its bounding-box prefilter is padded by 2 × 1e-9 × the box extent, so it never rejects a point the weight test
  accepts.
- Audit of every other point and boundary test in the checker: listed in D-039. No other place can miss a point on
  a shared edge: the `mustMerge` / `mustStaySeparate` block locator, `minGap_m` and `noSelfIntersections` work on
  distances with `TouchTol`, or on the winding number. Nothing else changed.
- Tests: new `AnchorContainmentTests` (12 tests, hand-built meshes, no case files):
  - three boxes with the anchor at the centre (the Q-S1 box among them), under all diagonal splits and all vertex
    rotations of the plan triangles: contained, and all checks pass;
  - points on outer edges, corners and a slanted edge: contained; 1e-6 m outside an edge: not contained;
  - the Q-S1 far-coordinate box, shifted to local coordinates through a 6-decimal OBJ, passes.
  - Each centre test asserts that the old rule misses in some triangulation. With the bound set to 0, 8 of the 12
    fail.
- No other behaviour change: all 291 `out/case_*/result.json` are byte-identical to a run on `main` @ `658cc96`
  before the change.
- validate (`scripts/validate.ps1`): build ✅ · tests 2477/2477 (Geometry 88, Cli 31, Generator 2198, Checker 98,
  Repair 62) · cases 291/291 · locks **FAIL** only for `src/M2W.Checker/Shell.cs` and
  `tests/M2W.Checker.Tests/AnchorContainmentTests.cs` (approved for Q-S1; owner re-tags at merge) · everything else
  green.
- Next: after the owner merges `gen/m4-r1` into `main`, merge `main` into `check/m4-qs1`, validate again on the
  regenerated cases (including the clean-control zero-repair test) and report. (Renumbered from D-038 to D-039 at the
  merge; the generator's R1 entry is D-038.)
- Blockers: none (waiting for the generator merge for task 5).

### 2026-11-22 · owner · R1 written into AGREEMENT.md; lock moved; branches for the M4 gate work
- R1 action 1 done. AGREEMENT.md has a new paragraph under the tolerance table: *"Roof steps below `minEdge`: roof
  regions that touch in plan (share an edge or a corner) and whose heights differ by less than `minEdge` are raised
  to the highest of them, repeated until no such step remains. Expected answers include this."*
  - Additive, so `schemaVersion` stays 0.1; recorded as D-037.
  - `lock-v0.1` moved to this commit (was `6eac5c9`). Only AGREEMENT.md changed among the locked paths.
- OWNER_GUIDE.md: the hidden-set seed and folder names are removed. The guide now says to keep the seed, folder,
  output and case details where no agent can read them. `cases/README.md` still names the old seed; the generator
  removes it when it regenerates `cases/` (locked path, approved as part of R1).
- Housekeeping: the `m2w-gen` worktree had lost its `.git` link (folder empty); it was pruned and re-created.
- Branches, all from `main` at this commit:
  - **generator:** `gen/m4-r1` in `m2w-gen`, for R1 actions 2 (expected answers after the flush, `roofStep` fault,
    regenerate `cases/`, drop the seed from `cases/README.md`). Regenerating `cases/` makes `locks` FAIL on that
    branch; this is expected and approved, and the owner moves the tag at merge.
  - **checker:** `check/m4-qs1` in `m2w-check`, for the Q-S1 fix and its test. Changing `src/M2W.Checker/` makes
    `locks` FAIL on that branch; same rule.
  - **solver:** `solve/m5` in `m2w-solve`, for M5 problems 1–3 (2026-11-21 benchmark entry). No locked path
    involved.
- validate (`scripts/validate.ps1`, after the commit and the tag move): build ✅ · tests 2465/2465 (Geometry 88,
  Cli 31, Generator 2198, Checker 86, Repair 62) · cases 291/291 · locks OK · **VALIDATE: GREEN**.
- Next: generator and checker work in parallel; the owner merges the generator first, then the checker, re-tags,
  runs a new private hidden set and holds the M4 gate.
- Blockers: none.

### 2026-11-21 · owner · Private benchmark run on geometry not made by the generator; 3 problems for M5
- The owner keeps a private benchmark set outside the repo: published wind-engineering benchmark buildings and a
  real urban district model. Like the hidden set, agents get totals only: no names, sources, geometry or margins.
  - Run on `solve/m4` @ `e04558b`.
  - Benchmarks with exact answers: **7/7 pass**, zero error. No repair operation runs besides welding: clean
    geometry comes back unchanged.
  - Real district (about 500 parts, 8.4k triangles, given as one object and as one object per part): all output
    shells are valid by an independent check and the output is deterministic. Status is `partial`: raised slabs
    (canopies) are filled down to the ground, as documented. The changes are explained by the gap closing and
    short-edge rules.
- **Problems for the solver to fix in M5** (behaviour is correct; none of these is a wrong answer):
  1. **Runtime does not scale.**
     - The real district takes about 50 s per run (35–60 s seen), against a mean of 7 ms per generated case. A
       city area of thousands of buildings would take many minutes.
     - Profile it and find the term that grows faster than linear (likely pairwise work over all components,
       plan faces or rebuild rounds).
     - Target: under 5 s for about 500 buildings.
     - Add a timing test with a large synthetic site (e.g. 500 random blocks) so this cannot regress unnoticed.
  2. **Warnings do not say where.**
     - With a single input object, the warning reads "block `<name>` has no support below part of it (it floats
       beyond groundSnap); filled down to the ground" and gives no location. A user with a city model in one
       object cannot find the canopy.
     - Each warning about a region (support fill, holes left open, shell rebuild fallback) should carry its plan
       position, e.g. the bounding box `[xmin, ymin, xmax, ymax]` in local coordinates, and its area or count.
     - Fields may be added to `report.json` (readers ignore unknown fields), but keep the existing ones as they
       are.
  3. **Report detail depends on how the input is split into objects.**
     - With one input object, all gap closing is reported as a single `closeGap` entry with `width_m: 0` and no
       `unionBlocks` entry.
     - With one object per part, parts that merely touch are logged as `closeGap` with `width_m: 0` (244 entries)
       besides `unionBlocks`.
     - Wanted: `closeGap` only for real gaps (width > 0), with the actual width and the plan position; touching or
       overlapping parts reported as unions; and the same count of operations whatever the object split.
     - No `AGREEMENT.md` change is needed: op names and `count` stay; only what is logged under them changes.
- These come after the R1 / Q-S1 actions above and do not block the M4 gate. Record each fix as a D-entry and list
  any remaining limit in `KNOWN_LIMITATIONS.md`.
- validate (`scripts/validate.ps1`, after this entry; docs only): build ✅ · tests 2465/2465 (Geometry 88, Cli 31,
  Generator 2198, Checker 86, Repair 62) · cases 291/291 · locks OK · **VALIDATE: GREEN**.
- Blockers: none.

### 2026-11-21 · owner · M4 independent review; decisions R1 (roof steps below `minEdge`) and Q-S1 approved
- **Review** (independent reviewer working for the owner; no repo code changed):
  - Hidden set (`run-all ../hidden --out ../hidden-out`): **290/290**, all `repaired`, every fault type 100 %. The
    291st entry in `../hidden` is `index.json`, not a case.
  - Worst share of the tolerance used: footprint 18 %, height 6 %, volume 94 % (`case_0388`), then 55 %
    (`case_0181`); 95 % of shells use under 0.02 %.
  - Two hidden runs give byte-identical `repaired.obj` for all 290 cases.
  - An independent re-check written from AGREEMENT.md alone (not the checker) finds no fault in 693 shells: closed,
    outward, on Z = 0, no edge below `minEdge`, no shells closer than `minGap`. It caught three planted faults.
  - The 44 hidden cases with non-default tolerances are handled at those tolerances (`minGap` 0.5 / 0.75 / 1.5,
    `groundSnap` 0.25 / 1).
  - Integrity: no case IDs, `expected.json` or checker/generator references in `src/M2W.Repair`; `solve/m4` touches
    no other role's or locked path; the solver sessions never read `../hidden` or used its seed.
  - Out-of-distribution inputs (hand-built): a cantilever is filled down to the ground and a sloped roof is passed
    through. Both are reported `partial` with clear warnings, not hidden.
- **Decision R1: approved.** Roof steps lower than `minEdge` are flushed to the higher roof, and expected answers
  include the flush.
  - Why: `case_0388` (crossing bars at 13.6 and 13.8 m) passes at 94 % of the volume tolerance. Its expected answer
    keeps a 0.2 m step whose walls are shorter than `minEdge`, so no valid output can match it. A 0.24 m step on the
    same bars would fail (about 1.13 %). The solver already flushes (D-030, D-036).
  - The rule is kept, not avoided: design tools do produce near-equal neighbouring heights.
  - Additive meaning, no file format change: `schemaVersion` stays 0.1.
- **Decision Q-S1: approved** as answered above: accept barycentric weights `>= -1e-9` in `ContainsInPlan`.
- **Actions:**
  1. **Owner:** add to AGREEMENT.md, under the tolerance table, under the owner's own commit: *"Roof regions that
     touch in plan (share an edge or a corner) and whose heights differ by less than `minEdge` are raised to the
     highest of them, repeated until no such step remains. Expected answers include this."*
  2. **Generator** (`gen/…`):
     - compute expected `volume_m3` / `maxHeight_m` after the flush rule, from the AGREEMENT text, not from solver
       code;
     - add a `roofStep` fault: about 30 cases, steps both just below and just above `minEdge`;
     - regenerate `cases/`, add a D-entry, and validate.
  3. **Checker** (`check/…`):
     - Q-S1 fix and a test with an anchor exactly on a triangle diagonal;
     - check that the existing checks still pass on the regenerated cases; add a D-entry; validate.
  4. **Solver:** no change expected. After the new cases land, run validate and report the `roofStep` results and
     margins.
  5. **Owner:**
     - merge, then move `lock-v0.1`;
     - regenerate the hidden set with a seed recorded only outside the repo;
     - run it and hold the M4 feature-freeze gate.
- **Reviewer recommendations, not yet decided:**
  - remove the hidden seed (90001) from `cases/README.md` and `OWNER_GUIDE.md`;
  - add 5–10 hand-built hidden cases: cantilever, sloped roof, courtyard near `minGap`, large next to tiny, a real
    design-tool export;
  - M5 README results table with the worst margin per check;
  - KNOWN_LIMITATIONS: overhangs filled, sloped roofs `partial`, output slivers, flush volume change, D-034 jogs;
  - CI determinism check.
- validate (`scripts/validate.ps1`, after this entry; docs only): build ✅ · tests 2465/2465 (Geometry 88, Cli 31,
  Generator 2198, Checker 86, Repair 62) · cases 291/291 · locks OK · **VALIDATE: GREEN**.
- Next: actions 1–5 in order (1 and 2 first; 3 can run in parallel).
- Blockers: none.

### 2026-11-21 · solver · M4 step 4: randomised property tests; M4 criteria met
- New `Step8PropertyTests`: FsCheck properties on random **faulty** sites. 1–4 blocks at random, or placed a random
  0.02–0.98 m from the previous one; 1–4 floors of random height; a random ledge below or above `minEdge` (30 %);
  floating/sunk within `groundSnap` (30 %); objects inside out (20 %); duplicate vertices (20 %). Positions carry
  small per-block offsets so no distance lands within rounding of `minGap` or `minEdge`. The seed comes from three
  FsCheck integers (one `PositiveInt` stays within about 1–100, so it repeated about 100 sites).
  1. any site repairs to valid output: `repaired`, closed outward shells, ground at 0, no edge below `minEdge`, no
     two shells closer than `minGap` (100 cases);
  2. moving to map coordinates and rotating keeps shell count, Euler number and volume (60 cases);
  3. repairing the output again changes nothing: same shells, Euler number and volume, and no repair op runs
     (60 cases).
  The M3 properties (fault-free sites) are kept. Stress runs: properties 1–3 at 1000/500/500 cases, six times in
  a row, and property 2 at 1500 cases three times: all pass.
- The properties found 9 defects, all fixed (each now also a fixed regression test in `Step8RegressionTests`):
  1. roof steps below `minEdge` were flushed only between faces that differ in height: a roof made of several plan
     faces was raised in part, leaving a new 0.2 m step. Equal heights now join the group;
  2. roofs touching only at a corner, 0.1 m apart, gave a 0.1 m vertical edge: faces sharing a vertex now group;
  3. flushing to a fixpoint: raising a bridge to 3.3 m put it within `minEdge` of a 3.5 m block;
  4. a thin ledge was raised to the highest neighbour (a taller block touching its end through a gap bridge),
     building a thin fin above its own block; now to the higher neighbour it shares the longest boundary with;
  5. overlapping corner fillets left clusters of points closer than `minEdge`, which snapping pinched into a point
     contact, so gap closing never settled: one convex hull per pair of components now;
  6. a facing strip only 0.033 m long vanished in snapping: strips shorter than `minEdge` along the walls no longer
     count, and fillets take the wall parts within `minGap` + `minEdge` (fat enough to survive snapping);
  7.–9. snapping bent fill polygons (gap bridges) onto points up to `minEdge` away and folded them, while keeping
     fills straight left slivers between a fill and a bent wall, and the result was not a fixpoint (the second
     repair snapped again). Fixed by restructuring the final phase (D-036): repairs at weld precision until stable,
     then the height field is baked into one outline per roof level and only those are snapped; if snapping leaves
     a gap, thin region or jog, the snapped plan becomes the base and the loop repeats.
- Tests: 62 solver tests (+8: 3 properties, 5 regression cases).
- Determinism: two full runs give byte-identical `repaired.obj` for all 291 cases. Runtime: mean 7 ms, max 171 ms
  per case.
- validate (`scripts/validate.ps1`): build ✅ · tests 2465/2465 (Geometry 88, Cli 31, Generator 2198, Checker 86,
  Repair 62) · cases 291/291 · locks OK · **VALIDATE: GREEN**.
- Per fault (passed/total): clean 10/10 · duplicateVertices 30/30 · farCoordinates 31/31 · flippedFaces 29/29 ·
  floatingBlock 30/30 · openSurface 31/31 · overlap 29/29 · shortEdge 30/30 · sliver 30/30 · smallGap 31/31 ·
  stackedFloors 32/32 · sunkBlock 30/30 · touchingWalls 30/30. All 291 reports `repaired`.
- Unseen sets (`m2w generate --seed N` into a scratch folder, only results read): seed 777 290/290, seed 2024
  290/290, seed 99991 290/290, all `repaired`.
- **M4 target (≥ 80 % overall, every failure explained): met** — 291/291 (100 %), no failures to explain. All four
  M4 items done: gap closing (step 1), sliver and short-edge removal (step 2), combined-fault cases (step 3),
  randomised properties (this step).
- Known risks for hidden cases (none fails today): (a) single jogs close to `minEdge` deep along long walls, where
  the least-change rule may move the other wall than the fault did (worst visible margin 32 %, unseen 50 %;
  D-034); (b) 100 of 291 outputs still contain triangles below `minAngleDeg` (not graded, reported; D-035);
  (c) Q-S1 (checker anchor on a shared edge).
- Next: owner feature-freeze gate for M4. Then M5 (README with results table, KNOWN_LIMITATIONS.md, CI).
- Blockers: Q-S1 (owner; no case affected right now).

### 2026-11-21 · solver · M4 step 3: combined-fault cases
- No solver change was needed: every case with two or more faults passes. By number of faults: 0 → 10/10,
  1 → 240/240, 2 → 14/14, 3 → 13/13, 4 → 14/14.
- Unseen cases: generated three new full sets with `m2w generate --seed N` into a scratch folder (outside the repo)
  and ran `run-all` on them, reading only `summary.json`, `result.json`, `report.json` and the inputs (never the
  generated `expected.json`). Seed 777: 290/290 · seed 2024: 290/290 · seed 99991: 290/290 (seed 1 reproduces the
  committed set: 290/290 + the example). All reports `repaired`.
- Margins on the new sets: worst 50 % of the footprint tolerance (seed 2024 `case_0262`), then 45 % (seed 777
  `case_0364`), 37 % (seed 99991 `case_0253`). All are single jogs where the fault had moved the other wall than
  the least-change rule moves (D-034). This is the main risk for hidden cases: a jog close to `minEdge` deep along
  a long wall. I have not switched to "move the longer wall": four cases suggest it, but it has no geometric
  reason and would be fitted to the grader.
- Tests: 54 solver tests (+2): one hand-built case with far coordinates, stacked floors with a 0.1 m ledge, a
  0.4 m gap and a block floating 0.3 m up with all faces flipped and duplicate vertices gives one closed shell
  with the expected volume, local coordinates, ground at 0, height 9, every op reported and no edge below
  `minEdge`; the same case is deterministic and a second repair changes nothing.
- validate (`scripts/validate.ps1`): build ✅ · tests 2457/2457 (Geometry 88, Cli 31, Generator 2198, Checker 86,
  Repair 54) · cases 291/291 · locks OK · **VALIDATE: GREEN**.
- Per fault (passed/total): clean 10/10 · duplicateVertices 30/30 · farCoordinates 31/31 · flippedFaces 29/29 ·
  floatingBlock 30/30 · openSurface 31/31 · overlap 29/29 · shortEdge 30/30 · sliver 30/30 · smallGap 31/31 ·
  stackedFloors 32/32 · sunkBlock 30/30 · touchingWalls 30/30.
- Next: M4 step 4, randomised property tests.
- Blockers: Q-S1 (owner; no case affected right now).

### 2026-11-21 · solver · M4 step 2: short-edge and sliver removal (shortEdge, sliver)
- The plan is now repaired in phases before the shells are built (`HeightFieldUnion`), each phase building the
  plan again with what the previous one added:
  1. at weld precision: gap bridges (step 1) until none is left;
  2. at weld precision: **thin regions** (one height, 2 × area / perimeter < `minEdge`, next to a higher roof) are
     raised to the highest roof beside them, as roof steps are (D-030): a ledge thinner than `minEdge` becomes part
     of the block above it and the footprint is kept. Op `raiseThinRegion`;
  3. at weld precision: **short outline edges** between two long walls with the same heights (outline = plan edges
     where the height changes, collinear pieces chained):
     - a section with a jog at each end, both stepping the same way (shallow bump or notch), no longer than its two
       neighbours together: cut back / filled out onto their line (ops `cutBump`, `fillNotch`);
     - a single jog between parallel walls: the shorter wall moves onto the other's line (fill if the strip is
       outside, cut if inside; ops `alignJog`, `alignJogCut`). Least change of area, see D-034;
     - a convex corner cut off by a short edge (not a kink in line with a wall): the corner is squared
       (`squareCorner`);
     Fills can now also cut (lower to a height); cuts apply after fills;
  4. final plan with points closer than `minEdge` merged and segments within `minEdge` of a point split there, so
     **no plan edge is shorter than `minEdge`**. Which point survives a merge: the one on the lowest outline (the
     footprint), then the sharper corner of the phase-3 plan, then the one on the higher roof (D-033). Gaps were
     bridged before this, so a gap narrower than `minEdge` is still filled to the lower block. Op
     `collapseShortEdges` (merges/splits that moved geometry beyond weld).
- Own output check (new): any output edge shorter than `minEdge` gives `partial`; triangles thinner than
  `minAngleDeg` are counted in a warning that does not change the status (not graded in v0.1, D-025, D-035).
- Bugs found while doing this (by the new FsCheck property and by tolerance margins, not by failures):
  - snapping by first-come point order could move a tall block's wall onto a lower block's across a gap narrower
    than `minEdge` (volume error gap × wall × height difference: > 1 % for a 0.2 m gap at a 30 m, 10 m wide
    block). Fixed by the merge priority above;
  - point priority alone shaved half a ledge off `case_0222`/`case_0160` (78 % of the volume tolerance used,
    still passing). Fixed by raising thin regions first (now 0 %) and the corner tie-break;
  - per-jog decisions moved both long neighbours of a shallow bump outward in `case_0241` (+3.7 m², 26 %); now
    decided per section (8 %).
- Tests: 52 solver tests (+9): thin ledge absorbed (volume exact, shortest edge ≥ `minEdge`), ledge of 0.3 m at
  `minEdge` 0.25 kept, Z-jog cut back / filled out, shallow bump cut and notch filled (only y = 0 and y = 10
  remain), cut corner squared, split vertex next to a corner merged, case `minEdge` respected (0.3 m jog kept at
  0.25, removed at 0.4), and an FsCheck property: rotated L-shaped block with a random bump, notch or jog of
  0.01–0.24 m gives one closed shell, no edge below `minEdge`, volume within depth × wall of the clean block, and
  repairing again changes nothing. Properties stress-run once at 600/600/300 cases: all pass.
- Determinism: two full runs give byte-identical `repaired.obj` for all 291 cases. All 291 reports `repaired`.
- validate (`scripts/validate.ps1`): build ✅ · tests 2455/2455 (Geometry 88, Cli 31, Generator 2198, Checker 86,
  Repair 52) · cases **291/291** · locks OK · **VALIDATE: GREEN**.
- Per fault (passed/total): clean 10/10 · duplicateVertices 30/30 · farCoordinates 31/31 · flippedFaces 29/29 ·
  floatingBlock 30/30 · openSurface 31/31 · overlap 29/29 · shortEdge 30/30 · sliver 30/30 · smallGap 31/31 ·
  stackedFloors 32/32 · sunkBlock 30/30 · touchingWalls 30/30.
- Margins (how much of the volume/footprint tolerance is used, worst first): `case_0336` 32 %, `case_0298` 19 %,
  `case_0180` 11 %, everything else < 10 %. 0336 and 0298 are jogs where the expected answer moved the other wall
  than the least-change rule does; which piece a jog fault moved cannot be told from the geometry (D-034). Known
  risk for hidden cases: a jog of depth close to `minEdge` (0.4 m in some cases) along a long wall could exceed 1 %.
- Slivers: 100 of 291 outputs have triangles below `minAngleDeg` (long low walls, tall narrow walls between close
  heights, thin roof pieces). Not graded (D-025); now reported in `warnings`. Removing them needs Steiner points
  on walls and roofs (possible without T-junctions: wall columns are shared per vertex). Not done; see D-035.
- Next: M4 step 3 (combined-fault cases: all 40+ pass already; review margins and partial paths) and step 4
  (properties: moving/rotating input keeps volume and topology, repair twice = once, extended to gaps and
  short edges).
- Blockers: Q-S1 (owner; no case affected right now).

### 2026-11-21 · solver · M4 step 1: gap closing below `minGap` (smallGap)
- New `Union/GapCloser`: a morphological closing of the height field, one height level at a time. For each roof
  level h, the slice "height ≥ h" of the plan is split into connected components; boundary walls (collinear pieces
  chained) of *different* components closer than `minGap` get a bridge polygon, filled from the ground up to h.
  Slices are nested, so a gap is filled up to the lower of the two blocks, and to the lower tower above a shared
  podium.
  - Walls facing each other within 10° of anti-parallel: the strip between them where one projects onto the other
    (an exact rectangle for parallel walls, so walls that end together add no new vertex).
  - Component pairs with no facing walls (corner to corner): for each pair of walls closer than `minGap`, the convex
    hull of the parts of each wall within `minGap` of the other and in front of it (a small fillet).
  - Gaps inside one component (a narrow inlet of a U) are left open (see D-032).
  - `HeightFieldUnion` now builds its plan in a repeatable `BuildPlan`; bridges are added as extra tagged polygons
    and the plan is built again (at most 4 rounds; a gap that cannot be bridged, or remains, gives `partial`).
  - A distance within 1e-6 m of `minGap` counts as `minGap` (OBJ precision); it is not closed.
  - Report op `closeGap` with `pair` (blocks on each side), `width_m` (narrowest), `fillHeight_m`, `minGap_m`.
- Tests: 43 solver tests (+10): parallel gap filled to the lower block (volume exact, op contents), gaps of 1.0 /
  1.2 / 5.0 m stay open, shorter neighbour bridged only over the facing stretch, corner-to-corner fillet, towers on
  one podium closed above it to the lower tower, narrow inlet of one block not closed, case `minGap` respected, and
  an FsCheck property: random rotation (0–360°), gap 0.02–1.98 m and height gives one shell with the exact bridged
  volume below `minGap` and two shells at or above it.
- Determinism: two full runs give byte-identical `repaired.obj` for all 291 cases. All 291 reports `repaired`, no
  warnings.
- validate (`scripts/validate.ps1`): build ✅ · tests 2446/2446 (Geometry 88, Cli 31, Generator 2198, Checker 86,
  Repair 43) · cases 274/291 · locks OK · **VALIDATE: GREEN**.
- Per fault (passed/total): clean 10/10 · duplicateVertices 29/30 · farCoordinates 30/31 · flippedFaces 27/29 ·
  floatingBlock 28/30 · openSurface 28/31 · overlap 29/29 · shortEdge 13/30 · sliver 30/30 · smallGap 31/31 ·
  stackedFloors 32/32 · sunkBlock 29/30 · touchingWalls 29/30.
- Also fixed by this step: shortEdge `case_0202` and `case_0302` (a jog left two parts of one block 0.17 m apart).
- Remaining 17 failures, all shortEdge cases (M4 step 2): 16 fail `minEdge_m` only (an output edge shorter than
  `minEdge`); `case_0196` fails `euler` (2 instead of 0: a courtyard closed off by a tiny feature).
- Next: M4 step 2, short-edge and sliver removal.
- Blockers: Q-S1 (owner; no case affected right now).

### 2026-11-19 · solver · M3 step 4: footprint union (stacked floors, overlaps, touching walls); M3 criteria met
- New `Union/HeightFieldUnion` (with `Planar/Planarizer`, `Planar/Dcel`, `Union/ShellBuilder`). The output is
  now rebuilt as a 2.5D height field, one closed shell per connected footprint:
  1. Each object's horizontal triangles are grouped by height level (levels within `weld` are one level); XOR of
     their edges leaves that level's boundary. Each boundary edge is tagged (object, level). Walls are not used.
  2. All tagged edges are planarised together (points within `weld` merged, edges split at vertices within `weld`
     and at crossings, coordinates on a 1e-6 m grid = the OBJ precision).
  3. Faces of the plan are labelled by a walk from outside that toggles each crossed edge's tags. Per object the
     toggles give its solid intervals; the face height is the highest roof over it, filled down to the ground.
     Every edge is checked for a consistent label; any open level boundary is reported (`partial`).
  4. Neighbouring roofs less than `minEdge` apart are raised to the higher one (the step would be an edge shorter
     than `minEdge`); op `flushRoofStep`. Only adjacent faces are touched, so separate blocks keep their heights.
  5. Edges with the same height on both sides are dropped, collinear chains merged, and each shell is built from a
     ground face, a roof per plan face (triangulated with holes) and walls whose vertex columns are split at every
     height used around that vertex, so all triangles meet edge to edge.
  Report ops: `mergeStackedFloors` (block, count), `unionBlocks` (shell, blocks), `flushRoofStep`, `rebuildShells`.
  A block floating beyond `groundSnap` with nothing under it is filled down and reported `partial`. If the union
  yields nothing, the per-object output of steps 1–3 is written instead, always as `partial`.
- Bugs found on the cases and fixed:
  - `case_0179` (touchingWalls): 13 self-intersecting pairs. B03's corners lie on B02's roof edge, and greedy
    best-ear clipping left four nearly collinear points to the end, forcing near-zero-area triangles. Roofs up to
    150 vertices now get an optimal triangulation (dynamic programming over valid diagonals, maximising the smallest
    angle); ear clipping is the fallback for larger polygons.
  - Welding test: walls with a vertex nudged within `weld` were taken as sloped. Vertical now means the plan
    projection is thinner than 2 × `weld`.
  - `case_0137` (overlap): roofs at 17.4 and 17.5 m left a 0.1 m step, below `minEdge`. Fixed by item 4.
- Tests: 33 solver tests (+11: stacked floors without internal floors, crossing bars, tower through podium,
  terrace of three touching blocks, separate blocks stay separate, courtyard keeps genus 1, roof step below / at
  `minEdge`, wall column split at a neighbour's height, and two FsCheck properties on random multi-block,
  multi-floor sites: rotating plus moving to map coordinates keeps shell count, Euler number and volume; repairing
  the output again changes nothing). Step 1/3 tests updated where the union changes the expected output (floors
  merge; a block floating 4 m up is filled down and reported partial).
- Determinism: two full runs give byte-identical `repaired.obj` for all 291 cases.
- validate (`scripts/validate.ps1`): build ✅ · tests 2436/2436 (Geometry 88, Cli 31, Generator 2198, Checker 86,
  Repair 33) · cases 252/291 · locks OK · **VALIDATE: GREEN**.
- Per fault (passed/total): clean 10/10 · duplicateVertices 25/30 · farCoordinates 28/31 · flippedFaces 24/29 ·
  floatingBlock 25/30 · openSurface 28/31 · overlap 29/29 · shortEdge 9/30 · sliver 28/30 · smallGap 11/31 ·
  stackedFloors 29/32 · sunkBlock 28/30 · touchingWalls 27/30.
- **M3 target (≥ 90 % on the M3 faults, 100 % on clean):** met. Cases with an M3 fault: 205/220 (93 %). Every case
  whose faults are all M3 faults passes: 195/195. Per fault over all its cases: farCoordinates 90 %,
  duplicateVertices 83 %, flippedFaces 83 %, openSurface 90 %, floatingBlock 83 %, sunkBlock 93 %,
  stackedFloors 91 %, overlap 100 %, touchingWalls 90 %. Every one of those failures is a combined case that also
  has smallGap or shortEdge (M4). Clean 10/10.
- All 39 remaining failures are M4 work:
  - 22 · a gap below `minGap` not closed (shellCount / mustMerge / minGap fail): 20 smallGap cases and 2 shortEdge
    cases whose jog leaves a gap.
  - 16 · an output edge shorter than `minEdge` (shortEdge: split vertices, tiny ledges or jogs kept as geometry).
  - 1 · `case_0196` (shortEdge): Euler 2 instead of 0, a courtyard closed off by a tiny feature.
  - The sliver failures (2) are among these: `case_0263` and `case_0385` also have smallGap.
- Next: owner midpoint review of M3. Then M4: gap closing below `minGap`, short-edge and sliver removal.
- Blockers: Q-S1 (owner; no case affected right now).

### 2026-11-19 · solver · M3 step 3: ground snap
- New: `Steps/GroundSnapper`. A block (all objects sharing `B<nn>`) whose lowest point is within `groundSnap` of
  Z = 0, above or below, is moved rigidly by minus that height (height unchanged; vertices landing within 1e-9 of
  the ground are set to exactly 0). Blocks further up are left alone (they may stand on another block). A block
  reaching below ground beyond `groundSnap` is not moved and gives `partial` with a warning. Report op
  `groundSnap` with `block` and `dz_m`.
- Tests: 21 solver tests (+4: FsCheck property over offsets within ±0.9 × groundSnap, floors of one block move
  together and other blocks separately, block on a podium not moved, deep sunk block reported partial).
- validate (`scripts/validate.ps1`, run from PowerShell; launched from Git Bash it cannot find Git Bash for the
  lock step): build ✅ · tests 2424/2424 (Geometry 88, Cli 31, Generator 2198, Checker 86, Repair 21) ·
  cases 159/291 · locks OK · **VALIDATE: GREEN**.
- Per fault (passed/total): clean 10/10 · duplicateVertices 22/30 · farCoordinates 21/31 · flippedFaces 21/29 ·
  floatingBlock 20/30 · openSurface 24/31 · overlap 0/29 · shortEdge 0/30 · sliver 17/30 · smallGap 10/31 ·
  stackedFloors 0/32 · sunkBlock 21/30 · touchingWalls 0/30.
- Every single-fault floatingBlock and sunkBlock case passes. The 10 + 9 failures are combined with stackedFloors,
  overlap, touchingWalls, smallGap, sliver or shortEdge.
- Next: M3 step 4, footprint union (stackedFloors, overlap, touchingWalls).
- Blockers: Q-S1 (owner).

### 2026-11-19 · solver · M3 step 2: orientation fix and hole closing
- New: `Steps/Orienter` (orientation spread across manifold edges by BFS, then each edge-connected component with
  negative volume turned inside out) and `Steps/HoleFiller` (first splits free edges at free-edge vertices lying on
  them within `weld`, so a split vertex on a neighbour's edge is joined, not filled; then fills free-edge loops,
  grouping coplanar nested loops into one polygon with holes so a deleted courtyard ground face is filled as a ring).
  Report ops: `fixOrientation`, `splitTJunctions`, `closeHoles` with counts. A loop that cannot be filled gives
  `partial` with a warning.
- First run filled each loop as a disk: `case_0107` and `case_0221` (courtyard ground deleted) then covered the
  courtyard twice (self-intersections, Euler 2 instead of 0). Fixed by the grouping above.
- Known limit: two *adjacent* deleted faces leave one bent loop; one planar fill would cut the corner. The
  generator deletes only non-adjacent faces; not handled.
- Tests: 17 solver tests (+6: courtyard helper, inside-out object, FsCheck property: any random 40 % of courtyard
  faces reversed comes back closed with the right volume, roof + ground deleted, courtyard ground deleted, split
  vertex on an edge).
- validate (`scripts/validate.ps1`): build ✅ · tests 2420/2420 (Geometry 88, Cli 31, Generator 2198, Checker 86,
  Repair 17) · cases 118/291 · locks OK · **VALIDATE: GREEN**.
- Per fault (passed/total): clean 10/10 · duplicateVertices 21/30 · farCoordinates 21/31 · flippedFaces 21/29 ·
  floatingBlock 0/30 · openSurface 23/31 · overlap 0/29 · shortEdge 0/30 · sliver 17/30 · smallGap 10/31 ·
  stackedFloors 0/32 · sunkBlock 0/30 · touchingWalls 0/30.
- Every flippedFaces and openSurface case where that is the only fault passes. The 8 flippedFaces and 8 openSurface
  failures are all combined with a fault from a later step (ground snap, stackedFloors, overlap, touchingWalls,
  smallGap, shortEdge).
- Next: M3 step 3, ground snap.
- Blockers: Q-S1 (owner).

### 2026-11-19 · solver · M3 step 1: coordinate shift, welding, duplicate and degenerate faces
- New in `src/M2W.Repair` (first real solver; `m2w run-all` now finds `M2W.Repair.MassingRepairer` instead of the stub):
  - `RepairPipeline`: one `Solid` per input object with its own vertex list, shifted by minus `case.json` `origin`.
  - `Welder`: grid + union-find clustering within `weld`; each cluster keeps its lowest-numbered vertex's position.
  - `FaceCleanup`: collapses repeated indices, drops faces with < 3 distinct vertices, triangulates polygons, drops
    triangles that repeat the same three vertices inside one object.
  - `Planar/Triangulator`: ear clipping with hole bridges that keeps collinear vertices and never emits a zero-area
    triangle (needed later so faces stay conforming).
  - `ShellCheck`: the solver's own closed/manifold/oriented/volume check; any failure gives `partial` with a warning.
  - Until step 4, each input object is written as one shell.
- Tests: 11 solver tests (clean box, far coordinates, welding incl. nudged copies and the tolerance boundary,
  duplicate/degenerate faces, block names, byte-identical output, triangulator on collinear points, courtyard hole,
  L shape both ways, FsCheck property: triangles tile random star polygons exactly).
- validate (`scripts/validate.ps1`): build ✅ · tests 2414/2414 (Geometry 88, Cli 31, Generator 2198, Checker 86,
  Repair 11) · cases 75/291 · locks OK · **VALIDATE: GREEN**.
- Per fault (passed/total): clean 10/10 · duplicateVertices 20/30 · farCoordinates 19/31 · flippedFaces 0/29 ·
  floatingBlock 0/30 · openSurface 0/31 · overlap 0/29 · shortEdge 0/30 · sliver 16/30 · smallGap 10/31 ·
  stackedFloors 0/32 · sunkBlock 0/30 · touchingWalls 0/30.
- Failures in this step's two faults: farCoordinates misses only `case_0284` among single-fault cases, which is
  Q-S1 (checker anchor on a shared edge). duplicateVertices: the 10 failing cases are all combined with a fault
  from a later step (openSurface, flippedFaces, smallGap, overlap, stackedFloors, shortEdge).
- Next: M3 step 2, orientation fix and hole closing.
- Blockers: Q-S1 (owner).

### 2026-11-18 · owner · lock check no longer skips silently (shared `scripts/`)
- Fixes the generator's "Waiting for owner" item (removed). `scripts/check-locks.sh` exits 1 when git cannot read
  the repository, and prints git's error. A failing `git diff` / `git ls-files` also exits 1. A missing tag still
  skips, now with a `WARNING`. `scripts/validate.ps1` runs the lock check with Git for Windows' bash (found next to
  `git.exe`), never WSL, and fails the step if that bash is missing. D-026.
- Verified: WSL bash in a scratch worktree, which used to skip and pass, now reports `locks: FAIL — git cannot read
  this repository` (exit 1). Git Bash in the same worktree reports `locks: OK`. In the main checkout, an edit to
  `src/M2W.Checker/Geom.cs` and an untracked `cases/probe.txt` each fail and are named; both reverted.
- validate (`scripts/validate.ps1`, now safe to use on Windows): build ✅ (0 errors) · tests 2403/2403 (Geometry
  88, Cli 31, Generator 2198, Checker 86) · cases passing 0/291 (stub solver, expected until M3) · locks OK ·
  **VALIDATE: GREEN**.
- Next: fast-forward `solve/m3` to `main`, generate the hidden cases, then start M3.
- Blockers: none.

### 2026-11-18 · owner · M2 gate
- Approved. `check/m2` (which already contains all of `gen/m2`) fast-forwarded into `main`. Commit `6eac5c9`
  locks the M2 paths, and `lock-v0.1` moved to it (was `0b79d96`, the M1 lock).
- Eye check: 5 cases drawn at random from the 290 generated (awk `srand(20261118)` shuffle of the sorted
  `cases/case_*` list), opened in ParaView 6.1.1. In each, the owner verified that the injected fault is present
  as `expected.json` describes it:
  - `case_0108` flippedFaces
  - `case_0151` touchingWalls
  - `case_0104` sunkBlock
  - `case_0308` stackedFloors
  - `case_0379` shortEdge + openSurface + flippedFaces + floatingBlock
- Locked from now on (`LOCKS`): `AGREEMENT.md`, `LOCKS`, `cases/`, `src/M2W.Checker/`, `tests/M2W.Checker.Tests/`.
  Probe: an edit to `src/M2W.Checker/Geom.cs` made `check-locks.sh` fail and name the file; reverted.
- AGREEMENT.md: added the owner-approved optional `blocks` / `anchor_m` per expected shell (generator D-017) with
  the generator's suggested text, plus the footprint definition (bottom-face area at z = 0). Additive;
  `schemaVersion` stays 0.1. The generator's "Waiting for owner" item for it is removed.
- Owner decisions this milestone: Q1 anchor matching (D-017, D-020), Q2 footprint definition (D-022), Q3 thin
  slivers not graded in v0.1 (D-025, KNOWN_LIMITATIONS.md).
- validate (`scripts/validate.sh`, Git Bash, on `main`): build ✅ (0 warnings) · tests 2403/2403 (Geometry 88,
  Cli 31, Generator 2198, Checker 86) · cases passing 0/291 (stub solver, expected until M3) · locks OK ·
  **VALIDATE: GREEN**.
- Next: M3. The solver works on `solve/m3`, which must be rebased onto or merged with `main` first: it branched
  from the M1 gate and has neither the cases nor the checker.
- Still waiting for owner: the lock check can falsely skip under WSL bash (generator item above). Until it is
  fixed, run `scripts/validate.sh` under Git Bash, not `validate.ps1`.

### 2026-11-18 · checker · M2 Q3 answered: thin slivers not graded in v0.1
- Owner answered Q3 with option B: no minimum-angle check; the checker keeps its 15 checks. Recorded as D-025.
  The limit is written up in the new `KNOWN_LIMITATIONS.md` (Checker section: what is not graded, the effect on
  `sliver` scores (16/30 pass after zero repair), why, and when to revisit). D-019 now points to D-025. Q3
  removed from "Waiting for owner". No code changed.
- validate (`scripts/validate.sh`, Git Bash): build ✅ (0 warnings) · tests 2403/2403 (Geometry 88, Cli 31,
  Generator 2198, Checker 86) · cases passing 0/291 (stub solver, expected until M3) · locks OK ·
  **VALIDATE: GREEN**.
- M2 checker criteria all met; no checker questions open. Ready for the owner's M2 gate (5-case eye check, then
  move `lock-v0.1` so `cases/`, `src/M2W.Checker/` and `tests/M2W.Checker.Tests/` are locked).
- Next: owner gate. After the lock, checker changes need the owner.
- Blockers: none.

### 2026-11-18 · checker · M2 checker: anchor matching, generated cases, a self-intersection fix
- Merged `gen/m2` into `check/m2` (merge commit). The checker's decisions were renumbered D-018 to D-024, because
  the generator had already used D-009 to D-017.
- Q1 answered (owner-approved `anchor_m` and `blocks`, generator D-017): expected shells are now matched by anchor
  (D-020). A shell with an anchor can only pair with a repaired shell that contains it in plan. Best fit remains
  the fallback for files without anchors. New tests: anchors catch a repair that swaps two blocks' sizes, which
  best fit would accept; an anchor outside every shell, or two anchors in one merged shell, leaves an expected
  shell unmatched; plan containment excludes a courtyard. Q2 answered: the generator's footprint is the same
  bottom-face area, and no case can differ (D-022).
- **Clean controls (M2 criterion):** all 10 generated clean controls pass all 15 checks with zero repair, graded
  through `CaseChecker` from files. All 290 generated cases grade without error in about 17 s.
- **Checker bug found and fixed:** grading the generated cases after zero repair showed 3 above-threshold smallGap
  cases failing `noSelfIntersections` with no real intersection. Rotated plots with mm-rounded coordinates give
  valid, nearly flat triangle fans (3e-8 rad), and the shared-vertex test (cut 0.1 mm, then measure distance)
  read them as contact. It is replaced by an exact angular test (D-019), with a regression test confirmed to fail
  on the old code. The checker was stricter than the agreement there; it no longer is.
- Zero repair passes per fault, now fully explained: farCoordinates 20/31 (the 20 single-fault cases: the origin
  shift is the fix) · smallGap 10/31 (the 10 above-threshold ones: staying apart is the answer) · sliver 16/30
  (thin slivers: no check looks at angles; owner Q3) · every other fault 0.
- validate (`scripts/validate.sh`, Git Bash): build ✅ (0 warnings) · tests 2403/2403 (Geometry 88, Cli 31,
  Generator 2198, Checker 86) · cases passing 0/291 (stub solver, expected until M3) · locks OK ·
  **VALIDATE: GREEN**.
- M2 checker criteria: all checks ✅ · unit tests on good and bad meshes ✅ · clean controls pass with zero repair ✅
  (10/10 generated, 7 hand-built). Ready for the owner's M2 gate once Q3 is decided.
- Next: Q3 (add a minimum-angle check or document the limit), then the owner's 5-case eye check and the M2 lock.
- Blockers: none. One owner question (Q3).

### 2026-11-18 · checker · M2 checker
- Changed (`src/M2W.Checker`, `tests/M2W.Checker.Tests` only):
  - `CaseChecker` (the `ICaseChecker` that `m2w check` now finds) → `Grader`. It writes 15 checks to `result.json`
    with a `detail` string each: parse, watertight, manifold (non-manifold edges and vertices), outwardNormals
    (winding and signed volume > 0), euler (V−E+F = 2−2·genus), noSelfIntersections, minGap_m, minEdge_m,
    shellCount, volume_m3, footprintArea_m2, maxHeight_m, groundContact, mustMerge, mustStaySeparate.
  - Exact geometry (`Geom`: closest points, segment/triangle contact, triangle distance, winding number),
    `Topology`, `SelfIntersection` (sweep and prune), `ShellGap` (exact minimum shell distance with box pruning,
    nesting counts as 0), `BlockLocator` (input blocks located in output shells by position), `Assignment` (Hungarian).
  - It never reads `report.json` (a test proves a garbage report changes nothing). An unreadable `repaired.obj` is
    graded as a failed `parse` and does not throw.
  - Design choices D-018 to D-024 in DECISIONS.md.
- Tests: 80 checker tests, with one hand-built mesh that must pass and one that must fail for every check
  (including boundary values: gaps 0.9/1.0/1.1 m, edges 0.2/0.25/0.3 m, ±1 % volume, weld on ground contact,
  touching-but-unmerged walls, a nested shell, a diagonal corner gap, folds, a shared-vertex crossing,
  21,600-triangle split faces).
- Clean controls with zero repair (origin shift, ear-clip triangulation, shell naming; `Kit.ZeroRepair`): bar, L,
  U, acute plot, courtyard (genus 1), podium + set-back tower, and a 5-block site in map coordinates, all pass every
  check, including through the 6-decimal OBJ round trip. `Generated_clean_controls_pass_with_zero_repair` runs the
  same proof on every clean case in `cases/`. There are none yet, because the generator's cases have not landed.
  **Re-run it after the M2 merge.**
- case_0007: the correct repair (one 30.3 × 10 × 18 box) passes all 15 checks via `m2w check` (exit 0). The
  unrepaired input fails shellCount, minGap, mustMerge, volume, footprint and groundContact (exit 1).
- `out/summary.json`: the checker brief says the checker writes it. Per D-004 (owner-approved) `m2w run-all`
  already does, and the checker does not duplicate it.
- validate (`scripts/validate.sh`): build ✅ (0 warnings) · tests 193/193 (Geometry 82, Cli 31, Checker 80) ·
  cases passing 0/1 (case_0007: repair=1 stub solver, check=2 no repaired.obj; expected until M3) · locks OK ·
  **VALIDATE: GREEN**.
- M2 checker criteria: all checks ✅ · unit tests on good and bad meshes ✅ · clean controls pass with zero
  repair: ✅ on hand-built massings, ⏳ on generated controls (none exist yet).
- Next: once the generator's cases are merged, run the clean-control test on them and look at the details of any
  failure. A failure there means the checker is too strict or the case is wrong, and I will settle it with the
  generator. Then the owner's 5-case eye check, and the M2 lock.
- Blockers: none for the checker itself. Two owner questions above (Q1 shell matching, Q2 footprint definition).
### 2026-11-18 · generator · M2 shell matching fields (owner-approved)
- Changed: `expected.shells[]` now has `blocks` and `anchor_m` (D-017), answering the checker's Q1. Added two optional
  properties to `ExpectedShell` in shared `M2W.Geometry` (no existing signature changed) plus
  `tests/M2W.Geometry.Tests/ExpectedShellFieldsTests.cs` (optional, round trip, bad values). Generator fills them;
  `HeightField.InteriorPoint` picks the anchor. New test: for all 290 cases each anchor ±0.5 m lies inside its own
  shell's blocks and outside every other block, and `blocks` partitions the case's blocks consistently with mustMerge.
  Regenerated `cases/`: only the 290 `expected.json` files changed; every `input.obj` and `case.json` is byte-identical.
  Checker's Q2 (footprint = bottom-face area): same definition as the generator, recorded in D-015; no case differs.
- validate (`scripts/validate.sh`, Git Bash): build ✅ · tests 2317/2317 (Geometry 88, Cli 31, Generator 2198) · cases passing 0/291 (stub solver, no checker yet; per-fault totals unchanged) · locks OK · **VALIDATE: GREEN**.
- Next: owner adds the AGREEMENT.md text (Waiting for owner) and re-tags; checker can switch to anchor matching.
  Note for merging: `check/m2` may also touch `ExpectedSpec.cs`; this change only adds members.
- Blockers: none.

### 2026-11-18 · generator · M2 test cases
- Changed (all in `src/M2W.Generator/`, `tests/M2W.Generator.Tests/`, `cases/`):
  - Seeded mini generative pipeline: convex site (rectangle, irregular, acute trapezoid, corner-cut) → 2–6 m setback →
    grid frame along the longest site edge → footprints (bar, L, U, courtyard, tower, tower on podium) → floor-by-floor
    extrusion (taller ground floor, typical 3.0–3.5 m) with setback tiers → placement with seeded rejection sampling.
  - Ground truth (`HeightField`): the corrected model merged as an exact 2.5D height field → shellCount, volume,
    footprint, maxHeight, genus, mustMerge, mustStaySeparate. Ambiguous designs are rejected and redrawn (D-015).
  - Emission (`Mesher`): whole blocks (`B01`) or stacked floors (`B01_F01`), planar polygons or triangles; every
    object is a closed outward manifold shell before faults.
  - All 12 faults injected (`Layouts`, `CaseBuilder`, `MeshFaults`); conventions in D-012/D-013/D-014; tolerances
    vary in some cases (D-010).
  - `CaseGenerator : ICaseGenerator` → `m2w generate cases` wrote 290 cases `case_0101`–`case_0390` (seed 1) plus
    `cases/index.json`; `_example/case_0007` untouched. 10 clean controls, 20 single-fault cases per fault, 40
    combined (2–4 faults). Per fault including combined cases: 29–32. smallGap: 10 below / 10 above minGap,
    4 at exactly 0.9× and 4 at 1.1×. 7.3 MB. Every case builds for 11 other seeds tested (incl. 90001).
  - Tests (1908): plan counts and IDs; height-field maths (incl. the case_0007 numbers 5454 m³ / 303 m², courtyard
    genus 1, union of overlaps); mesher closed/outward/volume/Euler for every shape × style; fault injectors keep
    shells closed; **for all 290 cases, vertical ray casting through the emitted meshes equals the model cell by
    cell**; expected == corrected model; clean and expected models differ only by closed gaps; clean controls meet
    the output rules (triangles, closed, no slivers/short edges, z = 0, volumes); each fault really shows in the
    written input and absent faults do not; same seed → byte-identical files; committed `cases/` == generator.
- Found and fixed while testing: a group's quarter-turn was drawn per block, so blocks of one group could end up
  rotated differently (case_0106). Rotation is now drawn once per group, and a test guards it.
- Notes for the checker (see D-011, D-014): clean controls are triangles with origin 0 but objects are named `B<nn>`
  (input naming), so "zero repair" = copy input.obj and rename objects to `shell_NNN`. Block names in
  mustMerge/mustStaySeparate are the prefix of the input object names (`B01_F03` belongs to `B01`). Expected shells are
  listed largest volume first. Floating/sunk blocks are expected back on the ground with their original height.
- For the owner at the gate: please glance at D-013 (floating = rigid move back) and D-014 (gap fill rule). They are
  my reading of AGREEMENT.md and decide what counts as correct.
- validate (`scripts/validate.sh`, Git Bash): build ✅ · tests 2021/2021 (Geometry 82, Cli 31, Generator 1908) ·
  cases passing 0/291 (stub solver, no checker yet; per fault: clean 0/10, overlap 0/29, stackedFloors 0/32,
  touchingWalls 0/30, smallGap 0/31, sliver 0/30, shortEdge 0/30, duplicateVertices 0/30, openSurface 0/31,
  flippedFaces 0/29, floatingBlock 0/30, sunkBlock 0/30, farCoordinates 0/31) · locks OK · **VALIDATE: GREEN**.
  (`validate.ps1` also GREEN but its lock step skips falsely here; see "Waiting for owner".)
- Next: owner gate M2 (5 random cases by eye; `index.json` notes say what was injected where). After the checker
  lands, run-all with a "copy input" repair on the clean controls should pass them; if the checker finds a case it
  thinks is wrong, the generator fixes the generator, not the case.
- Blockers: none.

### 2026-11-18 · owner · M1 gate
- Changed: M1 approved. `m1/foundations` fast-forwarded into `main` and tagged `lock-v0.1` (commit `0b79d96`);
  `AGREEMENT.md` and `LOCKS` are now locked. Role branches `gen/m2`, `check/m2`, `solve/m3` created from `main`.
- Owner answers (all approved as proposed): `summary.json` layout per D-004; `partial` → `repair` exit 1 per D-006;
  `generate` and `check` may stay stubs (exit 2 "not implemented yet") until M2.
- validate: build ✅ · tests 113/113 · cases passing 0/1 (expected until M2/M3) · locks OK · **VALIDATE: GREEN**.
- Next: M2. Generator on `gen/m2` implements `ICaseGenerator`; checker on `check/m2` implements `ICaseChecker`
  (see `src/M2W.Geometry/README.md`, "How a role plugs into m2w").
- Blockers: none.

### 2026-11-18 · shared (M2W.Geometry, M2W.Cli) · M1 Foundations
- Changed:
  - `src/M2W.Geometry`: `Vec3`; `Mesh` / `MeshObject` / `Face`; strict `ObjReader` (line-numbered errors) and
    `ObjWriter` (6 dp, LF, no BOM); `MeshMeasures` (bounds, signed volume, area, edge use counts);
    JSON models `CaseSpec`, `ExpectedSpec`, `RepairReport`, `CheckResult` (+ `RunSummary`) with validation;
    `CaseDiscovery`; stage interfaces `ICaseRepairer` / `ICaseChecker` / `ICaseGenerator` (D-003). README updated.
  - `src/M2W.Cli`: `generate`, `repair`, `check`, `run-all` wired (`CliApp`). Roles plug in by implementing a stage
    interface in their own project; until then repair is an honest stub (`status: failed`, no repaired.obj),
    and check/generate exit 2 "not implemented yet". `run-all` finds every folder containing `case.json` at any
    depth (so `cases/_example/case_0007` is found), rejects duplicate caseIds, and writes `out/summary.json`.
  - New shared test projects `tests/M2W.Geometry.Tests` (82 tests: OBJ round trip on case_0007 and an FsCheck
    property on random meshes, reader errors, writer format, measures, example case.json/expected.json validation,
    AGREEMENT.md report/result examples, required-field and bad-value cases, discovery) and `tests/M2W.Cli.Tests`
    (31 tests: run-all discovery, stub honesty, byte-identical reruns, per-fault counts, exit codes, stale-output
    removal, solver contract violations). `M2W.sln` committed with them.
  - `.gitattributes` added (D-008): this machine has `core.autocrlf=true`, which checked out `check-locks.sh`
    with CRLF and broke the lock step of `validate.ps1`.
- M1 criteria: Geometry mesh/OBJ/JSON models ✅ · round-trip test ✅ · case_0007 reads and validates ✅ ·
  m2w runs generate/repair/check/run-all ✅ (stubs, see owner question) · validate runs build, tests, run-all,
  locks and prints the summary ✅.
- validate (`scripts/validate.ps1` and `scripts/validate.sh`): build ✅ (0 warnings) · tests 113/113
  (Geometry 82, Cli 31; role test projects have no tests yet) · cases passing 0/1 (per fault: stackedFloors 0/1,
  smallGap 0/1, duplicateVertices 0/1, farCoordinates 0/1; expected until M2/M3) · locks: skipped, no `lock-v0.1`
  tag yet · **VALIDATE: GREEN**.
- Notes: `report.json` `runtime_ms` is the only output field that is not deterministic (AGREEMENT.md requires it).
  M1 work is committed on branch `m1/foundations` (M1 is shared work, so no role branch fits); merge to `main` at the gate.
- Next: owner gate for M1 (tag `lock-v0.1`), then M2 (generator and checker implement `ICaseGenerator` / `ICaseChecker`).
- Blockers: none. Three non-blocking questions under "Waiting for owner".

<!--
Template — copy for each session:

### YYYY-MM-DD · <role> · <milestone>
- Changed:
- validate: build ✅/❌ · tests x/y · cases passing x/y (per fault: …)
- Next:
- Blockers:
-->
