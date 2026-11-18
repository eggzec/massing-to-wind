# Progress log

## Waiting for owner

_Agents: put questions that need the owner here (newest first). Owner: answer inline and delete when done._

- **2026-11-18 · generator · lock check can falsely skip (shared `scripts/`, not changed).** On this machine
  `validate.ps1` calls `bash` = WSL bash. Inside a git *worktree* WSL's git cannot follow the `.git` file (it holds a
  Windows path), so `check-locks.sh` sees "not a git repository", reports `locks: no lock-v0.1 tag yet, skipping`
  and validate stays GREEN. `scripts/validate.sh` under Git Bash checks properly (`locks: OK`). Suggested fix (owner or
  shared work): in `check-locks.sh`, exit 1 when `git rev-parse --git-dir` fails, and skip only when that works but
  the tag is missing; or have `validate.ps1` call Git for Windows' bash explicitly.

## Current status

| Milestone | Status | Pass rate | Last updated |
| --- | --- | --- | --- |
| M1 Foundations | ✅ Passed (gate approved, `lock-v0.1` tagged) | 0/1 (no solver or checker yet, expected) | 2026-11-18 |
| M2 Test cases and checker | ✅ Passed (gate approved, `cases/` and checker locked, `lock-v0.1` moved) | 0/291 (stub solver, expected until M3) | 2026-11-18 |
| M3 Core repair | Not started | — | — |
| M4 Hard faults | Not started | — | — |
| M5 Package | Not started | — | — |

## Log (newest first)

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
