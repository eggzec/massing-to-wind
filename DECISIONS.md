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

## D-016 · 2026-11-18 · generator (M2) · Case IDs, order, seeds and regeneration
- Context: `run-all` rejects duplicate caseIds, and `cases/_example/case_0007` already exists. Hidden sets use other seeds.
- Choice: generated IDs run from `case_0101` (`case_0001`–`0100` kept for hand-made cases). The 290 recipes are
  shuffled with the master seed before IDs are given, so an ID says nothing about its faults and differs per seed.
  Each case has its own seed (hash of master seed and recipe position), recorded in `expected.json` and `index.json`.
  `generate` first deletes only `case_NNNN` folders that contain `case.json`.
- Alternatives: IDs grouped by fault (easier to browse, but invites ID-based overfitting); skipping only `0007`.
- Why: IDs stay clear of hand-made cases and carry no information; `index.json` gives the mapping for people.
- Revisit if: the owner wants stable IDs across seeds.

## D-015 · 2026-11-18 · generator (M2) · Exact ground truth from a 2.5D height field in a grid frame
- Context: expected.json must be computed from the clean geometry, including unions (overlap, touching, closed gaps)
  and genus, with no hand-typed numbers and no general 3D boolean library.
- Choice: every site has one grid frame (direction of its longest edge, a unit vector built with one square root,
  no trigonometry). Footprints are unions of rectangles in that frame; tiers (setbacks) are nested; everything
  stands on z = 0. So the union is a height field on a rectilinear grid: volume, footprint, height, shells and genus
  (footprint holes) are exact sums. Configurations that would be ambiguous are rejected and redrawn: diagonal-only
  contact (non-manifold), outline steps closer than `max(2 m, 2.5 × minGap, 4 × minEdge)`, separate shells closer
  than `max(4 m, 3 × minGap)` (except deliberate smallGap-above pairs). A test casts vertical rays through every
  emitted mesh and checks it against the height field, cell by cell, for all 290 cases.
- Alternatives: arbitrary-angle footprints with a polygon clipping library; voxel estimates (not exact).
- Why: exact, fast, independent of the solver's methods; the plot and the grid can still be at any angle.
- Revisit if: v0.2 needs curved or non-orthogonal footprints, bridges or overhangs.

## D-014 · 2026-11-18 · generator (M2) · smallGap answer: the gap is filled up to the lower block
- Context: AGREEMENT.md says gaps narrower than `minGap` are closed and the blocks merged, but not how high the
  fill goes when the blocks differ.
- Choice: below-threshold pairs are built so the answer is unambiguous. The smaller block B faces the larger block A
  across the gap, B's facing wall lies within A's, and B is no taller than A along it. The expected model extends B
  across the gap (including tiers that are flush with that wall), which is what morphological closing gives. Half the
  smallGap cases are above the threshold (1.1–2.0 × minGap) and must stay separate (`mustStaySeparate`); they are
  labelled `smallGap` too, because they test the same rule from the other side. Exactly 0.9 × and 1.1 × appear in
  4 single-fault cases each. 30 % of smallGap cases use `minGap` 0.5, 0.75 or 1.5 instead of 1.0.
- Alternatives: fill to the taller block (not what closing does); allow partial facing overlaps (fillets at the
  ends make the answer depend on the structuring element).
- Why: fair to any reasonable closing implementation; the threshold is tested from both sides.
- Revisit if: the owner wants gaps between walls of different extents.

## D-013 · 2026-11-18 · generator (M2) · floatingBlock / sunkBlock: rigid move within groundSnap, answer moves it back
- Context: "blocks within groundSnap of z = 0 are snapped to the ground". A lifted block could be moved down
  (height unchanged) or stretched down (height + lift). The two differ by more than the 1 % volume tolerance.
- Choice: the fault is a rigid vertical move of a whole block (all its floors) by 0.1–0.9 × `groundSnap`
  (0.9 × in a quarter of the single-fault cases). The expected answer is the clean model: moved back, height
  unchanged. Never beyond `groundSnap`, where the agreement gives no answer.
- Alternatives: stretch to the ground; also generate lifts above `groundSnap` (undefined by the agreement).
- Why: "snap" reads as "move"; the clean model is the design intent.
- Revisit if: the owner reads "snap" as "extend".

## D-012 · 2026-11-18 · generator (M2) · Sliver and shortEdge: what is injected and what is expected
- Context: both faults are "remove what is too small", so the repaired geometry should not depend on how it is removed.
- Choice: `sliver` adds thin triangles (smallest angle < 0.3 × `minAngleDeg`) or zero-area triangles along shared
  edges, re-routing the face through the new vertex; the surface stays closed and the solid is unchanged.
  `shortEdge` (one third each) adds a split vertex `0.1–0.8 × minEdge` from an edge end (geometry unchanged), or a
  ledge (upper floors set back by ε on one side), or a jog (a footprint rectangle moved ε off its neighbour's line),
  with ε = 0.1–0.8 × `minEdge`. Expected: the flush design. A ledge or jog may change a shell's volume by at most
  0.3 %, so filling it or cutting it both pass. Blocks of a closing gap pair are never given a ledge (it could push
  the gap over `minGap`).
- Alternatives: slivers that change geometry (slanted thin faces); bigger misalignments.
- Why: realistic exporter and modelling defects with one clear answer.
- Revisit if: the checker adds a minimum-angle check (the expected answer already has none below `minAngleDeg`).

## D-011 · 2026-11-18 · generator (M2) · Clean controls are already valid output
- Context: M2 requires clean controls that pass the checker "with zero repair".
- Choice: clean controls have origin [0, 0, 0], triangles only, one closed outward shell per block, every block on
  z = 0, no triangle angle below `minAngleDeg`, no edge below `minEdge`, blocks ≥ 4 m apart. Object names are
  `B01`, `B02` … (the input naming in AGREEMENT.md), not `shell_NNN`. Triangle meshes split walls at every floor and
  keep grid cells at an aspect ratio of 4 or less (all angles ≥ 14°). Other cases use polygons (55 %) or triangles.
- Alternatives: name objects `shell_NNN` (breaks the input naming rule); polygons (not valid output).
- Why: the only "repair" a clean control needs is a rename, which lets the checker prove it is not too strict.
- Revisit if: the checker wants clean inputs that are byte-for-byte valid `repaired.obj` files.

## D-010 · 2026-11-18 · generator (M2) · Tolerances vary in some cases
- Context: a solver that hard-codes the AGREEMENT.md defaults would pass every case if the defaults never changed.
- Choice: in about 30 % of the cases with the relevant fault, case.json moves one threshold: `minGap` 0.5/0.75/1.5
  (smallGap), `minEdge` 0.15/0.4 (shortEdge), `groundSnap` 0.25/1.0 (floating/sunk), `weld` 0.0005/0.005
  (duplicates), `minAngleDeg` 3/10 (sliver). `expected.json` `checks.minGap_m`/`minEdge_m` copy the case values.
  Clean controls use the defaults. The defaults themselves are unchanged.
- Alternatives: defaults everywhere.
- Why: a harder, fairer exam; it is what case.json tolerances are for.
- Revisit if: the owner wants v0.1 cases on defaults only.

## D-009 · 2026-11-18 · generator (M2) · Determinism: own PRNG, no trigonometry, a committed-cases test
- Context: same seed must give byte-identical files, ideally on every OS and .NET patch version.
- Choice: xoshiro256** seeded by SplitMix64, streams forked by tag (FNV-1a) so one extra draw does not shift other
  cases; directions from integer vectors and `Math.Sqrt` (IEEE exact) instead of `Math.Sin/Cos`; coordinates rounded
  to 6 decimals. `Committed_cases_match_the_generator` regenerates seed 1 and compares every byte with `cases/`.
- Alternatives: `System.Random` (algorithm may change), `Math.Sin/Cos` (results may differ by platform in the last bit).
- Why: hidden cases on the owner's machine and CI on Linux must match.
- Revisit if: the committed-cases test ever fails on CI.

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
