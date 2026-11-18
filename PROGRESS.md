# Progress log

## Waiting for owner

_Agents: put questions that need the owner here (newest first). Owner: answer inline and delete when done._

- **2026-11-18 · checker · Q1: how should expected shells be matched to repaired shells?** The checker brief says
  "match shells by position", but `expected.json` shells have no position (only volume, footprint, height, genus).
  For now the checker matches by best fit on those values (D-011). That works for all current cases. It cannot
  tell apart two expected shells with near-identical measures but different locations.
  **Proposal:** the generator adds `"blocks": ["B01","B02"]` to each entry of `expected.shells`, and the checker
  matches through block location (D-012). Readers already ignore unknown fields, but CLAUDE.md makes any JSON
  field change an escalation. Approve, or keep best-fit matching?
- **2026-11-18 · checker · Q2: confirm the footprint definition.** The checker takes `footprintArea_m2` to be the
  shell's closed bottom face (plan area of the downward faces at its lowest Z) (D-013). The generator must compute
  the same thing. The two definitions differ only if a block overhangs its ground floor (an upper floor wider
  than the one below). OK as is, or should it be the plan projection of the whole shell?

## Current status

| Milestone | Status | Pass rate | Last updated |
| --- | --- | --- | --- |
| M1 Foundations | ✅ Passed (gate approved, `lock-v0.1` tagged) | 0/1 (no solver or checker yet, expected) | 2026-11-18 |
| M2 Test cases and checker | In progress: checker done (2 owner questions); generator cases pending | 0/1 (no solver yet, expected) | 2026-11-18 |
| M3 Core repair | Not started | — | — |
| M4 Hard faults | Not started | — | — |
| M5 Package | Not started | — | — |

## Log (newest first)

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
  - Design choices D-009 to D-015 in DECISIONS.md.
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
