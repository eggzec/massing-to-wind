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

## D-015 · 2026-11-18 · checker (M2) · Shape of `result.json` entries
- Context: AGREEMENT.md gives `name/pass/value/limit` and says `watertight` value = free edges, nothing more.
- Choice: always the same 15 checks in the same order (`CheckNames.All`). Count checks (`parse`, `watertight`,
  `manifold`, `outwardNormals`, `euler`, `noSelfIntersections`, `mustMerge`, `mustStaySeparate`): value = number of
  violations, limit 0. `minGap_m`, `minEdge_m`: value = measured minimum, limit = expected. `shellCount`: count vs
  expected. `volume_m3`, `footprintArea_m2`, `maxHeight_m`: value/limit of the worst shell. `groundContact`: value =
  largest |lowest Z|, limit = weld. Each entry has a `detail` string (kept in `CheckEntry.Extra`, no schema change).
  If `repaired.obj` does not parse, `parse` fails and every other check fails with "not checked". `minGap_m` with
  fewer than two shells passes with value 0 and says so. `expected.json` `checks.watertight/outwardNormals/
  noSelfIntersections = false` → still measured and reported, but not enforced.
- Alternatives: one entry per shell per check; omit checks that cannot run.
- Why: stable names make `summary.json` `failedChecks` comparable across cases; detail keeps it debuggable.
- Revisit if: the owner wants per-shell entries.

## D-014 · 2026-11-18 · checker (M2) · Numeric tolerances inside the checker
- Context: exact geometry tests need a "touching" threshold, and the OBJ format rounds to 6 decimals.
- Choice: `Geom.TouchTol` = 1e-8 m for intersection/contact. `minGap_m` and `minEdge_m` pass at `limit − 1e-6`
  (one unit of the 6th decimal: a length on the limit can read back that much short). `groundContact` uses
  `case.json` `weld`. Volume/area/height use exactly the `expected.json` tolerances.
- Alternatives: no slack (an on-limit 1.0 m gap could fail by 1e-7); slack = `weld` (too generous: 1 mm).
- Why: strict where the agreement is strict, never stricter than the file format can represent. None of these is
  an AGREEMENT.md tolerance default.
- Revisit if: OBJ precision changes.

## D-013 · 2026-11-18 · checker (M2) · Footprint and height definitions
- Context: AGREEMENT.md names `footprintArea_m2` and `maxHeight_m` without defining them.
- Choice: footprint = plan area of the downward-facing triangles lying at the shell's lowest Z (within `weld`), i.e.
  the closed bottom face; height = highest Z (ground is Z = 0). Measured at the shell's own base so a floating
  shell fails `groundContact` only, not also footprint.
- Alternatives: area of the shell's projection onto the ground (differs only for overhangs/cantilevers).
- Why: matches "every shell has a closed bottom face at Z = 0" and the case_0007 example (303 m²).
- Revisit if: the generator computes plan projection for blocks with overhangs (owner question in PROGRESS.md).

## D-012 · 2026-11-18 · checker (M2) · Locating input blocks in repaired shells (mustMerge / mustStaySeparate)
- Context: groups name input blocks (`B01`), but output objects are `shell_NNN`; the solver's naming says nothing.
- Choice: read `input.obj`, subtract `origin`, group objects by block (`B01_F03` → `B01`). Each block's vertices
  and face centres vote for the repaired shell whose solid is nearest (0 inside, by winding number), if within
  `minGap + groundSnap + minEdge` of it. A point equally near two shells (within 1e-6 m, e.g. on a shared wall)
  does not vote. Most votes wins; ties go to the lowest shell index; no votes = "not found" = group violated.
  `input.obj` is read only when there are groups.
- Alternatives: trust object order or the solver's report (not allowed); block centroid only (fails for L/U
  shapes and moved blocks).
- Why: purely positional, so it cannot be gamed by naming, and it survives the legitimate moves a repair makes
  (ground snap, gap closing, short-edge collapse).
- Revisit if: a fault moves a block further than that distance.

## D-011 · 2026-11-18 · checker (M2) · Matching expected shells to repaired shells (pending owner)
- Context: the brief says "match shells by position", but `expected.json` shells carry no position.
- Choice: minimum-cost assignment (Hungarian method): cost = volume, footprint and height errors in tolerance
  units plus 1 per genus mismatch. Unmatched expected shells fail the measure checks; extra repaired shells fail
  `shellCount`. `euler` compares each matched shell with its expected genus.
- Alternatives: file order (meaningless); add a position or block list to each expected shell (JSON field change:
  escalated, see PROGRESS.md).
- Why: works with the locked schema; a wrong repair cannot pass by being matched to a "better" shell because every
  expected shell must be within tolerance of a distinct repaired shell.
- Revisit if: the owner approves a `blocks` field per expected shell; then match by block location (D-012).

## D-010 · 2026-11-18 · checker (M2) · Self-intersection scope and adjacency
- Context: "no two non-adjacent triangles intersect" leaves out folds between triangles that do share vertices.
- Choice: per shell (contact between shells is `minGap_m`). Broad phase: sweep and prune on X over triangle boxes.
  Narrow phase by shared vertex indices: 0 → any contact (exact segment/triangle tests); 1 → the opposite edges,
  or the edges leaving the shared vertex (minus 0.1 mm next to it), touch the other triangle; 2 → coplanar fold
  onto the same side of the shared edge; 3 → duplicate face.
- Alternatives: skip all adjacent pairs (misses folds); uniform 3D grid (large roof triangles span many cells).
- Why: correctness first; 21,600 triangles grade in about 3 s.
- Revisit if: hidden cases are much larger (then switch to a BVH).

## D-009 · 2026-11-18 · checker (M2) · Topology by vertex index; checker does not weld
- Context: two vertices at the same position with different indices could be treated as one or two.
- Choice: by index. A shell is one `o` object; `watertight`, `manifold`, `outwardNormals` and `euler` use its
  faces' vertex indices. Duplicate positions are therefore free edges, which is the `duplicateVertices` fault the
  solver must fix. Polygons are fan-triangulated for analysis (and fail `parse`).
- Alternatives: weld coincident positions within `weld` before checking.
- Why: the wind mesher sees indices; welding in the checker would hide an unrepaired fault.
- Revisit if: the owner says output may legitimately contain duplicate positions.

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
- Owner approved at the M1 gate (2026-11-18), including `partial` → 1 and stub `generate`/`check` until M2.

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
- Revisit if: the checker takes it over. (Owner approved this layout at the M1 gate, 2026-11-18.)

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
