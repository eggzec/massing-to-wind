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

## Solver

### Overhangs and canopies are filled down to the ground (`partial`)

- **What:** the output is a 2.5D height field (D-029). A part that stands on nothing, beyond `groundSnap` (a
  canopy, a raised slab, a cantilever), is filled from its roof down to the ground.
- **How it shows:** status `partial`; a warning per block with the regions, their area and plan box, e.g. "block
  B02 has no support below part of it …: 1 region of 18 m² at [2, 10, 8, 13]"; the same in the `fillUnsupported`
  operation (D-040).
- **Why:** closed shells with a bottom at Z = 0 are what AGREEMENT.md asks for; true overhangs need a 3D boolean.
- **Watch for:** volume and blockage under canopies are overstated by area × slab bottom height.

### Sloped roofs are not represented (`partial`)

- **What:** only horizontal faces build the height field. Sloped triangles (neither horizontal nor vertical) are
  ignored; the roof is rebuilt from the horizontal faces of the object alone.
- **How it shows:** status `partial`, with the count and plan box of the sloped triangles.
- **Revisit:** a sloped-roof height field or a 3D boolean (D-029).

### Output slivers are reported, not removed (D-035)

- **What:** long low walls, tall narrow walls and thin roof pieces give triangles below `minAngleDeg`. Not graded in
  v0.1 (D-025); the count is in `warnings`.

### Roof flushing changes volume (D-030, D-037)

- **What:** touching roofs whose heights differ by less than `minEdge` are raised to the highest of them, as
  AGREEMENT.md now states. Volume grows by at most step × raised roof area (below `minEdge` × that area); the
  maximum height is kept. Reported as `flushRoofStep` with from/to heights.

### Jogs: the least-change rule may move the other wall (D-034)

- **What:** a short jog between two long walls is removed by moving the shorter wall onto the line of the other.
  Which wall the fault moved is not knowable; when it moved the longer one, the footprint is off by depth × length.
- **Margins seen:** worst 32 % of the footprint tolerance on the visible cases, 50 % on the unseen sets.

### Report detail and object splits (D-039)

- **What:** an object is split into parts only when it holds two or more separate solids that are each closed.
  An object with an open or broken surface stays one part, so for such input the report can still depend on how it
  was split into objects. `weld.count` counts duplicate vertices within each part.

### Gap closing on curved outlines (D-038)

- **What:** walls are measured along the chord of a chain of nearly collinear edges. On a curved outline that
  bends more than 5 mm from its chords, a gap counts only if the edges themselves are closer than `minGap`.

### Runtime

- **Measured** (this machine, Release build): a synthetic district of 500 blocks (terraced rows, gaps of 0.1–0.9 m,
  tiers, raised slabs) takes 1.15 s with blocks on the axes and 1.37 s with city blocks turned by up to 8° and
  heights to 1 cm (was 9.0 s and 30.3 s); 2000 parts take 4.2 s. As one object or as one object per part makes
  no real difference. The test limit is 15 s (`Step9ScaleTests`).
- **Still grows faster than linear:** gap closing works level by level where gaps exist, so a site with many
  near-touching blocks over many distinct roof levels costs (levels with gaps) × (walls near gaps).
