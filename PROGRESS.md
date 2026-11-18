# Progress log

## Waiting for owner

_Agents: put questions that need the owner here (newest first). Owner: answer inline and delete when done._

- **2026-11-18 · generator · AGREEMENT.md text for the approved `blocks` / `anchor_m` fields (D-017).** AGREEMENT.md
  is locked, so I did not edit it. Suggested addition under **`expected.json`**, after the example:

  > Each entry in `expected.shells` may also carry `blocks` (the input block names in that shell, e.g.
  > `["B01", "B02"]`) and `anchor_m` (`[x, y]` in local coordinates, strictly inside the shell's footprint).
  > The checker matches a repaired shell to an expected one by which shell contains the anchor in plan.
  > `footprintArea_m2` is the area of the shell's bottom face at z = 0.

  and in the example shell: `"genus": 0, "blocks": ["B01", "B02"], "anchor_m": [10.0, 5.0]`. Then commit and
  `git tag -f lock-v0.1`. Additive and optional, so `schemaVersion` stays 0.1.

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
| M2 Test cases and checker | Generator part done (290 cases); checker part is on `check/m2` (not checked here) | 0/291 (stub solver, no checker yet) | 2026-11-18 |
| M3 Core repair | Not started | — | — |
| M4 Hard faults | Not started | — | — |
| M5 Package | Not started | — | — |

## Log (newest first)

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
