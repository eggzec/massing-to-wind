# Decision log

One entry per design choice someone could reasonably question. Newest first. Keep each entry short.

<!--
Template:

## D-NNN · YYYY-MM-DD · <role> · <title>
- Context: what problem forced a choice
- Choice: what was decided
- Alternatives: what else was considered
- Why: the deciding reason
- Revisit if: the condition that would change this
-->

## D-008 · 2026-11-18 · shared (M1) · `.gitattributes` forces LF for `*.sh`, `*.obj`, `*.json`
- Context: with `core.autocrlf=true` and no `.gitattributes`, Windows checkouts got CRLF; `check-locks.sh` then
  failed in bash (`set: pipefail\r: invalid option name`) and the example OBJ was CRLF despite AGREEMENT.md saying LF.
- Choice: `* text=auto` plus `eol=lf` for `*.sh`, `*.obj`, `*.json`. Repository content is unchanged (already LF).
- Alternatives: tell each machine to set `core.autocrlf=false`; make the reader/scripts CR-tolerant only.
- Why: one file fixes it for every clone, and generated cases stay byte-identical across platforms.
- Revisit if: the owner prefers a different line-ending policy.

## D-007 · 2026-11-18 · shared (M1) · Strict readers; only tolerances have defaults
- Context: AGREEMENT.md says "a missing required field is an input error" but does not list which fields are required.
- Choice: every field shown in the AGREEMENT.md examples is required, except `case.json` `tolerances` (and each
  tolerance inside it), which fall back to the AGREEMENT.md default table. `schemaVersion` must be exactly `"0.1"`.
  `faultsInjected` must use the 12 v0.1 fault names. OBJ: unknown keywords, index 0, out-of-range indices,
  non-finite numbers and faces with fewer than 3 vertices are errors; `vn vt usemtl mtllib g s` are ignored.
  Extra numbers after `v x y z` and `/t/n` suffixes on face indices are accepted and dropped.
- Alternatives: lenient readers that guess; every tolerance required.
- Why: the generator writes every field, so strictness only catches real mistakes; the tolerance table is
  explicitly labelled "Default".
- Revisit if: hidden or third-party inputs use OBJ lines outside the AGREEMENT.md list.

## D-006 · 2026-11-18 · shared (M1) · Exit codes where AGREEMENT.md is silent
- Context: AGREEMENT.md defines exit codes for `repair` and `check` only, and does not cover `partial`.
- Choice: `repair`: `repaired` → 0, `partial` or `failed` → 1, bad input → 2. `check`: missing/invalid case.json,
  expected.json or repaired.obj → 2; no checker yet → 2. `run-all`: 0 once every case has run and summary.json is
  written, whatever the pass rate; 2 for a missing folder, no cases, or two folders sharing a caseId. `generate`:
  no generator yet → 2. Unexpected exceptions are printed in full and give 1.
- Alternatives: `run-all` exits 1 if any case fails (would make `validate` red until every case passes).
- Why: `run-all` is the harness; pass rates live in summary.json, which validate prints. `partial` is not a full repair.
- Revisit if: the owner wants CI to fail on a pass-rate threshold (add a `--min-pass` option rather than changing 0).

## D-005 · 2026-11-18 · shared (M1) · m2w deletes stale output before each run
- Context: if a solver stops writing `repaired.obj`, an old copy in `out/<caseId>/` would still be graded.
- Choice: `repair` deletes `repaired.obj`, `report.json`, `result.json` in `out/<caseId>/` first; `check` deletes
  `result.json` first. Nothing else in `out/` is touched.
- Alternatives: wipe `out/` at the start of `run-all`.
- Why: makes a fake pass from stale files impossible without deleting anything we didn't write.
- Revisit if: never, unless outputs gain new files.

## D-004 · 2026-11-18 · shared (M1) · Layout of `out/summary.json`
- Context: AGREEMENT.md asks for "pass counts per fault type" but gives no layout; M1 needs `run-all` to write it.
- Choice: `{ schemaVersion, casesTotal, casesPassed, byFault: { <fault>: { total, passed } }, cases: [ { caseId, path,
  faults, repairExit, checkExit, pass, failedChecks } ] }`. Faults come from each case's `expected.json`
  `faultsInjected` (m2w may read it; the solver never sees it). No faults → `clean`; unreadable → `unknown`.
  A multi-fault case counts once under each fault. Written by `M2W.Cli` (`RunSummary` model in M2W.Geometry).
- Alternatives: read faults from `cases/index.json` (does not exist yet; per-case files are always present);
  let the checker write it (the checker brief says so, but there is no checker in M1).
- Why: works today and in M2 without depending on index.json; per-case rows make failures easy to find.
- Revisit if: the owner wants this layout in AGREEMENT.md (question in PROGRESS.md), or the checker takes it over.

## D-003 · 2026-11-18 · shared (M1) · Roles plug into m2w through interfaces found by reflection
- Context: three roles on three branches all need `m2w` to call their code. If each edits `Program.cs`, every
  merge conflicts, and `M2W.Repair` must not reference the checker or generator.
- Choice: `M2W.Geometry.Stages` defines `ICaseRepairer`, `ICaseChecker`, `ICaseGenerator`. `M2W.Cli/StageSet`
  loads `M2W.Repair`, `M2W.Checker`, `M2W.Generator` and uses the single public implementation in each (more than one
  is an error). The solver's request holds only `CaseSpec` and the input `Mesh`, so it physically cannot reach
  `expected.json`. m2w validates and writes all output files itself.
- Alternatives: each role edits the CLI switch; a plugin folder scanned at run time.
- Why: no shared-file edits after M1, honest-by-construction solver input, one place that enforces output rules.
- Revisit if: a role needs more inputs than the request carries (add a property; do not pass the case folder to the solver).

## D-002 · 2026-11-18 · shared (M1) · Separate test projects for shared code
- Context: M1 needs tests for `M2W.Geometry` and `M2W.Cli`, but the three existing test projects each belong to a role.
- Choice: add `tests/M2W.Geometry.Tests` and `tests/M2W.Cli.Tests` (same xUnit + FsCheck versions as the others),
  and add them to `M2W.sln`, which is now committed.
- Alternatives: put shared tests in a role's test project.
- Why: keeps role ownership clean; shared tests travel with shared code.
- Revisit if: the owner wants shared tests under LOCKS after the M1 gate.

## D-001 · 2026-11-18 · owner · Pure C#, no external validators in v0.1
- Context: one-week build; the checker must close the loop without OpenFOAM or Python.
- Choice: all generation, repair and checking in C# / .NET 8.
- Alternatives: OpenFOAM `surfaceCheck`/`checkMesh`, Python trimesh as independent validators.
- Why: keeps the loop fast and the stack aligned with the target role.
- Revisit if: v0.2 adds external validation (roadmap step 2).
