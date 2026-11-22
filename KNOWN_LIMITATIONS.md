# Known limitations (v0.1)

What the pipeline does not do, or does not grade, and why. Each entry says what to watch for and gives the
decision behind it (`DECISIONS.md`). Final for v0.1 (M5); v0.2 items are in the README roadmap.

## Checker

### Thin slivers are not graded

- **What:** no check looks at triangle angles. A repaired shell can contain thin triangles (smallest angle below
  `case.json` `minAngleDeg`) and still pass, as long as it is watertight, manifold, outward, free of
  self-intersections and right in size and position.
- **Zero-area triangles are still caught:** `noSelfIntersections` counts a face with zero area as an overlap with
  its neighbour (DECISIONS.md D-019).
- **Effect on scores:** a `sliver` case whose slivers are thin but have some area passes without repair. With the
  M2 case set (measured at the M2 gate; the cases were regenerated since), 16 of the 30 `sliver` cases passed after
  zero repair (smallest angles 0.07°–0.7°). A `sliver` pass rate
  in `out/summary.json` therefore overstates how well the solver removes slivers. Read it together with the
  solver's `report.json`.
- **Why:** owner decision at the M2 gate (2026-11-18, D-025). The alternative was a `minAngle_deg` check. It would
  also fail some valid repairs: a long, low wall (for example 60 m × 3 m) cannot be split into two triangles with
  every angle above 5° unless the solver adds vertices.
- **Revisit:** v0.2, if the wind mesher turns out to need a minimum angle. Then add a `minAngle_deg` check against
  `minAngleDeg`. The generator's expected answers and clean controls already meet it (smallest angle 14.3°).

## Solver

### Overhangs and canopies are filled down to the ground (`partial`)

- **What:** the output is a 2.5D height field (D-029), so there is nothing under a roof but solid. A part that stands on nothing, beyond `groundSnap` (a
  canopy, a raised slab, a cantilever), is filled from its roof down to the ground.
- **How it shows:** status `partial`; a warning per block with the regions, their area and plan box, e.g. "block
  B02 has no support below part of it …: 1 region of 18 m² at [2, 10, 8, 13]"; the same in the `fillUnsupported`
  operation (D-042).
- **Why:** closed shells with a bottom at Z = 0 are what AGREEMENT.md asks for; true overhangs need a 3D boolean.
- **Watch for:** volume and blockage under canopies are overstated by area × slab bottom height.

### Sloped roofs are not represented (`partial`)

- **What (D-029):** only horizontal faces build the height field. Sloped triangles (neither horizontal nor vertical) are
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
- **Margins seen** (M5, current cases and solver): worst 19 % of the footprint tolerance on the visible cases and
  44.5 % on the unseen sets (seeds 777, 2024, 99991); the worst cases are all `shortEdge` cases. The owner's private
  hidden set: worst 11.7 % (footprint).

### Report detail and object splits (D-041)

- **What:** an object is split into parts only when it holds two or more separate solids that are each closed.
  An object with an open or broken surface stays one part, so for such input the report can still depend on how it
  was split into objects. `weld.count` counts duplicate vertices within each part.

### Gap closing on curved outlines (D-040)

- **What:** walls are measured along the chord of a chain of nearly collinear edges. On a curved outline that
  bends more than 5 mm from its chords, a gap counts only if the edges themselves are closer than `minGap`.

### Runtime (D-040)

- **Measured** (development machine, Release build, in-process, median of 7 runs after warm-up, M5): a synthetic
  district of about 500 blocks (terraced rows, gaps of 0.1–0.9 m, tiers, raised slabs) takes 0.6 s with blocks on
  the axes and 0.8 s with city blocks turned by up to 8° and heights to 1 cm; single runs 0.4–1.8 s. 2000 parts
  take about 3–3.5 s. As one object or as one object per part makes no real difference. Before D-040: 9.0 s and
  30.3 s. The test limit is 15 s (`Step9ScaleTests`).
- **Still grows faster than linear:** gap closing works level by level where gaps exist, so a site with many
  near-touching blocks over many distinct roof levels costs (levels with gaps) × (walls near gaps).

### Output order: object order is canonical; two sub-tolerance exceptions (D-047)

- **What:** shells, vertices and triangles are written in an order that depends on the geometry only, so the same
  model with its objects in another order, or as one object instead of many, gives the same `repaired.obj` (tested
  on synthetic districts, also with vertices renumbered and faces reordered). Shuffling the objects of every visible
  case and every unseen case (1,240 cases, two shuffles each, 2,480 runs) gives the same bytes in all but 7 runs,
  which come from 4 unseen cases (below).
- **Not yet independent of order:** (1) where points of *different* objects lie within `weld` of each other, the
  plan keeps the one met first (3 of 930 unseen cases: points moved by 0.1–0.5 mm); (2) a roof with two equally good
  triangulations breaks the tie by the vertex its outline starts at (1 of 930: same vertices, other diagonals). All
  four still pass with the same checks passing. Fixing either changes the output of other cases (the diagonal rule
  alone changes 254 of 1,241 outputs), which the feature freeze does not allow; v0.2.
- **Within one object** welding keeps the position of the lowest-numbered vertex of each cluster (D-049), so
  renumbering the vertices of an object with near-duplicate points can move them by up to `weld`.

### Gaps inside one part are not closed (D-032)

- **What:** gap closing bridges gaps between *separate* parts (AGREEMENT.md: gaps between blocks are closed and the
  blocks merged). A narrow inlet of one U-shaped block, or a courtyard narrower than `minGap`, stays open.
- **Watch for:** narrow inlets and courtyards within a single block in the output; they are left as modelled.

### Blocks below ground beyond `groundSnap`; blocks above it (D-049)

- **What:** only a block whose lowest point is within `groundSnap` of Z = 0 is moved. A block further below is not
  moved and the status is `partial` (located). A block further above is left where it is (it may stand on another
  block); if nothing is under it, it is filled down (see canopies above).

### Input the engine cannot fully mend (`partial`)

- **What:** each of these gives status `partial` with a located warning, never a silent success: holes that cannot
  be closed (`closeHoles.leftOpen`, D-042, D-049); input polygons that are not simple (fan-triangulated, D-049); plan
  repairs that do not settle within 12 rounds (D-036); a roof that cannot be triangulated; an output shell that
  fails the solver's own closed-shell check; an output edge shorter than `minEdge`; and, if the union builds no
  shell at all, the per-object fallback (each object written as its own shell, `rebuildShells.fallback`, D-042).
- **Large roofs:** roofs up to 150 vertices are triangulated to maximise the smallest angle; above that, by ear
  clipping, which can give thinner triangles (D-031).

### `report.json` is not byte-identical run to run (D-028)

- **What:** `runtime_ms` is measured. Every other field, and `repaired.obj`, is deterministic for the same input.
