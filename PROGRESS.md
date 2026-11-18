# Progress log

## Waiting for owner

_Agents: put questions that need the owner here (newest first). Owner: answer inline and delete when done._

- (none)

## Current status

| Milestone | Status | Pass rate | Last updated |
| --- | --- | --- | --- |
| M1 Foundations | ✅ Passed (gate approved, `lock-v0.1` tagged) | 0/1 (no solver or checker yet, expected) | 2026-11-18 |
| M2 Test cases and checker | Not started | — | — |
| M3 Core repair | Not started | — | — |
| M4 Hard faults | Not started | — | — |
| M5 Package | Not started | — | — |

## Log (newest first)

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
