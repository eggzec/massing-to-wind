# Milestones — v0.1 (one week)

Each milestone ends at a **gate**: the owner reviews for about 15 minutes and either approves or sends it back.
Agents choose how to meet the criteria. They do not change the criteria.

## M1 — Foundations (Day 1) · Gate: owner approves and locks

- [ ] `M2W.Geometry`: mesh type (vertices, faces, named objects), OBJ reader/writer, JSON models for `case.json`, `expected.json`, `report.json`, `result.json`.
- [ ] Round-trip test: read OBJ → write OBJ → read again gives identical geometry.
- [ ] `cases/_example/case_0007` reads and validates against the JSON models.
- [ ] `m2w` command line runs `generate`, `repair`, `check`, `run-all` (stubs allowed for repair).
- [ ] `scripts/validate.sh` runs build, tests, `run-all` and the lock check, and prints the summary.

**At the gate:** owner tags `lock-v0.1` (locks `AGREEMENT.md`).

## M2 — Test cases and checker (Days 1–2) · Gate: owner approves, checker gets locked

Generator:
- [ ] Mini generative pipeline: site polygon → setback → footprints (bar, L, U, courtyard, tower) → floor-by-floor extrusion, seeded.
- [ ] Fault injection for all 12 faults in `AGREEMENT.md`, alone and in combinations.
- [ ] At least 20 cases per fault, plus 40 combined cases. `expected.json` computed from the clean geometry, not hand-typed.
- [ ] Clean (fault-free) control cases that must pass unchanged.

Checker:
- [ ] All checks: free edges, non-manifold edges, orientation (signed volume > 0), Euler number vs genus, self-intersections, min gap, min edge, shell count, volume, footprint area, height, mustMerge, mustStaySeparate.
- [ ] Checker unit tests on hand-built good and bad meshes (it must fail broken meshes and pass clean ones).
- [ ] Clean control cases pass the checker with zero repair (proves the checker is not too strict).

**At the gate:** owner checks 5 random cases by eye (images or a viewer), then moves `lock-v0.1` so `cases/` and `src/M2W.Checker/` are locked.

## M3 — Core repair (Days 2–4) · Gate: midpoint review

Solver, in this order:
- [ ] Coordinate shift, vertex welding, duplicate removal (`farCoordinates`, `duplicateVertices`).
- [ ] Orientation fix and hole closing (`flippedFaces`, `openSurface`).
- [ ] Ground snap (`floatingBlock`, `sunkBlock`).
- [ ] Footprint union for stacked floors, overlaps and touching walls (`stackedFloors`, `overlap`, `touchingWalls`).

**Target:** ≥ 90 % pass on these 8 faults, 100 % on clean controls.

## M4 — Hard faults (Days 4–6) · Gate: feature freeze at end of Day 6

- [ ] Gap closing below `minGap` (morphological closing) (`smallGap`).
- [ ] Sliver and short-edge removal (`sliver`, `shortEdge`).
- [ ] Combined-fault cases.
- [ ] Randomised property tests (FsCheck): moving or rotating input does not change volume or topology; repair twice = repair once.

**Target:** ≥ 80 % pass overall, every failure explained in `PROGRESS.md`.

## M5 — Package (Day 7)

- [ ] README: problem, approach, results table from `out/summary.json`, before/after images, roadmap.
- [ ] Short demo video or GIF (owner records).
- [ ] `KNOWN_LIMITATIONS.md`: what fails and why.
- [ ] CI (GitHub Actions) green.
