# Milestones — v0.1 (one week) · closed 2026-11-22, tag `v0.1.0`

v0.2 is below, after M5.

Each milestone ends at a **gate**: the owner reviews for about 15 minutes and either approves or sends it back.
Agents choose how to meet the criteria. They do not change the criteria.

## M1 — Foundations (Day 1) · Gate: owner approves and locks

- [x] `M2W.Geometry`: mesh type (vertices, faces, named objects), OBJ reader/writer, JSON models for `case.json`, `expected.json`, `report.json`, `result.json`.
- [x] Round-trip test: read OBJ → write OBJ → read again gives identical geometry.
- [x] `cases/_example/case_0007` reads and validates against the JSON models.
- [x] `m2w` command line runs `generate`, `repair`, `check`, `run-all` (stubs allowed for repair).
- [x] `scripts/validate.sh` runs build, tests, `run-all` and the lock check, and prints the summary.

**At the gate:** owner tags `lock-v0.1` (locks `AGREEMENT.md`).

## M2 — Test cases and checker (Days 1–2) · Gate: owner approves, checker gets locked

Generator:
- [x] Mini generative pipeline: site polygon → setback → footprints (bar, L, U, courtyard, tower) → floor-by-floor extrusion, seeded.
- [x] Fault injection for all 12 faults in `AGREEMENT.md`, alone and in combinations.
- [x] At least 20 cases per fault, plus 40 combined cases. `expected.json` computed from the clean geometry, not hand-typed.
- [x] Clean (fault-free) control cases that must pass unchanged.

Checker:
- [x] All checks: free edges, non-manifold edges, orientation (signed volume > 0), Euler number vs genus, self-intersections, min gap, min edge, shell count, volume, footprint area, height, mustMerge, mustStaySeparate.
- [x] Checker unit tests on hand-built good and bad meshes (it must fail broken meshes and pass clean ones).
- [x] Clean control cases pass the checker with zero repair (proves the checker is not too strict).

**At the gate:** owner checks 5 random cases by eye (images or a viewer), then moves `lock-v0.1` so `cases/` and `src/M2W.Checker/` are locked.

## M3 — Core repair (Days 2–4) · Gate: midpoint review

Solver, in this order:
- [x] Coordinate shift, vertex welding, duplicate removal (`farCoordinates`, `duplicateVertices`).
- [x] Orientation fix and hole closing (`flippedFaces`, `openSurface`).
- [x] Ground snap (`floatingBlock`, `sunkBlock`).
- [x] Footprint union for stacked floors, overlaps and touching walls (`stackedFloors`, `overlap`, `touchingWalls`).

**Target:** ≥ 90 % pass on these 8 faults, 100 % on clean controls.

## M4 — Hard faults (Days 4–6) · Gate: feature freeze at end of Day 6

- [x] Gap closing below `minGap` (morphological closing) (`smallGap`).
- [x] Sliver and short-edge removal (`sliver`, `shortEdge`).
- [x] Combined-fault cases.
- [x] Randomised property tests (FsCheck): moving or rotating input does not change volume or topology; repair twice = repair once.

**Target:** ≥ 80 % pass overall, every failure explained in `PROGRESS.md`.

## M5 — Package (Day 7)

- [x] README: problem, approach, results table from `out/summary.json`, before/after images, roadmap.
- [ ] Short demo video or GIF (owner records). The runner is ready (`demo/run-demo.ps1`, D-053); the recording
  is an asset and is not part of the `v0.1.0` tag.
- [x] `KNOWN_LIMITATIONS.md`: what fails and why.
- [x] CI (GitHub Actions) green.

---

# Milestones — v0.2 · DRAFT (2026-11-22), the owner approves it at the V0 gate

Same rules as v0.1: each milestone ends at a gate, agents choose how to meet the criteria and never change them.
Milestone names start with V so they never clash with v0.1's M1–M5. The backlog behind each item, with its
decision numbers and the locked files it touches, is in `docs/HANDOFF-v0.2.md`.

**Holds for every v0.2 milestone (regression guard):**
- validate green and CI green on Linux and Windows, with byte-identical output across the two (D-045, D-051).
- Every v0.1 case still passes. Every v0.1 `repaired.obj` that changes is listed with its reason in the PROGRESS
  entry (same check as the M5 guard: same shells and triangles, or an explained geometry change).
- Deterministic output; the 500-block district stays under the 5 s target (test limit 15 s, `Step9ScaleTests`).
- No hard-coded case IDs; the solver still reads only `input.obj` and `case.json`.

## V0 — Opening (owner) · Gate: owner approves this plan and the AGREEMENT changes

- [ ] Owner entry in PROGRESS: v0.1 feature freeze ended; this plan approved, changed or cut.
- [ ] `AGREEMENT.md` v0.2 changes, written by the owner, then `lock-v0.1` moved (or a new `lock-v0.2` tag):
  - how a sloped roof is described and graded (V3);
  - what output is expected for overhangs and canopies (V4);
  - the `minAngle_deg` check and whether it is graded or only reported (V2);
  - the validity-only mode: which checks run without `expected.json`, and how `result.json` says so (V1).
- [ ] New private hidden set and owner benchmarks re-run on `main` as the v0.2 baseline.
- [ ] Carry-over, small: xUnit2013 warning (`Step9WarningLocationTests.cs:102`); write the three `int[].Reverse()`
  calls as `Enumerable.Reverse` so the code also builds with C# 14 (D-051).

## V1 — Validity-only grading for real data · Gate: owner runs it on a real district

Real models have no `expected.json`. Today the owner grades them by hand.
- [ ] Checker: a validity-only mode that runs the checks needing no answer key (parse, watertight, manifold,
  outward normals, Euler, self-intersections, min gap, min edge, ground contact) and marks the others "not graded".
- [ ] CLI: run it on a folder of real models without `expected.json`; `summary.json` keeps graded and
  validity-only results apart.
- [ ] Tests: every v0.1 case gives the same validity results in both modes; a broken real-style model fails.

**Target:** the owner's real district grades valid in one command; v0.1 results unchanged.

## V2 — Minimum angle (D-025, D-035) · Gate: owner looks at 5 refined cases in ParaView

- [ ] Checker: `minAngle_deg` against `case.json` `minAngleDeg` (graded or reported, as decided at V0).
- [ ] Solver: refine walls and roofs so no output triangle is below `minAngleDeg`; vertices are added only on
  existing edges and faces, so the geometry does not change.
- [ ] Report: before/after count of thin triangles; output size growth stated.

**Target:** every case meets `minAngleDeg`, or each exception is explained; volume and footprint unchanged
(refinement only); a `sliver` pass then means the slivers are gone.

## V3 — Sloped roofs, still a height field (D-029) · Gate: owner reviews by eye and on a real model

- [ ] Generator: roof types beyond flat (shed, gable, hip), seeded, with expected answers computed from the clean
  geometry; flat-roof cases keep their v0.1 answers.
- [ ] Solver: piecewise planar roofs in the height field, so sloped input is no longer `partial`.
- [ ] Checker: volume and height checks correct on sloped roofs (tests on hand-built sloped shells).

**Target:** ≥ 90 % on the new sloped-roof cases, 100 % on clean controls, all v0.1 cases still pass.

## V4 — Overhangs and canopies, a 3D union (D-015, D-029, D-042) · Gate: owner review, then v0.2 feature freeze

The largest item: it leaves the 2.5D height field. It can be cut from v0.2 at the V0 gate.
- [ ] Generator: canopies, cantilevers, raised slabs and bridges, with exact expected answers.
- [ ] Solver: a 3D union for parts with nothing under them, instead of filling down to the ground
  (`fillUnsupported` becomes the exception, not the rule).
- [ ] Checker: tests on shells with overhangs (Euler number, self-intersections, ground contact per shell).

**Target:** ≥ 80 % on the new cases, every failure explained; overhang volume no longer overstated.

## V5 — Package v0.2 · Gate: owner tags `v0.2.0`

- [ ] README results and roadmap; KNOWN_LIMITATIONS updated (each entry current, with its D-number).
- [ ] Demo updated for the new cases (Rhino scene if the owner has added Rhino by then).
- [ ] CI green; owner benchmarks and private hidden set re-run; tag `v0.2.0`.

**Later (not v0.2):** external validation with OpenFOAM (`surfaceCheck`, `snappyHexMesh`, `checkMesh`; D-001),
Rhino/Grasshopper component, batch repair on AWS, wind-comfort surrogate model (README roadmap).
