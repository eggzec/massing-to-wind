# Known limitations (v0.1)

What the pipeline does not do, or does not grade, and why. Each entry says what to watch for. Milestone M5
completes this file; until then, roles add entries as they find them.

## Checker

### Thin slivers are not graded

- **What:** no check looks at triangle angles. A repaired shell can contain thin triangles (smallest angle below
  `case.json` `minAngleDeg`) and still pass, as long as it is watertight, manifold, outward, free of
  self-intersections and right in size and position.
- **Zero-area triangles are still caught:** `noSelfIntersections` counts a face with zero area as an overlap with
  its neighbour (DECISIONS.md D-019).
- **Effect on scores:** a `sliver` case whose slivers are thin but have some area passes without repair. With the
  M2 cases, 16 of the 30 `sliver` cases pass after zero repair (smallest angles 0.07°–0.7°). A `sliver` pass rate
  in `out/summary.json` therefore overstates how well the solver removes slivers. Read it together with the
  solver's `report.json`.
- **Why:** owner decision at the M2 gate (2026-11-18, D-025). The alternative was a `minAngle_deg` check. It would
  also fail some valid repairs: a long, low wall (for example 60 m × 3 m) cannot be split into two triangles with
  every angle above 5° unless the solver adds vertices.
- **Revisit:** v0.2, if the wind mesher turns out to need a minimum angle. Then add a `minAngle_deg` check against
  `minAngleDeg`. The generator's expected answers and clean controls already meet it (smallest angle 14.3°).
